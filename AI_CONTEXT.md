# iRacing Telemetry SDK - AI Context & Implementation Guide

> **AI Assistant Instructions**: This document provides comprehensive guidance for AI coding assistants to understand and implement applications using the iRacing Telemetry SDK. The SDK uses source code generation and requires specific patterns for proper operation.

## SDK Overview

The **ITelemetryClient<T>** is the core interface of the iRacing Telemetry SDK that provides high-performance access to iRacing simulator telemetry data using a **channel-based architecture**. It supports both live telemetry streaming from active iRacing sessions and playback of IBT (iRacing Binary Telemetry) files with strongly-typed data structures generated at compile time.

## Critical Requirements for AI Tools

⚠️ **Essential Constraints**:
- **Channel-Based Architecture**: Uses `System.Threading.Channels` for high-performance, lock-free data streaming
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

- **Channel-Based Streaming**: Uses `System.Threading.Channels` for high-performance, lock-free data streaming
- **Generic Type Safety**: Uses source code generation to create strongly-typed telemetry data structures
- **Dual Data Sources**: Works with live iRacing sessions or IBT file playback
- **High Performance**: Optimized with `ref struct`, `ReadOnlySpan<T>`, and unsafe code for zero-allocation processing
- **Multiple Stream Types**: Separate channels for telemetry data, session info, connection state, and errors
- **Asynchronous Operations**: Non-blocking operations throughout using async/await patterns with `IAsyncEnumerable<T>`

## Implementation Patterns

The SDK offers **two main approaches** for consuming telemetry data:

1. **🟢 SIMPLE APPROACH**: Use channel extension methods for event-like patterns
2. **🔴 ADVANCED APPROACH**: Use direct channel consumption for maximum performance and control

---

## 🟢 SIMPLE APPROACH: Channel Extensions (Recommended)

### Quick Start Pattern

Use the `SubscribeToAllStreamsAsync` extension method for the easiest migration from event-based code:
```csharp
[RequiredTelemetryVars([TelemetryVar.Speed, TelemetryVar.RPM, TelemetryVar.Gear])]
public class Program
{
    public static async Task Main(string[] args)
    {
        var logger = LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger("App");
        using var client = TelemetryClient<TelemetryData>.Create(logger);
        using var cts = new CancellationTokenSource();

        // Use extension method for simplified consumption (replaces old events)
        var subscriptionTask = client.SubscribeToAllStreamsAsync(
            onTelemetryUpdate: data => Console.WriteLine($"Speed: {data.Speed}, RPM: {data.RPM}, Gear: {data.Gear}"),
            onSessionInfoUpdate: session => Console.WriteLine($"Track: {session.WeekendInfo.TrackDisplayName}"),
            onConnectStateChanged: state => Console.WriteLine($"Connection: {state.State}"),
            onError: error => Console.WriteLine($"Error: {error.Exception.Message}"),
            cancellationToken: cts.Token);

        await Task.WhenAny(client.Monitor(cts.Token), subscriptionTask);
    }
}
```

### Individual Stream Subscription

For selective data consumption, use individual extension methods:

```csharp
[RequiredTelemetryVars([TelemetryVar.Speed, TelemetryVar.RPM])]
public class Program
{
    public static async Task Main(string[] args)
    {
        var logger = LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger("App");
        using var client = TelemetryClient<TelemetryData>.Create(logger);
        using var cts = new CancellationTokenSource();

        // Subscribe to only the streams you need
        var telemetryTask = client.SubscribeToTelemetryAsync(
            data => Console.WriteLine($"Speed: {data.Speed}, RPM: {data.RPM}"),
            cts.Token);

        var sessionTask = client.SubscribeToSessionInfoAsync(
            session => Console.WriteLine($"Track: {session.WeekendInfo.TrackDisplayName}"),
            cts.Token);

        await Task.WhenAny(client.Monitor(cts.Token), telemetryTask, sessionTask);
    }
}
```

### Available Extension Methods

