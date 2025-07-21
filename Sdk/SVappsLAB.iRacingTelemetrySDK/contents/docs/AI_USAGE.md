# iRacing Telemetry SDK - AI Context & Implementation Guide

> **AI Assistant Instructions**: This document provides comprehensive guidance for AI coding assistants to understand and implement applications using the iRacing Telemetry SDK. The SDK uses source code generation and requires specific patterns for proper operation.

## SDK Overview

The **ITelemetryClient<T>** is the core interface of the iRacing Telemetry SDK that provides high-performance access to iRacing simulator telemetry data using an **async data streaming architecture**. It supports both live telemetry streaming from active iRacing sessions and playback of IBT (iRacing Binary Telemetry) files with strongly-typed data structures generated at compile time.

## Critical Requirements for AI Tools

⚠️ **Essential Constraints**:
- **Async Data Streaming**: Uses `System.Threading.Channels` internally for high-performance, lock-free data streaming
- **Source Generation Dependency**: The `[RequiredTelemetryVars]` attribute triggers compile-time code generation. The `TelemetryData` struct is NOT manually created.
- **Enum-Based Variable Identification**: Use `TelemetryVar` enum values instead of strings to identify telemetry variables (v1.0+ only)
- **Nullable Properties**: Generated `TelemetryData` struct has nullable properties (`float?`, `int?`, `bool?`) - handle appropriately
- **Target Framework**: .NET 8.0+ required
- **Package Dependencies**: `Microsoft.Extensions.Logging`, `Microsoft.Extensions.Hosting`, and dependency injection support recommended
- **Windows Dependency**: Live telemetry requires Windows (iRacing memory-mapped files). IBT playback works cross-platform.
- **Interface-Based**: Use `ITelemetryClient<T>` interface, not concrete `TelemetryClient<T>` class directly

## Project Setup

### Required NuGet Packages
```xml
<PackageReference Include="SVappsLAB.iRacingTelemetrySDK" Version="latest" />
<PackageReference Include="Microsoft.Extensions.Logging" Version="8.0.0" />
<PackageReference Include="Microsoft.Extensions.Logging.Console" Version="8.0.0" />
<PackageReference Include="Microsoft.Extensions.Hosting" Version="8.0.0" />
<PackageReference Include="Microsoft.Extensions.DependencyInjection" Version="8.0.0" />
```

### Project File Requirements
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
</Project>
```

## Key Features

- **Async Data Streaming**: Uses `System.Threading.Channels` internally for high-performance, lock-free data streaming
- **Generic Type Safety**: Uses source code generation to create strongly-typed telemetry data structures
- **Dual Data Sources**: Works with live iRacing sessions or IBT file playback
- **High Performance**: Optimized with `ref struct`, `ReadOnlySpan<T>`, and unsafe code for zero-allocation processing
- **Multiple Stream Types**: Separate channels for telemetry data, session info, connection state, and errors
- **Asynchronous Operations**: Non-blocking operations throughout using async/await patterns with `IAsyncEnumerable<T>`

## 🚨 Critical: Nullable Properties Handling

### Why Properties Are Nullable

**All telemetry properties are nullable** (`float?`, `int?`, `bool?`) because iRacing variables have dynamic availability:
- Some variables only exist in live sessions (not in IBT files)
- Some variables only exist in specific car types or session types
- Variables may be unavailable during certain racing conditions

### AI Agent Guidelines

**✅ ALWAYS use these patterns:**

```csharp
// ✅ Null-conditional operator with fallback
var speedDisplay = $"Speed: {data.Speed?.ToString("F1") ?? "N/A"}";

// ✅ Direct arithmetic (preserves null semantics)
var speedMph = data.Speed * 2.23694f; // Result is null if Speed is null

// ✅ Explicit null handling
var speed = data.Speed ?? 0f;
var speed = data.Speed.GetValueOrDefault();
var hasValue = data.Speed.HasValue;

// ✅ Boolean checks (essential for bool? properties)
if (data.IsOnTrackCar == true) { /* handle when explicitly true */ }
if (data.IsOnTrackCar.GetValueOrDefault()) { /* handle when true or null as false */ }

// ✅ Safe conditional checks
if (data.Speed.HasValue && data.Speed.Value > 100) { /* process */ }
```

**❌ NEVER use these patterns:**

```csharp
// ❌ Direct .Value access (throws if null)
var speed = data.Speed.Value; // NullReferenceException if Speed is null

// ❌ Implicit bool conversion (compilation error)
if (data.IsOnTrackCar) { } // Cannot convert bool? to bool

// ❌ Direct math without null handling
var calculation = data.Speed + data.RPM; // May be null unexpectedly
```

### Common Nullable Scenarios

```csharp
// ✅ Speed/Distance calculations
var speedKph = data.Speed.HasValue ? data.Speed.Value * 3.6f : (float?)null;
var speedMph = data.Speed * 2.23694f; // Preserves null

// ✅ Boolean flag handling
var onTrack = data.IsOnTrackCar == true;
var hasIncidents = data.PlayerIncidents > 0;

// ✅ Array/enum access
var gearText = data.Gear?.ToString() ?? "N";
var surfaceType = data.PlayerTrackSurface?.ToString() ?? "Unknown";

// ✅ Display formatting
Console.WriteLine($"Speed: {data.Speed?.ToString("F1") ?? "---"} mph");
Console.WriteLine($"Gear: {data.Gear?.ToString() ?? "N"}");
Console.WriteLine($"RPM: {data.RPM?.ToString("F0") ?? "----"}");
```

## Implementation Patterns

The SDK offers **two main approaches** for consuming telemetry data:

1. **🟢 SIMPLE APPROACH**: Use subscription extension methods for event-like patterns
2. **🔴 ADVANCED APPROACH**: Use direct stream consumption for maximum performance and control

---

## 🟢 SIMPLE APPROACH: Subscription Extensions (Recommended)

### Quick Start Pattern

Use the `SubscribeToAllStreams` extension method for the easiest migration from event-based code:
```csharp
[RequiredTelemetryVars([TelemetryVar.Speed, TelemetryVar.RPM, TelemetryVar.Gear])]
public class Program
{
    public static async Task Main(string[] args)
    {
        var logger = LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger("App");
        await using var client = TelemetryClient<TelemetryData>.Create(logger);
        using var cts = new CancellationTokenSource();

        // Use extension method for simplified consumption with async callbacks
        var subscriptionTask = client.SubscribeToAllStreams(
            onTelemetryUpdate: async data => Console.WriteLine($"Speed: {data.Speed}, RPM: {data.RPM}, Gear: {data.Gear}"),
            onSessionInfoUpdate: async session => Console.WriteLine($"Track: {session.WeekendInfo.TrackDisplayName}"),
            onConnectStateChanged: async state => Console.WriteLine($"Connection: {state}"),
            onError: async error => Console.WriteLine($"Error: {error.Message}"),
            cancellationToken: cts.Token);

        var monitorTask = client.Monitor(cts.Token);

        await Task.WhenAny(monitorTask, subscriptionTask);
    }
}
```

### Individual Stream Subscription

For selective data consumption, use direct stream access:

```csharp
[RequiredTelemetryVars([TelemetryVar.Speed, TelemetryVar.RPM])]
public class Program
{
    public static async Task Main(string[] args)
    {
        var logger = LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger("App");
        await using var client = TelemetryClient<TelemetryData>.Create(logger);
        using var cts = new CancellationTokenSource();

        // Subscribe to only the streams you need
        var telemetryTask = Task.Run(async () =>
        {
            await foreach (var data in client.TelemetryData.WithCancellation(cts.Token))
            {
                Console.WriteLine($"Speed: {data.Speed}, RPM: {data.RPM}");
            }
        }, cts.Token);

        var sessionTask = Task.Run(async () =>
        {
            await foreach (var session in client.SessionData.WithCancellation(cts.Token))
            {
                Console.WriteLine($"Track: {session.WeekendInfo?.TrackDisplayName ?? "Unknown"}");
            }
        }, cts.Token);

        var monitorTask = client.Monitor(cts.Token);

        await Task.WhenAny(monitorTask, telemetryTask, sessionTask);
    }
}
```

### Available Extension Methods

**Important**: All delegates in `SubscribeToAllStreams` are **optional** - you only need to provide the callbacks you care about.

| Extension Method | Purpose | Stream Type | Parameter Type |
|------------------|---------|-------------|----------------|
| `SubscribeToAllStreams` | All streams with optional delegates | Multiple | All delegates optional |
| Direct stream access | High-frequency telemetry data (60Hz) | `TelemetryData` | `T` (generated struct) |
| Direct stream access | Parsed session information | `SessionData` | `TelemetrySessionInfo` |
| Direct stream access | Raw YAML session data | `SessionDataYaml` | `string` |
| Direct stream access | Connection state changes | `ConnectStates` | `ConnectState` enum |
| Direct stream access | Error notifications | `Errors` | `Exception` |

### Migration from Events (Pre-v1.0)

If you're migrating from the old event-based API:

```csharp
// OLD (Events - no longer available):
// client.OnTelemetryUpdate += (sender, data) => { /* handle */ };
// client.OnSessionInfoUpdate += (sender, session) => { /* handle */ };
// client.OnError += (sender, error) => { /* handle */ };

