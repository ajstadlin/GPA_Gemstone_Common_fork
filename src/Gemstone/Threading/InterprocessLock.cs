﻿//******************************************************************************************************
//  InterprocessLock.cs - Gbtc
//
//  Copyright © 2012, Grid Protection Alliance.  All Rights Reserved.
//
//  Licensed to the Grid Protection Alliance (GPA) under one or more contributor license agreements. See
//  the NOTICE file distributed with this work for additional information regarding copyright ownership.
//  The GPA licenses this file to you under the MIT License (MIT), the "License"; you may
//  not use this file except in compliance with the License. You may obtain a copy of the License at:
//
//      http://www.opensource.org/licenses/MIT
//
//  Unless agreed to in writing, the subject software distributed under the License is distributed on an
//  "AS-IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. Refer to the
//  License for the specific language governing permissions and limitations.
//
//  Code Modification History:
//  ----------------------------------------------------------------------------------------------------
//  03/21/2011 - J. Ritchie Carroll
//       Generated original version of source code.
//  06/30/2011 - Stephen Wills
//       Applying changes from Jian (Ryan) Zuo: updated to allow unauthorized users to attempt to grant
//       themselves lower than full access to existing mutexes and semaphores.
//  08/12/2011 - J. Ritchie Carroll
//       Modified creation methods such that locking natives are created in a synchronized fashion.
//  09/21/2011 - J. Ritchie Carroll
//       Added Mono implementation exception regions.
//  12/14/2012 - Starlynn Danyelle Gilliam
//       Modified Header.
//  12/18/2013 - J. Ritchie Carroll
//       Improved operational behavior.
//
//******************************************************************************************************

using System;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading;
using Gemstone.Identity;
using Gemstone.Security.Cryptography.HashAlgorithmExtensions;

namespace Gemstone.Threading;

/// <summary>
/// Defines helper methods related to inter-process locking.
/// </summary>
public static class InterprocessLock
{
    /// <summary>
    /// Default value for <see cref="Mutex"/> global flag.
    /// </summary>
    public const bool DefaultMutexGlobal = true;

    /// <summary>
    /// Default value for <see cref="NamedSemaphore"/> maximum count.
    /// </summary>
    public const int DefaultSemaphoreMaximumCount = 10;

    /// <summary>
    /// Default value for <see cref="NamedSemaphore"/> initial count.
    /// </summary>
    public const int DefaultSemaphoreInitialCount = -1;
    
    /// <summary>
    /// Default value for <see cref="NamedSemaphore"/> global flag.
    /// </summary>
    public const bool DefaultSemaphoreGlobal = true;

    private const int MutexHash = 0;
    private const int SemaphoreHash = 1;

    /// <summary>
    /// Gets a uniquely named inter-process <see cref="Mutex"/> associated with the running application, typically used to detect whether an instance
    /// of the application is already running.
    /// </summary>
    /// <param name="perUser">Indicates whether to generate a different name for the <see cref="Mutex"/> dependent upon the user running the application.</param>
    /// <returns>A uniquely named inter-process <see cref="Mutex"/> specific to the application; <see cref="Mutex"/> is created if it does not exist.</returns>
    /// <remarks>
    /// <para>
    /// This function uses a hash of the assembly's GUID when creating the <see cref="Mutex"/>, if it is available. If it is not available, it uses a hash
    /// of the simple name of the assembly. Although the name is hashed to help guarantee uniqueness, it is still entirely possible that another application
    /// may use that name with the same hashing algorithm to generate its <see cref="Mutex"/> name. Therefore, it is best to ensure that the
    /// <see cref="GuidAttribute"/> is defined in the AssemblyInfo of your application.
    /// </para>
    /// </remarks>
    /// <exception cref="UnauthorizedAccessException">The named mutex exists, but the user does not have the minimum needed security access rights to use it.</exception>
    [MethodImpl(MethodImplOptions.Synchronized)]
    public static Mutex GetNamedMutex(bool perUser)
    {
        Assembly entryAssembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
        GuidAttribute? attribute = entryAssembly.GetCustomAttributes(typeof(GuidAttribute), true).FirstOrDefault() as GuidAttribute;
        string? name = attribute?.Value ?? entryAssembly.GetName().Name;

        if (perUser)
            name += UserInfo.CurrentUserID;

        return GetNamedMutex(name!, !perUser);
    }

