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
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Threading.Tasks;

namespace SVappsLAB.iRacingTelemetrySDK.Metrics;

// use opentelemetry naming conventions:
// lowercase, dot-namespaced, no unit or '_total' suffix in the name (exporters add those)
internal static class MetricNames
{
    const string Prefix = "svappslab.iracingsdk.";

    public const string TelemetryRecordsProcessed = Prefix + "telemetry.records.processed";
    public const string TelemetryRecordsDropped = Prefix + "telemetry.records.dropped";

    // latency names: sdk + handler = total. 'decode' is the SDK-side
    public const string TelemetryDecodeDuration = Prefix + "telemetry.decode.duration";
    public const string TelemetrySdkDuration = Prefix + "telemetry.sdk.duration";
    public const string TelemetryHandlerDuration = Prefix + "telemetry.handler.duration";
    public const string TelemetryTotalDuration = Prefix + "telemetry.total.duration";
    public const string TelemetrySampleInterval = Prefix + "telemetry.sample.interval";

    public const string SessionInfoRecordsProcessed = Prefix + "session_info.records.processed";
    public const string SessionInfoProcessDuration = Prefix + "session_info.process.duration";
    public const string SessionInfoRecordsDropped = Prefix + "session_info.records.dropped";
    public const string SessionInfoSize = Prefix + "session_info.size";
    public const string SessionInfoParseAttempts = Prefix + "session_info.parse.attempts";

    public const string PlaybackLag = Prefix + "playback.lag";

    // attributes
    public const string DataSource = Prefix + "data_source";
    public const string DropReason = Prefix + "drop.reason";
    public const string SessionInfoStream = Prefix + "session_info.stream";
    public const string ErrorType = "error.type"; // opentelemetry semantic convention
}

internal static class MetricTags
{
    // set only when an operation fails, with the exception's full type name - per
    // opentelemetry error.type convention.
    public static KeyValuePair<string, object?> ErrorType(Exception ex) =>
        new(MetricNames.ErrorType, ex.GetType().FullName);
}

internal class TelemetryMeters
{
    // sentinel start value to denote "duration is not being collected"
    internal const long NotTiming = 0;

    // preallocated tags keep recording allocation-free
    static readonly KeyValuePair<string, object?> MissedTickTag = new(MetricNames.DropReason, "missed_tick");
    static readonly KeyValuePair<string, object?> ConsumerOverflowTag = new(MetricNames.DropReason, "consumer_overflow");

    // decoding one record: p50 2.2us, p99 7.4us, p99.9 16.5us, rare millisecond outliers
    // when the machine stalls. microsecond resolution up to 100us is where the signal is.
    static readonly InstrumentAdvice<double> DecodeAdvice = new()
    {
        HistogramBucketBoundaries = [0.000001, 0.0000025, 0.000005, 0.00001, 0.000025, 0.00005, 0.0001, 0.00025, 0.0005, 0.001, 0.005, 0.025]
    };

    // sdk and total latency share boundaries so the two can be compared bucket for bucket -
    // the difference between them is what the consumer's handler cost.
    //
    // 0.01667s is one frame at 60Hz: anything above it is older than the next record iRacing has already written.
    static readonly InstrumentAdvice<double> LatencyAdvice = new()
    {
        HistogramBucketBoundaries = [0.000002, 0.000005, 0.00001, 0.000025, 0.00005, 0.0001, 0.00025, 0.001, 0.0025, 0.01, 0.01667, 0.03333, 0.1, 0.5]
    };

    // the consumer's own code, need wide range as some users handlers take a while.
    //
    // 0.01667s matters most here: a handler slower than one frame cannot sustain 60Hz. Sustained
    // overload can cause consumer_overflow in async delivery or missed_tick drops in live synchronous delivery.
    static readonly InstrumentAdvice<double> HandlerAdvice = new()
    {
        HistogramBucketBoundaries = [0.000001, 0.0000025, 0.000005, 0.00001, 0.000025, 0.0001, 0.00025, 0.001, 0.0025, 0.01, 0.01667, 0.03333, 0.1, 1]
    };

