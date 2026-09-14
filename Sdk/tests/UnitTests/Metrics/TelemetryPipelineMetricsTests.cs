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

using Microsoft.Extensions.Logging.Abstractions;
using SVappsLAB.iRacingTelemetrySDK;
using SVappsLAB.iRacingTelemetrySDK.Metrics;
using System.Diagnostics.Metrics;

namespace UnitTests.Metrics;

// hand written rather than source generated - the accessor binds by public property name at
// runtime, so this exercises a realistic decode without running the generator in this project
public struct PipelineTelemetryData
{
    public float Speed { get; set; }
    public float RPM { get; set; }
    public int Gear { get; set; }
}

// runs a real ibt file through every delivery path and checks the pipeline instruments
public class TelemetryPipelineMetricsTests
{
    const string DroppedOverflow = MetricNames.TelemetryRecordsDropped + "|consumer_overflow";
    const string DroppedMissedTick = MetricNames.TelemetryRecordsDropped + "|missed_tick";

    // a few hundred records - more than the 60-record delivery buffer, so a slow consumer is
    // guaranteed to fall far enough behind for records to be evicted
    static string IbtFile => Path.Combine(AppContext.BaseDirectory, "data", "ibt", "metrics-playback.ibt");

    // hands out a meter per client and remembers which ones it made, so the collector picks out
    // exactly one client's instruments while other tests using the "ibt" data source run in parallel
    sealed class TestMeterFactory : IMeterFactory
    {
        readonly List<Meter> _meters = [];

        public Meter Create(MeterOptions options)
        {
            var meter = new Meter(options);
            lock (_meters)
                _meters.Add(meter);
            return meter;
        }

        public bool Owns(Meter meter)
        {
            lock (_meters)
                return _meters.Any(m => ReferenceEquals(m, meter));
        }

        public void Dispose()
        {
            lock (_meters)
            {
                foreach (var meter in _meters)
                    meter.Dispose();
                _meters.Clear();
            }
        }
    }

    // keyed by instrument name, plus the drop reason when present so the two drop kinds stay separate
    sealed class Collector : IDisposable
    {
        readonly MeterListener _listener = new();
        readonly Dictionary<string, List<double>> _values = [];

