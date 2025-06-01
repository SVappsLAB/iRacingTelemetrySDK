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
 * limitations under the License.using Microsoft.CodeAnalysis;
**/

using Microsoft.Extensions.Logging.Abstractions;
using SVappsLAB.iRacingTelemetrySDK;
using SVappsLAB.iRacingTelemetrySDK.Models;

namespace IBT_Tests
{
    [RequiredTelemetryVars(["IsOnTrackCar", "SessionTick", "EngineWarnings", "rpm", "SessionTimeRemain"])]
    public class Fixture : IAsyncLifetime
    {
        const int TIMEOUT_SECS = 5;
        public TelemetrySessionInfo TelemetrySessionInfo { get; private set; } = default!;
        public ITelemetryClient<TelemetryData> TelemetryClient = default!;
        private string _ibtPath;

        public Fixture(string ibtPath)
        {
            _ibtPath = ibtPath;
        }

        public async ValueTask InitializeAsync()
        {
            var delayTs = new CancellationTokenSource(TimeSpan.FromSeconds(TIMEOUT_SECS));

            // cancel when either the test context is cancelled or after 5 seconds
            var cts = CancellationTokenSource.CreateLinkedTokenSource(
               TestContext.Current.CancellationToken,
               delayTs.Token
               );

            TelemetryClient = TelemetryClient<TelemetryData>.Create(NullLogger.Instance, new IBTOptions(_ibtPath));
            TelemetryClient.OnSessionInfoUpdate += (object? sender, TelemetrySessionInfo si) =>
            {
                TelemetrySessionInfo = si;

                // now that we have the sessionInfo, we can cancel monitoring 
                cts.Cancel();
            };

            await TelemetryClient.Monitor(cts.Token);

            // check if the timeout cancellation was requested
            if (delayTs.IsCancellationRequested)
            {
                throw new Exception("timeout. unable to read session. cancelling the monitoring");
            }
        }

        public ValueTask DisposeAsync()
        {
            TelemetryClient.Dispose();

            return ValueTask.CompletedTask;
        }
    }

}