    // live intervals cluster hard at one frame: in the fitted session 99.99% of records
    // arrived exactly one tick apart and 0.01% two ticks apart.
    //
    // the cluster is bracketed rather than split - (0.015, 0.0185] is "on time" - but with a
    // boundary on 0.01667 inside it, so early and late frames separate without losing the
    // single-bucket health check.
    //
    // (0.025, 0.04] brackets the two-frame cluster: exactly one missed frame. the buckets
    // below 0.005 are where max-speed ibt playback fits.
    static readonly InstrumentAdvice<double> IntervalAdvice = new()
    {
        HistogramBucketBoundaries = [0.00001, 0.0001, 0.001, 0.005, 0.0125, 0.015, 0.01667, 0.0185, 0.025, 0.04, 0.0667, 0.1, 0.25, 1]
    };

    // previous acquisition timestamp, for the sample interval. only touched by the read loop
    long _lastAcquiredTimestamp = NotTiming;

    public TelemetryMeters(Meter meter)
    {
        Processed = meter.CreateCounter<long>(
            MetricNames.TelemetryRecordsProcessed,
            unit: "{record}",
            description: "Number of telemetry records processed. Failed records carry the error.type attribute.");

        // missed_tick: iRacing wrote ticks the sdk never read (live only - the read loop fell
        // behind). consumer_overflow: records were read but evicted from the async delivery
        // buffer because the consumer's handler could not keep up
        Dropped = meter.CreateCounter<long>(
            MetricNames.TelemetryRecordsDropped,
            unit: "{record}",
            description: "Number of telemetry records lost before reaching the consumer.");

        Decode = meter.CreateHistogram<double>(
            MetricNames.TelemetryDecodeDuration,
            unit: "s",
            description: "SDK time decoding one telemetry record into a typed sample.",
            tags: null,
            advice: DecodeAdvice);

        // the pipeline instruments all measure from the same origin: the instant the data provider
        // reported new data, before decoding. iRacing does not timestamp its samples, so nothing
        // upstream of that is measurable - the missed_tick drop count is the proxy for it
        SdkLatency = meter.CreateHistogram<double>(
            MetricNames.TelemetrySdkDuration,
            unit: "s",
            description: "SDK time: acquiring a telemetry record until the consumer's handler received it.",
            tags: null,
            advice: LatencyAdvice);

        Handler = meter.CreateHistogram<double>(
            MetricNames.TelemetryHandlerDuration,
            unit: "s",
            description: "Consumer time: the handler's own work on one telemetry record.",
            tags: null,
            advice: HandlerAdvice);

        Total = meter.CreateHistogram<double>(
            MetricNames.TelemetryTotalDuration,
            unit: "s",
            description: "End to end: acquisition until the handler finished, including SDK and handler time.",
            tags: null,
            advice: LatencyAdvice);

        SampleInterval = meter.CreateHistogram<double>(
            MetricNames.TelemetrySampleInterval,
            unit: "s",
            description: "Elapsed-time gap between consecutive telemetry record acquisitions.",
            tags: null,
            advice: IntervalAdvice);
    }
    public Counter<long> Processed { get; private set; }
    public Counter<long> Dropped { get; private set; }
    public Histogram<double> Decode { get; private set; }
    public Histogram<double> SdkLatency { get; private set; }
    public Histogram<double> Handler { get; private set; }
    public Histogram<double> Total { get; private set; }
    public Histogram<double> SampleInterval { get; private set; }

    // this runs for every telemetry record, so only read the clock when a listener
    // is collecting the duration histogram. counter adds are near-free without a listener
    internal long StartTiming() => Decode.Enabled ? Stopwatch.GetTimestamp() : NotTiming;

    internal void RecordProcessed(long startTimestamp)
    {
        Processed.Add(1);

        if (startTimestamp != NotTiming)
            Decode.Record(Stopwatch.GetElapsedTime(startTimestamp).TotalSeconds);
    }

    internal void RecordFailed(long startTimestamp, Exception ex)
    {
        var errorType = MetricTags.ErrorType(ex);
        Processed.Add(1, errorType);

        if (startTimestamp != NotTiming)
            Decode.Record(Stopwatch.GetElapsedTime(startTimestamp).TotalSeconds, errorType);
    }

    internal void TicksMissed(long count) => Dropped.Add(count, MissedTickTag);
    internal void ConsumerOverflow(long count) => Dropped.Add(count, ConsumerOverflowTag);

