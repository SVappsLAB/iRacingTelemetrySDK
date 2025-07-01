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

using Microsoft.Extensions.Logging;
using SVappsLAB.iRacingTelemetrySDK;

namespace SpeedRPMGear
{
    // these are the telemetry variables we want to track
    [RequiredTelemetryVars(["gear", "isOnTrackCar", "rpm", "speed"])]
    internal class Program
    {
        static async Task Main(string[] args)
        {
            var counter = 0;
            var logger = LoggerFactory
                    .Create(builder => builder
                    .SetMinimumLevel(LogLevel.Debug)
                    .AddConsole())
                    .CreateLogger("logger");

            // if you pass in a IBT filename, we'll use that, otherwise default to LIVE mode
            IBTOptions? ibtOptions = null;
            if (args.Length == 1)
                ibtOptions = new IBTOptions(args[0]);

            logger.LogInformation("Press Ctrl-C to exit...");

            // create telemetry client 
            using var tc = TelemetryClient<TelemetryData>.Create(logger, ibtOptions);

            // subscribe to telemetry updates
            tc.OnTelemetryUpdate += OnTelemetryUpdate;

            // use this cancellation token to end processing
            using var cts = new CancellationTokenSource();
            var cancellationToken = cts.Token;

            // start keyboard monitoring
            // - pause telemetry events when 'p' key is pressed
            // - resume telemetry events when 'r' key is pressed
            // - exit program when Ctrl-C is pressed
            var keyboardTask = MonitorKeyboardAsync();

            // start iRacing monitoring
            var monitorTask = tc.Monitor(cancellationToken);

            // wait for either task to complete
            // - when 'live', the keyboard task (Ctrl-C) is most likely to complete first (before the iRacing session ends)
            // - when playing 'IBT' files, the monitoring task is most likely to complete first (at end-of-file)
            await Task.WhenAny(keyboardTask, monitorTask);

            // regardless of which task completes first,
            // set the cancellation token so the other task can complete
            cts.Cancel();

            // await for all tasks to complete
            await Task.WhenAll(monitorTask, keyboardTask);


            // event handler
            void OnTelemetryUpdate(object? sender, TelemetryData e)
            {
                // to reduce logging, only log every 60th update (once a second)
                if ((counter++ % 60f) != 0)
                    return;

                // convert speed from m/s to mph
                var mph = e.Speed * 2.23694f;
                logger.LogInformation("gear: {gear}, rpm: {rpm}, speed: {speed}", e.Gear, e.RPM.ToString("F0"), mph.ToString("F0"));
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

