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

using Microsoft.Extensions.Logging;
using SVappsLAB.iRacingTelemetrySDK;

namespace MinimalExample
{
    // 1. Define the telemetry variables you want to track
    [RequiredTelemetryVars([TelemetryVar.Speed, TelemetryVar.RPM])]
    internal class Program
    {
        public static async Task Main(string[] args)
        {
            // 2. Create logger
            var logger = LoggerFactory.Create(builder => builder.AddConsole())
                                      .CreateLogger("MinimalExample");

            // 3. Choose data source
            IBTOptions? ibtOptions = null;  // null for live telemetry from iRacing
                                            // = new IBTOptions("gt3_spa.ibt");  IBT filepath for file playback
            ibtOptions = new IBTOptions(args[0]);

            // 4. Create telemetry client
            await using var client = TelemetryClient<TelemetryData>.Create(logger, ibtOptions);

            // 5. Use cancellation token for proper shutdown
            using var cts = new CancellationTokenSource();

            // 6. Subscribe to all telemetryData streams using async delegate methods for simplified consumption
            // note: SubscribeToAllStreams handles single-reader channel limitation internally - safe for multiple callbacks
            var subscriptionTask = client.SubscribeToAllStreams(
                onTelemetryUpdate: async telemetryData =>
                {
                    Console.WriteLine($"Speed: {telemetryData.Speed}, RPM: {telemetryData.RPM}");
                },
                onSessionInfoUpdate: async session =>
                {
                    var driverCount = session.DriverInfo?.Drivers?.Count ?? 0;
                    Console.WriteLine($"Drivers: {driverCount}");
                },
                onConnectStateChanged: async state =>
                {
                    Console.WriteLine($"Connection: {state}");
                },
                onError: async error =>
                {
                    Console.WriteLine($"Error: {error.Message}");
                },
                cancellationToken: cts.Token);

            // 7. Enable graceful shutdown with Ctrl+C
            Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

            // 8. Start monitoring and wait for completion
            var monitorTask = client.Monitor(cts.Token);

            await Task.WhenAny(monitorTask, subscriptionTask);
        }
    }
}