    // like StartTiming, the pipeline clock is only read while a listener collects.
    // returns the acquisition timestamp every later pipeline measurement starts from
    internal long MarkAcquired()
    {
        if (!(SdkLatency.Enabled || Total.Enabled || SampleInterval.Enabled))
        {
            // forget the previous acquisition, so re-enabling can't report the idle gap as one interval
            _lastAcquiredTimestamp = NotTiming;
            return NotTiming;
        }

        var now = Stopwatch.GetTimestamp();
        if (_lastAcquiredTimestamp != NotTiming && SampleInterval.Enabled)
            SampleInterval.Record(Stopwatch.GetElapsedTime(_lastAcquiredTimestamp, now).TotalSeconds);
        _lastAcquiredTimestamp = now;

        return now;
    }

    // the consumer is about to receive the record. returns the timestamp the handler duration starts from
    internal long RecordHandoff(long acquiredTimestamp)
    {
        if (acquiredTimestamp != NotTiming && SdkLatency.Enabled)
            SdkLatency.Record(Stopwatch.GetElapsedTime(acquiredTimestamp).TotalSeconds);

        return Handler.Enabled ? Stopwatch.GetTimestamp() : NotTiming;
    }

    // the consumer has finished with the record
    internal void RecordConsumed(long acquiredTimestamp, long handlerStartTimestamp)
    {
        var total = acquiredTimestamp != NotTiming && Total.Enabled;
        if (!total && handlerStartTimestamp == NotTiming)
            return;

        var now = Stopwatch.GetTimestamp();
        if (handlerStartTimestamp != NotTiming)
            Handler.Record(Stopwatch.GetElapsedTime(handlerStartTimestamp, now).TotalSeconds);
        if (total)
            Total.Record(Stopwatch.GetElapsedTime(acquiredTimestamp, now).TotalSeconds);
    }
}

internal class SessionInfoMeters
{
    // parsing session info yaml takes milliseconds, occasionally longer for large sessions
    static readonly InstrumentAdvice<double> DurationAdvice = new()
    {
        HistogramBucketBoundaries = [0.001, 0.0025, 0.005, 0.01, 0.025, 0.05, 0.1, 0.25, 0.5, 1, 2.5, 5]
    };

    // session info yaml runs from a few kilobytes for a test session to hundreds for a full field
    static readonly InstrumentAdvice<long> SizeAdvice = new()
    {
        HistogramBucketBoundaries = [4_096, 16_384, 32_768, 65_536, 131_072, 262_144, 524_288, 1_048_576]
    };

    // one bucket per parse strategy - anything above 1 means the yaml needed repairing first
    static readonly InstrumentAdvice<long> AttemptsAdvice = new()
    {
        HistogramBucketBoundaries = [1, 2, 3]
    };

    static readonly KeyValuePair<string, object?> ParseBacklogTag = new(MetricNames.DropReason, "parse_backlog");
    static readonly KeyValuePair<string, object?> ConsumerOverflowTag = new(MetricNames.DropReason, "consumer_overflow");
    static readonly KeyValuePair<string, object?> SessionDataStreamTag = new(MetricNames.SessionInfoStream, "session_data");
    static readonly KeyValuePair<string, object?> SessionDataYamlStreamTag = new(MetricNames.SessionInfoStream, "session_data_yaml");

    public SessionInfoMeters(Meter meter)
    {
        Processed = meter.CreateCounter<long>(
            MetricNames.SessionInfoRecordsProcessed,
            unit: "{record}",
            description: "Number of session info records processed. Failed records carry the error.type attribute.");

        Duration = meter.CreateHistogram<double>(
            MetricNames.SessionInfoProcessDuration,
            unit: "s",
            description: "Time spent parsing each session info record.",
            tags: null,
            advice: DurationAdvice);

        // parse_backlog: updates arrived faster than they could be parsed and were evicted from the
        // internal queue unparsed. consumer_overflow: parsed updates were evicted from a public stream
        // because nobody read them in time - tagged with which stream
        Dropped = meter.CreateCounter<long>(
            MetricNames.SessionInfoRecordsDropped,
            unit: "{record}",
            description: "Number of session info records lost before reaching the consumer.");

        Size = meter.CreateHistogram<long>(
            MetricNames.SessionInfoSize,
            unit: "By",
            description: "UTF-8 encoded size of each session info record.",
            tags: null,
            advice: SizeAdvice);

        ParseAttempts = meter.CreateHistogram<long>(
            MetricNames.SessionInfoParseAttempts,
            unit: "{attempt}",
            description: "Parse attempts each successfully parsed session info record needed.",
            tags: null,
            advice: AttemptsAdvice);
    }
    public Counter<long> Processed { get; private set; }
    public Histogram<double> Duration { get; private set; }
    public Counter<long> Dropped { get; private set; }
    public Histogram<long> Size { get; private set; }
    public Histogram<long> ParseAttempts { get; private set; }