// NEW (Subscription Extensions - recommended with async callbacks):
// Note: All delegates are optional - only provide the ones you need
var subscriptionTask = client.SubscribeToAllStreams(
    onTelemetryUpdate: async data => { /* handle - data is T (generated struct) */ },
    onSessionInfoUpdate: async session => { /* handle - session is TelemetrySessionInfo */ },
    onError: async error => { /* handle - error is Exception */ },
    cancellationToken: cancellationToken);
```

---

## 🤖 AI Agent Guide: Two Consumption Approaches

### Overview for AI Agents

The v1.0 SDK provides two distinct patterns for consuming telemetry data. Choose based on your application requirements:

| Approach | When to Use | Performance | Complexity |
|----------|-------------|-------------|-------------|
| **Extension Method** | Simple applications, rapid prototyping | High (sufficient for most use cases) | Low |
| **Direct Stream Access** | Maximum performance, custom logic | Highest (650K+ records/sec) | Medium |

### Extension Method Approach (Recommended for Most Cases)

**Benefits:**
- Simplified API similar to events
- Automatic task management
- Built-in error handling
- Perfect for AI-generated code

**Pattern:**
```csharp
// Single method handles all streams with async callbacks (all delegates are optional)
var subscriptionTask = client.SubscribeToAllStreams(
    onTelemetryUpdate: async data => { /* process telemetry */ },
    onSessionInfoUpdate: async session => { /* process session */ },
    onRawSessionInfoUpdate: async yaml => { /* process raw YAML */ },
    onConnectStateChanged: async state => { /* handle connection - state is ConnectState enum */ },
    onError: async error => { /* handle errors - error is Exception */ },
    cancellationToken: cancellationToken
);

var monitorTask = client.Monitor(cancellationToken);

await Task.WhenAny(monitorTask, subscriptionTask);
```

### Direct Stream Access Approach (Maximum Performance)

**Benefits:**
- Maximum performance and flexibility
- Custom backpressure handling
- Selective stream consumption
- Advanced async patterns

**Pattern:**
```csharp
// Consume each stream independently
var telemetryTask = Task.Run(async () =>
{
    await foreach (var data in client.TelemetryData.WithCancellation(cancellationToken))
    {
        // Process telemetry data
    }
}, cancellationToken);

var sessionTask = Task.Run(async () =>
{
    await foreach (var session in client.SessionData.WithCancellation(cancellationToken))
    {
        // Process session info
    }
}, cancellationToken);

var monitorTask = client.Monitor(cancellationToken);

await Task.WhenAny(monitorTask, telemetryTask, sessionTask);
```

### AI Agent Decision Tree

When generating code, follow this decision tree:

1. **Does the application need maximum performance (>500K records/sec)?**
   - No → Use Extension Method Approach
   - Yes → Use Direct Stream Access

2. **Does the application need custom backpressure handling?**
   - No → Use Extension Method Approach
   - Yes → Use Direct Stream Access

3. **Does the application only need specific streams?**
   - No → Use Extension Method Approach (with null delegates for unused streams)
   - Yes → Use Direct Stream Access

4. **Is this a prototype or simple application?**
   - Yes → Use Extension Method Approach
   - No → Consider Direct Stream Access

---

## 🔴 ADVANCED APPROACH: Direct Stream Consumption

### When to Use Direct Streams

- **Maximum Performance**: Need absolute best performance (650K+ records/sec)
- **Complex Processing**: Require advanced backpressure handling
- **Custom Patterns**: Need custom consumption logic beyond simple callbacks
- **Selective Consumption**: Only process data under specific conditions

### Core Data Streaming Pattern

```csharp
[RequiredTelemetryVars([TelemetryVar.Speed, TelemetryVar.RPM, TelemetryVar.Gear])]
public class Program
{
    public static async Task Main(string[] args)
    {
        var logger = LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger("App");
        await using var client = TelemetryClient<TelemetryData>.Create(logger);
        using var cts = new CancellationTokenSource();

        // Direct stream consumption for maximum performance
        var telemetryTask = Task.Run(async () =>
        {
            await foreach (var data in client.TelemetryData.WithCancellation(cts.Token))
            {
                // High-performance processing
                Console.WriteLine($"Speed: {data.Speed}, RPM: {data.RPM}, Gear: {data.Gear}");
            }
        }, cts.Token);

        var sessionTask = Task.Run(async () =>
        {
            await foreach (var session in client.SessionData.WithCancellation(cts.Token))
            {
                Console.WriteLine($"Track: {session.WeekendInfo.TrackName}");
            }
        }, cts.Token);

        var monitorTask = client.Monitor(cts.Token);

        await Task.WhenAny(monitorTask, telemetryTask, sessionTask);
    }
}
```

### Available Async Streams

```csharp
ITelemetryClient<TelemetryData> client;

// Primary data streams (all are IAsyncEnumerable<T>)
client.TelemetryData         // IAsyncEnumerable<TelemetryData> - 60Hz telemetry
client.SessionData           // IAsyncEnumerable<TelemetrySessionInfo> - session updates
client.SessionDataYaml       // IAsyncEnumerable<string> - raw YAML session data
client.ConnectStates         // IAsyncEnumerable<ConnectState> - connection changes
client.Errors                // IAsyncEnumerable<Exception> - error notifications

