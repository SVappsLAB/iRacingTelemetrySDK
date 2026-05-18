# iRacing Telemetry SDK Migration Guide

This guide covers upgrading to **v2.0**.

If you're on a pre-1.0 release, see [Migrating from pre-1.0](#migrating-from-pre-10) at the bottom first.

---

## v1.x → v2.0

v2.0 simplifies how applications consume telemetry. One `Monitor()` call now owns the full monitoring lifetime, and public streams close automatically when monitoring ends. Most v1.x apps will get smaller after the upgrade.

There are three changes to be aware of, plus one new shutdown contract.

### 1. `SubscribeToAllStreams` was removed

Use the new `Monitor(handlers, cancellationToken)` overload instead. It takes a `TelemetryHandlers<T>` container and runs the producer and handlers under a single coordinated lifetime.

**Before (v1.x):**
```csharp
using var cts = new CancellationTokenSource();

var subscriptionTask = client.SubscribeToAllStreams(
    onTelemetryUpdate: async data =>
        Console.WriteLine($"Speed: {data.Speed}, RPM: {data.RPM}"),
    cancellationToken: cts.Token);

var monitorTask = client.Monitor(cts.Token);

await Task.WhenAny(monitorTask, subscriptionTask);
```

**After (v2.0):**
```csharp
using var cts = new CancellationTokenSource();

var handlers = new TelemetryHandlers<TelemetryData>
{
    OnTelemetryUpdate = data =>
    {
        Console.WriteLine($"Speed: {data.Speed}, RPM: {data.RPM}");
        return Task.CompletedTask;
    }
};

await client.Monitor(handlers, cts.Token);
```

You no longer coordinate two tasks or two cancellation tokens. Handler exceptions fault the `Monitor(...)` call directly; SDK-side processing errors flow through the optional `OnError` handler if you provide one.

### 2. `Monitor(CancellationToken)` no longer throws on cancellation

Cancelling the token now causes `Monitor(ct)` to return normally with the record count instead of throwing `OperationCanceledException`. The same applies to the new `Monitor(handlers, ct)` overload.

**Before (v1.x):**
```csharp
try
{
    await client.Monitor(cts.Token);
}
catch (OperationCanceledException)
{
    // expected on shutdown
}
finally
{
    FlushBuffers();
}
```

**After (v2.0):**
```csharp
await client.Monitor(cts.Token);
FlushBuffers();
```

**Important:** If your old code had cleanup logic inside the `catch (OperationCanceledException)` block, move it into a `finally` block or to code that runs after `await client.Monitor(...)`. The catch block is dead code on the cancellation path now, and any logic placed there will silently stop running.

### 3. Public streams complete automatically when `Monitor()` exits

In v1.x, you had to thread a reader cancellation token through every `await foreach` so the loop would exit when monitoring stopped. In v2.0 the SDK completes its public streams (`TelemetryData`, `SessionData`, `Errors`, etc.) when `Monitor()` ends, so the foreach exits naturally.

**Before (v1.x):**
```csharp
var telemetryTask = Task.Run(async () =>
{
    await foreach (var data in client.TelemetryData.WithCancellation(cts.Token))
    {
        Process(data);
    }
}, cts.Token);

var monitorTask = client.Monitor(cts.Token);
await Task.WhenAny(monitorTask, telemetryTask);
```

**After (v2.0):**
```csharp
var telemetryTask = Task.Run(async () =>
{
    await foreach (var data in client.TelemetryData)
    {
        Process(data);
    }
});

var monitorTask = client.Monitor(cts.Token);
await Task.WhenAll(monitorTask, telemetryTask);
```

You can still pass `.WithCancellation(token)` if you want a single consumer to exit *before* monitoring stops. It is no longer needed for normal shutdown.

### Hung-handler shutdown contract (new)

If a callback registered via `Monitor(handlers, ct)` does not return within 5 seconds after monitoring ends, the call throws `TimeoutException`. Keep handlers fast — especially `OnTelemetryUpdate`, which can run at 60 Hz — and queue expensive work to your own background pipeline.

### Quick checklist

- [ ] Replace `SubscribeToAllStreams(...)` calls by defining `TelemetryHandlers<T>` and passing it to `Monitor(handlers, ct)`.
- [ ] Remove `try { ... } catch (OperationCanceledException) { ... }` wrappers around `Monitor(ct)`; move any cleanup into `finally` or post-await code.
- [ ] Drop `.WithCancellation(ct)` from normal `await foreach` loops on the client's streams.
- [ ] Audit handlers for callbacks that may take longer than 5 seconds.

See `Samples/MinimalExample/` for a complete v2.0 reference.

---

## Migrating from pre-1.0

Pre-1.0 releases used a different API shape (string variables, .NET events, sync disposal). The table below covers the essentials. After completing these, follow the [v1.x → v2.0](#v1x--v20) section above.

| Pre-1.0 | v1.x+ | What to change |
|---------|-------|----------------|
| `[RequiredTelemetryVars(["Speed"])]` | `[RequiredTelemetryVars([TelemetryVar.Speed])]` | Use the `TelemetryVar` enum instead of strings. |
| `client.OnTelemetryUpdate += ...` | `await foreach (var d in client.TelemetryData)` or `Monitor(handlers, ct)` | Replace .NET events with async streams or handler callbacks. |
| `using var client = ...` | `await using var client = ...` | The client is now `IAsyncDisposable`. |
| `using SVappsLAB.iRacingTelemetrySDK.Models;` | `using SVappsLAB.iRacingTelemetrySDK;` | Models moved into the main namespace. |
| `await client.GetTelemetryVariables()` | `client.GetTelemetryVariables()` | The method is synchronous now. |
| Direct value access (`data.Speed * 2`) | Nullable access (`data.Speed * 2` or `data.Speed ?? 0f`) | Properties are nullable; handle nulls explicitly. |