| Extension Method | Purpose | Stream Type |
|------------------|---------|-------------|
| `SubscribeToAllStreamsAsync` | All streams with optional delegates | Multiple |
| `SubscribeToTelemetryAsync` | High-frequency telemetry data (60Hz) | `TelemetryData` |
| `SubscribeToSessionInfoAsync` | Parsed session information | `TelemetrySessionInfo` |
| `SubscribeToRawSessionInfoAsync` | Raw YAML session data | `string` |
| `SubscribeToConnectStateAsync` | Connection state changes | `ConnectStateChangedEventArgs` |
| `SubscribeToErrorsAsync` | Error notifications | `ExceptionEventArgs` |
| `WaitForFirstDataAsync` | Wait for first data from any stream | Various |

### Migration from Events (Pre-v1.0)

If you're migrating from the old event-based API:

```csharp
// OLD (Events - no longer available):
// client.OnTelemetryUpdate += (sender, data) => { /* handle */ };
// client.OnSessionInfoUpdate += (sender, session) => { /* handle */ };
// client.OnError += (sender, error) => { /* handle */ };

// NEW (Channel Extensions - recommended):
var subscriptionTask = client.SubscribeToAllStreamsAsync(
    onTelemetryUpdate: data => { /* handle */ },
    onSessionInfoUpdate: session => { /* handle */ },
    onError: error => { /* handle */ },
    cancellationToken: cancellationToken);
```

---

## 🔴 ADVANCED APPROACH: Direct Channel Consumption

### When to Use Direct Channels

- **Maximum Performance**: Need absolute best performance (650K+ records/sec)
- **Complex Processing**: Require advanced backpressure handling
- **Custom Patterns**: Need custom consumption logic beyond simple callbacks
- **Selective Consumption**: Only process data under specific conditions

### Core Channel-Based Pattern

```csharp
[RequiredTelemetryVars([TelemetryVar.Speed, TelemetryVar.RPM, TelemetryVar.Gear])]
public class Program
{
    public static async Task Main(string[] args)
    {
        var logger = LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger("App");
        using var client = TelemetryClient<TelemetryData>.Create(logger);
        using var cts = new CancellationTokenSource();

        // Direct channel consumption for maximum performance
        var telemetryTask = Task.Run(async () =>
        {
            await foreach (var data in client.TelemetryDataStream.ReadAllAsync(cts.Token))
            {
                // High-performance processing
                Console.WriteLine($"Speed: {data.Speed}, RPM: {data.RPM}, Gear: {data.Gear}");
            }
        }, cts.Token);

        var sessionTask = Task.Run(async () =>
        {
            await foreach (var session in client.SessionDataStream.ReadAllAsync(cts.Token))
            {
                Console.WriteLine($"Track: {session.WeekendInfo.TrackName}");
            }
        }, cts.Token);

        await Task.WhenAny(client.Monitor(cts.Token), telemetryTask, sessionTask);
    }
}
```

### Available Channel Streams

```csharp
ITelemetryClient<TelemetryData> client;

// Primary data streams (all are ChannelReader<T>)
client.TelemetryDataStream         // ChannelReader<TelemetryData> - 60Hz telemetry
client.SessionDataStream           // ChannelReader<TelemetrySessionInfo> - session updates  
client.RawSessionDataStream        // ChannelReader<string> - raw YAML session data
client.ConnectStateStream          // ChannelReader<ConnectStateChangedEventArgs> - connection changes
client.ErrorStream                 // ChannelReader<ExceptionEventArgs> - error notifications

// Consume with await foreach pattern
await foreach (var data in client.TelemetryDataStream.ReadAllAsync(cancellationToken))
{
    // Process telemetry data at maximum speed
}
```

### Advanced Channel Patterns