// Consume with await foreach pattern
await foreach (var data in client.TelemetryData.WithCancellation(cancellationToken))
{
    // Process telemetry data at maximum speed
}
```

### Data Stream Buffer Behavior

**Important: Understanding the Ring Buffer**

All data streams use a **60-sample ring buffer** with FIFO (First-In-First-Out) semantics and destructive reads:

- **Buffer Capacity**: 60 samples per stream
- **Buffering Duration**: At iRacing's 60Hz update rate, provides up to **1 second** of data buffering
- **Read Semantics**: Destructive - once consumed from the stream, data is removed from the buffer
- **Overflow Behavior**: When buffer fills to capacity, **oldest unread samples are automatically dropped**
- **Non-Blocking**: The SDK never blocks iRacing's data stream, ensuring continuous telemetry flow

**What This Means for Your Application:**

```csharp
// ✅ If your processing keeps up with 60Hz (~16ms per sample):
// - You receive every telemetry sample
// - No data loss occurs
// - Buffer typically holds only 1-2 samples

// ⚠️ If your processing is slower than 60Hz:
// - Buffer accumulates up to 60 samples (1 second)
// - Once buffer fills, oldest samples are automatically discarded
// - You receive the most recent data, but some intermediate samples are lost
// - This prevents memory exhaustion and keeps your app responsive

// Example: Expensive processing
await foreach (var data in client.TelemetryData.WithCancellation(ct))
{
    // If this takes >16ms per iteration, samples will be dropped
    await PerformExpensiveAnalysis(data); // ⚠️ May cause sample loss
}

// ✅ Better: Keep consumption fast, offload heavy work
await foreach (var data in client.TelemetryData.WithCancellation(ct))
{
    // Fast: Just capture the data
    _ = Task.Run(() => PerformExpensiveAnalysis(data));
}
```

**Design Principle**: The ring buffer with drop-oldest strategy ensures your application always receives the **most current telemetry** without blocking iRacing or risking memory issues, at the cost of potentially missing intermediate samples if processing cannot keep pace.

### Advanced Streaming Patterns

```csharp
// Pattern 1: Selective Processing with Conditions
await foreach (var data in client.TelemetryData.WithCancellation(cancellationToken))
{
    // Only process when car is on track and above certain speed
    if (data.IsOnTrackCar == true && (data.Speed ?? 0) > 50)
    {
        ProcessHighSpeedData(data);
    }
}

// Pattern 2: Batched Processing for Performance
var batch = new List<TelemetryData>();
await foreach (var data in client.TelemetryData.WithCancellation(cancellationToken))
{
    batch.Add(data);
    if (batch.Count >= 60) // Process once per second at 60Hz
    {
        ProcessBatch(batch);
        batch.Clear();
    }
}

// Pattern 3: Multiple Stream Coordination
var tasks = new[]
{
    ConsumeStream(client.TelemetryData, ProcessTelemetry),
    ConsumeStream(client.SessionData, ProcessSession),
    ConsumeStream(client.Errors, ProcessError)
};

await Task.WhenAll(tasks);

static async Task ConsumeStream<T>(IAsyncEnumerable<T> stream, Action<T> processor)
{
    await foreach (var item in stream.WithCancellation(cancellationToken))
    {
        processor(item);
    }
}
```

---

## Common Usage Patterns

### Variable Categories
| Category | Variables | Notes |
|----------|-----------|-------|
| **Basic Vehicle** | `Speed`, `RPM`, `Gear`, `Throttle`, `Brake` | Core driving metrics |
| **Position** | `LapDistPct`, `IsOnTrack`, `PlayerTrackSurface` | Track position |
| **Safety** | `PlayerIncidents`, `EngineWarnings` | Warnings and penalties |
| **Session** | `SessionTime`, `SessionNum`, `IsOnTrackCar` | Session state |

### Data Modes and ClientOptions
```csharp
// Live mode (Windows only, requires iRacing running)
var client = TelemetryClient<TelemetryData>.Create(logger);

// IBT file mode (cross-platform)
var ibtOptions = new IBTOptions("file.ibt", playBackSpeedMultiplier: 1);
var client = TelemetryClient<TelemetryData>.Create(logger, ibtOptions);

// With ClientOptions (for metrics support, etc.)
// Note: There are only 2 Create method overloads:
//   1. Create(logger, ibtOptions = null)
//   2. Create(logger, ibtOptions, clientOptions)
var clientOptions = new ClientOptions { MeterFactory = meterFactory };
var client = TelemetryClient<TelemetryData>.Create(logger, ibtOptions, clientOptions);
```


### Dependency Injection Pattern (Recommended)
```csharp
// In Program.cs with Host Builder
var builder = Host.CreateDefaultBuilder(args)
    .ConfigureServices((context, services) =>
    {
        services.AddMetrics();
        services.AddLogging(logging =>
        {
            logging.SetMinimumLevel(LogLevel.Debug);
            logging.AddConsole();
        });

        // Register TelemetryClient as singleton
        services.AddSingleton<ITelemetryClient<TelemetryData>>(provider =>
        {
            var logger = provider.GetRequiredService<ILogger<Program>>();
            var meterFactory = provider.GetRequiredService<System.Diagnostics.Metrics.IMeterFactory>();
            
            var clientOptions = new ClientOptions { MeterFactory = meterFactory };
            IBTOptions? ibtOptions = args.Length == 1 ? new IBTOptions(args[0]) : null;
            
            return TelemetryClient<TelemetryData>.Create(logger, clientOptions, ibtOptions);
        });
    });

using var host = builder.Build();
var client = host.Services.GetRequiredService<ITelemetryClient<TelemetryData>>();
```

## Core Setup (Required for Both Approaches)

### 1. Define Required Telemetry Variables

Use the `[RequiredTelemetryVars]` attribute to specify which telemetry variables your application needs using `TelemetryVar` enum values. The source generator will create a strongly-typed `TelemetryData` struct with nullable properties.

```csharp
using SVappsLAB.iRacingTelemetrySDK;

[RequiredTelemetryVars([TelemetryVar.Speed, TelemetryVar.RPM, TelemetryVar.Gear])]
public class Program
{
    // Generated TelemetryData will have:
    // public float? Speed { get; init; }
    // public float? RPM { get; init; }
    // public int? Gear { get; init; }
}
```

### 2. Create and Configure the Client

```csharp
using Microsoft.Extensions.Logging;
using SVappsLAB.iRacingTelemetrySDK;

// Set up logging
var logger = LoggerFactory
    .Create(builder => builder
        .SetMinimumLevel(LogLevel.Information)
        .AddConsole())
    .CreateLogger("TelemetryApp");

// Create client for live data (Windows only, requires iRacing running)
await using var client = TelemetryClient<TelemetryData>.Create(logger);

// OR create client for IBT file playback (cross-platform)
var ibtOptions = new IBTOptions("path/to/file.ibt", playBackSpeedMultiplier: 1);
await using var client = TelemetryClient<TelemetryData>.Create(logger, ibtOptions);

// OR with ClientOptions for metrics support
// Note: When using ClientOptions, BOTH ibtOptions and clientOptions must be provided
var clientOptions = new ClientOptions { MeterFactory = meterFactory };
await using var client = TelemetryClient<TelemetryData>.Create(logger, ibtOptions, clientOptions);
```

### 3. Handle Nullable Properties (v1.0+ Critical)

All telemetry properties in v1.0+ are nullable (`float?`, `int?`, `bool?`) to handle cases where data might not be available.

```csharp
// ✅ Safe arithmetic with nullable values (preserves null semantics)
var speedMph = data.Speed * 2.23694f; // Result is float?, not float

// ✅ Explicit null checking when needed
if (data.Speed.HasValue)
{
    var speed = data.Speed.Value * 2.23694f;
}

// ✅ Boolean nullable comparisons
if (data.IsOnTrackCar == true) { /* car is on track */ }