    /// <summary>
    /// Gets a uniquely named inter-process <see cref="Mutex"/> associated with the specified <paramref name="name"/> that identifies a source object
    /// needing concurrency locking.
    /// </summary>
    /// <param name="name">Identifying name of source object needing concurrency locking (e.g., a path and file name).</param>
    /// <param name="global">Determines if mutex should be marked as global; set value to <c>false</c> for local.</param>
    /// <returns>A uniquely named inter-process <see cref="Mutex"/> specific to <paramref name="name"/>; <see cref="Mutex"/> is created if it does not exist.</returns>
    /// <remarks>
    /// <para>
    /// This function uses a hash of the <paramref name="name"/> when creating the <see cref="Mutex"/>, not the actual <paramref name="name"/> - this way
    /// restrictions on the <paramref name="name"/> length do not need to be a user concern. All processes needing an inter-process <see cref="Mutex"/> need
    /// to use this same function to ensure access to the same <see cref="Mutex"/>.
    /// </para>
    /// <para>
    /// The <paramref name="name"/> can be a string of any length (must not be empty, null or white space) and is not case-sensitive. All hashes of the
    /// <paramref name="name"/> used to create the global <see cref="Mutex"/> are first converted to lower case.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException">Argument <paramref name="name"/> cannot be empty, null or white space.</exception>
    /// <exception cref="UnauthorizedAccessException">The named mutex exists, but the user does not have the minimum needed security access rights to use it.</exception>
    [MethodImpl(MethodImplOptions.Synchronized)]
    public static Mutex GetNamedMutex(string name, bool global = DefaultMutexGlobal)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentNullException(nameof(name), "Argument cannot be empty, null or white space.");

        // When requested, prefix mutex name with "Global\" such that mutex
        // will apply to all active application sessions
        string mutexName = $"{(global ? "Global" : "Local")}\\{GetHashedName(name, MutexHash)}";

        if (!Mutex.TryOpenExisting(mutexName, out Mutex? mutex))
            mutex = new Mutex(false, mutexName);