    // session info updates are infrequent, so the caller always measures elapsed time
    internal void RecordProcessed(TimeSpan elapsed, int sizeBytes, int parseAttempts)
    {
        Processed.Add(1);
        Duration.Record(elapsed.TotalSeconds);
        Size.Record(sizeBytes);
        ParseAttempts.Record(parseAttempts);
    }

    // a failure means every parse strategy was exhausted, so there is no attempt count to add
    internal void RecordFailed(TimeSpan elapsed, int sizeBytes, Exception ex)
    {
        var errorType = MetricTags.ErrorType(ex);
        Processed.Add(1, errorType);
        Duration.Record(elapsed.TotalSeconds, errorType);
        Size.Record(sizeBytes, errorType);
    }

    internal void ParseBacklogOverflow(long count) => Dropped.Add(count, ParseBacklogTag);
    internal void SessionDataOverflow(long count) => Dropped.Add(count, ConsumerOverflowTag, SessionDataStreamTag);
    internal void SessionDataYamlOverflow(long count) => Dropped.Add(count, ConsumerOverflowTag, SessionDataYamlStreamTag);
}

internal class PlaybackMeters
{
    // lag ranges from zero when on time to many seconds when playback falls far behind
    // measured over a 1x replay of an 8 minute live recording: p50 7.0ms, p90 13.2ms,
    // p99 14.7ms, p99.9 91.7ms, max 297.6ms.
    //
    // 0.01667s is the threshold that matters - a record read less than one frame behind
    // schedule is effectively on time. the old tail ran to 60s, which is well past the point
    // where a replay has stopped being a replay; 30s is kept only so a hang lands somewhere.
    static readonly InstrumentAdvice<double> LagAdvice = new()
    {
        HistogramBucketBoundaries = [0.0005, 0.001, 0.0025, 0.005, 0.01, 0.01667, 0.03333, 0.0667, 0.1, 0.25, 0.5, 1, 5, 30]
    };

    public PlaybackMeters(Meter meter)
    {
        // recorded for every record of a paced ibt playback, zero when on time. a healthy playback
        // sits near zero; a rising tail means the consumer or the machine cannot keep up with the
        // requested speed. max-speed playback has no schedule and records nothing
        Lag = meter.CreateHistogram<double>(
            MetricNames.PlaybackLag,
            unit: "s",
            description: "How far behind its scheduled time each paced IBT playback record was read.",
            tags: null,
            advice: LagAdvice);
    }
    public Histogram<double> Lag { get; private set; }

    internal void RecordLag(TimeSpan lag) => Lag.Record(lag.TotalSeconds);
}

internal interface IMetricsService : IAsyncDisposable
{
    TelemetryMeters Telemetry { get; }
    SessionInfoMeters SessionInfo { get; }
    PlaybackMeters Playback { get; }
}

internal class MetricsService : IMetricsService
{
    private readonly Meter _meter;
    public TelemetryMeters Telemetry { get; private set; }
    public SessionInfoMeters SessionInfo { get; private set; }
    public PlaybackMeters Playback { get; private set; }

    public MetricsService(IMeterFactory? meterFactory, string dataSource)
    {
        var tags = new List<KeyValuePair<string, object?>>
        {
            new(MetricNames.DataSource, dataSource)
        };

        // the version lets metrics backends tell instrumentation revisions apart
        var version = typeof(MetricsService).Assembly.GetName().Version?.ToString();

        _meter = meterFactory != null ?
            meterFactory.Create(TelemetryMetrics.MeterName, version, tags) :
            new Meter(TelemetryMetrics.MeterName, version, tags);

        Telemetry = new TelemetryMeters(_meter);
        SessionInfo = new SessionInfoMeters(_meter);
        Playback = new PlaybackMeters(_meter);
    }

    public ValueTask DisposeAsync()
    {
        _meter.Dispose();
        GC.SuppressFinalize(this);
        return ValueTask.CompletedTask;
    }
}
