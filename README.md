# iRacing Telemetry SDK for C# .NET

High-performance .NET SDK for accessing **live telemetry data** from iRacing simulator and **IBT file playback**. Features compile-time code generation for strongly-typed telemetry access with lock-free performance optimizations.

[![NuGet](https://img.shields.io/nuget/v/SVappsLAB.iRacingTelemetrySDK)](https://www.nuget.org/packages/SVappsLAB.iRacingTelemetrySDK)
[![License](https://img.shields.io/badge/license-Apache%202.0-blue.svg)](LICENSE)

Perfect for building **real-time dashboards**, **data analysis tools**, **race engineering applications**, and **telemetry visualizations**.

## Table of Contents

- [Features](#features)
- [How It Compares](#how-it-compares)
- [Requirements](#requirements)
- [Quick Example](#quick-example)
- [Getting Started](#getting-started)
- [Understanding Telemetry Variables](#understanding-telemetry-variables)
- [Controlling the Simulator](#controlling-the-simulator)
- [Samples](#samples)
- [Advanced Usage](./docs/ADVANCED.md)
- [Architecture and Design](./docs/ARCHITECTURE.md)
- [Documentation](#documentation)
- [AI-Assisted Development](#ai-assisted-development)
- [Building from Source](#building-from-source)
- [License](#license)

## Features

- **Type Safety**: Enum-based telemetry variables with IntelliSense support and compile-time validation
- **High Performance**: Processes 600,000+ telemetry records/second with lock-free data streaming architecture
- **Background Processing**: Dedicated threads for telemetry collection and processing - your app's processing speed never blocks the streaming telemetry data
- **Live Telemetry**: Real-time access to 200+ variables including speed, RPM, tire data during iRacing sessions
- **IBT File Playback**: Analyze historical telemetry using the same API as live data
- **Modern Async API**: Async data streams with bounded buffering and automatic overload handling
- **Dynamic Variable Lookup**: Look up any telemetry variable by name at runtime via `GetValue(string)` - useful when the variable set isn't known at compile time
- **Built-in Metrics**: Integrated performance monitoring via System.Diagnostics.Metrics
- **Pause and Resume**: Control data flow while background processing continues
- **Sim Control**: Remotely control the simulator (pit commands, replay, cameras, chat, and more)

## How It Compares

There are several .NET libraries for reading iRacing telemetry.
Most are thin wrappers over the iRacing shared-memory layout: variables are done by string name at runtime, receive data through events on the caller's thread.

This SDK takes a different approach. It treats telemetry as a **typed, high-throughput data stream** rather than a bag of named values.

| | **iRacingTelemetrySDK** | **Typical .NET iRacing libraries** |
|---|---|---|
| **Variable access** | Compile-time generated `TelemetryData` struct — only the variables you declare | Runtime lookup by string name or dictionary indexing |
| **Type safety** | Enum-based selection, validated at build time, full IntelliSense | Strings resolved at runtime; typos surface as runtime errors or nulls |
| **API model** | Async data streams with `async`/`await`, bounded buffering, and automatic overload handling | Blocking event handlers or manual polling loops |
| **Threading** | Dedicated background tasks for collection and YAML parsing — your handler never blocks the data source | Callbacks commonly block further frame processing until they return |
| **Backpressure** | Bounded 60-sample ring buffer with drop-oldest; slow consumers cost bounded data, never memory | Not supported |
| **Live + IBT parity** | Identical strongly-typed API for both | Frequently separate code paths, or live-only |
| **Throughput** | 600,000+ records/sec on IBT playback | Unknown |
| **Observability** | Built-in `System.Diagnostics.Metrics` counters and histograms | Not supported |
| **Simulator control** | Pit, replay, camera, chat, and broadcast commands included | Varies |
| **AI agent support** | Dedicated agent-facing usage and reference docs | Rare |


**Why it's fast:**

- **Source-generated structs** eliminate runtime reflection and string lookups from the hot path — only the variables you asked for are ever decoded
- **Lock-free bounded streams** keep telemetry current under load instead of queueing unboundedly behind a slow consumer
- **Minimal-allocation reads** — each sample is copied once into a reused buffer (guarding against iRacing overwriting it mid-read), then decoded field-by-field via `ReadOnlySpan<T>` with no further allocations, keeping steady-state GC pressure near zero
- **Independent background tasks** mean CPU-intensive session-info YAML parsing never stalls the 60Hz telemetry path

Full details, including the threading model, buffering semantics, and built-in metrics: **[Architecture and Design](./docs/ARCHITECTURE.md)**

## Requirements

- **.NET 8.0+**

## Quick Example

```csharp
using Microsoft.Extensions.Logging;
using SVappsLAB.iRacingTelemetrySDK;

[RequiredTelemetryVars([TelemetryVar.Speed, TelemetryVar.RPM])]

public class Program
{
    public static async Task Main()
    {
        var logger = LoggerFactory.Create(b => b.AddConsole()).CreateLogger("App");
        await using var client = TelemetryClient<TelemetryData>.Create(logger);

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
    }
}
```

## Getting Started

To incorporate **iRacingTelemetrySDK** into your projects, follow these steps:

1. **Install the Package:** Add the **iRacingTelemetrySDK** NuGet package to your project using your preferred package manager.

    ```
    dotnet add package SVappsLAB.iRacingTelemetrySDK
    ```

1. Add the **RequiredTelemetryVars** attribute to the main class of your project

    The attribute takes an array of TelemetryVar enum values. These enum values identify the iRacing telemetry variables you want to use in your program.

    ```csharp
    // these are the telemetry variables we want to track
    [RequiredTelemetryVars([TelemetryVar.IsOnTrackCar, TelemetryVar.RPM, TelemetryVar.Speed, TelemetryVar.PlayerTrackSurface])]

    internal class Program
    {
      ...
    }
    ```

    A source generator will be leveraged to create a new .NET `TelemetryData` type you can use in your code.  For the attribute above, the created type will look like

    ```csharp
    public record struct TelemetryData
    {
        public bool? IsOnTrackCar { get; init; }
        public float? RPM { get; init; }
        public float? Speed { get; init; }
        public TrackLocation? PlayerTrackSurface { get; init; }
    }
    ```
1. Create an instance of the TelemetryClient

    The TelemetryClient implements `IAsyncDisposable` and should be used with `await using` for proper resource cleanup.

    The TelemetryClient runs in one of two modes: Live or IBT file playback.

    **For live telemetry**, you only need to provide a logger:

    ```csharp
    // Live telemetry from iRacing
    await using var tc = TelemetryClient<TelemetryData>.Create(logger);
    ```

    **For IBT playback**, provide the path to the IBT file and an optional playback speed multiplier:

    ```csharp
    // Process IBT file at 10x speed
    var ibtOptions = new IBTOptions(@"C:\path\to\file.ibt", 10);
    await using var tc = TelemetryClient<TelemetryData>.Create(logger, ibtOptions);

    // Maximum speed processing (the default)
    var fastOptions = new IBTOptions(@"C:\path\to\file.ibt", int.MaxValue);
    await using var fastTc = TelemetryClient<TelemetryData>.Create(logger, fastOptions);
    ```

    **Speed multiplier values:**
    - `1` = Normal speed (60 records/sec)
    - `20` = 20x speed (1,200 records/sec)
    - `int.MaxValue` = Maximum speed processing

1. Subscribe to data streams

    Pass a `TelemetryHandlers<T>` to `Monitor(...)`; it consumes the streams for you and returns when monitoring ends:

    ```csharp
    var handlers = new TelemetryHandlers<TelemetryData>
    {
        OnTelemetryUpdate = data =>
        {
            // Properties are nullable - handle accordingly
            var speed = data.Speed?.ToString("F1") ?? "N/A";
            var rpm = data.RPM?.ToString("F0") ?? "N/A";

            logger.LogInformation("Speed: {speed} mph, RPM: {rpm}", speed, rpm);
            return Task.CompletedTask;
        },
        OnSessionInfoUpdate = session =>
        {
            var driverCount = session.DriverInfo?.Drivers?.Count ?? 0;
            logger.LogInformation("Drivers in session: {count}", driverCount);
            return Task.CompletedTask;
        },
        OnConnectStateChanged = state =>
        {
            logger.LogInformation("Connection: {state}", state);
            return Task.CompletedTask;
        },
        OnError = error =>
        {
            logger.LogError(error, "Telemetry error");
            return Task.CompletedTask;
        }
    };

    await tc.Monitor(handlers, cts.Token);
    ```

    > **`OnError` vs. handler exceptions:** `OnError` receives only *SDK-side* processing errors (for example a failed YAML parse or a read error). Exceptions thrown by your own handler code are **not** routed to `OnError` — they fault the `Monitor(...)` call directly so bugs surface loudly rather than being silently swallowed. If a handler has recoverable per-item failures, wrap that work in a `try/catch` inside the handler.

    Need multiple independent consumers, custom backpressure, or maximum IBT throughput? See **[Advanced usage](./docs/ADVANCED.md)**.

1. Monitor for data changes

    The client uses multiple tasks (multi-threading) to monitor all iRacing data. Monitoring stops when the `CancellationToken` is cancelled (or when end-of-file is reached for IBT files).

    ```csharp
    using var cts = new CancellationTokenSource();

    // cancel on Ctrl+C
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

    await tc.Monitor(handlers, cts.Token);
    ```

## Understanding Telemetry Variables

### Telemetry Variables

The iRacing simulator generates extensive telemetry data. This SDK lets you select which telemetry data you want to track and generates a strongly-typed struct with named variables you can access directly in your project.

#### Availability

iRacing outputs different variables depending on the context. Some variables available in live sessions might not be available in offline IBT files, and vice versa.

To check variable availability, use the [./Samples/DumpVariables_DumpSessionInfo](https://github.com/SVappsLAB/iRacingTelemetrySDK/tree/main/Samples/DumpVariables_DumpSessionInfo) utility. This will generate a CSV file listing available variables and a YAML file with complete session info.

Once you know what variables are available and you have the list of which ones you want to use, you're ready to start using the SDK.

#### Nullable Properties

All telemetry properties are nullable (`float?`, `int?`, `bool?`) to accurately represent iRacing's variable availability model. Some variables are only available in certain sessions or contexts.

**Recommended patterns for handling null values:**

```csharp
// Null-conditional formatting
var speedDisplay = $"Speed: {data.Speed?.ToString("F1") ?? "N/A"}";

// Direct arithmetic (preserves null semantics)
var speedMph = data.Speed * 2.23694f; // Result is null if Speed is null

// Explicit null handling
var speed = data.Speed ?? 0f;
var hasValue = data.Speed.HasValue;

// Boolean checks
if (data.IsOnTrackCar == true) { /* ... */ }
```

### Dynamic Variable Lookup

For scenarios where the telemetry variables you need aren't known until runtime, like a dashboard that lets end users pick which values to display, or an integration (such as a game engine) that prefers a string-keyed lookup over a compile-time generated struct - use `GetValue(string)` instead of (or alongside) the strongly-typed `T`:

```csharp
// no RequiredTelemetryVars needed - use the DynamicTelemetryData placeholder
// GetValue requires Synchronous delivery mode - see below
var clientOptions = new ClientOptions { DeliveryMode = TelemetryDeliveryMode.Synchronous };
await using var client = TelemetryClient<DynamicTelemetryData>.Create(logger, ibtOptions: null, clientOptions);

var handlers = new TelemetryHandlers<DynamicTelemetryData>
{
    OnTelemetryUpdate = _ =>
    {
        if (client.GetValue("Speed") is float speed)
        {
            Console.WriteLine($"Speed: {speed}");
        }
        return Task.CompletedTask;
    }
};

await client.Monitor(handlers, cts.Token);
```

`GetValue` matches names case-insensitively and works for any variable reported by `GetTelemetryVariables()`. It returns an 'object' or a  'null' if the variable is unknown.

The SDK only works in one delivery mode at a time, you can't create the client in one mode and read data in the other. `GetValue` is only usable when the client is created with `new ClientOptions { DeliveryMode = TelemetryDeliveryMode.Synchronous }`.

Note: in `Synchronous` mode, each sample is blocking and awaited directly by your handler before the next record is read.

## Controlling the Simulator

In addition to reading telemetry, the SDK can send commands *to* the simulator. Commands are grouped by feature
area and are fire-and-forget — they return immediately and are ignored if iRacing is not
running. Commands work independently of monitoring and connection state, though sending while
iRacing is not connected logs a warning since the command will likely have no effect.

```csharp
using SVappsLAB.iRacingTelemetrySDK.SimControl;

// get the sim controller from your telemetry client
var sim = client.SimControl;

// pit service (only while in the car)
sim.Pit.AddFuel(30);
sim.Pit.ChangeTire(TireLocation.LeftFront);
sim.Pit.RequestFastRepair();

// replay control (only while out of the car)
sim.Replay.Search(ReplaySearchMode.NextIncident);
sim.Replay.SetPlaySpeed(2);

// cameras
sim.Camera.SwitchToPosition(CameraFocus.AtLeader, cameraGroup: 1, camera: 1);
sim.Camera.SwitchToCar("001", cameraGroup: 1, camera: 1);
```

## Samples

See [Samples Directory](./Samples/README.md) for ready-to-run example projects including:
- Basic telemetry monitoring
- IBT file analysis
- Simulator control, including cameras, replay, pit service, and broadcast commands
- Data export utilities
- Track analysis tools

## Documentation

- **[Architecture and Design](./docs/ARCHITECTURE.md)** - Threading model, data streaming and buffering, performance characteristics, and built-in metrics
- **[Advanced Usage](./docs/ADVANCED.md)** - Direct stream access, multiple consumers, and cancellation behavior
- **[Migration Guide](./docs/MIGRATION_GUIDE.md)** - Upgrading from early pre-1.0 releases

## AI-Assisted Development

To use this repo as part of your own consuming application, point your AI coding agent to these repository docs:

- **[SDK usage guide for agents](./docs/ai/SDK_USAGE.md)** - Recommended usage for consumer applications.
- **[Advanced SDK reference for agents](./docs/ai/SDK_REFERENCE.md)** - Advanced stream, metrics, DI, and troubleshooting patterns.

To have your agent use them automatically, add a line like this to your project's `AGENTS.md`, `CLAUDE.md`, or `.cursorrules` (your agent needs the ability to fetch URLs):

```
When working with the iRacing Telemetry SDK, read
https://raw.githubusercontent.com/SVappsLAB/iRacingTelemetrySDK/main/docs/ai/SDK_USAGE.md first,
and https://raw.githubusercontent.com/SVappsLAB/iRacingTelemetrySDK/main/docs/ai/SDK_REFERENCE.md for advanced patterns.
```

## Building from Source

If you've cloned or forked the repository, you can build, test, and package the SDK with the standard .NET CLI from the repository root.

```bash
# build the SDK solution
dotnet build .\Sdk\SVappsLAB.iRacingTelemetrySDK.slnx

# build the sample solution
dotnet build .\Samples\Samples.slnx

# create a NuGet package
dotnet pack .\Sdk\SVappsLAB.iRacingTelemetrySDK\SVappsLAB.iRacingTelemetrySDK.csproj
```

### Running tests

Tests are split into categories so you can run subsets based on your environment. Live tests require iRacing to be running on Windows.

```bash
# unit tests only
dotnet test --project .\Sdk\tests\UnitTests\UnitTests.csproj

# repeatable offline smoke tests using bundled IBT files
dotnet run --project .\Sdk\tests\SmokeTests\SmokeTests.csproj -- --filter-trait Category=ibt

# live smoke tests, requires an active iRacing session
dotnet run --project .\Sdk\tests\SmokeTests\SmokeTests.csproj -- --filter-trait Category=live

# all test projects, including tests that may require live/manual setup
dotnet test --solution .\Sdk\SVappsLAB.iRacingTelemetrySDK.slnx
```
See [Sdk/tests/README.md](./Sdk/tests/README.md) for manual test commands and filtering notes.

### Running the samples

```bash
# live iRacing data
dotnet run --project .\Samples\MinimalExampleAsync\MinimalExampleAsync.csproj

# IBT file playback
dotnet run --project .\Samples\MinimalExampleAsync\MinimalExampleAsync.csproj -- path\to\file.ibt
```

See the [Samples](./Samples/README.md) directory for the individual example projects.

## License

This project is licensed under the Apache License 2.0. See [LICENSE](./LICENSE) file for details.