        return mutex;
    }

    /// <summary>
    /// Gets a uniquely named inter-process <see cref="NamedSemaphore"/> associated with the running application, typically used to detect whether some number of
    /// instances of the application are already running.
    /// </summary>
    /// <param name="perUser">Indicates whether to generate a different name for the <see cref="NamedSemaphore"/> dependent upon the user running the application.</param>
    /// <param name="maximumCount">The maximum number of requests for the semaphore that can be granted concurrently.</param>
    /// <param name="initialCount">The initial number of requests for the semaphore that can be granted concurrently, or -1 to default to <paramref name="maximumCount"/>.</param>
    /// <returns>A uniquely named inter-process <see cref="NamedSemaphore"/> specific to entry assembly; <see cref="NamedSemaphore"/> is created if it does not exist.</returns>
    /// <remarks>
    /// <para>
    /// This function uses a hash of the assembly's GUID when creating the <see cref="NamedSemaphore"/>, if it is available. If it is not available, it uses a hash
    /// of the simple name of the assembly. Although the name is hashed to help guarantee uniqueness, it is still entirely possible that another application
    /// may use that name with the same hashing algorithm to generate its <see cref="NamedSemaphore"/> name. Therefore, it is best to ensure that the
    /// <see cref="GuidAttribute"/> is defined in the AssemblyInfo of your application.
    /// </para>
    /// <para>
    /// On POSIX systems, the <see cref="NamedSemaphore"/> exhibits kernel persistence, meaning instances will remain active beyond the lifespan of the
    /// creating process. Named semaphores must be explicitly removed by invoking <see cref="NamedSemaphore.Unlink()"/> when they are no longer needed.
    /// Kernel persistence necessitates careful design consideration regarding the responsibility for invoking <see cref="NamedSemaphore.Unlink()"/>.
    /// Since the common use case for named semaphores is across multiple applications, it is advisable for the last exiting process to handle the
    /// cleanup. In cases where an application may crash before calling <see cref="NamedSemaphore.Unlink()"/>, the semaphore persists in the system,
    /// potentially leading to resource leakage. Implementations should include strategies to address and mitigate this risk.
    /// </para>
    /// </remarks>
    /// <exception cref="UnauthorizedAccessException">The named semaphore exists, but the user does not have the minimum needed security access rights to use it.</exception>
    [MethodImpl(MethodImplOptions.Synchronized)]
    public static NamedSemaphore GetNamedSemaphore(bool perUser, int maximumCount = DefaultSemaphoreMaximumCount, int initialCount = DefaultSemaphoreInitialCount)
    {
        Assembly entryAssembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
        GuidAttribute? attribute = entryAssembly.GetCustomAttributes(typeof(GuidAttribute), true).FirstOrDefault() as GuidAttribute;
        string? name = attribute?.Value ?? entryAssembly.GetName().Name;

        if (perUser)
            name += UserInfo.CurrentUserID;

        return GetNamedSemaphore(name!, maximumCount, initialCount, !perUser);
    }

    /// <summary>
    /// Gets a uniquely named inter-process <see cref="NamedSemaphore"/> associated with the specified <paramref name="name"/> that identifies a source object
    /// needing concurrency locking.
    /// </summary>
    /// <param name="name">Identifying name of source object needing concurrency locking (e.g., a path and file name).</param>
    /// <param name="maximumCount">The maximum number of requests for the semaphore that can be granted concurrently.</param>
    /// <param name="initialCount">The initial number of requests for the semaphore that can be granted concurrently, or -1 to default to <paramref name="maximumCount"/>.</param>
    /// <param name="global">Determines if semaphore should be marked as global; set value to <c>false</c> for local.</param>
    /// <returns>A uniquely named inter-process <see cref="NamedSemaphore"/> specific to <paramref name="name"/>; <see cref="NamedSemaphore"/> is created if it does not exist.</returns>
    /// <remarks>
    /// <para>
    /// This function uses a hash of the <paramref name="name"/> when creating the <see cref="NamedSemaphore"/>, not the actual <paramref name="name"/> - this way
    /// restrictions on the <paramref name="name"/> length do not need to be a user concern. All processes needing an inter-process <see cref="NamedSemaphore"/> need
    /// to use this same function to ensure access to the same <see cref="NamedSemaphore"/>.
    /// </para>
    /// <para>
    /// The <paramref name="name"/> can be a string of any length (must not be empty, null or white space) and is not case-sensitive. All hashes of the
    /// <paramref name="name"/> used to create the global <see cref="NamedSemaphore"/> are first converted to lower case.
    /// </para>
    /// <para>
    /// On POSIX systems, the <see cref="NamedSemaphore"/> exhibits kernel persistence, meaning instances will remain active beyond the lifespan of the
    /// creating process. Named semaphores must be explicitly removed by invoking <see cref="NamedSemaphore.Unlink()"/> when they are no longer needed.
    /// Kernel persistence necessitates careful design consideration regarding the responsibility for invoking <see cref="NamedSemaphore.Unlink()"/>.
    /// Since the common use case for named semaphores is across multiple applications, it is advisable for the last exiting process to handle the
    /// cleanup. In cases where an application may crash before calling <see cref="NamedSemaphore.Unlink()"/>, the semaphore persists in the system,
    /// potentially leading to resource leakage. Implementations should include strategies to address and mitigate this risk.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException">Argument <paramref name="name"/> cannot be empty, null or white space.</exception>
    /// <exception cref="UnauthorizedAccessException">The named semaphore exists, but the user does not have the minimum needed security access rights to use it.</exception>
    [MethodImpl(MethodImplOptions.Synchronized)]
    public static NamedSemaphore GetNamedSemaphore(string name, int maximumCount = DefaultSemaphoreMaximumCount, int initialCount = DefaultSemaphoreInitialCount, bool global = DefaultSemaphoreGlobal)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentNullException(nameof(name), "Argument cannot be empty, null or white space.");

        if (initialCount < 0)
            initialCount = maximumCount;

        // When requested, prefix semaphore name with "Global\" such that semaphore
        // will apply to all active application sessions
        string semaphoreName = $"{(global ? "Global" : "Local")}\\{GetHashedName(name, SemaphoreHash)}";

        if (!NamedSemaphore.TryOpenExisting(semaphoreName, out NamedSemaphore? semaphore))
            semaphore = new NamedSemaphore(initialCount, maximumCount, semaphoreName);

        return semaphore;
    }

    internal static string GetHashedName(string name, int hashIndex)
    {
        // Create a name that is specific to an object (e.g., a path and file name).
        // Note that we use a SHA hash to create a short common name for the name parameter
        // that was passed into the function - this allows the parameter to be very long, e.g.,
        // a file path, and still meet minimum mutex/semaphore name requirements.
        // This is not being used for security, just to create a unique name, so SHA is fine.
        SHA256 hash = SHA256.Create();
        return $"{hash.GetStringHash($"{name.ToLowerInvariant()}{hashIndex}").Replace('\\', '-')}";
    }
}
