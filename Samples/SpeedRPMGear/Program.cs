/**
 * Copyright (C)2024 Scott Velez
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

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SVappsLAB.iRacingTelemetrySDK;

namespace SpeedRPMGear
{
    // these are the telemetry variables we want to track
    [RequiredTelemetryVars([TelemetryVar.Gear, TelemetryVar.IsOnTrackCar, TelemetryVar.RPM, TelemetryVar.Speed])]
    internal class Program
    {
        static async Task Main(string[] args)
        {
            // Create host builder with dependency injection
            var builder = Host.CreateDefaultBuilder(args)
                .ConfigureServices((context, services) =>
                {
                    // Add metrics support
                    services.AddMetrics();

                    // Configure logging
                    services.AddLogging(logging =>
                    {
                        logging.SetMinimumLevel(LogLevel.Debug);
                        logging.AddConsole();
                    });

                    // Register TelemetryClient as a singleton
                    services.AddSingleton<ITelemetryClient<TelemetryData>>(provider =>
                    {
                        var logger = provider.GetRequiredService<ILogger<Program>>();
                        var meterFactory = provider.GetRequiredService<System.Diagnostics.Metrics.IMeterFactory>();

                        // if you pass in a IBT filename, we'll use that, otherwise default to LIVE mode
                        IBTOptions? ibtOptions = null;
                        if (args.Length == 1)
                            ibtOptions = new IBTOptions(args[0]);

                        var clientOptions = new ClientOptions { MeterFactory = meterFactory };
                        return TelemetryClient<TelemetryData>.Create(logger, clientOptions, ibtOptions);
                    });
                });

            using var host = builder.Build();

            var logger = host.Services.GetRequiredService<ILogger<Program>>();
            var tc = host.Services.GetRequiredService<ITelemetryClient<TelemetryData>>();

            var counter = 0;
            logger.LogInformation("Press Ctrl-C to exit...");

            // use this cancellation token to end processing
            using var cts = new CancellationTokenSource();
            var cancellationToken = cts.Token;

            // start telemetry consumption
            var telemetryTask = Task.Run(async () =>
            {
                await foreach (var data in tc.TelemetryDataStream.ReadAllAsync(cancellationToken))
                {
                    OnTelemetryUpdate(data);
                }
            }, cancellationToken);

            // start keyboard monitoring
            // - pause telemetry events when 'p' key is pressed
            // - resume telemetry events when 'r' key is pressed
            // - exit program when Ctrl-C is pressed
            var keyboardTask = MonitorKeyboardAsync();

            // start iRacing monitoring
            var monitorTask = tc.Monitor(cancellationToken);

            // wait for any task to complete
            // - when 'live', the keyboard task (Ctrl-C) is most likely to complete first (before the iRacing session ends)
            // - when playing 'IBT' files, the monitoring task is most likely to complete first (at end-of-file)
            await Task.WhenAny(keyboardTask, monitorTask, telemetryTask);

            // regardless of which task completes first,
            // set the cancellation token so the other tasks can complete
            cts.Cancel();

            // await for all tasks to complete
            await Task.WhenAll(monitorTask, keyboardTask, telemetryTask);


            // telemetry data handler
            async Task OnTelemetryUpdate(TelemetryData e)
            {
                // to reduce logging, only log every 60th update (once a second)
                if ((counter++ % 60f) != 0)
                    return;

                // convert speed from m/s to mph
                var mph = e.Speed * 2.23694f;
                logger.LogInformation("gear: {gear}, rpm: {rpm}, speed: {speed}", e.Gear, e.RPM?.ToString("F0"), mph?.ToString("F0"));
            }
            async Task MonitorKeyboardAsync()
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    if (Console.KeyAvailable)
                    {
                        var key = Console.ReadKey(intercept: true);
                        if (key.Key == ConsoleKey.P)
                        {
                            tc.Pause();
                            logger.LogInformation("Telemetry paused.");
                        }
                        else if (key.Key == ConsoleKey.R)
                        {
                            tc.Resume();
                            logger.LogInformation("Telemetry resumed.");
                        }
                    }
                    // wait a short time to avoid busy waiting
                    await Task.Delay(100, cancellationToken);
                }
            }
        }
    }
}

