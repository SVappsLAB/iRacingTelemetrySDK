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

using Microsoft.Extensions.Logging.Abstractions;
using SVappsLAB.iRacingTelemetrySDK;
using SVappsLAB.iRacingTelemetrySDK.Models;

namespace IBT_Tests
{
    [RequiredTelemetryVars(["IsOnTrackCar", "SessionTick", "EngineWarnings", "rpm", "SessionTimeRemain"])]
    public class Fixture : IAsyncLifetime
    {
        CancellationTokenSource cts = new CancellationTokenSource();
        public TelemetrySessionInfo TelemetrySessionInfo { get; private set; } = default!;
        public ITelemetryClient<TelemetryData> TelemetryClient = default!;
        private string _ibtPath;

        public Fixture(string ibtPath)
        {
            _ibtPath = ibtPath;
        }

        public async Task InitializeAsync()
        {
            var tcs = new TaskCompletionSource<bool>();

            TelemetryClient = TelemetryClient<TelemetryData>.Create(NullLogger.Instance, _ibtPath);
            TelemetryClient.OnSessionInfoUpdate += (object? sender, TelemetrySessionInfo si) =>
            {
                TelemetrySessionInfo = si;

                //set flag that we are ready to run tests
                tcs.SetResult(true);

                // now that we have the sessionInfo, we can cancel monitoring the rest of the file
                cts.Cancel();
            };

            var task = TelemetryClient.Monitor(cts.Token);

            // monitoring has been cancelled. wait for monitor exit 
            await task;

            // wait for the signal that we have the sessioninfo
            await tcs.Task;
        }

        public Task DisposeAsync()
        {
            TelemetryClient.Dispose();

            return Task.CompletedTask;
        }
    }

}
