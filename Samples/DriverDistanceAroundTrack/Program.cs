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
using SVappsLAB.iRacingTelemetrySDK.Models;

namespace DriverDistanceAroundTrack
{
    // these are the 'live' telemetry variables we want to track
    [RequiredTelemetryVars(["CarIdxLapDistPct"])]
    internal class Program
    {

        static async Task Main(string[] args)
        {
            Dictionary<int, DriverPosition> _driverPositions = new();
            var counter = 0;
            var logger = LoggerFactory
                    .Create(builder => builder
                        .AddConsole()
                        .AddSimpleConsole(o => o.SingleLine = true)
                        .SetMinimumLevel(LogLevel.Debug))
                    .CreateLogger("logger");

            using var tc = TelemetryClient<TelemetryData>.Create(logger);

            // subscribe to updates
            tc.OnSessionInfoUpdate += OnSessionInfoUpdate;
            tc.OnTelemetryUpdate += OnTelemetryUpdate;

            // start monitoring - exit with Ctrl-C
            await tc.Monitor(CancellationToken.None);

            // session handler
            void OnSessionInfoUpdate(object? _, TelemetrySessionInfo e)
            {
                foreach (var driver in e.DriverInfo.Drivers)
                {
                    // if there is a new driver, add them
                    if (!_driverPositions.ContainsKey(driver.CarIdx))
                        _driverPositions.Add(driver.CarIdx, new DriverPosition(driver.CarNumber, driver.UserName));
                    // if the driver info changed, update it
                    else if (_driverPositions[driver.CarIdx].DriverName != driver.UserName)
                        _driverPositions[driver.CarIdx] = new DriverPosition(driver.CarNumber, driver.UserName);
                }
            }

            // telemetry handler
            void OnTelemetryUpdate(object? _, TelemetryData e)
            {
                // to reduce logging, only log every 60th update (once a second)
                if ((counter++ % 60f) != 0)
                    return;

                // hacky... but it works
                // move the cursor to the top of the console, so we can get a nicely formatted output, without scrolling
                Console.SetCursorPosition(0, 0);

                for (int carIdx = 0; carIdx < 64; carIdx++)
                {
                    // for any car that is on track, update their lap distance
                    if (e.CarIdxLapDistPct[carIdx] > 0)
                    {
                        if (_driverPositions.TryGetValue(carIdx, out DriverPosition? driverPosition))
                        {
                            // update the driver's lap distance
                            driverPosition.LapDistance = e.CarIdxLapDistPct[carIdx];

                            logger.LogInformation(driverPosition.ToString());
                        }
                    }
                }

                // show all driver's distances around the track
                for (int i = 0; i < 64; i++)
                {
                    if (_driverPositions.TryGetValue(i, out DriverPosition? driverPosition))
                    {
                        if (driverPosition.LapDistance > 0)
                        {
                            var carNum = driverPosition.CarNumber.PadLeft(4);
                            var driverName = $"\"{driverPosition.DriverName}\"".PadRight(30);
                            var distance = (driverPosition.LapDistance * 100).ToString("F0").PadLeft(2);

                            var dataStr = $"#{carNum}:{driverName} is {distance}% around the track";
                            Console.WriteLine(dataStr);
                            //logger.LogInformation("Driver: {info}", driverPosition);
                        }
                    }
                }
            }
        }
    }
}

