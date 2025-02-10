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

namespace LocationAndWarnings
{

    // these are the telemetry variables we want to track
    [RequiredTelemetryVars(["enginewarnings", "IsOnTrack", "PlayerTrackSurface", "PlayerTrackSurfaceMaterial"])]
    internal class Program
    {
        // pass the IBT file you want to analyze
        public static async Task Main(string[] args)
        {
            var counter = 0;
            var logger = LoggerFactory
                    .Create(builder => builder
                    .AddConsole().AddSimpleConsole(o => o.SingleLine = true))
                    .CreateLogger("logger");

            IBTOptions? ibtOptions = null;
            if (args.Length == 1)
                ibtOptions = new IBTOptions(args[0]);

            logger.LogInformation("processing data from \"{source}\"", ibtOptions == null ? "online LIVE session" : "offline IBT file");

            // create telemetry client 
            using var tc = TelemetryClient<TelemetryData>.Create(logger, ibtOptions);

            // subscribe to telemetry updates
            tc.OnTelemetryUpdate += OnTelemetryUpdate;

            // start monitoring telemetry - press ctrl-c to exit
            await tc.Monitor(CancellationToken.None);
            logger.LogInformation("done");


            void OnTelemetryUpdate(object? sender, TelemetryData e)
            {
                // slow things down, only output information every 2 seconds
                if ((counter++ % (2 * 60f)) != 0)
                    return;

                // figure out where the car is, and what the track surface is
                var trackSurface = Enum.GetName(e.PlayerTrackSurface);
                var trackSurfaceMaterial = Enum.GetName(e.PlayerTrackSurfaceMaterial);

                // engine warnings
                var engineWarnings = GetEngineWarnings(e.EngineWarnings);

                //  EngineWarnings,Boolean IsOnTrack,Int32 PlayerTrackSurface,Int32 PlayerTrackSurfaceMaterial,Int32 TrackWetness);
                var message = $"OnTrack:({e.IsOnTrack}), TrackSurface:({trackSurface}), TrackSurfaceMaterial:({trackSurfaceMaterial}), EngineWarnings:({engineWarnings})";
                logger.LogInformation(message);
            }

            string GetEngineWarnings(irsdk_EngineWarnings engineWarnings)
            {
                var warnings = new List<string>();
                foreach (var flag in Enum.GetValues<irsdk_EngineWarnings>())
                {
                    // check if the flag is set
                    if (engineWarnings.HasFlag(flag))
                    {
                        var flagName = Enum.GetName(flag);
                        if (!string.IsNullOrEmpty(flagName))
                            warnings.Add(flagName);
                    }
                }
                return string.Join(",", warnings);
            }
        }
    }
}

