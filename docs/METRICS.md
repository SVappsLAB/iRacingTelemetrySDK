# Metrics and Diagnostics

The SDK publishes runtime instrumentation through `System.Diagnostics.Metrics`. The meter is named `SVappsLAB.iRacingTelemetrySDK`, carries the SDK assembly version, and follows [OpenTelemetry metric naming conventions](https://opentelemetry.io/docs/specs/semconv/general/metrics/).

For the threading model and buffering policy these instruments measure, see [Architecture and Design](./ARCHITECTURE.md).

## Table of Contents

- [Available Metrics](#available-metrics)
- [Metric Attributes](#metric-attributes)
- [Telemetry Pipeline Latency](#telemetry-pipeline-latency)
  - [Latency versus cadence](#latency-versus-cadence)
- [OpenTelemetry](#opentelemetry)
- [Dependency Injection](#dependency-injection)
- [Monitoring with dotnet-counters](#monitoring-with-dotnet-counters)
- [Live-Session Example](#live-session-example)
  - [Baseline: a fast synchronous consumer](#baseline-a-fast-synchronous-consumer)
  - [When the consumer is too slow](#when-the-consumer-is-too-slow)

## Available Metrics

| Instrument | Type | Unit | Description |
|------------|------|------|-------------|
| `svappslab.iracingsdk.telemetry.records.processed` | Counter | `{record}` | Telemetry records processed |
| `svappslab.iracingsdk.telemetry.records.dropped` | Counter | `{record}` | Telemetry records lost before reaching the consumer |
| `svappslab.iracingsdk.telemetry.decode.duration` | Histogram | `s` | SDK time decoding one telemetry record into a typed sample |
| `svappslab.iracingsdk.telemetry.sdk.duration` | Histogram | `s` | SDK time from acquisition until the consumer's handler received the record |
| `svappslab.iracingsdk.telemetry.handler.duration` | Histogram | `s` | Consumer time: the handler's own work on one telemetry record |
| `svappslab.iracingsdk.telemetry.total.duration` | Histogram | `s` | End to end: Latency of one record, from acquisition until the handler finished; equals SDK plus handler time |
| `svappslab.iracingsdk.telemetry.sample.interval` | Histogram | `s` | Cadence between records: the previous record's acquisition until this one's. Not a latency - see [Latency versus cadence](#latency-versus-cadence) |
| `svappslab.iracingsdk.session_info.records.processed` | Counter | `{record}` | Session-info records processed |
| `svappslab.iracingsdk.session_info.process.duration` | Histogram | `s` | Time spent parsing each session-info record |
| `svappslab.iracingsdk.session_info.records.dropped` | Counter | `{record}` | Session-info records lost before reaching the consumer |
| `svappslab.iracingsdk.session_info.size` | Histogram | `By` | UTF-8 encoded size of each session-info record |
| `svappslab.iracingsdk.session_info.parse.attempts` | Histogram | `{attempt}` | Parse attempts required by each successfully parsed session-info record |
| `svappslab.iracingsdk.playback.lag` | Histogram | `s` | How far behind its scheduled time each paced IBT playback record was read |

## Metric Attributes

- `svappslab.iracingsdk.data_source`: `live` or `ibt`. Set on the meter, so it appears on every measurement.
- `error.type` (the two processed counters, `telemetry.decode.duration`, `session_info.process.duration`, and `session_info.size`): the exception's full type name. It is present only for failed records. The remaining histograms - `telemetry.sdk.duration`, `telemetry.handler.duration`, `telemetry.total.duration`, `telemetry.sample.interval`, `session_info.parse.attempts`, and `playback.lag` - never carry it.
- `svappslab.iracingsdk.drop.reason` (telemetry dropped counter):
  - `missed_tick`: live only. iRacing wrote ticks the SDK never read because its read loop fell behind, for example behind a slow synchronous handler.
  - `consumer_overflow`: async delivery only. Records were read but evicted from the 60-record delivery buffer because the `OnTelemetryUpdate` handler could not keep up.
- `svappslab.iracingsdk.drop.reason` (session-info dropped counter):
  - `parse_backlog`: updates arrived faster than they could be parsed and were evicted unparsed from the 10-record internal queue.
  - `consumer_overflow`: records were evicted from a 60-record public stream because nobody read them in time. Also carries `svappslab.iracingsdk.session_info.stream`: `session_data` (`SessionData` / `OnSessionInfoUpdate`) or `session_data_yaml` (`SessionDataYaml` / `OnRawSessionInfoUpdate`). Both streams are filled on every update, so a stream the application never reads starts counting drops after 60 updates; filter on the stream you consume.

`svappslab.iracingsdk.session_info.parse.attempts` is 1 when the YAML parsed as-is and 2 when it had to be repaired first. A parse that fails records no attempt count; it appears as `error.type` on the processed counter instead. A rising attempt count explains slower parse durations.

`svappslab.iracingsdk.playback.lag` is recorded only for IBT playback with a `playBackSpeedMultiplier` set. It is zero when a record is read on time. A rising tail means the consumer or machine cannot keep up with the requested speed. Max-speed playback has no schedule and records nothing.

Each histogram provides bucket boundaries suited to what it measures, so exporters report useful resolution without extra view configuration. The telemetry boundaries were fitted to a real 8-minute, 19,915-record live session replayed through the pipeline:

| Instrument | Measured (1x replay, async delivery) | Range covered |
|---|---|---|
| `telemetry.decode.duration` | p50 7.8 µs, p99 37 µs, max 1.7 ms | 1 µs to 25 ms |
| `telemetry.sdk.duration` | p50 23 µs, p99 80 µs, max 2.6 ms | 2 µs to 500 ms |
| `telemetry.handler.duration` | p50 1.3 µs, p99 5.1 µs, max 287 µs | 1 µs to 1 s |
| `telemetry.total.duration` | p50 26 µs, p99 85 µs, max 2.9 ms | 2 µs to 500 ms |
| `telemetry.sample.interval` | p50 15.5 ms, p99 31 ms | 10 µs to 1 s |
| `playback.lag` | p50 7.0 ms, p99 15 ms, max 298 ms | 500 µs to 30 s |

A later live run - the one in [Live-Session Example](#baseline-a-fast-synchronous-consumer) - stayed inside every one of these ranges, its widest outliers being a 4.879 ms `total.duration` and a 65.178 ms `sample.interval`.

`sdk.duration` and `total.duration` share boundaries so they can be compared bucket for bucket; their difference is the consumer's handler cost. The telemetry clocks are read only while a listener collects a histogram that needs them, so the hot path costs nothing extra when metrics are not collected.

## Telemetry Pipeline Latency

The pipeline instruments follow each record from acquisition to the consumer. Acquisition is when the live data-ready event fires or the next IBT record is read. iRacing does not timestamp its samples, so nothing before acquisition is measurable; the `missed_tick` drop count stands in for it.

```
within one record - latency

acquire ──► decode record ──► [ delivery buffer ] ──► handler start ──► handler end
   │         └ decode.duration ┘                        │                  │
   ├────────────── sdk.duration ───────────────────────►│                  │
   │                                                     ├ handler.duration ►
   └──────────────────────── total.duration ───────────────────────────────►

between records - cadence

acquire(n-1) ────── sample.interval ──────► acquire(n) ────── sample.interval ──────► acquire(n+1)
```

- **`sdk.duration`** — the SDK's share, everything before the consumer sees the record.
- **`handler.duration`** — the consumer's share.
- **`total.duration`** — the two together: `sdk + handler = total`.
- **`sample.interval`** — how often records arrive, measured from the previous record's acquisition to this one's.

`decode.duration` is the SDK-side detail inside `sdk.duration`: the cost of turning the raw buffer into a typed sample, with no queueing. `sdk.duration` minus `decode.duration` is delivery-buffer wait time.

### Latency versus cadence

`total.duration` and `sample.interval` both start from an acquisition, but measure different axes:

- **`total.duration` is a latency.** It lives entirely inside one record: that record's acquisition until the handler returned for that same record. It answers *how long did this record take to process*.
- **`sample.interval` is a cadence.** It spans two records and ignores what happened to either one. It answers *how often are records arriving*. A consumer whose handler does nothing at all still records the same intervals.

In a healthy live session the two differ by roughly three orders of magnitude - about 0.008 ms of work inside each 16.7 ms frame, [measured below](#baseline-a-fast-synchronous-consumer). That ratio, rather than either number on its own, is the processing headroom.

Reading them together isolates the slow stage:

| What you see | What it means |
|---|---|
| `total.duration` rises, `sample.interval` flat | Per-record work got slower, but records still arrive on schedule. Split it with `handler.duration` to see whether the consumer or the SDK is responsible. |
| `sample.interval` rises, `total.duration` flat | Records are arriving less often; nothing in the pipeline got slower. The source stalled, or ticks were missed - check `missed_tick` drops. |
| Both rise | Typically live synchronous delivery, where a slow handler blocks the read loop, so per-record latency drags the arrival gap out with it. |


The consumer's work is measured consistently on every delivery path:

| Delivery path | What `telemetry.handler.duration` measures |
|---------------|--------------------------------------------|
| `OnTelemetryUpdate`, `Async` mode | The callback, entry to exit |
| `await foreach` over `TelemetryData` | The caller's loop body, up to asking for the next record |
| `OnTelemetryUpdate`, `Synchronous` mode | The callback, entry to exit, including `GetValue()` calls inside it |

Interpret the values as follows:

- **Live**: `telemetry.sample.interval` should sit near 0.01667 s (60 Hz). A distribution split between one and two frames means ticks are being missed, which also appears as `missed_tick` drops. `telemetry.total.duration` says nothing about this; it stays flat while frames are being dropped.
- **Async delivery**: a rising `telemetry.sdk.duration` means records are waiting in the delivery buffer behind the consumer. If the consumer stays behind, `consumer_overflow` drops follow.
- **Synchronous delivery**: there is no buffer, so `sdk.duration` falls to decode cost plus the handoff itself - measured at roughly twice decode - and `total.duration` is roughly decode plus handler. A slow handler shows up as handler time and `missed_tick` drops rather than SDK time.
- **IBT at max speed** (the default `PlayBackSpeedMultiplier`): there is no schedule to keep, so `sample.interval` measures how fast the file is being consumed rather than a 60 Hz cadence; it and `sdk.duration` describe throughput, not real-time latency. At a paced speed both behave like live.
- **Async delivery with no consumer**: a client that neither sets `OnTelemetryUpdate` nor enumerates `TelemetryData` never records `sdk.duration`, `handler.duration`, or `total.duration`.

## OpenTelemetry

Subscribe to the SDK meter by name in the application's OpenTelemetry configuration:

```csharp
services.AddOpenTelemetry()
    .WithMetrics(metrics => metrics.AddMeter(TelemetryMetrics.MeterName));
```

Configure the exporter appropriate to the application separately. The SDK publishes metrics; it does not configure an OpenTelemetry exporter or collector.

The [OpenTelemetry sample](../Samples/Metrics/OpenTelemetry/) is a runnable version of this, using a console exporter so it needs no collector. Subscribing by meter name needs neither DI nor an `IMeterFactory`.

## Dependency Injection

Pass the application's `IMeterFactory` to the client so the SDK creates its meter through the DI container:

```csharp
using System.Diagnostics.Metrics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

[RequiredTelemetryVars([TelemetryVar.Speed, TelemetryVar.RPM])]
public class Program
{
    public static async Task Main(string[] args)
    {
        var host = Host.CreateDefaultBuilder(args)
            .ConfigureServices((context, services) =>
            {
                services.AddMetrics();
                services.AddLogging(logging => logging.AddConsole());
            })
            .Build();

        var logger = host.Services.GetRequiredService<ILogger<Program>>();
        var meterFactory = host.Services.GetRequiredService<IMeterFactory>();
        var clientOptions = new ClientOptions { MeterFactory = meterFactory };

        await using var client = TelemetryClient<TelemetryData>.Create(logger, null, clientOptions);
    }
}
```

## Monitoring with dotnet-counters

Any `System.Diagnostics.Metrics` consumer can collect these instruments. The [MeterListener sample](../Samples/Metrics/MeterListener/) does it in-process with a `MeterListener` and no extra packages. To attach to a running application:

```bash
dotnet-counters monitor --name "YourApp" --counters SVappsLAB.iRacingTelemetrySDK
```

To capture from application startup, including the initial session-info parse:

```bash
dotnet-counters collect --counters SVappsLAB.iRacingTelemetrySDK --format csv -o metrics.csv -- YourApp.exe
```

## Live-Session Example

### Baseline: a fast synchronous consumer

A report from the [MeterListener sample](../Samples/Metrics/MeterListener/) against a live iRacing session: synchronous delivery, and a handler that does two `GetValue()` calls. The run covered 12,704 records, about three and a half minutes.

```
--- metrics ---
  session_info.parse.attempts                                            26 recorded   avg          1 {attempt}   max          1 {attempt}
  session_info.process.duration                                          26 recorded   avg    9.283 ms   max   60.118 ms
  session_info.records.processed                                         26 {record}
  session_info.size                                                      26 recorded   avg     54,772 By   max     56,027 By
  telemetry.decode.duration                                          12,704 recorded   avg      3.2 us   max    1.380 ms
  telemetry.handler.duration                                         12,704 recorded   avg      0.8 us   max    253.5 us
  telemetry.records.processed                                        12,704 {record}
  telemetry.sample.interval                                          12,703 recorded   avg   16.680 ms   max   65.178 ms
  telemetry.sdk.duration                                             12,704 recorded   avg      6.2 us   max    2.362 ms
  telemetry.total.duration                                           12,704 recorded   avg      7.6 us   max    4.879 ms
```

- **Cadence**: `telemetry.sample.interval` averaged 16.680 ms - 59.95 Hz, iRacing's 60 Hz. Consecutive five-second reports each added exactly 300 records.
- **Headroom**: 7.6 µs of end-to-end work inside a 16.68 ms frame is about 0.05% of the frame; the rest is the consumer's to spend.
- **Where the time goes**: `sdk.duration` 6.2 µs, of which `decode.duration` is 3.2 µs, and the handler's two `GetValue()` calls 0.8 µs. Synchronous delivery has no buffer, so the SDK's remaining share is handoff rather than queueing. The 0.6 µs by which `sdk` plus `handler` falls short of `total` is the cost of recording `sdk.duration` itself, which lands between the two clock reads - the price of collecting, not of the pipeline.
- **Nothing was dropped**: neither `records.dropped` counter appears at all. A `MeterListener` prints only instruments that recorded something, so an absent row is a zero.
- **The outliers are the source, not the pipeline**: the widest `sample.interval` was 65.178 ms, about four frames, yet no `missed_tick` drops were counted. Missed ticks come from iRacing's own tick counter, so zero means the sim wrote nothing during that gap rather than the read loop falling behind.
- **Session info**: 26 updates, roughly one every eight seconds, averaging 53 KiB and 9.283 ms to parse. Parsing runs off the telemetry path, so even the 60.118 ms outlier costs the telemetry pipeline nothing. `parse.attempts` never exceeded 1, so no YAML needed repairing.

### When the consumer is too slow

The following one-second snapshots are from a live iRacing session, consumed three ways. Slow handlers wait 50 ms per record, so they can handle about 16 records per second against iRacing's 60 Hz.

| Consumer | `records.processed` | `records.dropped` | `drop.reason` | `telemetry.decode.duration` p50 / p95 / p99 |
|----------|---------------------|-------------------|---------------|------------------------------------------------|
| Async, fast handler | 61/s | 0/s | | 6.8 µs / 9.2 µs / 16.6 µs |
| Async, slow handler | 60/s | 44/s | `consumer_overflow` | 6.1 µs / 8.6 µs / 12.6 µs |
| Synchronous, slow handler | 15/s | 46/s | `missed_tick` | 8.0 µs / 22.1 µs / 22.1 µs |

- **Async, slow handler**: the SDK still reads every tick (60/s processed), but the handler falls behind and the 60-record buffer evicts the difference as `consumer_overflow`. Only the consumer is too slow.
- **Synchronous, slow handler**: the handler blocks the read loop, so processed falls to the handler's pace and unread ticks surface as `missed_tick`. Processed plus dropped still accounts for iRacing's 60 Hz.
- Creating a telemetry record takes single-digit microseconds either way, so record decoding is not the limiting stage.
- Each run parsed the session-info YAML once at startup, taking 71–73 ms.

Processing rates, latency distributions, and dropped-record counts identify whether the producer or a downstream consumer is the limiting stage.

## Related Documentation

- **[README](../README.md)** - Installation, quick start, and telemetry variables
- **[Architecture and Design](./ARCHITECTURE.md)** - Threading model, data streaming and buffering, and performance characteristics
- **[Advanced Usage](./ADVANCED.md)** - Direct stream access, multiple consumers, and cancellation behavior
- **[SDK reference for agents](./ai/SDK_REFERENCE.md)** - Advanced stream, DI, and troubleshooting patterns
