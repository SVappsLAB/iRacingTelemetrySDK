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
            // if you pass in a IBT filename, we'll use that, otherwise default to LIVE mode
            var ibtFile = args.Length == 1 ? args[0] : null;
            var counter = 0;
            var logger = LoggerFactory
                    .Create(builder => builder
                    .AddConsole().AddSimpleConsole(o => o.SingleLine = true))
                    .CreateLogger("logger");

            // create telemetry client 
            using var tc = TelemetryClient<TelemetryData>.Create(logger, ibtFile);

            // subscribe to telemetry updates
            tc.OnTelemetryUpdate += OnTelemetryUpdate;

            // start monitoring - exit with Ctrl-C
            await tc.Monitor(CancellationToken.None);

            // event handler
            void OnTelemetryUpdate(object? sender, TelemetryData e)
            {
                // don't bother with data if we are not in car and on track 
                if (!e.IsOnTrackCar)
                    return;

                // to reduce logging, only log every 60th update (once a second)
                if ((counter++ % 60f) != 0)
                    return;

                // convert speed from m/s to mph
                var mph = e.Speed * 2.23694f;
                logger.LogInformation("gear: {gear}, rpm: {rpm}, speed: {speed}", e.Gear, e.RPM.ToString("F0"), mph.ToString("F0"));
            }
        }
    }
}

