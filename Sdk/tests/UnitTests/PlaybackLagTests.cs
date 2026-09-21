/**
 * Copyright (C) 2024-2026 Scott Velez
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
 * limitations under the License.
**/

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using SVappsLAB.iRacingTelemetrySDK.IBTPlayback;

namespace UnitTests;

public class PlaybackLagTests
{
    [Fact]
    public void RecordLag_IsZero_WhenRecordIsAheadOfSchedule()
    {
        var lags = new List<TimeSpan>();
        var governor = new SimpleGovernor(NullLogger.Instance, 1, lags.Add);
        governor.StartPlayback();

        // record 6000 is due 100 seconds into a 1x playback
        governor.RecordLag(6000);

        Assert.Equal([TimeSpan.Zero], lags);
    }

    [Fact]
    public async Task RecordLag_ReportsTimePastSchedule_WhenRecordIsLate()
    {
        var lags = new List<TimeSpan>();
        var governor = new SimpleGovernor(NullLogger.Instance, 1, lags.Add);
        governor.StartPlayback();

        // record 0 is due immediately, so everything waited here is lag
        await Task.Delay(100, TestContext.Current.CancellationToken);
        governor.RecordLag(0);

        var lag = Assert.Single(lags);
        Assert.True(lag >= TimeSpan.FromMilliseconds(90), $"lag was {lag.TotalMilliseconds}ms");
    }

    [Fact]
    public void RecordLag_ReportsNothing_AtMaxSpeed()
    {
        var lags = new List<TimeSpan>();
        var governor = new SimpleGovernor(NullLogger.Instance, int.MaxValue, lags.Add);
        governor.StartPlayback();

        governor.RecordLag(0);

        Assert.Empty(lags);
    }
}
