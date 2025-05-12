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

using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace SVappsLAB.iRacingTelemetrySDK.IBTPlayback
{
    public record GovernorStats(long elapsedMs, int CurrentRecord, int TargetRecord, double OldDelay, double NewDelay);

    public interface IPlaybackGovernor
    {
        public void StartPlayback();
        public Task GovernSpeed(int recNum);
        public GovernorStats GetStats();
    }

    public class SimpleGovernor : IPlaybackGovernor
    {
        const int STANDARD_HZ = 60;
        const double STANDARD_MS_PER_RECORD = 1000d / STANDARD_HZ;
        ILogger _logger;
        int _playbackSpeedMultiplier;
        double _adjustmentAmountInMs;
        TimeSpan _delayTimeSpan;            // current delay amount
        Stopwatch _stopwatch = new Stopwatch();
        GovernorStats? _governorStats;

        public SimpleGovernor(ILogger logger, int playbackSpeedMultiplier)
        {
            _logger = logger;
            _playbackSpeedMultiplier = playbackSpeedMultiplier;
            _adjustmentAmountInMs = STANDARD_MS_PER_RECORD / _playbackSpeedMultiplier / 5; // make delay +- changes in 5% increments
        }

        public GovernorStats GetStats() => _governorStats ?? throw new InvalidOperationException("No stats available.");

        public void StartPlayback()
        {
            // at startup, the processing time seems to be about 1/4 of what we need to match the playback speed
            // so we start with an artificially small delay that is 1/4 of the standard delay.
            // the normal governor logic will align us with the correct target delay
            _delayTimeSpan = TimeSpan.FromMilliseconds(STANDARD_MS_PER_RECORD / _playbackSpeedMultiplier) / 4;
            _stopwatch.Start();
        }

        public Task GovernSpeed(int recNum)
        {
            if (!_stopwatch.IsRunning)
                throw new InvalidOperationException("Playback has not been started.");

            // short circuit if we are at max speed
            if (_playbackSpeedMultiplier == int.MaxValue)
            {
                return Task.CompletedTask;
            }

            // every 30 records, (1/2 of the 60hz rate), recalculate the delay amount to account for drift
            if (recNum != 0)
            {
                if (recNum % (STANDARD_HZ / 2) == 0)
                {
                    CalculateGoverningDelay(recNum);
                }
            }
            // slow down processing to match the playback speed
            return Task.Delay(_delayTimeSpan);
        }

        void CalculateGoverningDelay(int currentRecNum)
        {
            var elapsed = _stopwatch.ElapsedMilliseconds;
            var targetRecNum = elapsed / (STANDARD_MS_PER_RECORD / _playbackSpeedMultiplier);

            var oldDelay = _delayTimeSpan;
            if (currentRecNum < targetRecNum)
            {
                if (_delayTimeSpan.TotalMilliseconds > _adjustmentAmountInMs)
                    _delayTimeSpan -= TimeSpan.FromMilliseconds(_adjustmentAmountInMs);
                else
                    _delayTimeSpan = TimeSpan.Zero;
            }
            else
            {
                _delayTimeSpan += TimeSpan.FromMilliseconds(_adjustmentAmountInMs);
            }

            _governorStats = new GovernorStats(elapsed, currentRecNum, (int)targetRecNum, oldDelay.TotalMilliseconds, _delayTimeSpan.TotalMilliseconds);

            _logger.LogDebug("calc governor. stats: {stats}", _governorStats);
        }
    }
}