// ✅ String formatting with null-conditional operators
Console.WriteLine($"Speed: {data.Speed?.ToString("F1") ?? "N/A"}");
```

## Complete Examples

### Example 1: 🟢 Simple Approach - Speed, RPM, and Gear Display

```csharp
using Microsoft.Extensions.Logging;
using SVappsLAB.iRacingTelemetrySDK;

namespace BasicTelemetryApp
{
    [RequiredTelemetryVars([TelemetryVar.Speed, TelemetryVar.RPM, TelemetryVar.Gear, TelemetryVar.IsOnTrackCar])]
    internal class Program
    {
        static async Task Main(string[] args)
        {
            var logger = LoggerFactory
                .Create(builder => builder
                    .SetMinimumLevel(LogLevel.Information)
                    .AddConsole())
                .CreateLogger("BasicApp");

            // Support both live and IBT file modes
            IBTOptions? ibtOptions = args.Length == 1 ? new IBTOptions(args[0]) : null;
            await using var client = TelemetryClient<TelemetryData>.Create(logger, ibtOptions);
            using var cts = new CancellationTokenSource();

            var counter = 0;
            // Use extension method for simplified consumption with async callbacks
            var subscriptionTask = client.SubscribeToAllStreams(
                onTelemetryUpdate: async data =>
                {
                    // Limit logging output to once per second
                    if ((counter++ % 60) != 0 || data.IsOnTrackCar != true) return;

                    var speedMph = data.Speed * 2.23694f; // Convert m/s to mph
                    logger.LogInformation($"Gear: {data.Gear}, RPM: {data.RPM?.ToString("F0") ?? "N/A"}, Speed: {speedMph?.ToString("F0") ?? "N/A"} mph");
                },
                onSessionInfoUpdate: async session =>
                {
                    logger.LogInformation($"Track: {session.WeekendInfo.TrackDisplayName}");
                },
                onConnectStateChanged: async state =>
                {
                    logger.LogInformation($"Connection: {state}");
                },
                onError: async error =>
                {
                    logger.LogError(error, "Telemetry error occurred");
                },
                cancellationToken: cts.Token);

            Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

            var monitorTask = client.Monitor(cts.Token);

            await Task.WhenAny(monitorTask, subscriptionTask);
        }
    }
}
```

### Example 2: 🔴 Advanced Approach - High-Performance Direct Stream Consumption

```csharp
using Microsoft.Extensions.Logging;
using SVappsLAB.iRacingTelemetrySDK;
using System.Collections.Generic;

namespace HighPerformanceApp
{
    [RequiredTelemetryVars([TelemetryVar.Speed, TelemetryVar.RPM, TelemetryVar.Gear, TelemetryVar.IsOnTrackCar])]
    internal class Program
    {
        private static readonly List<TelemetryData> _dataBuffer = new();
        private static int _processedCount = 0;

        static async Task Main(string[] args)
        {
            var logger = LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger("HighPerf");
            await using var client = TelemetryClient<TelemetryData>.Create(logger);
            using var cts = new CancellationTokenSource();

            // Direct stream consumption for maximum performance (650K+ records/sec)
            var telemetryTask = Task.Run(async () =>
            {
                await foreach (var data in client.TelemetryData.WithCancellation(cts.Token))
                {
                    // Selective processing - only when car is on track and above 50 m/s
                    if (data.IsOnTrackCar == true && (data.Speed ?? 0) > 50)
                    {
                        // Batch processing for efficiency
                        _dataBuffer.Add(data);

                        if (_dataBuffer.Count >= 60) // Process once per second at 60Hz
                        {
                            ProcessBatch(_dataBuffer, logger);
                            _dataBuffer.Clear();
                        }
                    }

                    _processedCount++;

                    // Performance monitoring
                    if (_processedCount % 6000 == 0) // Every 100 seconds at 60Hz
                    {
                        logger.LogInformation($"Processed {_processedCount} records at high speed");
                    }
                }
            }, cts.Token);

            // Monitor session changes with direct stream access
            var sessionTask = Task.Run(async () =>
            {
                await foreach (var session in client.SessionData.WithCancellation(cts.Token))
                {
                    // Direct access to session data - no overhead from extension methods
                    logger.LogInformation($"Session Update - Track: {session.WeekendInfo.TrackDisplayName}, " +
                                        $"Drivers: {session.DriverInfo?.Drivers?.Count ?? 0}");
                }
            }, cts.Token);

            // Direct error handling
            var errorTask = Task.Run(async () =>
            {
                await foreach (var error in client.Errors.WithCancellation(cts.Token))
                {
                    logger.LogError(error, "High-performance telemetry error");
                }
            }, cts.Token);

            Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

            // Coordinate all tasks for maximum throughput
            var monitorTask = client.Monitor(cts.Token);

            await Task.WhenAny(monitorTask, telemetryTask, sessionTask, errorTask);
            
            // Final batch processing if any data remains
            if (_dataBuffer.Count > 0)
            {
                ProcessBatch(_dataBuffer, logger);
            }
            
            logger.LogInformation($"Final count: {_processedCount} records processed");
        }

        private static void ProcessBatch(List<TelemetryData> batch, ILogger logger)
        {
            // High-performance batch processing
            var avgSpeed = batch.Where(d => d.Speed.HasValue).Average(d => d.Speed!.Value) * 2.23694f; // m/s to mph
            var maxRpm = batch.Where(d => d.RPM.HasValue).Max(d => d.RPM!.Value);
            
            logger.LogInformation($"Batch processed: {batch.Count} records, Avg Speed: {avgSpeed:F1} mph, Max RPM: {maxRpm:F0}");
        }
    }
}
```

### Example 3: Track Position and Surface Analysis

```csharp
using Microsoft.Extensions.Logging;
using SVappsLAB.iRacingTelemetrySDK;

namespace TrackAnalysisApp
{
    [RequiredTelemetryVars([TelemetryVar.IsOnTrack, TelemetryVar.PlayerTrackSurface, TelemetryVar.PlayerTrackSurfaceMaterial, TelemetryVar.EngineWarnings, TelemetryVar.PlayerIncidents, TelemetryVar.LapDistPct])]
    internal class Program
    {
        static async Task Main(string[] args)
        {
            var logger = LoggerFactory
                .Create(builder => builder
                    .SetMinimumLevel(LogLevel.Information)
                    .AddConsole())
                .CreateLogger("TrackAnalysis");

            IBTOptions? ibtOptions = args.Length == 1 ? new IBTOptions(args[0]) : null;
            await using var client = TelemetryClient<TelemetryData>.Create(logger, ibtOptions);

            using var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

            var counter = 0;
            var subscriptionTask = client.SubscribeToAllStreams(
                onTelemetryUpdate: async data =>
                {
                    if ((counter++ % 120) != 0) return; // Output every 2 seconds

                    var trackSurface = data.PlayerTrackSurface.HasValue ? Enum.GetName(data.PlayerTrackSurface.Value) ?? "Unknown" : "N/A";
                    var surfaceMaterial = data.PlayerTrackSurfaceMaterial.HasValue ? Enum.GetName(data.PlayerTrackSurfaceMaterial.Value) ?? "Unknown" : "N/A";
                    var warnings = data.EngineWarnings.HasValue ? GetEngineWarningsList(data.EngineWarnings.Value) : "N/A";
                    var incidents = data.PlayerIncidents.HasValue ? GetIncidentInfo(data.PlayerIncidents.Value) : "N/A";

                    logger.LogInformation($"Lap: {data.LapDistPct?.ToString("P1") ?? "N/A"}, OnTrack: {data.IsOnTrack}, " +
                                        $"Surface: {trackSurface}, Material: {surfaceMaterial}, " +
                                        $"Warnings: {warnings}, Incidents: {incidents}");
                },
                cancellationToken: cts.Token
            );

            var monitorTask = client.Monitor(cts.Token);

            await Task.WhenAny(monitorTask, subscriptionTask);
        }

