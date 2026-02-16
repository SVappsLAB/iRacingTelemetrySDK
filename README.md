# iRacing Telemetry SDK for C# .NET

High-performance .NET SDK for accessing **live telemetry data** from iRacing simulator and **IBT file playback**. Features compile-time code generation for strongly-typed telemetry access with lock-free performance optimizations.

[![NuGet](https://img.shields.io/nuget/v/SVappsLAB.iRacingTelemetrySDK)](https://www.nuget.org/packages/SVappsLAB.iRacingTelemetrySDK)
[![License](https://img.shields.io/badge/license-Apache%202.0-blue.svg)](LICENSE)

Perfect for building **real-time dashboards**, **data analysis tools**, **race engineering applications**, and **telemetry visualizations**.

## Table of Contents

- [Features](#features)
- [Requirements](#requirements)
- [Quick Example](#quick-example)
- [Getting Started](#getting-started)
- [Understanding Telemetry Variables](#understanding-telemetry-variables)
- [Samples](#samples)
- [Documentation](#documentation)
- [AI-Assisted Development](#ai-assisted-development)
- [Performance and Design](#performance-and-design)
- [Performance Monitoring](#performance-monitoring)
- [License](#license)

## Features

- **Type Safety**: Enum-based telemetry variables with IntelliSense support and compile-time validation
- **High Performance**: Processes 600,000+ telemetry records/second with lock-free data streaming architecture
- **Background Processing**: Dedicated threads for telemetry collection and processing - your app's processing speed never blocks the streaming telemetry data
- **Live Telemetry**: Real-time access to 200+ variables including speed, RPM, tire data during iRacing sessions
- **IBT File Playback**: Analyze historical telemetry using the same API as live data
- **Modern Async API**: Async data streams with automatic backpressure handling
- **Built-in Metrics**: Integrated performance monitoring via System.Diagnostics.Metrics
- **Pause and Resume**: Control data flow while background processing continues

## Requirements

- **.NET 8.0+**
- **Windows** for live iRacing telemetry
- **Cross-platform** for IBT file playback

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

        var subscriptionTask = client.SubscribeToAllStreams(
            onTelemetryUpdate: async data =>
                Console.WriteLine($"Speed: {data.Speed}, RPM: {data.RPM}"),
            cancellationToken: cts.Token
        );

        var monitorTask = client.Monitor(cts.Token);

        await Task.WhenAny(monitorTask, subscriptionTask);
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

    A source generator will be leveraged to create a new .Net `TelemetryData` type you can use in your code.  For the attribute above, the created type will look like

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

    The async streaming API provides two consumption patterns:

    **Option A: Extension Method (Can be used for simple scenarios)**
    ```csharp
    // Subscribe to all data streams with async delegate methods for simplified consumption
    var subscriptionTask = tc.SubscribeToAllStreams(
        onTelemetryUpdate: OnTelemetryUpdate,
        onSessionInfoUpdate: OnSessionInfoUpdate,
        onConnectStateChanged: OnConnectStateChanged,
        onError: OnError,
        cancellationToken: cts.Token);

    // Async event handler methods
    Task OnTelemetryUpdate(TelemetryData data)
    {
        // Properties are nullable - handle accordingly
        var speed = data.Speed?.ToString("F1") ?? "N/A";
        var rpm = data.RPM?.ToString("F0") ?? "N/A";

        logger.LogInformation("Speed: {speed} mph, RPM: {rpm}", speed, rpm);
        return Task.CompletedTask;
    }

    Task OnSessionInfoUpdate(TelemetrySessionInfo session)
    {
        var driverCount = session.DriverInfo?.Drivers?.Count ?? 0;
        logger.LogInformation("Drivers in session: {count}", driverCount);
        return Task.CompletedTask;
    }
    ```

    **Option B: Direct Stream Access (For advanced scenarios)**
    ```csharp
    // Consume data streams directly for maximum performance and flexibility
    var telemetryTask = Task.Run(async () =>
    {
        await foreach (var data in tc.TelemetryData.WithCancellation(cts.Token))
        {
            // Handle nullable properties explicitly
            if (data.Speed.HasValue && data.RPM.HasValue)
            {
                logger.LogInformation("Speed: {speed:F1}, RPM: {rpm:F0}",
                    data.Speed.Value, data.RPM.Value);
            }
        }
    }, cts.Token);

    var sessionTask = Task.Run(async () =>
    {
        await foreach (var session in tc.SessionData.WithCancellation(cts.Token))
        {
            var trackName = session.WeekendInfo?.TrackName ?? "Unknown";
            logger.LogInformation("Track: {track}", trackName);
        }
    }, cts.Token);
    ```

1. Monitor for data changes

    The client uses multiple tasks (multi-threading) to monitor all iRacing data. Monitoring stops when the `CancellationToken` is cancelled (or when end-of-file is reached for IBT files).

    **For Extension Method approach:**
    ```csharp
    using var cts = new CancellationTokenSource();

    // cancel on Ctrl+C
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

    // Start both monitoring and subscription tasks concurrently
    var monitorTask = tc.Monitor(cts.Token);

    await Task.WhenAny(monitorTask, subscriptionTask);
    ```

    **For Direct Stream approach:**
    ```csharp
    using var cts = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

    // Start monitoring and all consumption tasks concurrently
    var monitorTask = tc.Monitor(cts.Token);

    await Task.WhenAny(monitorTask, telemetryTask, sessionTask);
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
// ✅ Null-conditional formatting
var speedDisplay = $"Speed: {data.Speed?.ToString("F1") ?? "N/A"}";

// ✅ Direct arithmetic (preserves null semantics)
var speedMph = data.Speed * 2.23694f; // Result is null if Speed is null

// ✅ Explicit null handling
var speed = data.Speed ?? 0f;
var hasValue = data.Speed.HasValue;

// ✅ Boolean checks
if (data.IsOnTrackCar == true) { /* ... */ }
```

## Samples

See [Samples Directory](./Samples/README.md) for ready-to-run example projects including:
- Basic telemetry monitoring
- IBT file analysis
- Data export utilities
- Track analysis tools

## Documentation

- **[Migration Guide](./MIGRATION_GUIDE.md)** - Upgrading from early pre-1.0 releases
- **[AI Agent Guide](./Sdk/SVappsLAB.iRacingTelemetrySDK/contents/.ai/AGENTS.md)** - Support for AI coding agents: SDK rules, patterns, and examples

## AI-Assisted Development

This package includes an **AI agent guide** that can be referenced in your project.

```
.ai/SVappsLAB.iRacingTelemetrySDK/AGENTS.md
```

Point your AI coding agent to this file for SDK-specific patterns, complete examples, and common pitfalls. For example, reference this file in your prompt, or add this to your project's `AGENTS.md`, `CLAUDE.md`, `.cursorrules`, so it's always available:

```
When working with iRacing telemetry, read the .ai/SVappsLAB.iRacingTelemetrySDK/AGENTS.md reference for SDK usage rules and examples.
```

## Performance and Design

The SDK is designed for high performance with zero data loss through async data streaming architecture.

### Data Streaming Benefits

**Data Safety:**
- **60-sample ring buffer** with FIFO (First-In-First-Out) and destructive-read semantics
- Provides **up to 1 second** of buffering at iRacing's 60Hz update rate
- When the buffer fills, **oldest unread samples are automatically dropped** to make room for new data
- **Drop-oldest strategy** ensures the SDK never blocks and prioritizes the most recent telemetry

**Performance:**
- **Lock-free operations** eliminate blocking and contention
- **Asynchronous processing** keeps your main thread responsive
- **~2x performance improvement** over traditional event-based approaches
- **600,000+ records/sec** processing capability for IBT files

**Flexibility:**
- **Two consumption patterns**: Simple extension methods or direct stream access
- **Independent streams**: Consume only the data types you need
- **Cancellation support**: Graceful shutdown with `CancellationToken`

### Architecture Overview

**Key Design Principles:**

- **Strongly Typed Telemetry Data**: Use strong type checking throughout
- **Non-blocking Streams**: All real-time data flows through asynchronous data streams
- **Separation of Concerns**: Telemetry data (high-frequency, time-critical) and Session Info processing (low frequency, CPU-intensive) run independently
- **Background Processing**: CPU-intensive YAML parsing runs on a separate thread to prevent any telemetry data drops.

```mermaid
graph TB
    subgraph "Data Sources"
        iRacing[iRacing Simulator<br/>60Hz Live Data]
        IBT[IBT File<br/>Historical Data]
    end

    subgraph "Task1 - Telemetry Data"
        MainTask[Telemetry Data Stream]
    end

    subgraph "Task2 - Session Info"
        SessionTask[Session Info Processing<br/>YAML Parsing]
    end

    subgraph "High Performance Data Streams"
        TelemetryStream[TelemetryData]
        SessionStream[SessionData]
        RawStream[SessionDataYaml]
    end

    iRacing --> MainTask
    IBT --> MainTask

    MainTask --> TelemetryStream
    MainTask --> SessionTask
    SessionTask --> SessionStream
    SessionTask --> RawStream

    classDef dataSource fill:#e1f5fe
    classDef processing fill:#f3e5f5
    classDef stream fill:#e8f5e8

    class iRacing,IBT dataSource
    class MainTask,SessionTask processing
    class TelemetryStream,SessionStream,RawStream stream
```


### Performance Monitoring

The SDK includes built-in support for System.Diagnostics.Metrics to help monitor performance and diagnose issues.

#### Available Metrics

The SDK exposes several metrics automatically:

- **telemetry_records_processed_total**: Counter of processed telemetry records
- **telemetry_records_dropped_total**: Counter of dropped records (when channels are full)
- **telemetry_processing_duration_microseconds**: Histogram of telemetry processing time
- **sessioninfo_records_processed_total**: Counter of session info updates processed
- **sessioninfo_processing_duration_milliseconds**: Histogram of session info processing time

#### Enabling Metrics with Dependency Injection

```csharp
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
                services.AddMetrics();  // Enable metrics support
                services.AddLogging(logging => logging.AddConsole());
            })
            .Build();

        var logger = host.Services.GetRequiredService<ILogger<Program>>();
        var meterFactory = host.Services.GetRequiredService<IMeterFactory>();

        // Create client with metrics support
        var clientOptions = new ClientOptions { MeterFactory = meterFactory };
        await using var client = TelemetryClient<TelemetryData>.Create(logger, null, clientOptions);

        // Your telemetry code here...
    }
}
```

#### Monitoring with dotnet-counters

Use any monitoring tool that supports System.Diagnostics.Metrics, like OpenTelemetry or the free Microsoft provided `dotnet-counters` tool to monitor SDK performance in real-time:

```bash
# Monitor all SDK metrics for a running application named "YourApp"
dotnet-counters monitor --name "YourApp" --counters SVappsLAB.iRacingTelemetrySDK

# Sample output:
# [SVappsLAB.iRacingTelemetrySDK]
#     telemetry_records_processed_total                    45,231
#     telemetry_records_dropped_total                           0
#     sessioninfo_records_processed_total                      12
```

This helps identify performance bottlenecks, monitor processing rates, and detect if records are being dropped due to slow consumption.

## License

This project is licensed under the Apache License 2.0. See [LICENSE](./LICENSE) file for details.
