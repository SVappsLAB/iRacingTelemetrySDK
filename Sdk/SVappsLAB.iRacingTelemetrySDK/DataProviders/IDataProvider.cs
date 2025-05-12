/**
 * Copyright (C) 2024-2025 Scott Velez
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 * http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.using Microsoft.CodeAnalysis;
**/

using System;
using System.Collections.Generic;
using SVappsLAB.iRacingTelemetrySDK.irSDKDefines;

namespace SVappsLAB.iRacingTelemetrySDK.DataProviders
{
    internal interface IDataProvider : IDisposable
    {
        void OpenDataSource();
        /// <summary>
        /// Gets a value indicating whether a connection to the data source is established.
        /// </summary>
        bool IsConnected { get; }

        /// <summary>
        /// Checks if the session information has been updated since the last check.
        /// </summary>
        /// <returns>True if session information was updated; otherwise, false.</returns>
        bool IsSessionInfoUpdated();

        /// <summary>
        /// Gets the current iRacing SDK header containing telemetry session metadata.
        /// </summary>
        /// <returns>The current iRacing SDK header.</returns>
        /// <exception cref="InvalidOperationException">Thrown when the data source is not open.</exception>
        irsdk_header GetHeader();

        /// <summary>
        /// Gets the session information as YAML text.
        /// </summary>
        /// <returns>The session information in YAML format, or an empty string if not available.</returns>
        /// <exception cref="InvalidOperationException">Thrown when the memory view is not acquired.</exception>
        string GetSessionInfoYaml();

        /// <summary>
        /// Gets the dictionary of variable headers that describe available telemetry variables.
        /// </summary>
        /// <returns>A dictionary of variable headers, or null if headers are not initialized.</returns>
        VarHeaderDictionary? GetVarHeaders();

        /// <summary>
        /// Gets the value of a telemetry variable by name.
        /// </summary>
        /// <param name="varName">The name of the variable to retrieve.</param>
        /// <returns>The value of the variable, which could be a scalar or an array.</returns>
        /// <exception cref="InvalidOperationException">Thrown when variable headers or telemetry buffer are not initialized.</exception>
        /// <exception cref="KeyNotFoundException">Thrown when the specified variable name does not exist.</exception>
        /// <exception cref="IndexOutOfRangeException">Thrown when the variable's data would exceed buffer boundaries.</exception>
        object GetVarValue(string varName);

        /// <summary>
        /// Waits for new telemetry data to become available.
        /// </summary>
        /// <param name="timeout">Maximum time to wait for new data.</param>
        /// <returns>True if new data is available; otherwise, false (timeout).</returns>
        bool WaitForDataReady(TimeSpan timeout);
    }
}