        static string GetEngineWarningsList(EngineWarnings warnings)
        {
            var activeWarnings = new List<string>();
            foreach (var flag in Enum.GetValues<EngineWarnings>())
            {
                if (warnings.HasFlag(flag) && flag != EngineWarnings.None)
                {
                    activeWarnings.Add(Enum.GetName(flag) ?? flag.ToString());
                }
            }
            return activeWarnings.Count > 0 ? string.Join(", ", activeWarnings) : "None";
        }

        static string GetIncidentInfo(IncidentFlags incidents)
        {
            // Extract incident report and penalty separately
            var incidentReport = (int)(incidents & IncidentFlags.IncidentRepMask);
            var incidentPenalty = (int)(incidents & IncidentFlags.IncidentPenMask);

            var reportType = incidentReport switch
            {
                0x0001 => "Loss of Control",
                0x0002 => "Off Track",
                0x0004 => "Contact",
                0x0005 => "Collision",
                0x0007 => "Car Contact",
                0x0008 => "Car Collision",
                _ => incidentReport > 0 ? $"Unknown({incidentReport:X})" : "None"
            };

            var penaltyType = incidentPenalty switch
            {
                0x0100 => "0x",
                0x0200 => "1x",
                0x0300 => "2x",
                0x0400 => "4x",
                _ => incidentPenalty > 0 ? $"Unknown({incidentPenalty:X})" : "None"
            };

            return $"{reportType} ({penaltyType})";
        }
    }
}
```

### Example 3: Data Export and Analysis

```csharp
using Microsoft.Extensions.Logging;
using SVappsLAB.iRacingTelemetrySDK;

namespace DataExportApp
{
    [RequiredTelemetryVars([TelemetryVar.Speed, TelemetryVar.RPM, TelemetryVar.SteeringWheelAngle, TelemetryVar.Throttle, TelemetryVar.Brake])]
    internal class Program
    {
        static async Task Main(string[] args)
        {
            var logger = LoggerFactory
                .Create(builder => builder
                    .SetMinimumLevel(LogLevel.Information)
                    .AddConsole())
                .CreateLogger("DataExport");

            IBTOptions? ibtOptions = args.Length == 1 ? new IBTOptions(args[0]) : null;
            await using var client = TelemetryClient<TelemetryData>.Create(logger, ibtOptions);

            var dataPoints = new List<TelemetryData>();
            var sessionInfo = "";

            using var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true;
                cts.Cancel();
                ExportCollectedData(dataPoints, sessionInfo, logger);
            };

            var subscriptionTask = client.SubscribeToAllStreams(
                onTelemetryUpdate: async data =>
                {
                    dataPoints.Add(data);
                    if (dataPoints.Count % 3600 == 0) // Log once a minute (60 Hz * 60 sec)
                    {
                        logger.LogInformation($"Collected {dataPoints.Count} data points...");
                    }
                },
                onRawSessionInfoUpdate: async yaml =>
                {
                    if (string.IsNullOrEmpty(sessionInfo))
                    {
                        sessionInfo = yaml;
                        logger.LogInformation("Session info captured");
                    }
                },
                onConnectStateChanged: async state =>
                {
                    if (state == ConnectState.Connected)
                    {
                        var variables = client.GetTelemetryVariables();
                        logger.LogInformation($"Available variables: {variables.Count}");

                        // Export variable definitions
                        await ExportVariableDefinitions(variables);
                    }
                },
                cancellationToken: cts.Token
            );

            var monitorTask = client.Monitor(cts.Token);

            await Task.WhenAny(monitorTask, subscriptionTask);
        }

        static async Task ExportVariableDefinitions(IEnumerable<TelemetryVariable> variables)
        {
            var timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            var filename = $"TelemetryVariables-{timestamp}.csv";

            await using var writer = new StreamWriter(filename);
            await writer.WriteLineAsync("Name,Type,Length,IsTimeValue,Description,Units");

            foreach (var variable in variables.OrderBy(v => v.Name))
            {
                await writer.WriteLineAsync($"{variable.Name},{variable.Type.Name}," +
                                          $"{variable.Length},{variable.IsTimeValue}," +
                                          $"\"{variable.Desc}\",\"{variable.Units}\"");
            }
        }

        static void ExportCollectedData(List<TelemetryData> dataPoints, string sessionInfo, ILogger logger)
        {
            var timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");

            // Export telemetry data
            using (var writer = new StreamWriter($"TelemetryData-{timestamp}.csv"))
            {
                writer.WriteLine("Speed,RPM,SteeringWheelAngle,Throttle,Brake");
                foreach (var data in dataPoints)
                {
                    writer.WriteLine($"{data.Speed},{data.RPM},{data.SteeringWheelAngle}," +
                                   $"{data.Throttle},{data.Brake}");
                }
            }

            // Export session info
            if (!string.IsNullOrEmpty(sessionInfo))
            {
                File.WriteAllText($"SessionInfo-{timestamp}.yaml", sessionInfo);
            }

            logger.LogInformation($"Exported {dataPoints.Count} data points");
        }
    }
}
```

## Advanced Usage

### IBT File Options

```csharp
// Play at normal speed (1x)
var ibtOptions = new IBTOptions("replay.ibt", playBackSpeedMultiplier: 1);

// Play at 10x speed
var ibtOptions = new IBTOptions("replay.ibt", playBackSpeedMultiplier: 10);

// Play as fast as possible  (default)
var ibtOptions = new IBTOptions("replay.ibt", playBackSpeedMultiplier: int.MaxValue);
```

### Pause and Resume

```csharp
// Pause telemetry stream writes (processing continues in background)
client.Pause();

// Resume telemetry stream writes
client.Resume();

// Check pause state
if (client.IsPaused)
{
    Console.WriteLine("Client is paused");
}
```

**Thread Safety and Behavior:**
- Both `Pause()` and `Resume()` are thread-safe and can be called from any thread
- Both methods are idempotent - safe to call multiple times without side effects
- Changes are not immediate due to eventual consistency (typically 1-2 samples, ~16-32ms at 60Hz)
- A few telemetry samples may pass through channels before pause/resume takes full effect

### Connection Status Monitoring

**Option A: Using Extension Method**
```csharp
using var cts = new CancellationTokenSource();

var subscriptionTask = client.SubscribeToAllStreams(
    onConnectStateChanged: async state =>  // state is ConnectState enum
    {
        switch (state)
        {
            case ConnectState.Connected:
                Console.WriteLine("Connected to iRacing");
                break;
            case ConnectState.Disconnected:
                Console.WriteLine("Disconnected from iRacing");
                break;
        }
    },
    cancellationToken: cts.Token
);