```csharp
// Pattern 1: Selective Processing with Conditions
await foreach (var data in client.TelemetryDataStream.ReadAllAsync(cancellationToken))
{
    // Only process when car is on track and above certain speed
    if (data.IsOnTrackCar == true && (data.Speed ?? 0) > 50)
    {
        ProcessHighSpeedData(data);
    }
}

// Pattern 2: Batched Processing for Performance
var batch = new List<TelemetryData>();
await foreach (var data in client.TelemetryDataStream.ReadAllAsync(cancellationToken))
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
    ConsumeStream(client.TelemetryDataStream, ProcessTelemetry),
    ConsumeStream(client.SessionDataStream, ProcessSession),
    ConsumeStream(client.ErrorStream, ProcessError)
};

await Task.WhenAll(tasks);

static async Task ConsumeStream<T>(ChannelReader<T> stream, Action<T> processor)
{
    await foreach (var item in stream.ReadAllAsync(cancellationToken))
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

// With metrics support and dependency injection
var clientOptions = new ClientOptions { MeterFactory = meterFactory };
var client = TelemetryClient<TelemetryData>.Create(logger, clientOptions, ibtOptions);
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
using var client = TelemetryClient<TelemetryData>.Create(logger);

// OR create client for IBT file playback (cross-platform)
var ibtOptions = new IBTOptions("path/to/file.ibt", playBackSpeedMultiplier: 1);
using var client = TelemetryClient<TelemetryData>.Create(logger, ibtOptions);

// OR with ClientOptions for metrics support and dependency injection
var clientOptions = new ClientOptions { MeterFactory = meterFactory };
using var client = TelemetryClient<TelemetryData>.Create(logger, clientOptions, ibtOptions);
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
            using var client = TelemetryClient<TelemetryData>.Create(logger, ibtOptions);
            using var cts = new CancellationTokenSource();

            var counter = 0;
            // Use extension method for simplified consumption (replaces old events)
            var subscriptionTask = client.SubscribeToAllStreamsAsync(
                onTelemetryUpdate: data =>
                {
                    // Limit logging output to once per second
                    if ((counter++ % 60) != 0 || data.IsOnTrackCar != true) return;

                    var speedMph = data.Speed * 2.23694f; // Convert m/s to mph
                    logger.LogInformation($"Gear: {data.Gear}, RPM: {data.RPM?.ToString("F0") ?? "N/A"}, Speed: {speedMph?.ToString("F0") ?? "N/A"} mph");
                },
                onSessionInfoUpdate: session =>
                {
                    logger.LogInformation($"Track: {session.WeekendInfo.TrackDisplayName}");
                },
                onConnectStateChanged: state =>
                {
                    logger.LogInformation($"Connection: {state.State}");
                },
                onError: error =>
                {
                    logger.LogError(error.Exception, "Telemetry error occurred");
                },
                cancellationToken: cts.Token);

            Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
            await Task.WhenAny(client.Monitor(cts.Token), subscriptionTask);
        }
    }
}
```

