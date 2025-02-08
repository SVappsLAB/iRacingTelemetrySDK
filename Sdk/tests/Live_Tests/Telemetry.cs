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
using Microsoft.Extensions.Logging.Abstractions;
using SVappsLAB.iRacingTelemetrySDK;
using SVappsLAB.iRacingTelemetrySDK.Models;

namespace Live_Tests
{
    // the variables we want to use
    [RequiredTelemetryVars(["EngineWarnings", "IsOnTrackCar", "PlayerTrackSurface", "rpm", "SessionTick", "SessionTimeRemain"])]
    public class Telemetry
    {
        [Fact]
        public async Task EnsureEventsFired()
        {
            // ensure all these 'events' were triggered
            bool test_OnConnectStateChangedCalled = false;
            bool test_OnSessionInfoUpdateCalled = false;
            bool test_OnTelemetryUpdateCalled = false;

            ILogger logger = NullLogger.Instance;
            CancellationTokenSource cts = new CancellationTokenSource();

            using var telemetryClient = TelemetryClient<TelemetryData>.Create(logger);

            // set up event handlers
            telemetryClient.OnConnectStateChanged +=
                (object? _o, ConnectStateChangedEventArgs e) => test_OnConnectStateChangedCalled = true;
            telemetryClient.OnTelemetryUpdate +=
                (object? _o, TelemetryData _e) => test_OnTelemetryUpdateCalled = true;
            telemetryClient.OnSessionInfoUpdate +=
                (object? _o, TelemetrySessionInfo e) => test_OnSessionInfoUpdateCalled = true;

            // start monitoring
            var monitorTask = telemetryClient.Monitor(cts.Token);

            // loop up to 5 seconds, checking if the events have been triggered
            for (int i = 0; i < 5; i++)
            {
                var allEventsFired = test_OnConnectStateChangedCalled && test_OnTelemetryUpdateCalled && test_OnSessionInfoUpdateCalled;
                if (allEventsFired)
                {
                    break;
                }
                await Task.Delay(TimeSpan.FromSeconds(1));
            }

            Assert.True(test_OnConnectStateChangedCalled, "OnConnectStateChanged not called");
            Assert.True(test_OnTelemetryUpdateCalled, "OnTelemetryUpdate not called");
            Assert.True(test_OnSessionInfoUpdateCalled, "OnSessionInfoUpdate not called");

            // cancel monitoring
            cts.Cancel();
            await monitorTask;
        }

        [Fact]
        public async Task VerifyTelemetry()
        {
            ILogger logger = NullLogger.Instance;
            CancellationTokenSource cts = new CancellationTokenSource();

            // create client and set telemetry handler
            using var telemetryClient = TelemetryClient<TelemetryData>.Create(logger);
            telemetryClient.OnTelemetryUpdate += onNewTelemetryData;

            // telemetry data we expect to receive
            EngineWarnings engineWarnings = 0;
            var isOnTrackCar = false;
            TrackLocation playerTrackSurface = TrackLocation.NotInWorld;
            var rpm = 0.0f;
            var sessionTick = 0.0d;
            var sessionTimeRemain = 0.0d;

            // grab telemetry data when event is fired
            void onNewTelemetryData(object? _o, TelemetryData e)
            {
                // test flags
                engineWarnings = e.EngineWarnings;
                // test bool
                isOnTrackCar = e.IsOnTrackCar;
                // test enums
                playerTrackSurface = e.PlayerTrackSurface;
                // test floats
                rpm = e.RPM;
                // test ints
                sessionTick = e.SessionTick;
                // test doubles
                sessionTimeRemain = e.SessionTimeRemain;

                // we received data. cancel monitoring
                cts.Cancel();
            }

            // start monitoring
            var monitorTask = telemetryClient.Monitor(cts.Token);

            // loop up to 5 seconds, checking if we received telemetry data
            for (int i = 0; i < 5; i++)
            {
                if (isOnTrackCar)
                {
                    break;
                }
                await Task.Delay(TimeSpan.FromSeconds(1));
            }


            // check if the telemetry data is as expected
            Assert.True(engineWarnings == 0, "should be no EngineWarnings flags");
            Assert.True(isOnTrackCar, "IsOnTrackCar is false");

            var isValidTrackSurface =
                playerTrackSurface == TrackLocation.InPitStall ||
                playerTrackSurface == TrackLocation.OnTrack;
            Assert.True(isValidTrackSurface, "PlayerTrackSurface is not 0");

            Assert.True(rpm > 0, "rpm should be greater than 0");
            Assert.True(sessionTick > 0, "SessionTick is not greater than 0");
            Assert.True(sessionTimeRemain > 0, "SessionTimeRemain is not greater than 0");

            // cancel monitoring
            cts.Cancel();
            await monitorTask;
        }
    }
}
