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
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SVappsLAB.iRacingTelemetrySDK
{
    /// <summary>
    /// Extension methods for ITelemetryClient to help with async stream consumption patterns
    /// </summary>
    public static class SubscriptionExtensions
    {
        /// <summary>
        /// Subscribes to all data streams concurrently
        /// </summary>
        /// <typeparam name="T">The telemetry data type</typeparam>
        /// <param name="client">The telemetry client</param>
        /// <param name="onTelemetryUpdate">Async function to invoke for telemetry updates (optional)</param>
        /// <param name="onSessionInfoUpdate">Async function to invoke for session info updates (optional)</param>
        /// <param name="onRawSessionInfoUpdate">Async function to invoke for raw session info updates (optional)</param>
        /// <param name="onConnectStateChanged">Async function to invoke for connection state changes (optional)</param>
        /// <param name="onError">Async function to invoke for error notifications (optional)</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Task that completes when all streams are closed or cancelled</returns>
        /// <remarks>
        /// <para>
        /// Each stream processes items sequentially. Async callbacks are awaited before processing the next item
        /// in that stream. For 60Hz telemetry, ensure callbacks complete quickly (under 16ms) to avoid
        /// buffering/dropping frames.
        /// </para>
        /// <para>
        /// Callbacks execute on the calling thread's SynchronizationContext. For UI applications, callbacks run
        /// on the UI thread enabling direct control access. For long-running callbacks, use ConfigureAwait(false)
        /// or offload work to avoid blocking.
        /// </para>
        /// <para>
        /// <strong>Single-Reader Channels:</strong> This method properly handles the underlying single-reader channel
        /// limitation by creating separate subscriptions for each stream. Multiple callbacks can be registered via
        /// this method without causing undefined behavior.
        /// </para>
        /// </remarks>
        public static async Task SubscribeToAllStreams<T>(this ITelemetryClient<T> client,
            Func<T, Task>? onTelemetryUpdate = null,
            Func<TelemetrySessionInfo, Task>? onSessionInfoUpdate = null,
            Func<string, Task>? onRawSessionInfoUpdate = null,
            Func<ConnectState, Task>? onConnectStateChanged = null,
            Func<Exception, Task>? onError = null,
            CancellationToken cancellationToken = default) where T : struct
        {
            var tasks = new List<Task>();

            if (onTelemetryUpdate != null)
            {
                tasks.Add(client.SubscribeToTelemetry(onTelemetryUpdate, cancellationToken));
            }

            if (onSessionInfoUpdate != null)
            {
                tasks.Add(client.SubscribeToSessionInfo(onSessionInfoUpdate, cancellationToken));
            }

            if (onRawSessionInfoUpdate != null)
            {
                tasks.Add(client.SubscribeToRawSessionInfo(onRawSessionInfoUpdate, cancellationToken));
            }

            if (onConnectStateChanged != null)
            {
                tasks.Add(client.SubscribeToConnectState(onConnectStateChanged, cancellationToken));
            }

            if (onError != null)
            {
                tasks.Add(client.SubscribeToErrors(onError, cancellationToken));
            }

            if (tasks.Count > 0)
            {
                await Task.WhenAll(tasks);
            }
        }

        private static async Task SubscribeToTelemetry<T>(this ITelemetryClient<T> client,
             Func<T, Task> onTelemetryUpdate,
             CancellationToken cancellationToken = default) where T : struct
        {
            await foreach (var data in client.TelemetryData.WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                try
                {
                    await onTelemetryUpdate(data);
                }
                catch
                {
                    // Re-throw to fault the subscription task
                    // Users can handle via try-catch around SubscribeToAllStreams or monitor task exceptions
                    throw;
                }
            }
        }

        private static async Task SubscribeToSessionInfo<T>(this ITelemetryClient<T> client,
            Func<TelemetrySessionInfo, Task> onSessionInfoUpdate,
            CancellationToken cancellationToken = default) where T : struct
        {
            await foreach (var sessionInfo in client.SessionData.WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                try
                {
                    await onSessionInfoUpdate(sessionInfo);
                }
                catch
                {
                    // Re-throw to fault the subscription task
                    // Users can handle via try-catch around SubscribeToAllStreams or monitor task exceptions
                    throw;
                }
            }
        }
        private static async Task SubscribeToRawSessionInfo<T>(this ITelemetryClient<T> client,
            Func<string, Task> onRawSessionInfoUpdate,
            CancellationToken cancellationToken = default) where T : struct
        {
            await foreach (var rawSessionInfo in client.SessionDataYaml.WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                try
                {
                    await onRawSessionInfoUpdate(rawSessionInfo);
                }
                catch
                {
                    // Re-throw to fault the subscription task
                    // Users can handle via try-catch around SubscribeToAllStreams or monitor task exceptions
                    throw;
                }
            }
        }
        private static async Task SubscribeToErrors<T>(this ITelemetryClient<T> client,
            Func<Exception, Task> onError,
            CancellationToken cancellationToken = default) where T : struct
        {
            await foreach (var error in client.Errors.WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                try
                {
                    await onError(error);
                }
                catch
                {
                    // Re-throw to fault the subscription task
                    // Users can handle via try-catch around SubscribeToAllStreams or monitor task exceptions
                    throw;
                }
            }
        }
        private static async Task SubscribeToConnectState<T>(this ITelemetryClient<T> client,
            Func<ConnectState, Task> onConnectStateChanged,
            CancellationToken cancellationToken = default) where T : struct
        {
            await foreach (var connectState in client.ConnectStates.WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                try
                {
                    await onConnectStateChanged(connectState);
                }
                catch
                {
                    // Re-throw to fault the subscription task
                    // Users can handle via try-catch around SubscribeToAllStreams or monitor task exceptions
                    throw;
                }
            }
        }
    }
}
