# iRacing Telemetry SDK - Advanced SDK Reference
<!-- VERSION: 2.2.0 -->

This file contains advanced patterns for AI coding assistants building consumer applications. Read `docs/ai/SDK_USAGE.md` first.

## Project Setup

```xml
<PropertyGroup>
  <TargetFramework>net8.0</TargetFramework>
  <Nullable>enable</Nullable>
  <ImplicitUsings>enable</ImplicitUsings>
</PropertyGroup>
```

```xml
<PackageReference Include="SVappsLAB.iRacingTelemetrySDK" Version="2.x" />
<PackageReference Include="Microsoft.Extensions.Logging.Console" Version="8.0.0" />
```

## Client Creation

```csharp
// Live telemetry from iRacing.
await using var liveClient = TelemetryClient<TelemetryData>.Create(logger);

// IBT playback. Default speed is int.MaxValue, which processes as fast as possible.
var ibtOptions = new IBTOptions("file.ibt");
await using var ibtClient = TelemetryClient<TelemetryData>.Create(logger, ibtOptions);

// IBT playback at real-time speed.
var realtimeOptions = new IBTOptions("file.ibt", playBackSpeedMultiplier: 1);
await using var realtimeClient = TelemetryClient<TelemetryData>.Create(logger, realtimeOptions);

// Metrics support.
var clientOptions = new ClientOptions { MeterFactory = meterFactory };
await using var metricsClient = TelemetryClient<TelemetryData>.Create(logger, ibtOptions, clientOptions);

// Synchronous delivery - required to call GetValue(); it throws InvalidOperationException under
// the default Async mode. See "Telemetry Delivery Mode" below.
var syncOptions = new ClientOptions { DeliveryMode = TelemetryDeliveryMode.Synchronous };
await using var syncClient = TelemetryClient<TelemetryData>.Create(logger, ibtOptions, syncOptions);
```

Validate IBT file paths before creating `IBTOptions` when paths come from user input. The client constructor throws `FileNotFoundException` for missing files.

`playBackSpeedMultiplier: 1` runs IBT playback at real-time speed. The default, `int.MaxValue`, processes as fast as possible. Choose `1` for visualization-style apps and the default for batch analysis.

### Telemetry Delivery Mode

`ClientOptions.DeliveryMode` controls how samples move from the background read loop to `OnTelemetryUpdate`, and the SDK only operates in one mode at a time - you cannot create a client in one mode and read its data with the other's mechanism:

