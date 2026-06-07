# iRacing Telemetry SDK - Advanced SDK Reference
<!-- VERSION: 2.0.1 -->

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
```

Validate IBT file paths before creating `IBTOptions` when paths come from user input. The client constructor throws `FileNotFoundException` for missing files.

`playBackSpeedMultiplier: 1` runs IBT playback at real-time speed. The default, `int.MaxValue`, processes as fast as possible. Choose `1` for visualization-style apps and the default for batch analysis.

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
