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
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace SVappsLAB.iRacingTelemetrySDK.IBTPlayback;

internal interface IPlaybackGovernor
{
    public void StartPlayback(int recordsPerSecond = SimpleGovernor.DEFAULT_RECORDS_PER_SECOND);
    public Task GovernSpeed(int recNum, CancellationToken ct = default);
    public void RecordLag(int recNum);
}

internal class SimpleGovernor : IPlaybackGovernor
{
    internal const int DEFAULT_RECORDS_PER_SECOND = 60;

    readonly ILogger _logger;
    readonly int _playbackSpeedMultiplier;
    readonly Action<TimeSpan>? _onPlaybackLag;
    readonly Stopwatch _stopwatch = new Stopwatch();
    double _msPerRecord;

    bool IsMaxSpeed => _playbackSpeedMultiplier == int.MaxValue;

    // onPlaybackLag is called from RecordLag with how far behind its scheduled time a record is being read
    public SimpleGovernor(ILogger logger, int playbackSpeedMultiplier, Action<TimeSpan>? onPlaybackLag = null)
    {
        _logger = logger;
        _playbackSpeedMultiplier = playbackSpeedMultiplier;
        _onPlaybackLag = onPlaybackLag;
    }

    // recordsPerSecond is the file's tick rate - the rate the records were written at
    public void StartPlayback(int recordsPerSecond = DEFAULT_RECORDS_PER_SECOND)
    {
        if (recordsPerSecond <= 0)
        {
            _logger.LogWarning("IBT file has tick rate {tickRate}, playing back at {defaultRate} records per second",
                recordsPerSecond, DEFAULT_RECORDS_PER_SECOND);
            recordsPerSecond = DEFAULT_RECORDS_PER_SECOND;
        }

        _msPerRecord = 1000d / recordsPerSecond / _playbackSpeedMultiplier;
        _stopwatch.Restart();
    }

    public async Task GovernSpeed(int recNum, CancellationToken ct = default)
    {
        if (!_stopwatch.IsRunning)
            throw new InvalidOperationException("Playback has not been started.");

        // no schedule at max speed. returning before the first await completes synchronously,
        // so the unpaced path costs nothing per record
        if (IsMaxSpeed)
            return;

        // Task.Delay can wake early, wait until the deadline has really passed
        double waitMs;
        while ((waitMs = DueMs(recNum) - _stopwatch.Elapsed.TotalMilliseconds) > 0)
            await Task.Delay(TimeSpan.FromMilliseconds(waitMs), ct).ConfigureAwait(false);
    }

    // reports how far behind its scheduled time a record is being read, zero when on time.
    // nothing is reported at max speed, where there is no schedule
    public void RecordLag(int recNum)
    {
        if (_onPlaybackLag == null || IsMaxSpeed)
            return;

        var lagMs = Math.Max(0, _stopwatch.Elapsed.TotalMilliseconds - DueMs(recNum));
        _onPlaybackLag(TimeSpan.FromMilliseconds(lagMs));
    }

    double DueMs(int recNum) => recNum * _msPerRecord;
}
