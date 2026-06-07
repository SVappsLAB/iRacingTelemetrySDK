# Advanced Usage

Most applications should use the handler-callback `Monitor(handlers, ct)` pattern shown in the [README](../README.md). It consumes all the data streams for you and is the recommended approach for normal application code.

This document covers **direct stream access** for apps that need:

- multiple independent consumers of the same data,
- custom backpressure or coordination between consumers, or
- maximum IBT throughput.

## Direct Stream Access

Instead of passing handlers to `Monitor(...)`, you can start monitoring with just a `CancellationToken` and consume the public streams directly. Run `Monitor(cts.Token)` alongside one `await foreach` loop per stream, then await everything together:

```csharp
using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

// start monitoring; it feeds the public streams
var monitorTask = tc.Monitor(cts.Token);

var telemetryTask = Task.Run(async () =>
{
    await foreach (var data in tc.TelemetryData)
    {
        // handle nullable properties explicitly
        if (data.Speed.HasValue && data.RPM.HasValue)
        {
            logger.LogInformation("Speed: {speed:F1}, RPM: {rpm:F0}",
                data.Speed.Value, data.RPM.Value);
        }
    }
});

var sessionTask = Task.Run(async () =>
{
    await foreach (var session in tc.SessionData)
    {
        var trackName = session.WeekendInfo?.TrackName ?? "Unknown";
        logger.LogInformation("Track: {track}", trackName);
    }
});

// start monitoring and all consumption tasks concurrently
await Task.WhenAll(monitorTask, telemetryTask, sessionTask);
```

The public streams (`TelemetryData`, `SessionData`, `SessionDataYaml`, `ConnectStates`, `Errors`) complete automatically when `Monitor()` ends, so the `await foreach` loops finish on their own. You only need `.WithCancellation(token)` when ONE consumer should stop before monitoring ends.

Each stream is single-reader: use exactly one `await foreach` per stream. If multiple consumers need the same data, fan out in your application code (for example, read once and dispatch to several handlers, or use a channel/broadcast block).

## Cancellation behavior

`Monitor(ct)` (and `Monitor(handlers, ct)`) return normally with the processed record count when the token is cancelled — they do **not** throw `OperationCanceledException`. There is no need to wrap the call in a `try/catch` for `OperationCanceledException`.

## `OnError` vs. handler exceptions

> **`OnError` vs. handler exceptions:** `OnError` receives only *SDK-side* processing errors (for example a failed YAML parse or a read error). Exceptions thrown by your own handler code are **not** routed to `OnError` — they fault the `Monitor(...)` call directly so bugs surface loudly rather than being silently swallowed. If a handler has recoverable per-item failures, wrap that work in a `try/catch` inside the handler.

## More

For additional advanced stream, metrics, DI, and troubleshooting patterns, see the SDK reference for AI coding assistants: [`./ai/SDK_REFERENCE.md`](./ai/SDK_REFERENCE.md).
