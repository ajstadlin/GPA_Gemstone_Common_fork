﻿//******************************************************************************************************
//  CommandException.cs - Gbtc
//
//  Copyright © 2010, Grid Protection Alliance.  All Rights Reserved.
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
//  08/27/2014 - J. Ritchie Carroll
//       Generated original version of source code.
//
//******************************************************************************************************

using System;

namespace Gemstone.Console;

/// <summary>
/// Represents an exception that is thrown when <see cref="Command.Execute(string, string, int)"/> reports standard error output.
/// </summary>
public class CommandException : Exception
{
    #region [ Constructors ]

    /// <summary>
    /// Creates a new <see cref="CommandException"/>.
    /// </summary>
    public CommandException()
    {
    }

    /// <summary>
    /// Creates a new <see cref="CommandException"/> with the specified parameters.
    /// </summary>
    /// <param name="message">The error message that explains the reason for the exception.</param>
    /// <param name="processCompleted">Flag that determines if the source of command exception completed processing.</param>
    /// <param name="exitCode">Exit code of command process, assuming process successfully completed.</param>
    /// <param name="innerException">The exception that is the cause of the current exception, or a null reference if no inner exception is specified.</param>
    public CommandException(string message, bool processCompleted, int exitCode, Exception? innerException = null) : base(message, innerException)
    {
        ProcessCompleted = processCompleted;
        ExitCode = exitCode;
    }

    #endregion

    #region [ Properties ]

    /// <summary>
    /// Gets flag that determines if the source of this command exception completed processing.
    /// </summary>
    public bool ProcessCompleted { get; }

    /// <summary>
    /// Gets exit code from command process, assuming successful process completion.
    /// </summary>
    public int ExitCode { get; }

    #endregion
}