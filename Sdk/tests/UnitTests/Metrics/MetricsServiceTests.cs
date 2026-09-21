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

using SVappsLAB.iRacingTelemetrySDK;
using SVappsLAB.iRacingTelemetrySDK.Metrics;
using System.Diagnostics.Metrics;

namespace UnitTests.Metrics;

public class MetricsServiceTests
{
    record Measurement(double Value, Dictionary<string, object?> Tags);

    // listens only to instruments whose meter carries this test's unique data source,
    // so tests running in parallel never see each other's measurements
    sealed class Recorder : IDisposable
    {
        readonly MeterListener _listener = new();
        readonly List<(string Name, Measurement Measurement)> _measurements = [];
        readonly Dictionary<string, Instrument> _instruments = [];

        public Recorder(string dataSource, params string[] instrumentNames)
        {
            _listener.InstrumentPublished = (instrument, listener) =>
            {
                var ours = instrument.Meter.Tags?.Any(t => t.Key == MetricNames.DataSource && Equals(t.Value, dataSource)) == true;
                if (!ours)
                    return;

                lock (_instruments)
                    _instruments[instrument.Name] = instrument;

                if (instrumentNames.Contains(instrument.Name))
                    listener.EnableMeasurementEvents(instrument);
            };
            _listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) => Add(instrument.Name, value, tags));
            _listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) => Add(instrument.Name, value, tags));
            _listener.Start();
        }

        void Add(string name, double value, ReadOnlySpan<KeyValuePair<string, object?>> tags)
        {
            var measurement = new Measurement(value, tags.ToArray().ToDictionary(t => t.Key, t => t.Value));
            lock (_measurements)
                _measurements.Add((name, measurement));
        }

        public Measurement[] For(string name)
        {
            lock (_measurements)
                return _measurements.Where(m => m.Name == name).Select(m => m.Measurement).ToArray();
        }

        public Instrument Instrument(string name)
        {
            lock (_instruments)
                return _instruments[name];
        }

        public void Dispose() => _listener.Dispose();
    }

    static string UniqueDataSource() => $"test-{Guid.NewGuid():N}";

    [Fact]
    public async Task TelemetryRecordProcessed_RecordsCountAndDurationInSeconds()
    {
        var dataSource = UniqueDataSource();
        await using var service = new MetricsService(null, dataSource);
        using var recorder = new Recorder(dataSource, MetricNames.TelemetryRecordsProcessed, MetricNames.TelemetryDecodeDuration);

        var start = service.Telemetry.StartTiming();
        service.Telemetry.RecordProcessed(start);

        Assert.NotEqual(TelemetryMeters.NotTiming, start);
        var processed = Assert.Single(recorder.For(MetricNames.TelemetryRecordsProcessed));
        Assert.Equal(1, processed.Value);
        Assert.Empty(processed.Tags);

        var duration = Assert.Single(recorder.For(MetricNames.TelemetryDecodeDuration));
        Assert.InRange(duration.Value, 0, 1);
    }

    [Fact]
    public async Task TelemetryStartTiming_SkipsClock_WhenDurationIsNotCollected()
    {
        var dataSource = UniqueDataSource();
        await using var service = new MetricsService(null, dataSource);
        using var recorder = new Recorder(dataSource, MetricNames.TelemetryRecordsProcessed);

        var start = service.Telemetry.StartTiming();
        service.Telemetry.RecordProcessed(start);

        Assert.Equal(TelemetryMeters.NotTiming, start);
        Assert.Single(recorder.For(MetricNames.TelemetryRecordsProcessed));
        Assert.Empty(recorder.For(MetricNames.TelemetryDecodeDuration));
    }

    [Fact]
    public async Task TelemetryRecordFailed_TagsCountAndDurationWithErrorType()
    {
        var dataSource = UniqueDataSource();
        await using var service = new MetricsService(null, dataSource);
        using var recorder = new Recorder(dataSource, MetricNames.TelemetryRecordsProcessed, MetricNames.TelemetryDecodeDuration);

        service.Telemetry.RecordFailed(service.Telemetry.StartTiming(), new InvalidOperationException());

        var processed = Assert.Single(recorder.For(MetricNames.TelemetryRecordsProcessed));
        Assert.Equal("System.InvalidOperationException", processed.Tags[MetricNames.ErrorType]);
        var duration = Assert.Single(recorder.For(MetricNames.TelemetryDecodeDuration));
        Assert.Equal("System.InvalidOperationException", duration.Tags[MetricNames.ErrorType]);
    }

    [Fact]
    public async Task TelemetryDropped_IsTaggedWithReason()
    {
        var dataSource = UniqueDataSource();
        await using var service = new MetricsService(null, dataSource);
        using var recorder = new Recorder(dataSource, MetricNames.TelemetryRecordsDropped);

        service.Telemetry.TicksMissed(3);
        service.Telemetry.ConsumerOverflow(1);

        var dropped = recorder.For(MetricNames.TelemetryRecordsDropped);
        Assert.Equal(2, dropped.Length);
        Assert.Equal((3d, "missed_tick"), (dropped[0].Value, dropped[0].Tags[MetricNames.DropReason]));
        Assert.Equal((1d, "consumer_overflow"), (dropped[1].Value, dropped[1].Tags[MetricNames.DropReason]));
    }

    [Fact]
    public async Task SessionInfo_RecordsSuccessAndFailureOnOneCounter()
    {
        var dataSource = UniqueDataSource();
        await using var service = new MetricsService(null, dataSource);
        using var recorder = new Recorder(dataSource, MetricNames.SessionInfoRecordsProcessed, MetricNames.SessionInfoProcessDuration);

        service.SessionInfo.RecordProcessed(TimeSpan.FromMilliseconds(5), 40_000, 1);
        service.SessionInfo.RecordFailed(TimeSpan.FromMilliseconds(20), 50_000, new FormatException());

        var processed = recorder.For(MetricNames.SessionInfoRecordsProcessed);
        Assert.Equal(2, processed.Length);
        Assert.Empty(processed[0].Tags);
        Assert.Equal("System.FormatException", processed[1].Tags[MetricNames.ErrorType]);

        var durations = recorder.For(MetricNames.SessionInfoProcessDuration);
        Assert.Equal([0.005, 0.02], durations.Select(d => d.Value));
    }

    [Fact]
    public async Task SessionInfo_RecordsSizeAndParseAttempts()
    {
        var dataSource = UniqueDataSource();
        await using var service = new MetricsService(null, dataSource);
        using var recorder = new Recorder(dataSource, MetricNames.SessionInfoSize, MetricNames.SessionInfoParseAttempts);

        service.SessionInfo.RecordProcessed(TimeSpan.FromMilliseconds(5), 40_000, 2);
        service.SessionInfo.RecordFailed(TimeSpan.FromMilliseconds(20), 50_000, new FormatException());

        var sizes = recorder.For(MetricNames.SessionInfoSize);
        Assert.Equal([40_000d, 50_000d], sizes.Select(s => s.Value));
        Assert.Empty(sizes[0].Tags);
        Assert.Equal("System.FormatException", sizes[1].Tags[MetricNames.ErrorType]);

        // a failed parse exhausted every strategy, so only the successful record has an attempt count
        var attempts = Assert.Single(recorder.For(MetricNames.SessionInfoParseAttempts));
        Assert.Equal(2, attempts.Value);
    }

    [Fact]
    public async Task SessionInfoDropped_IsTaggedWithReasonAndStream()
    {
        var dataSource = UniqueDataSource();
        await using var service = new MetricsService(null, dataSource);
        using var recorder = new Recorder(dataSource, MetricNames.SessionInfoRecordsDropped);

        service.SessionInfo.ParseBacklogOverflow(1);
        service.SessionInfo.SessionDataOverflow(1);
        service.SessionInfo.SessionDataYamlOverflow(1);

        var dropped = recorder.For(MetricNames.SessionInfoRecordsDropped);
        Assert.Equal(3, dropped.Length);

        Assert.Equal("parse_backlog", dropped[0].Tags[MetricNames.DropReason]);
        Assert.False(dropped[0].Tags.ContainsKey(MetricNames.SessionInfoStream));

        Assert.Equal(("consumer_overflow", "session_data"), (dropped[1].Tags[MetricNames.DropReason], dropped[1].Tags[MetricNames.SessionInfoStream]));
        Assert.Equal(("consumer_overflow", "session_data_yaml"), (dropped[2].Tags[MetricNames.DropReason], dropped[2].Tags[MetricNames.SessionInfoStream]));
    }

    [Fact]
    public async Task TelemetryPipeline_RecordsEveryStageInSeconds()
    {
        var dataSource = UniqueDataSource();
        await using var service = new MetricsService(null, dataSource);
        using var recorder = new Recorder(dataSource,
            MetricNames.TelemetrySdkDuration, MetricNames.TelemetryHandlerDuration,
            MetricNames.TelemetryTotalDuration, MetricNames.TelemetrySampleInterval);

        var first = service.Telemetry.MarkAcquired();
        var acquired = service.Telemetry.MarkAcquired();
        var handlerStart = service.Telemetry.RecordHandoff(acquired);
        await Task.Delay(20, TestContext.Current.CancellationToken);
        service.Telemetry.RecordConsumed(acquired, handlerStart);

        Assert.NotEqual(TelemetryMeters.NotTiming, first);
        Assert.NotEqual(TelemetryMeters.NotTiming, handlerStart);

        // the first acquisition has nothing to measure an interval against
        Assert.InRange(Assert.Single(recorder.For(MetricNames.TelemetrySampleInterval)).Value, 0, 1);
        Assert.InRange(Assert.Single(recorder.For(MetricNames.TelemetrySdkDuration)).Value, 0, 0.015);

        var handler = Assert.Single(recorder.For(MetricNames.TelemetryHandlerDuration)).Value;
        var endToEnd = Assert.Single(recorder.For(MetricNames.TelemetryTotalDuration)).Value;
        Assert.InRange(handler, 0.015, 1);
        Assert.True(endToEnd >= handler, $"end-to-end {endToEnd}s is below handler {handler}s");
    }

    [Fact]
    public async Task TelemetryPipeline_SkipsClock_WhenNotCollected()
    {
        var dataSource = UniqueDataSource();
        await using var service = new MetricsService(null, dataSource);
        using var recorder = new Recorder(dataSource);

        var acquired = service.Telemetry.MarkAcquired();
        var handlerStart = service.Telemetry.RecordHandoff(acquired);
        service.Telemetry.RecordConsumed(acquired, handlerStart);

        Assert.Equal(TelemetryMeters.NotTiming, acquired);
        Assert.Equal(TelemetryMeters.NotTiming, handlerStart);
    }

    [Fact]
    public async Task PlaybackLag_RecordsInSeconds()
    {
        var dataSource = UniqueDataSource();
        await using var service = new MetricsService(null, dataSource);
        using var recorder = new Recorder(dataSource, MetricNames.PlaybackLag);

        service.Playback.RecordLag(TimeSpan.Zero);
        service.Playback.RecordLag(TimeSpan.FromMilliseconds(250));

        Assert.Equal([0d, 0.25], recorder.For(MetricNames.PlaybackLag).Select(m => m.Value));
    }

    [Fact]
    public async Task Instruments_FollowNamingAndUnitConventions()
    {
        var dataSource = UniqueDataSource();
        await using var service = new MetricsService(null, dataSource);
        using var recorder = new Recorder(dataSource);

        var expectedUnits = new Dictionary<string, string>
        {
            [MetricNames.TelemetryRecordsProcessed] = "{record}",
            [MetricNames.TelemetryRecordsDropped] = "{record}",
            [MetricNames.TelemetryDecodeDuration] = "s",
            [MetricNames.TelemetrySdkDuration] = "s",
            [MetricNames.TelemetryHandlerDuration] = "s",
            [MetricNames.TelemetryTotalDuration] = "s",
            [MetricNames.TelemetrySampleInterval] = "s",
            [MetricNames.SessionInfoRecordsProcessed] = "{record}",
            [MetricNames.SessionInfoRecordsDropped] = "{record}",
            [MetricNames.SessionInfoProcessDuration] = "s",
            [MetricNames.SessionInfoSize] = "By",
            [MetricNames.SessionInfoParseAttempts] = "{attempt}",
            [MetricNames.PlaybackLag] = "s",
        };

        foreach (var (name, expectedUnit) in expectedUnits)
        {
            var instrument = recorder.Instrument(name);
            Assert.Matches("^svappslab\\.iracingsdk\\.[a-z_.]+$", instrument.Name);
            Assert.Equal(TelemetryMetrics.MeterName, instrument.Meter.Name);
            Assert.False(string.IsNullOrEmpty(instrument.Meter.Version));
            Assert.Equal(expectedUnit, instrument.Unit);
        }
    }
}
