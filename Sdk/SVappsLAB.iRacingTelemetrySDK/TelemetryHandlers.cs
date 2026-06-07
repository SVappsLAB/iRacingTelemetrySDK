/**
 * Copyright (C) 2024-2026 Scott Velez
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
 * limitations under the License.
**/

using System;
using System.Threading.Tasks;

namespace SVappsLAB.iRacingTelemetrySDK
{
    /// <summary>
    /// Callback handlers used by <see cref="ITelemetryClient{T}.Monitor(TelemetryHandlers{T}, System.Threading.CancellationToken)"/>.
    /// </summary>
    /// <typeparam name="T">The telemetry data type.</typeparam>
    /// <remarks>
    /// Handlers are awaited sequentially per stream. Keep handlers fast, especially
    /// <see cref="OnTelemetryUpdate"/>, which can run at 60Hz. Queue expensive work to
    /// application-owned background processing when needed.
    /// </remarks>
    public sealed class TelemetryHandlers<T> where T : struct
    {
        /// <summary>
        /// Called when a telemetry data update is available.
        /// </summary>
        public Func<T, Task>? OnTelemetryUpdate { get; init; }

        /// <summary>
        /// Called when parsed session information is available.
        /// </summary>
        public Func<TelemetrySessionInfo, Task>? OnSessionInfoUpdate { get; init; }

        /// <summary>
        /// Called when raw YAML session information is available.
        /// </summary>
        public Func<string, Task>? OnRawSessionInfoUpdate { get; init; }

        /// <summary>
        /// Called when the telemetry source connection state changes.
        /// </summary>
        public Func<ConnectState, Task>? OnConnectStateChanged { get; init; }

        /// <summary>
        /// Called when the SDK publishes a telemetry processing error.
        /// </summary>
        /// <remarks>
        /// Exceptions thrown by user handlers are not routed here. Handler exceptions
        /// fault the <c>Monitor(...)</c> call directly.
        /// </remarks>
        public Func<Exception, Task>? OnError { get; init; }
    }
}