// Check connection status at any time
if (client.IsConnected)
{
    Console.WriteLine("Currently connected");
}
```

**Option B: Direct Stream Access**
```csharp
var connectionTask = Task.Run(async () =>
{
    await foreach (var state in client.ConnectStates.WithCancellation(cancellationToken))  // state is ConnectState enum
    {
        switch (state)
        {
            case ConnectState.Connected:
                Console.WriteLine("Connected to iRacing");
                break;
            case ConnectState.Disconnected:
                Console.WriteLine("Disconnected from iRacing");
                break;
        }
    }
}, cancellationToken);
```

### Error Notification

**Option A: Using Extension Method**
```csharp
var subscriptionTask = client.SubscribeToAllStreams(
    onError: async error =>  // error is Exception type
    {
        Console.WriteLine($"Telemetry error: {error.Message}");

        // Log full exception details
        logger.LogError(error, "Telemetry client error occurred");
    },
    cancellationToken: cts.Token
);
```

**Option B: Direct Stream Access**
```csharp
var errorTask = Task.Run(async () =>
{
    await foreach (var error in client.Errors.WithCancellation(cancellationToken))  // error is Exception type
    {
        Console.WriteLine($"Telemetry error: {error.Message}");

        // Log full exception details
        logger.LogError(error, "Telemetry client error occurred");
    }
}, cancellationToken);
```

## Comprehensive Telemetry Variables Reference

The SDK provides access to 200+ telemetry variables from iRacing. Here are the most commonly used:

### 🚗 Vehicle Dynamics & Control
| Variable | Type | Units | Description |
|----------|------|-------|-------------|
| `Speed` | `float` | m/s | Vehicle speed |
| `RPM` | `float` | rpm | Engine RPM |
| `Gear` | `int` | - | Current gear (-1=reverse, 0=neutral, 1+=forward) |
| `Throttle` | `float` | 0.0-1.0 | Throttle pedal position |
| `Brake` | `float` | 0.0-1.0 | Brake pedal position |
| `Clutch` | `float` | 0.0-1.0 | Clutch pedal position |
| `SteeringWheelAngle` | `float` | rad | Steering wheel angle |
| `SteeringWheelTorque` | `float` | N·m | Force feedback torque |
| `LongAccel` | `float` | m/s² | Longitudinal, lateral, vertical G-forces |

### 🏁 Track Position & Timing
| Variable | Type | Units | Description |
|----------|------|-------|-------------|
| `LapDistPct` | `float` | 0.0-1.0 | Distance around current lap |
| `LapCurrentLapTime` | `float` | s | Current lap time |
| `LapBestLapTime` | `float` | s | Best lap time this session |
| `LapLastLapTime` | `float` | s | Last completed lap time |
| `IsOnTrack` | `bool` | - | Whether car is on track surface |
| `IsOnTrackCar` | `bool` | - | Whether player's car is on track |
| `PlayerTrackSurface` | `int` | enum | Track surface type (asphalt, concrete, etc.) |
| `PlayerTrackSurfaceMaterial` | `int` | enum | Surface material properties |

### ⚠️ Safety & Incidents
| Variable | Type | Units | Description |
|----------|------|-------|-------------|
| `PlayerIncidents` | `IncidentFlags` | flags | Incident type and penalty level |
| `EngineWarnings` | `EngineWarnings` | flags | Engine warning indicators |
| `SessionFlags` | `SessionFlags` | flags | Yellow, red, checkered flags |
| `CarIdxTrackSurface` | `int[]` | enum | Track surface for each car |

**IncidentFlags Usage**:
```csharp
var reportType = (int)(data.PlayerIncidents & IncidentFlags.IncidentRepMask);
var penaltyLevel = (int)(data.PlayerIncidents & IncidentFlags.IncidentPenMask);
```

### 🔧 Vehicle Systems & Status
| Variable | Type | Units | Description |
|----------|------|-------|-------------|
| `FuelLevel` | `float` | L | Current fuel level |
| `FuelUsePerHour` | `float` | L/h | Fuel consumption rate |
| `WaterTemp` | `float` | °C | Engine coolant temperature |
| `OilTemp` | `float` | °C | Engine oil temperature |
| `OilPress` | `float` | bar | Engine oil pressure |
| `Voltage` | `float` | V | Electrical system voltage |
| `ManifoldPress` | `float` | bar | Intake manifold pressure |

### 🏆 Session & Race Information
| Variable | Type | Units | Description |
|----------|------|-------|-------------|
| `SessionTime` | `double` | s | Current session time |
| `SessionTimeRemain` | `double` | s | Time remaining in session |
| `SessionNum` | `int` | - | Current session number |
| `SessionState` | `int` | enum | Session state (practice, qualifying, race) |
| `SessionLapsRemain` | `int` | - | Laps remaining (if applicable) |
| `SessionLapsTotal` | `int` | - | Total laps in session |

### 🌡️ Environment & Track Conditions
| Variable | Type | Units | Description |
|----------|------|-------|-------------|
| `AirTemp` | `float` | °C | Ambient air temperature |
| `TrackTemp` | `float` | °C | Track surface temperature |
| `RelativeHumidity` | `float` | % | Relative humidity |
| `WindVel` | `float` | m/s | Wind speed |
| `WindDir` | `float` | rad | Wind direction |
| `TrackWetness` | `int` | enum | Track wetness level |

### 🚦 Multi-Car Data (Arrays)
| Variable | Type | Description |
|----------|------|-------------|
| `CarIdxLapDistPct` | `float[]` | Lap distance for each car |
| `CarIdxPosition` | `int[]` | Race position for each car |
| `CarIdxClassPosition` | `int[]` | Class position for each car |
| `CarIdxF2Time` | `float[]` | Time behind leader for each car |
| `CarIdxOnPitRoad` | `bool[]` | Pit road status for each car |

To discover all available variables, use:

```csharp
var variables = client.GetTelemetryVariables();
foreach (var variable in variables.OrderBy(v => v.Name))
{
    Console.WriteLine($"{variable.Name}: {variable.Desc} ({variable.Units})");
}
```

## ⚠️ Critical Anti-Patterns & Common Pitfalls

### ❌ DO NOT: Manually Create TelemetryData Struct
```csharp
// WRONG - This will cause compilation errors
public struct TelemetryData
{
    public float Speed { get; set; }
    public float RPM { get; set; }
}
```
**Why**: The `TelemetryData` struct is generated by the source generator based on the `[RequiredTelemetryVars]` attribute.

### ❌ DO NOT: Use String-Based Variable Names (v1.0+)
```csharp
// WRONG - v1.0+ uses enums, not strings
[RequiredTelemetryVars(["Speed", "RPM", "Gear"])]
public class Program { }
```
**Correct v1.0+ syntax**:
```csharp
[RequiredTelemetryVars([TelemetryVar.Speed, TelemetryVar.RPM, TelemetryVar.Gear])]
public class Program { }
```

### ❌ DO NOT: Use TelemetryClient Without Generic Parameter
```csharp
// WRONG - Missing generic type parameter
var client = TelemetryClient.Create(logger);
```
**Correct**:
```csharp
ITelemetryClient<TelemetryData> client = TelemetryClient<TelemetryData>.Create(logger);
```

### ❌ DO NOT: Perform Heavy Operations in Channel Readers
```csharp
// WRONG - Blocking channel consumption
await foreach (var data in client.TelemetryData.WithCancellation(cancellationToken))
{
    Thread.Sleep(100); // Blocks stream consumption
    await SaveToDatabase(data); // Slow I/O operations
    ComplexCalculation(); // CPU-intensive work
}
```
**Why**: Telemetry arrives at 60Hz. Heavy operations can cause buffer overflow and data loss.

**Correct**:
```csharp
// Option 1: Queue for background processing
var dataQueue = new ConcurrentQueue<TelemetryData>();
await foreach (var data in client.TelemetryData.WithCancellation(cancellationToken))
{
    dataQueue.Enqueue(data);
}