        public Collector(TestMeterFactory factory)
        {
            _listener.InstrumentPublished = (instrument, listener) =>
            {
                if (factory.Owns(instrument.Meter))
                    listener.EnableMeasurementEvents(instrument);
            };
            _listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) => Add(Key(instrument, tags), value));
            _listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) => Add(Key(instrument, tags), value));
            _listener.Start();
        }

        static string Key(Instrument instrument, ReadOnlySpan<KeyValuePair<string, object?>> tags)
        {
            foreach (var tag in tags)
            {
                if (tag.Key == MetricNames.DropReason)
                    return $"{instrument.Name}|{tag.Value}";
            }
            return instrument.Name;
        }

        void Add(string key, double value)
        {
            lock (_values)
            {
                if (!_values.TryGetValue(key, out var list))
                    _values[key] = list = [];
                list.Add(value);
            }
        }

        double[] Values(string key)
        {
            lock (_values)
                return _values.TryGetValue(key, out var list) ? list.ToArray() : [];
        }

        // for a histogram, how many records it measured
        public int Count(string key) => Values(key).Length;

        // for a counter, its total
        public long Sum(string key) => (long)Values(key).Sum();

        public double Median(string key)
        {
            var values = Values(key);
            if (values.Length == 0)
                return 0;
            Array.Sort(values);
            return values[values.Length / 2];
        }

        public string Describe()
        {
            lock (_values)
                return string.Join(", ", _values.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key}={kv.Value.Count}"));
        }

        public void Dispose() => _listener.Dispose();
    }

    static ITelemetryClient<PipelineTelemetryData> CreateClient(TestMeterFactory factory, TelemetryDeliveryMode mode = TelemetryDeliveryMode.Async) =>
        TelemetryClient<PipelineTelemetryData>.Create(
            NullLogger.Instance,
            new IBTOptions(IbtFile),
            new ClientOptions { MeterFactory = factory, DeliveryMode = mode });

    [Fact]
    public async Task AsyncHandler_MeasuresEveryPipelineStage()
    {
        using var factory = new TestMeterFactory();
        await using var client = CreateClient(factory);
        using var collector = new Collector(factory);

        var received = 0;
        await client.Monitor(new TelemetryHandlers<PipelineTelemetryData>
        {
            OnTelemetryUpdate = _ =>
            {
                received++;
                return Task.CompletedTask;
            }
        }, TestContext.Current.CancellationToken);

        Assert.True(received > 0, $"no telemetry reached the handler. instruments: {collector.Describe()}");

        var processed = collector.Sum(MetricNames.TelemetryRecordsProcessed);
        Assert.Equal(processed, collector.Count(MetricNames.TelemetryDecodeDuration));

        // one interval per acquisition, minus the first which has nothing to measure against
        Assert.Equal(processed - 1, collector.Count(MetricNames.TelemetrySampleInterval));

        // the consumer-side stages fire once per record that reached the handler
        Assert.Equal(received, collector.Count(MetricNames.TelemetrySdkDuration));
        Assert.Equal(received, collector.Count(MetricNames.TelemetryHandlerDuration));
        Assert.Equal(received, collector.Count(MetricNames.TelemetryTotalDuration));

        // every acquired record either reached the consumer or was counted as evicted
        Assert.Equal(processed, received + collector.Sum(DroppedOverflow));

        // ibt playback cannot skip records
        Assert.Equal(0, collector.Sum(DroppedMissedTick));
    }

    [Fact]
    public async Task SlowAsyncHandler_ShowsUpAsHandlerTimeAndConsumerOverflow()
    {
        using var factory = new TestMeterFactory();
        await using var client = CreateClient(factory);
        using var collector = new Collector(factory);

        await client.Monitor(new TelemetryHandlers<PipelineTelemetryData>
        {
            // blocking rather than awaiting a timer keeps the delay predictable
            OnTelemetryUpdate = _ =>
            {
                Thread.Sleep(2);
                return Task.CompletedTask;
            }
        }, TestContext.Current.CancellationToken);

        Assert.True(collector.Sum(DroppedOverflow) > 0,
            $"expected the slow consumer to lose records to eviction. instruments: {collector.Describe()}");

        // the cost of the callback is attributed to the handler
        Assert.True(collector.Median(MetricNames.TelemetryHandlerDuration) >= 0.001,
            $"expected a ~2ms handler to measure at least 1ms, got {collector.Median(MetricNames.TelemetryHandlerDuration)}s");

        // and waiting in the buffer behind that handler shows up as sdk latency
        Assert.True(collector.Median(MetricNames.TelemetrySdkDuration) >= 0.001,
            $"expected records to wait behind the slow handler, got a median sdk latency of {collector.Median(MetricNames.TelemetrySdkDuration)}s");
    }

    [Fact]
    public async Task SyncHandler_MeasuresEveryRecordWithNoQueueWait()
    {
        using var factory = new TestMeterFactory();
        await using var client = CreateClient(factory, TelemetryDeliveryMode.Synchronous);
        using var collector = new Collector(factory);

        await client.Monitor(new TelemetryHandlers<PipelineTelemetryData>
        {
            OnTelemetryUpdate = _ => Task.CompletedTask
        }, TestContext.Current.CancellationToken);

        var processed = collector.Sum(MetricNames.TelemetryRecordsProcessed);
        Assert.True(processed > 0, $"nothing was processed. instruments: {collector.Describe()}");

        // the handler runs inline, so every acquired record is measured
        Assert.Equal(processed, collector.Count(MetricNames.TelemetrySdkDuration));
        Assert.Equal(processed, collector.Count(MetricNames.TelemetryHandlerDuration));
        Assert.Equal(processed, collector.Count(MetricNames.TelemetryTotalDuration));

        // no buffer to evict from
        Assert.Equal(0, collector.Sum(DroppedOverflow));

        // sdk latency collapses to the decode cost
        Assert.True(collector.Median(MetricNames.TelemetrySdkDuration) < 0.001,
            $"synchronous sdk latency should be near zero, got a median of {collector.Median(MetricNames.TelemetrySdkDuration)}s");
    }

    [Fact]
    public async Task SessionInfo_RecordsSizeAndParseAttemptsForEachParse()
    {
        using var factory = new TestMeterFactory();
        await using var client = CreateClient(factory);
        using var collector = new Collector(factory);

        await client.Monitor(new TelemetryHandlers<PipelineTelemetryData>
        {
            OnTelemetryUpdate = _ => Task.CompletedTask
        }, TestContext.Current.CancellationToken);

        var processed = collector.Sum(MetricNames.SessionInfoRecordsProcessed);
        Assert.True(processed > 0, $"no session info was parsed. instruments: {collector.Describe()}");

        Assert.Equal(processed, collector.Count(MetricNames.SessionInfoSize));
        Assert.Equal(processed, collector.Count(MetricNames.SessionInfoParseAttempts));

        // a real session info yaml is kilobytes, not a handful of bytes
        Assert.True(collector.Median(MetricNames.SessionInfoSize) > 1024,
            $"session info size looks wrong: {collector.Median(MetricNames.SessionInfoSize)} bytes");
        Assert.InRange(collector.Median(MetricNames.SessionInfoParseAttempts), 1, 2);
    }

    [Fact]
    public async Task StreamConsumer_IsMeasuredLikeTheHandlerPath()
    {
        using var factory = new TestMeterFactory();
        await using var client = CreateClient(factory);
        using var collector = new Collector(factory);

        var received = 0;
        var reader = Task.Run(async () =>
        {
            await foreach (var _ in client.TelemetryData)
            {
                // slow on purpose, so the assertion below proves the loop body is what gets measured
                Thread.Sleep(2);
                received++;
            }
        }, TestContext.Current.CancellationToken);

        await client.Monitor(TestContext.Current.CancellationToken);
        await reader;

        Assert.True(received > 0, $"no telemetry reached the stream. instruments: {collector.Describe()}");

        Assert.Equal(received, collector.Count(MetricNames.TelemetrySdkDuration));
        Assert.Equal(received, collector.Count(MetricNames.TelemetryHandlerDuration));
        Assert.Equal(received, collector.Count(MetricNames.TelemetryTotalDuration));

        Assert.True(collector.Median(MetricNames.TelemetryHandlerDuration) >= 0.001,
            $"expected a ~2ms foreach body to measure at least 1ms, got {collector.Median(MetricNames.TelemetryHandlerDuration)}s");
    }
}
