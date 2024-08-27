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

namespace IBT_Tests.Files
{
    public class ReadFile
    {
        CancellationTokenSource cts = new CancellationTokenSource();

        // TODO: convert these to use xunit raised event assertions
        [Fact]
        public async Task ValidFileSucceeds()
        {
            var ibtFile = @"../../../data/race_test/lamborghinievogt3_spa up.ibt";

            using var tc = TelemetryClient<TelemetryData>.Create(NullLogger.Instance, new IBTOptions(ibtFile, int.MaxValue));

            var gotConnected = false;
            EventHandler<ConnectStateChangedEventArgs> handler = (_sender, e) =>
            {
                if (e.State == ConnectState.Connected)
                {
                    gotConnected = true;
                }
            };

            tc.OnConnectStateChanged += handler;
            await tc.Monitor(cts.Token);
            tc.OnConnectStateChanged -= handler;

            Assert.True(gotConnected);
        }

        [Fact]
        public void InvalidFileThrows()
        {
            Assert.Throws<FileNotFoundException>(() =>
            {
                var ibtFile = @"no-such-file-name";
                using var tc = TelemetryClient<TelemetryData>.Create(NullLogger.Instance, new IBTOptions(ibtFile));
            });
        }
    }
}