// Option 2: Use separate task for processing
await foreach (var data in client.TelemetryData.WithCancellation(cancellationToken))
{
    _ = Task.Run(() => ProcessDataAsync(data)); // Fire and forget
}
```

### ❌ DO NOT: Forget Resource Disposal
```csharp
// WRONG - Memory leaks
var client = TelemetryClient<TelemetryData>.Create(logger);
// Client never disposed
```
**Correct**:
```csharp
await using var client = TelemetryClient<TelemetryData>.Create(logger);
// or
await client.DisposeAsync();
```

### ❌ DO NOT: Access Non-Declared Variables
```csharp
[RequiredTelemetryVars([TelemetryVar.Speed, TelemetryVar.RPM])]
public class Program
{
    client.OnTelemetryUpdate += (sender, data) =>
    {
        var gear = data.Gear; // COMPILATION ERROR - Gear not declared
    };
}
```

### ❌ DO NOT: Ignore Nullable Properties (v1.0+)
```csharp
// WRONG - Will cause compilation errors with bool?
if (data.IsOnTrackCar) { } // Cannot convert bool? to bool

// WRONG - May cause unexpected behavior
var speed = data.Speed; // speed is float?, not float
Console.WriteLine($"Speed: {speed:F1}"); // May not format as expected
```
**Correct approaches**:
```csharp
// ✅ Explicit boolean comparison
if (data.IsOnTrackCar == true) { }

// ✅ Handle nullable formatting
Console.WriteLine($"Speed: {data.Speed?.ToString("F1") ?? "N/A"}");

// ✅ Use GetValueOrDefault() only when zero is meaningful
var speed = data.Speed.GetValueOrDefault(); // Use sparingly
```

### ❌ DO NOT: Use IBT Files on Non-Existent Paths
```csharp
// WRONG - Will throw FileNotFoundException immediately
var ibtOptions = new IBTOptions("nonexistent.ibt");
var client = TelemetryClient<TelemetryData>.Create(logger, ibtOptions);
```

### ❌ DO NOT: Use Blocking Calls in Async Context
```csharp
// WRONG - Blocking async context
await Task.Run(() =>
{
    client.Monitor(cancellationToken).Wait(); // Blocks thread pool thread
});
```
**Correct**:
```csharp
await client.Monitor(cancellationToken);
```

## Performance Considerations

1. **Throttle Output**: Telemetry data arrives at 60 Hz. Consider throttling console output or file writes
2. **Memory Usage**: For long-running applications, be mindful of data collection growth
3. **Stream Consumption**: Keep stream readers lightweight to avoid overflow and data loss
4. **IBT Playback**: Large IBT files can consume significant memory during processing
5. **Buffer Capacity**: 60-sample ring buffer (1 second at 60Hz) prevents memory issues but drops oldest data if consumption is too slow

## Integration Patterns & Data Flow

### Database Integration Pattern (Stream-Based)
```csharp
[RequiredTelemetryVars([TelemetryVar.Speed, TelemetryVar.RPM, TelemetryVar.Gear, TelemetryVar.LapDistPct, TelemetryVar.SessionTime])]
public class DatabaseLogger
{
    private readonly ConcurrentQueue<TelemetryData> _dataQueue = new();
    private readonly CancellationTokenSource _backgroundCts = new();

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var client = TelemetryClient<TelemetryData>.Create(logger);

        // Background database writer
        var writerTask = Task.Run(async () =>
        {
            while (!_backgroundCts.Token.IsCancellationRequested)
            {
                if (_dataQueue.TryDequeue(out var data))
                {
                    await SaveToDatabase(data);
                }
                await Task.Delay(10); // Prevent busy waiting
            }
        });

        // Stream consumer that queues data for background processing
        var consumerTask = Task.Run(async () =>
        {
            await foreach (var data in client.TelemetryData.WithCancellation(cancellationToken))
            {
                _dataQueue.Enqueue(data);
            }
        }, cancellationToken);

        var monitorTask = client.Monitor(cancellationToken);

        await Task.WhenAny(monitorTask, consumerTask);
        _backgroundCts.Cancel();
        await writerTask;
    }
}
```

### Real-Time Dashboard Pattern (Stream-Based)
```csharp
[RequiredTelemetryVars([TelemetryVar.Speed, TelemetryVar.RPM, TelemetryVar.Gear, TelemetryVar.Throttle, TelemetryVar.Brake])]
public class DashboardService
{
    private TelemetryData _latestData;
    private readonly Timer _updateTimer;

    public event Action<DashboardData> OnDashboardUpdate;

    public DashboardService()
    {
        // Update dashboard at 10Hz (lower than telemetry rate)
        _updateTimer = new Timer(SendDashboardUpdate, null, 0, 100);
    }

    public async Task StartTelemetry(CancellationToken cancellationToken)
    {
        await using var client = TelemetryClient<TelemetryData>.Create(logger);

        // Start telemetry consumption task
        var telemetryTask = Task.Run(async () =>
        {
            await foreach (var data in client.TelemetryData.WithCancellation(cancellationToken))
            {
                _latestData = data; // Just store latest, don't process here
            }
        }, cancellationToken);

        var monitorTask = client.Monitor(cancellationToken);

        await Task.WhenAny(monitorTask, telemetryTask);
    }

    private void SendDashboardUpdate(object state)
    {
        var dashData = new DashboardData
        {
            SpeedMph = _latestData.Speed * 2.23694f,
            RPM = _latestData.RPM,
            Gear = _latestData.Gear,
            ThrottlePercent = _latestData.Throttle * 100f,
            BrakePercent = _latestData.Brake * 100f
        };
        OnDashboardUpdate?.Invoke(dashData);
    }
}
```


## Source Generation Details & Constraints

### How Source Generation Works
The SDK uses Roslyn source generators to create the `TelemetryData` struct at compile time:

1. **Attribute Processing**: The `[RequiredTelemetryVars]` attribute is processed during compilation
2. **Code Generation**: A struct is generated with properties matching the specified variable names
3. **Type Safety**: The generated struct provides compile-time type checking and IntelliSense

### Generated Code Structure
Given this attribute:
```csharp
[RequiredTelemetryVars([TelemetryVar.Speed, TelemetryVar.RPM, TelemetryVar.Gear, TelemetryVar.IsOnTrack])]
```

The source generator creates:
```csharp
// Generated automatically - DO NOT MODIFY
public readonly struct TelemetryData
{
    public readonly float Speed;
    public readonly float RPM;
    public readonly int Gear;
    public readonly bool IsOnTrack;

    public TelemetryData(float speed, float rpm, int gear, bool isOnTrack)
    {
        Speed = speed;
        RPM = rpm;
        Gear = gear;
        IsOnTrack = isOnTrack;
    }
}
```

### Variable Name Resolution
- **Enum-Based**: Use `TelemetryVar` enum values instead of strings for type safety
- **Exact Matching**: Enum values correspond exactly to iRacing's internal variable names
- **Type Inference**: Types are determined from iRacing's variable definitions
- **Array Support**: Array variables like `TelemetryVar.CarIdxLapDistPct` become `float[]` properties

### Compilation Requirements
- **Build Order**: Source generation happens during compilation, before your code is compiled
- **Clean Builds**: Sometimes required after changing `[RequiredTelemetryVars]` attributes
- **IDE Support**: Modern IDEs show generated code in "Dependencies > Analyzers"

### Source Generator Constraints (v1.0+)
```csharp
// ✅ VALID: TelemetryVar enum array literals
[RequiredTelemetryVars([TelemetryVar.Speed, TelemetryVar.RPM, TelemetryVar.Gear])]

