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
 * limitations under the License.
**/

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SVappsLAB.iRacingTelemetrySDK
{
    /// <summary>
    /// Extension methods for ITelemetryClient to help with channel consumption patterns
    /// </summary>
    public static class ChannelExtensions
    {
        /// <summary>
        /// Subscribes to telemetry data from the channel and invokes the provided action for each item
        /// </summary>
        /// <typeparam name="T">The telemetry data type</typeparam>
        /// <param name="client">The telemetry client</param>
        /// <param name="onTelemetryUpdate">Action to invoke for each telemetry update</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Task that completes when the channel is closed or cancelled</returns>
        public static Task SubscribeToTelemetryAsync<T>(this ITelemetryClient<T> client,
            Action<T> onTelemetryUpdate,
            CancellationToken cancellationToken = default) where T : struct
        {
            return Task.Run(async () =>
            {
                await foreach (var data in client.TelemetryDataStream.ReadAllAsync(cancellationToken))
                {
                    onTelemetryUpdate(data);
                }
            }, cancellationToken);
        }

        /// <summary>
        /// Subscribes to session info from the channel and invokes the provided action for each item
        /// </summary>
        /// <typeparam name="T">The telemetry data type</typeparam>
        /// <param name="client">The telemetry client</param>
        /// <param name="onSessionInfoUpdate">Action to invoke for each session info update</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Task that completes when the channel is closed or cancelled</returns>
        public static Task SubscribeToSessionInfoAsync<T>(this ITelemetryClient<T> client,
            Action<TelemetrySessionInfo> onSessionInfoUpdate,
            CancellationToken cancellationToken = default) where T : struct
        {
            return Task.Run(async () =>
            {
                await foreach (var sessionInfo in client.SessionDataStream.ReadAllAsync(cancellationToken))
                {
                    onSessionInfoUpdate(sessionInfo);
                }
            }, cancellationToken);
        }

        /// <summary>
        /// Subscribes to raw session info from the channel and invokes the provided action for each item
        /// </summary>
        /// <typeparam name="T">The telemetry data type</typeparam>
        /// <param name="client">The telemetry client</param>
        /// <param name="onRawSessionInfoUpdate">Action to invoke for each raw session info update</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Task that completes when the channel is closed or cancelled</returns>
        public static Task SubscribeToRawSessionInfoAsync<T>(this ITelemetryClient<T> client,
            Action<string> onRawSessionInfoUpdate,
            CancellationToken cancellationToken = default) where T : struct
        {
            return Task.Run(async () =>
            {
                await foreach (var rawSessionInfo in client.RawSessionDataStream.ReadAllAsync(cancellationToken))
                {
                    onRawSessionInfoUpdate(rawSessionInfo);
                }
            }, cancellationToken);
        }

        /// <summary>
        /// Subscribes to error notifications from the channel and invokes the provided action for each item
        /// </summary>
        /// <typeparam name="T">The telemetry data type</typeparam>
        /// <param name="client">The telemetry client</param>
        /// <param name="onError">Action to invoke for each error notification</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Task that completes when the channel is closed or cancelled</returns>
        public static Task SubscribeToErrorsAsync<T>(this ITelemetryClient<T> client,
            Action<ExceptionEventArgs> onError,
            CancellationToken cancellationToken = default) where T : struct
        {
            return Task.Run(async () =>
            {
                await foreach (var error in client.ErrorStream.ReadAllAsync(cancellationToken))
                {
                    onError(error);
                }
            }, cancellationToken);
        }

        /// <summary>
        /// Subscribes to connection state changes from the channel and invokes the provided action for each item
        /// </summary>
        /// <typeparam name="T">The telemetry data type</typeparam>
        /// <param name="client">The telemetry client</param>
        /// <param name="onConnectStateChanged">Action to invoke for each connection state change</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Task that completes when the channel is closed or cancelled</returns>
        public static Task SubscribeToConnectStateAsync<T>(this ITelemetryClient<T> client,
            Action<ConnectStateChangedEventArgs> onConnectStateChanged,
            CancellationToken cancellationToken = default) where T : struct
        {
            return Task.Run(async () =>
            {
                await foreach (var connectState in client.ConnectStateStream.ReadAllAsync(cancellationToken))
                {
                    onConnectStateChanged(connectState);
                }
            }, cancellationToken);
        }

        /// <summary>
        /// Subscribes to all data streams concurrently
        /// </summary>
        /// <typeparam name="T">The telemetry data type</typeparam>
        /// <param name="client">The telemetry client</param>
        /// <param name="onTelemetryUpdate">Action to invoke for telemetry updates (optional)</param>
        /// <param name="onSessionInfoUpdate">Action to invoke for session info updates (optional)</param>
        /// <param name="onRawSessionInfoUpdate">Action to invoke for raw session info updates (optional)</param>
        /// <param name="onError">Action to invoke for error notifications (optional)</param>
        /// <param name="onConnectStateChanged">Action to invoke for connection state changes (optional)</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Task that completes when all channels are closed or cancelled</returns>
        public static async Task SubscribeToAllStreamsAsync<T>(this ITelemetryClient<T> client,
            Action<T>? onTelemetryUpdate = null,
            Action<TelemetrySessionInfo>? onSessionInfoUpdate = null,
            Action<string>? onRawSessionInfoUpdate = null,
            Action<ExceptionEventArgs>? onError = null,
            Action<ConnectStateChangedEventArgs>? onConnectStateChanged = null,
            CancellationToken cancellationToken = default) where T : struct
        {
            var tasks = new List<Task>();

            if (onTelemetryUpdate != null)
            {
                tasks.Add(client.SubscribeToTelemetryAsync(onTelemetryUpdate, cancellationToken));
            }

            if (onSessionInfoUpdate != null)
            {
                tasks.Add(client.SubscribeToSessionInfoAsync(onSessionInfoUpdate, cancellationToken));
            }

            if (onRawSessionInfoUpdate != null)
            {
                tasks.Add(client.SubscribeToRawSessionInfoAsync(onRawSessionInfoUpdate, cancellationToken));
            }

            if (onError != null)
            {
                tasks.Add(client.SubscribeToErrorsAsync(onError, cancellationToken));
            }

            if (onConnectStateChanged != null)
            {
                tasks.Add(client.SubscribeToConnectStateAsync(onConnectStateChanged, cancellationToken));
            }

            if (tasks.Count > 0)
            {
                await Task.WhenAll(tasks);
            }
        }

        /// <summary>
        /// Waits for the first item from any of the specified streams
        /// </summary>
        /// <typeparam name="T">The telemetry data type</typeparam>
        /// <param name="client">The telemetry client</param>
        /// <param name="waitForTelemetry">Whether to wait for telemetry data</param>
        /// <param name="waitForSessionInfo">Whether to wait for session info</param>
        /// <param name="waitForRawSessionInfo">Whether to wait for raw session info</param>
        /// <param name="waitForError">Whether to wait for error notifications</param>
        /// <param name="waitForConnectState">Whether to wait for connection state changes</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Task that completes when the first item is received from any specified stream</returns>
        public static async Task WaitForFirstDataAsync<T>(this ITelemetryClient<T> client,
            bool waitForTelemetry = true,
            bool waitForSessionInfo = true,
            bool waitForRawSessionInfo = false,
            bool waitForError = false,
            bool waitForConnectState = false,
            CancellationToken cancellationToken = default) where T : struct
        {
            var tasks = new List<Task>();

            if (waitForTelemetry)
            {
                tasks.Add(Task.Run(async () =>
                {
                    await foreach (var _ in client.TelemetryDataStream.ReadAllAsync(cancellationToken))
                    {
                        return; // Exit after first item
                    }
                }, cancellationToken));
            }

            if (waitForSessionInfo)
            {
                tasks.Add(Task.Run(async () =>
                {
                    await foreach (var _ in client.SessionDataStream.ReadAllAsync(cancellationToken))
                    {
                        return; // Exit after first item
                    }
                }, cancellationToken));
            }

            if (waitForRawSessionInfo)
            {
                tasks.Add(Task.Run(async () =>
                {
                    await foreach (var _ in client.RawSessionDataStream.ReadAllAsync(cancellationToken))
                    {
                        return; // Exit after first item
                    }
                }, cancellationToken));
            }

            if (waitForError)
            {
                tasks.Add(Task.Run(async () =>
                {
                    await foreach (var _ in client.ErrorStream.ReadAllAsync(cancellationToken))
                    {
                        return; // Exit after first item
                    }
                }, cancellationToken));
            }

            if (waitForConnectState)
            {
                tasks.Add(Task.Run(async () =>
                {
                    await foreach (var _ in client.ConnectStateStream.ReadAllAsync(cancellationToken))
                    {
                        return; // Exit after first item
                    }
                }, cancellationToken));
            }

            if (tasks.Count > 0)
            {
                await Task.WhenAny(tasks);
            }
        }
    }
}