### Example 2: 🔴 Advanced Approach - High-Performance Direct Channel Consumption

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
            using var client = TelemetryClient<TelemetryData>.Create(logger);
            using var cts = new CancellationTokenSource();

            // Direct channel consumption for maximum performance (650K+ records/sec)
            var telemetryTask = Task.Run(async () =>
            {
                await foreach (var data in client.TelemetryDataStream.ReadAllAsync(cts.Token))
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

            // Monitor session changes with direct channel access
            var sessionTask = Task.Run(async () =>
            {
                await foreach (var session in client.SessionDataStream.ReadAllAsync(cts.Token))
                {
                    // Direct access to session data - no overhead from extension methods
                    logger.LogInformation($"Session Update - Track: {session.WeekendInfo.TrackDisplayName}, " +
                                        $"Drivers: {session.DriverInfo?.Drivers?.Count ?? 0}");
                }
            }, cts.Token);

            // Direct error handling
            var errorTask = Task.Run(async () =>
            {
                await foreach (var error in client.ErrorStream.ReadAllAsync(cts.Token))
                {
                    logger.LogError(error.Exception, "High-performance telemetry error");
                }
            }, cts.Token);

            Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
            
            // Coordinate all tasks for maximum throughput
            await Task.WhenAny(client.Monitor(cts.Token), telemetryTask, sessionTask, errorTask);
            
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
            using var client = TelemetryClient<TelemetryData>.Create(logger, ibtOptions);

            var counter = 0;
            client.OnTelemetryUpdate += (sender, data) =>
            {
                if ((counter++ % 120) != 0) return; // Output every 2 seconds

                var trackSurface = data.PlayerTrackSurface.HasValue ? Enum.GetName(data.PlayerTrackSurface.Value) ?? "Unknown" : "N/A";
                var surfaceMaterial = data.PlayerTrackSurfaceMaterial.HasValue ? Enum.GetName(data.PlayerTrackSurfaceMaterial.Value) ?? "Unknown" : "N/A";
                var warnings = data.EngineWarnings.HasValue ? GetEngineWarningsList(data.EngineWarnings.Value) : "N/A";
                var incidents = data.PlayerIncidents.HasValue ? GetIncidentInfo(data.PlayerIncidents.Value) : "N/A";

                logger.LogInformation($"Lap: {data.LapDistPct?.ToString("P1") ?? "N/A"}, OnTrack: {data.IsOnTrack}, " +
                                    $"Surface: {trackSurface}, Material: {surfaceMaterial}, " +
                                    $"Warnings: {warnings}, Incidents: {incidents}");
            };

            using var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

            await client.Monitor(cts.Token);
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
            using var client = TelemetryClient<TelemetryData>.Create(logger, ibtOptions);

            var dataPoints = new List<TelemetryData>();
            var sessionInfo = "";

            // Collect telemetry data
            client.OnTelemetryUpdate += (sender, data) =>
            {
                dataPoints.Add(data);
                if (dataPoints.Count % 3600 == 0) // Log once a minute (60 Hz * 60 sec)
                {
                    logger.LogInformation($"Collected {dataPoints.Count} data points...");
                }
            };

            // Capture session information
            client.OnRawSessionInfoUpdate += (sender, yaml) =>
            {
                if (string.IsNullOrEmpty(sessionInfo))
                {
                    sessionInfo = yaml;
                    logger.LogInformation("Session info captured");
                }
            };

            // Get available telemetry variables
            client.OnConnectStateChanged += async (sender, args) =>
            {
                if (args.State == ConnectState.Connected)
                {
                    var variables = await client.GetTelemetryVariables();
                    logger.LogInformation($"Available variables: {variables.Count()}");

                    // Export variable definitions
                    await ExportVariableDefinitions(variables);
                }
            };

            using var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true;
                cts.Cancel();
                ExportCollectedData(dataPoints, sessionInfo, logger);
            };

            await client.Monitor(cts.Token);
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
// Pause telemetry event firing (processing continues)
client.Pause();

// Resume telemetry events
client.Resume();
```

### Connection Status Monitoring

```csharp
client.OnConnectStateChanged += (sender, args) =>
{
    switch (args.State)
    {
        case ConnectState.Connected:
            Console.WriteLine("Connected to iRacing");
            break;
        case ConnectState.Disconnected:
            Console.WriteLine("Disconnected from iRacing");
            break;
    }
};

// Check connection status at any time
if (client.IsConnected())
{
    Console.WriteLine("Currently connected");
}
```

### Error Notification

```csharp
client.OnError += (sender, args) =>
{
    Console.WriteLine($"Telemetry error: {args.Exception.Message}");

    // Log full exception details
    logger.LogError(args.Exception, "Telemetry client error occurred");
};
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
var variables = await client.GetTelemetryVariables();
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
await foreach (var data in client.TelemetryDataStream.ReadAllAsync(cancellationToken))
{
    Thread.Sleep(100); // Blocks channel consumption
    await SaveToDatabase(data); // Slow I/O operations
    ComplexCalculation(); // CPU-intensive work
}
```
**Why**: Telemetry arrives at 60Hz. Heavy operations can cause channel overflow and data loss.

**Correct**:
```csharp
// Option 1: Queue for background processing
var dataQueue = new ConcurrentQueue<TelemetryData>();
await foreach (var data in client.TelemetryDataStream.ReadAllAsync(cancellationToken))
{
    dataQueue.Enqueue(data);
}

// Option 2: Use separate task for processing
await foreach (var data in client.TelemetryDataStream.ReadAllAsync(cancellationToken))
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
using var client = TelemetryClient<TelemetryData>.Create(logger);
// or
client.Dispose();
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
3. **Channel Consumption**: Keep channel readers lightweight to avoid overflow and data loss
4. **IBT Playback**: Large IBT files can consume significant memory during processing
5. **Channel Capacity**: Default bounded channels prevent memory issues but may drop data if consumption is too slow

## Integration Patterns & Data Flow

### Database Integration Pattern (Channel-Based)
```csharp
[RequiredTelemetryVars([TelemetryVar.Speed, TelemetryVar.RPM, TelemetryVar.Gear, TelemetryVar.LapDistPct, TelemetryVar.SessionTime])]
public class DatabaseLogger
{
    private readonly ConcurrentQueue<TelemetryData> _dataQueue = new();
    private readonly CancellationTokenSource _backgroundCts = new();

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var client = TelemetryClient<TelemetryData>.Create(logger);

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
            await foreach (var data in client.TelemetryDataStream.ReadAllAsync(cancellationToken))
            {
                _dataQueue.Enqueue(data);
            }
        }, cancellationToken);

        await Task.WhenAny(client.Monitor(cancellationToken), consumerTask);
        _backgroundCts.Cancel();
        await writerTask;
    }
}
```

### Real-Time Dashboard Pattern (Channel-Based)
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
        using var client = TelemetryClient<TelemetryData>.Create(logger);

        // Start telemetry consumption task
        var telemetryTask = Task.Run(async () =>
        {
            await foreach (var data in client.TelemetryDataStream.ReadAllAsync(cancellationToken))
            {
                _latestData = data; // Just store latest, don't process here
            }
        }, cancellationToken);

        await Task.WhenAny(client.Monitor(cancellationToken), telemetryTask);
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

### Variable Validation
The source generator validates variable names at compile time:
```csharp
// ✅ Valid iRacing variable
[RequiredTelemetryVars([TelemetryVar.Speed])] // Compiles successfully

// ❌ Invalid enum value would not compile
// [RequiredTelemetryVars([TelemetryVar.InvalidVar])] // Compilation error
```

## Threading and Async Patterns

The ITelemetryClient is designed for async/await usage with channel-based streaming:

```csharp
// Proper async pattern with channels
var monitorTask = client.Monitor(cancellationToken);

var telemetryConsumer = Task.Run(async () =>
{
    await foreach (var data in client.TelemetryDataStream.ReadAllAsync(cancellationToken))
    {
        // Process telemetry
    }
});

await Task.WhenAny(monitorTask, telemetryConsumer);
```

## Channel Architecture Details

### Channel Types and Behavior
- **TelemetryDataStream**: High-frequency (60Hz) bounded channel for telemetry data
- **SessionDataStream**: Low-frequency bounded channel for session info updates
- **RawSessionDataStream**: Low-frequency bounded channel for raw YAML session data
- **ConnectStateStream**: Event-based channel for connection state changes
- **ErrorStream**: Event-based channel for error notifications

### Channel Consumption Patterns
```csharp
// Pattern 1: Consume all items as they arrive
await foreach (var data in client.TelemetryDataStream.ReadAllAsync(cancellationToken))
{
    ProcessData(data);
}

// Pattern 2: Consume with timeout and selective processing
var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
timeout.CancelAfter(TimeSpan.FromSeconds(5));

await foreach (var data in client.TelemetryDataStream.ReadAllAsync(timeout.Token))
{
    if (ShouldProcess(data))
        ProcessData(data);
}

// Pattern 3: Multiple stream coordination
var tasks = new[]
{
    ConsumeStream(client.TelemetryDataStream, ProcessTelemetry),
    ConsumeStream(client.SessionDataStream, ProcessSession),
    ConsumeStream(client.ErrorStream, ProcessError)
};

await Task.WhenAll(tasks);
```

## License

This SDK is licensed under the Apache License, Version 2.0. See the LICENSE file for details.
