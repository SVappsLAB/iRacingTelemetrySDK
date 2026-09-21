# Metrics Samples

Two samples that collect the SDK's built-in diagnostic metrics.

| Sample | Collected with | Extra packages |
|--------|----------------|----------------|
| [MeterListener](./MeterListener/) | `System.Diagnostics.Metrics.MeterListener` | none - it is built into .NET |
| [OpenTelemetry](./OpenTelemetry/) | OpenTelemetry `MeterProvider` + console exporter | `OpenTelemetry`, `OpenTelemetry.Exporter.Console` |

The two programs are deliberately identical apart from collection. Both request the same telemetry variables, and both read them the same way inside the handler - so the metrics they produce are directly comparable.

## Running

```bash
cd MeterListener        # or OpenTelemetry

dotnet run                             # live iRacing session
dotnet run <file.ibt>                  # replay an IBT file at max speed
dotnet run <file.ibt> 1                # replay at recorded speed, so playback.lag is recorded too
```

Ctrl-C ends a live run. Both samples report every 5 seconds and once more for the whole run.

To see the pipeline under stress, give the handler some work - `await Task.Delay(50)` - and watch `handler.duration` climb while `records.dropped` starts counting `missed_tick`.

## What is being collected

Neither sample creates instruments or passes anything to `TelemetryClient`. The SDK always publishes a meter named `SVappsLAB.iRacingTelemetrySDK` (the `TelemetryMetrics.MeterName` constant), and both samples subscribe to it *by name*. If nothing is listening, the SDK skips reading its clocks, so the instrumentation costs nothing when unused.

Thirteen instruments arrive, in three groups:

**Telemetry pipeline** - one record's journey from acquisition to your handler

| Instrument | Type | Measures |
|---|---|---|
| `telemetry.records.processed` | Counter | records that made it through |
| `telemetry.records.dropped` | Counter | records lost, tagged with `drop.reason` |
| `telemetry.decode.duration` | Histogram | turning the raw buffer into a typed sample |
| `telemetry.sdk.duration` | Histogram | acquisition until your handler was called |
| `telemetry.handler.duration` | Histogram | your handler's own work |
| `telemetry.total.duration` | Histogram | `sdk + handler`, end to end |
| `telemetry.sample.interval` | Histogram | gap *between* records - cadence, not latency |

**Session info** - the YAML block iRacing publishes alongside telemetry

| Instrument | Type | Measures |
|---|---|---|
| `session_info.records.processed` | Counter | session-info updates parsed |
| `session_info.records.dropped` | Counter | updates lost, tagged with `drop.reason` |
| `session_info.process.duration` | Histogram | time to parse one update |
| `session_info.size` | Histogram | UTF-8 size of one update |
| `session_info.parse.attempts` | Histogram | 1 if the YAML parsed as-is, 2 if it had to be repaired |

**Playback**

| Instrument | Type | Measures |
|---|---|---|
| `playback.lag` | Histogram | how far behind schedule a paced IBT record was read - only recorded when a playback speed is set |

All names carry the `svappslab.iracingsdk.` prefix. Measurements are tagged with `data_source` (`live` or `ibt`) on the meter, plus `drop.reason`, `error.type`, and `session_info.stream` per measurement where they apply.

See [Metrics and Diagnostics](../../docs/METRICS.md) for details.

## MeterListener vs. OpenTelemetry

The samples differ in one thing - **who aggregates**.

`MeterListener` sample hands you raw measurements, one callback per recorded value.  You must aggregate and calculate the stats yourself. 
OpenTelemetry sample aggregates into sums and bucketed histograms and hands the result to an exporter. Great for Grafana dashboards.

| | MeterListener | OpenTelemetry |
|---|---|---|
| Dependencies | none | the OpenTelemetry SDK + an exporter |
| Subscribe | `InstrumentPublished` + a callback per measurement type | `.AddMeter(TelemetryMetrics.MeterName)` |
| Aggregation | yours to write | built in |
| Histograms | whatever you compute - count/sum/max here. The SDK's bucket `advice` is **not** applied, so no percentiles | full bucket distributions using the boundaries the SDK advises, so p50/p95/p99 come out of the backend |
| Tags | a raw `ReadOnlySpan` you fold into a key yourself; the meter-level `data_source` tag is not included | handled as dimensions, with meter-level tags reported alongside |
| Output | whatever you print | any exporter - console here, or `.AddOtlpExporter()` to a collector, Prometheus, or Grafana |
| Cost | a few allocations and your own locking on the SDK's threads | a metrics pipeline and an export thread |


## Sample output

**MeterListener** - single stats sample, grabbed from console output:

```
--- final metrics ---
  session_info.parse.attempts              1 recorded   avg          1 {attempt}   max          1 {attempt}
  session_info.process.duration            1 recorded   avg   62.944 ms   max   62.944 ms
  session_info.records.processed           1 {record}
  session_info.size                        1 recorded   avg     26,463 By   max     26,463 By
  telemetry.decode.duration          123,936 recorded   avg      0.5 us   max    1.696 ms
  telemetry.handler.duration         123,936 recorded   avg      0.2 us   max    206.7 us
  telemetry.records.processed        123,936 {record}
  telemetry.sample.interval          123,935 recorded   avg      2.3 us   max    3.349 ms
  telemetry.sdk.duration             123,936 recorded   avg      0.8 us   max    2.536 ms
  telemetry.total.duration           123,936 recorded   avg      1.2 us   max    2.715 ms
```


## Related Documentation

- **[Metrics and Diagnostics](../../docs/METRICS.md)** - reference, attributes, bucket boundaries, interpretation
- **[Architecture and Design](../../docs/ARCHITECTURE.md)** - the threading model and buffering policy these metrics measure
- **[Samples](../README.md)** - the rest of the example projects
