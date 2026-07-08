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

using Microsoft.Extensions.Logging;
using SVappsLAB.iRacingTelemetrySDK;

namespace MinimalExampleSync;

internal class Program
{
    public static async Task Main(string[] args)
    {
        // 1. Create logger
        var logger = LoggerFactory.Create(builder => builder.AddConsole())
                                  .CreateLogger("MinimalExampleSync");

        // 2. Choose data source
        IBTOptions? ibtOptions = null;  // null for live telemetry from iRacing
                                        // = new IBTOptions("gt3_spa.ibt");  IBT filepath for file playback
        ibtOptions = new IBTOptions(args[0]);

        // 3. Create telemetry client in Synchronous delivery mode.
        // Sync mode is slower than the default Async mode, but it can be used if the telemetry variables
        // are not known ahead of time. Such as applications that allow the user to select which
        // telemetry variables to display at runtime.
        //
        // See MinimalExampleAsync for the default, higher-throughput mode.
        var clientOptions = new ClientOptions { DeliveryMode = TelemetryDeliveryMode.Synchronous };
        await using var client = TelemetryClient<DynamicTelemetryData>.Create(logger, ibtOptions, clientOptions);

        // 4. Use cancellation token for proper shutdown
        using var cts = new CancellationTokenSource();

        // 5. Enable graceful shutdown with Ctrl+C
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

        // 6. Define handlers
        var handlers = new TelemetryHandlers<DynamicTelemetryData>
        {
            OnTelemetryUpdate = _ =>
            {
                // 8. look up telemetry dynamically by name, instead of strongly-typed properties
                var speed = client.GetValue("Speed");
                var rpm = client.GetValue("RPM");
                Console.WriteLine($"Speed: {speed}, RPM: {rpm}");
                return Task.CompletedTask;
            },
            OnSessionInfoUpdate = session =>
            {
                var driverCount = session.DriverInfo?.Drivers?.Count ?? 0;
                Console.WriteLine($"Drivers: {driverCount}");
                return Task.CompletedTask;
            },
            OnConnectStateChanged = state =>
            {
                Console.WriteLine($"Connection: {state}");
                return Task.CompletedTask;
            },
            OnError = error =>
            {
                Console.WriteLine($"Error: {error.Message}");
                return Task.CompletedTask;
            }
        };

        // 9. Monitor telemetry data stream until cancellation
        await client.Monitor(handlers, cts.Token);
    }
}
