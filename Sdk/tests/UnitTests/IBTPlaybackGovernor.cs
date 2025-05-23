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

using System.Diagnostics;

#if DEBUG
using Microsoft.Extensions.Logging;
using Serilog;
#endif
using SVappsLAB.iRacingTelemetrySDK.IBTPlayback;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace UnitTests
{
    public class LogFixture
    {
        public Microsoft.Extensions.Logging.ILogger Logger;
        public LogFixture()
        {
#if DEBUG
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.File("governorStatsOutput.log")
                .CreateLogger();

            var loggerFactory = LoggerFactory.Create(builder =>
            {
                builder.AddSerilog();
            });

            // create logger for test data
            Logger = loggerFactory.CreateLogger("testLogger");
#else
            Logger = NullLogger.Instance;
#endif
        }
    }
    public class IBTPlaybackGovernor : IClassFixture<LogFixture>
    {
        Microsoft.Extensions.Logging.ILogger _logger;
        public IBTPlaybackGovernor(LogFixture logFixture)
        {
            _logger = logFixture.Logger;
        }
        public static TheoryData<int, int> Data =>
            new TheoryData<int, int>
            {
                    {  1, 5},
                    { 10, 5},
                    {1000, 5}
            };

        [Theory]
        [Trait("Category", "Manual")]
        [MemberData(nameof(Data))]
        public async Task GovernorTests(int speedMultiplier, int secsOfDataToSimulate)
        {
            var recsToProcess = speedMultiplier * secsOfDataToSimulate * 60;  // 60 records per second

            IPlaybackGovernor g = new SimpleGovernor(_logger, speedMultiplier);
            g.StartPlayback();

            var sw = new Stopwatch();
            sw.Start();
            for (int i = 0; i < recsToProcess; i++)
            {
                await g.GovernSpeed(i);
            }
            sw.Stop();

            // differential
            var elapsedSeconds = sw.ElapsedMilliseconds / 1000d;
            var timeDiffInSeconds = Math.Abs(secsOfDataToSimulate - elapsedSeconds);

            // we should be time accurate within 1 second at the end of the run
            Assert.True(timeDiffInSeconds < 1);
        }
    }
}