- **`TelemetryDeliveryMode.Async` (default).** Samples are pushed onto a 60-item drop-oldest channel and consumed by an independent task - high throughput, but production and consumption are decoupled. Because there'd be no way to guarantee it reflects the same record as the `T` sample the handler is currently processing, `GetValue()` is disallowed in this mode and throws `InvalidOperationException`.
- **`TelemetryDeliveryMode.Synchronous`.** Each sample is awaited directly by `OnTelemetryUpdate` from the same loop that reads it - the next record isn't read until the handler returns. This applies only to `OnTelemetryUpdate`; the session, raw-session, connect-state, and error handlers are still served from their bounded channels in this mode. `GetValue()` only works in this mode, and is guaranteed to match the delivered sample for the handler's full duration. Trade-off: processing/playback speed is bounded by handler speed, and the raw `TelemetryData` async-enumerable stream throws `InvalidOperationException` if accessed (there's nothing to queue - use the handler-based `Monitor` overload).

Choose `Synchronous` only when code needs `GetValue()` - whether alone or mixed with the strongly-typed `T` in the same handler; otherwise `Async` is the better default.

## Handler Callback Pattern

Use this for most applications.

```csharp
var handlers = new TelemetryHandlers<TelemetryData>
{
    OnTelemetryUpdate = data =>
    {
        ProcessTelemetry(data);
        return Task.CompletedTask;
    },
    OnSessionInfoUpdate = session =>
    {
        ProcessSession(session);
        return Task.CompletedTask;
    },
    OnRawSessionInfoUpdate = yaml =>
    {
        SaveYaml(yaml);
        return Task.CompletedTask;
    },
    OnConnectStateChanged = state =>
    {
        Console.WriteLine($"Connection: {state}");
        return Task.CompletedTask;
    },
    OnError = error =>
    {
        logger.LogError(error, "Telemetry SDK error");
        return Task.CompletedTask;
    }
};

await client.Monitor(handlers, cts.Token);
```

`Monitor(handlers, ct)` starts monitoring, consumes selected streams, and returns when monitoring ends. Cancelling the token makes `Monitor` return normally with the processed record count.

Handler exceptions fault the `Monitor(...)` call directly. SDK-side processing errors are delivered to `OnError` when supplied.

## Error Handling

`OnError` receives only SDK-side processing errors (for example a failed YAML parse or a read error). Exceptions thrown by your own handler code are NOT routed to `OnError` — they fault the `Monitor(...)` call directly, so bugs surface loudly instead of being silently swallowed. For recoverable per-item failures, wrap the work in a `try/catch` inside the handler.

Keep `OnError` itself simple: log the error and return. Do not throw from `OnError`.

## Direct Stream Pattern

Use this only when the application needs custom stream coordination.

```csharp
var telemetryTask = Task.Run(async () =>
{
    await foreach (var data in client.TelemetryData)
    {
        ProcessTelemetry(data);
    }
});

var sessionTask = Task.Run(async () =>
{
    await foreach (var session in client.SessionData)
    {
        ProcessSession(session);
    }
});

var monitorTask = client.Monitor(cts.Token);

await Task.WhenAll(monitorTask, telemetryTask, sessionTask);
```

Public streams complete automatically when `Monitor()` exits. Use `.WithCancellation(token)` only when a specific reader should stop before monitoring ends.

## Available Streams

| Stream | Type | Description |
| --- | --- | --- |
| `client.TelemetryData` | `IAsyncEnumerable<TelemetryData>` | 60 Hz telemetry samples |
| `client.SessionData` | `IAsyncEnumerable<TelemetrySessionInfo>` | Parsed session info |
| `client.SessionDataYaml` | `IAsyncEnumerable<string>` | Raw YAML session data |
| `client.ConnectStates` | `IAsyncEnumerable<ConnectState>` | `Connected` and `Disconnected` updates |
| `client.Errors` | `IAsyncEnumerable<Exception>` | SDK processing errors |

Streams are optimized for a single concurrent reader. If multiple consumers need the same data, fan out in application code.

Use `SessionData` for parsed access. Use `SessionDataYaml` only when the application needs the raw YAML string for custom parsing or storage.

## Client Status And Control

```csharp
bool connected = client.IsConnected;

client.Pause();
bool paused = client.IsPaused;
client.Resume();

IReadOnlyList<TelemetryVariable> vars = client.GetTelemetryVariables();
foreach (var variable in vars)
{
    Console.WriteLine($"{variable.Name}: {variable.Desc} ({variable.Units})");
}
```

`TelemetryVariable` includes `Name`, `Desc`, `Units`, `Type`, `Length`, and `IsTimeValue`.

`Pause()` suppresses stream writes while internal processing continues. `Resume()` restarts stream writes. Pause and resume are thread-safe and idempotent.

## Simulator Control (SimControl)

`client.SimControl` returns an `ISimController` for sending commands *to* the simulator. Commands are synchronous `void` calls (do not `await`), fire-and-forget with no acknowledgement, and are ignored when iRacing is not running. They work independently of `Monitor(...)` and connection state, but only affect a live Windows session — never IBT playback. Sending while disconnected logs a warning.

```csharp
using SVappsLAB.iRacingTelemetrySDK.SimControl;

var sim = client.SimControl;
```

| Group | Purpose | Representative methods |
| --- | --- | --- |
| `sim.Camera` | Camera focus and state | `SwitchToPosition(CameraFocus, group, camera)`, `SwitchToCar(carNumber, group, camera)`, `SetState(CameraState)` |
| `sim.Replay` | Replay playback and tape search | `SetPlaySpeed(speed, slowMotion)`, `SetPlayPosition(mode, frame)`, `Search(ReplaySearchMode)`, `SearchSessionTime(...)`, `EraseTape()` |
| `sim.Pit` | Pit service requests (in-car only) | `AddFuel(liters)`, `ChangeTire(TireLocation, pressureKPa)`, `ChangeTireCompound(index)`, `RequestFastRepair()`, `CleanWindshield()`, `ClearAll()`, cancel variants |
| `sim.Chat` | Chat window and macros | `Open()`, `Close()`, `SendMacro(n)`, `ReplyToPrivateChat()` |
| `sim.TelemetryRecording` | Disk (IBT) recording | `Start()`, `Stop()`, `Restart()` |
| `sim.VideoCapture` | Screenshot and video capture | `CaptureScreenshot()`, `Start()`, `Stop()`, `Toggle()`, `ShowTimer()`, `HideTimer()` |
| `sim.ForceFeedback` | FFB configuration | `SetMaxForce(maxForceNm)` |
| `sim.Textures` | Car texture reloading | `ReloadAll()`, `ReloadForCar(carIdx)` |

Notes for generated code:

- Pit commands only work while the player is in the car; replay/camera control while out of the car.
- Invalid arguments (negative fuel, out-of-range indexes) throw `ArgumentException`-family exceptions at the call site — validation is the only feedback mechanism.
- There is no query API: commands do not report simulator state. Read telemetry variables (for example `CamCarIdx`, `ReplayPlaySpeed`) to observe effects.

## Buffer Behavior

Streams use bounded buffers. At 60 Hz, the telemetry buffer holds about one second of samples. If consumers are slower than producers, oldest unread samples are dropped so the SDK does not block the telemetry source.

For production code, keep handlers fast and move expensive work to an application-owned queue or channel:

```csharp
using System.Threading.Channels;

var workQueue = Channel.CreateBounded<TelemetryData>(
    new BoundedChannelOptions(120)
    {
        FullMode = BoundedChannelFullMode.DropOldest,
        SingleReader = true,
        SingleWriter = true
    });

var workerTask = Task.Run(async () =>
{
    await foreach (var item in workQueue.Reader.ReadAllAsync())
    {
        await PerformExpensiveAnalysis(item);
    }
});

var handlers = new TelemetryHandlers<TelemetryData>
{
    OnTelemetryUpdate = data =>
    {
        workQueue.Writer.TryWrite(data);
        return Task.CompletedTask;
    }
};

await client.Monitor(handlers, cts.Token);

workQueue.Writer.TryComplete();
await workerTask;
```

For prototypes only, `_ = Task.Run(() => PerformExpensiveAnalysis(data));` can be acceptable. Do not make unbounded `Task.Run` fan-out the production default.

## Common Variable Categories

| Category | Variables |
| --- | --- |
| Vehicle | `Speed`, `RPM`, `Gear`, `Throttle`, `Brake`, `Clutch`, `SteeringWheelAngle` |
| Position | `LapDistPct`, `IsOnTrack`, `IsOnTrackCar`, `PlayerTrackSurface` |
| Timing | `SessionTime`, `LapCurrentLapTime`, `LapBestLapTime`, `LapLastLapTime` |
| Safety | `PlayerIncidents`, `EngineWarnings`, `SessionFlags` |
| Systems | `FuelLevel`, `WaterTemp`, `OilTemp`, `OilPress` |
| Environment | `AirTemp`, `TrackTemp`, `WindVel`, `TrackWetness` |
| Multi-car | `CarIdxLapDistPct`, `CarIdxPosition`, `CarIdxOnPitRoad` |

Use `client.GetTelemetryVariables()` to discover variables available in the current live session or IBT file.

## Source Generator Notes

- `[RequiredTelemetryVars]` targets classes only.
- It must use compile-time constant `TelemetryVar` enum values.
- Only declared variables become properties on generated `TelemetryData`.
- Generated properties are nullable.
- A clean build may be needed after changing the attribute.
- In Visual Studio, generated code is visible under Dependencies > Analyzers > SVappsLAB.iRacingTelemetrySDK.CodeGen.

## Dependency Injection

The SDK does not provide DI extension methods. Register the client manually:

```csharp
services.AddSingleton<ITelemetryClient<TelemetryData>>(provider =>
{
    var logger = provider.GetRequiredService<ILogger<Program>>();
    IBTOptions? ibtOptions = args.Length == 1 ? new IBTOptions(args[0]) : null;
    return TelemetryClient<TelemetryData>.Create(logger, ibtOptions);
});
```

If using metrics:

```csharp
services.AddMetrics();

services.AddSingleton<ITelemetryClient<TelemetryData>>(provider =>
{
    var logger = provider.GetRequiredService<ILogger<Program>>();
    var meterFactory = provider.GetRequiredService<IMeterFactory>();
    var options = new ClientOptions { MeterFactory = meterFactory };
    return TelemetryClient<TelemetryData>.Create(logger, null, options);
});
```

## Final Checklist For Generated Code

- [ ] `[RequiredTelemetryVars]` uses `TelemetryVar` enum values.
- [ ] Every referenced telemetry property is declared in `[RequiredTelemetryVars]`.
- [ ] `TelemetryData` is not manually defined.
- [ ] Client is created with `TelemetryClient<TelemetryData>.Create(...)`.
- [ ] Client is disposed with `await using`.
- [ ] Nullable telemetry properties are handled.
- [ ] Normal apps define `TelemetryHandlers<TelemetryData>` and pass it to `Monitor(handlers, ct)`.
- [ ] Direct streams have one reader per SDK stream.
- [ ] Telemetry handlers avoid blocking work.
- [ ] `GetTelemetryVariables()` is not awaited.