// ✅ VALID: Constant enum arrays
private static readonly TelemetryVar[] REQUIRED_VARS = [TelemetryVar.Speed, TelemetryVar.RPM];
[RequiredTelemetryVars(REQUIRED_VARS)]

// ❌ INVALID: String arrays (pre-v1.0 syntax)
[RequiredTelemetryVars(["Speed", "RPM"])] // Compilation error in v1.0+

// ❌ INVALID: Runtime-determined arrays
[RequiredTelemetryVars(GetVariablesFromConfig())] // Compilation error

// ❌ INVALID: Variables from other assemblies
[RequiredTelemetryVars(ExternalClass.Variables)] // May not work
```

### Multiple Attribute Support
```csharp
// Each class can have its own set of variables
[RequiredTelemetryVars([TelemetryVar.Speed, TelemetryVar.RPM])]
public class BasicMonitor { }

[RequiredTelemetryVars([TelemetryVar.Speed, TelemetryVar.RPM, TelemetryVar.Gear, TelemetryVar.Throttle, TelemetryVar.Brake])]
public class DetailedMonitor { }

// Different TelemetryData structs are generated for each
```


### Debugging Generated Code
View generated code in your IDE:
1. **Visual Studio**: Solution Explorer > Dependencies > Analyzers > SVappsLAB.iRacingTelemetrySDK.CodeGen
2. **VS Code**: Use "Go to Definition" on `TelemetryData`
3. **Build Output**: Check `obj/Generated/` folder

---

## 🤖 AI Agent v1.0 Quick Reference

### Essential v1.0 Patterns for AI Code Generation

**✅ Correct v1.0 Client Creation:**
```csharp
// Live telemetry
await using var client = TelemetryClient<TelemetryData>.Create(logger);

// IBT playback
var ibtOptions = new IBTOptions(@"C:\path\to\file.ibt", speedMultiplier: 10);
await using var client = TelemetryClient<TelemetryData>.Create(logger, ibtOptions);

// With ClientOptions (requires BOTH ibtOptions and clientOptions)
var clientOptions = new ClientOptions { MeterFactory = meterFactory };
await using var client = TelemetryClient<TelemetryData>.Create(logger, ibtOptions, clientOptions);
```

**✅ Correct v1.0 Variable Declaration:**
```csharp
[RequiredTelemetryVars([TelemetryVar.Speed, TelemetryVar.RPM, TelemetryVar.Gear])]
public class Program { /* AI generates this */ }
```

**✅ Correct v1.0 Data Consumption (Extension Method):**
```csharp
// All delegates are OPTIONAL - only provide what you need
var subscriptionTask = client.SubscribeToAllStreams(
    onTelemetryUpdate: async data => { /* handle data - T type */ },
    onSessionInfoUpdate: async session => { /* handle session - TelemetrySessionInfo */ },
    onConnectStateChanged: async state => { /* handle connection - ConnectState enum */ },
    onError: async error => { /* handle errors - Exception */ },
    cancellationToken: cts.Token
);

var monitorTask = client.Monitor(cts.Token);

await Task.WhenAny(monitorTask, subscriptionTask);
```

**✅ Correct v1.0 Data Consumption (Direct Channels):**
```csharp
var telemetryTask = Task.Run(async () =>
{
    await foreach (var data in client.TelemetryData.WithCancellation(cts.Token))
    {
        // Process telemetry
    }
}, cts.Token);

var monitorTask = client.Monitor(cts.Token);

await Task.WhenAny(monitorTask, telemetryTask);
```

**✅ Correct v1.0 Nullable Handling:**
```csharp
// Display with fallback
var display = $"Speed: {data.Speed?.ToString("F1") ?? "N/A"}";

// Boolean checks
if (data.IsOnTrackCar == true) { /* explicit true check */ }

// Safe arithmetic
var speedMph = data.Speed * 2.23694f; // Preserves null semantics
```

**✅ Correct v1.0 Synchronous Methods:**
```csharp
// v1.0: GetTelemetryVariables() is synchronous (no await), IsConnected is a property
var variables = client.GetTelemetryVariables();
var isConnected = client.IsConnected;
```

### ❌ Anti-Patterns to NEVER Generate

```csharp
// ❌ v0.x event-based patterns (compilation errors in v1.0)
client.OnTelemetryUpdate += (sender, data) => { };

// ❌ String-based variables (compilation errors in v1.0)
[RequiredTelemetryVars(["Speed", "RPM"])]

// ❌ Direct .Value access (runtime errors)
var speed = data.Speed.Value;

// ❌ Implicit bool conversion (compilation errors)
if (data.IsOnTrackCar) { }

// ❌ Synchronous disposal (v1.0 requires async)
using var client = TelemetryClient<TelemetryData>.Create(logger);

// ❌ Await on synchronous methods (unnecessary in v1.0)
var variables = await client.GetTelemetryVariables();
```

### AI Decision Framework

1. **Always use `TelemetryVar` enums** instead of strings for variable declaration
2. **Always use `await using`** for client disposal
3. **Always handle nullable properties** with null-conditional operators or explicit checks
4. **Choose Extension Method approach** unless maximum performance is explicitly required
5. **Always use `SubscribeToAllStreams`** method name (not `SubscribeToAllStreamsAsync`)
6. **Never await `GetTelemetryVariables()`** - it's synchronous in v1.0

### Variable Validation
The source generator validates variable names at compile time:
```csharp
// ✅ Valid iRacing variable
[RequiredTelemetryVars([TelemetryVar.Speed])] // Compiles successfully

// ❌ Invalid enum value would not compile
// [RequiredTelemetryVars([TelemetryVar.InvalidVar])] // Compilation error
```

## Threading and Async Patterns

The ITelemetryClient is designed for async/await usage with async data streaming:

```csharp
// Proper async pattern with channels
var monitorTask = client.Monitor(cancellationToken);

var telemetryConsumer = Task.Run(async () =>
{
    await foreach (var data in client.TelemetryData.WithCancellation(cancellationToken))
    {
        // Process telemetry
    }
});

await Task.WhenAny(monitorTask, telemetryConsumer);
```

## Data Streaming Architecture Details

### Stream Types and Behavior
- **TelemetryData**: High-frequency (60Hz) 60-sample ring buffer with drop-oldest behavior (1 second buffering)
- **SessionData**: Low-frequency 60-sample ring buffer with drop-oldest behavior
- **SessionDataYaml**: Low-frequency 60-sample ring buffer with drop-oldest behavior
- **ConnectStates**: 60-sample ring buffer for connection state changes
- **Errors**: 60-sample ring buffer for error notifications

All channels use FIFO semantics with destructive reads. When buffer fills, oldest unread items are automatically dropped.

### Stream Consumption Patterns
```csharp
// Pattern 1: Consume all items as they arrive
await foreach (var data in client.TelemetryData.WithCancellation(cancellationToken))
{
    ProcessData(data);
}

// Pattern 2: Consume with timeout and selective processing
var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
timeout.CancelAfter(TimeSpan.FromSeconds(5));

await foreach (var data in client.TelemetryData.WithCancellation(timeout.Token))
{
    if (ShouldProcess(data))
        ProcessData(data);
}

// Pattern 3: Multiple stream coordination
var tasks = new[]
{
    ConsumeStream(client.TelemetryData, ProcessTelemetry),
    ConsumeStream(client.SessionData, ProcessSession),
    ConsumeStream(client.Errors, ProcessError)
};

await Task.WhenAll(tasks);
```

## License

This SDK is licensed under the Apache License, Version 2.0. See the LICENSE file for details.
