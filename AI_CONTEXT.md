# iRacing Telemetry SDK - AI Context & Implementation Guide

> **AI Assistant Instructions**: This document provides comprehensive guidance for AI coding assistants to understand and implement applications using the iRacing Telemetry SDK. The SDK uses source code generation and requires specific patterns for proper operation.

## SDK Overview

The **TelemetryClient** is the core component of the iRacing Telemetry SDK that provides high-performance access to iRacing simulator telemetry data. It supports both live telemetry streaming from active iRacing sessions and playback of IBT (iRacing Binary Telemetry) files with strongly-typed data structures generated at compile time.

## Critical Requirements for AI Tools

⚠️ **Essential Constraints**:
- **Source Generation Dependency**: The `[RequiredTelemetryVars]` attribute triggers compile-time code generation. The `TelemetryData` struct is NOT manually created.
- **Target Framework**: .NET 8.0+ required
- **Package Dependencies**: `Microsoft.Extensions.Logging` and `Microsoft.Extensions.Logging.Console` are required
- **Windows Dependency**: Live telemetry requires Windows (iRacing memory-mapped files). IBT playback works cross-platform.
- **Generic Type Parameter**: `TelemetryClient<T>` where `T` is the generated `TelemetryData` struct

## Project Setup

### Required NuGet Packages
```xml
<PackageReference Include="SVappsLAB.iRacingTelemetrySDK" Version="latest" />
<PackageReference Include="Microsoft.Extensions.Logging" Version="8.0.0" />
<PackageReference Include="Microsoft.Extensions.Logging.Console" Version="8.0.0" />
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

- **Generic Type Safety**: Uses source code generation to create strongly-typed telemetry data structures
- **Dual Data Sources**: Works with live iRacing sessions or IBT file playback
- **High Performance**: Optimized with `ref struct`, `ReadOnlySpan<T>`, and unsafe code for zero-allocation processing
- **Event-Driven Architecture**: Subscribe to telemetry updates, connection changes, and session info events
- **Asynchronous Operations**: Non-blocking operations throughout using async/await patterns

## Quick Reference for AI Tools

### Core Pattern (Always Required)
```csharp
// 1. Define telemetry variables (triggers source generation)
[RequiredTelemetryVars(["Speed", "RPM", "Gear"])]
public class Program
{
    // 2. Create logger and client
    var logger = LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger("App");
    using var client = TelemetryClient<TelemetryData>.Create(logger);

    // 3. Subscribe to events
    client.OnTelemetryUpdate += (sender, data) => {
        // data.Speed, data.RPM, data.Gear are now available
    };

    // 4. Start monitoring
    await client.Monitor(cancellationToken);
}
```

### Common Variable Categories
| Category | Variables | Notes |
|----------|-----------|-------|
| **Basic Vehicle** | `Speed`, `RPM`, `Gear`, `Throttle`, `Brake` | Core driving metrics |
| **Position** | `LapDistPct`, `IsOnTrack`, `PlayerTrackSurface` | Track position |
| **Safety** | `PlayerIncidents`, `EngineWarnings` | Warnings and penalties |
| **Session** | `SessionTime`, `SessionNum`, `IsOnTrackCar` | Session state |

### Data Modes
```csharp
// Live mode (Windows only, requires iRacing running)
var client = TelemetryClient<TelemetryData>.Create(logger);

// IBT file mode (cross-platform)
var ibtOptions = new IBTOptions("file.ibt", playBackSpeedMultiplier: 1);
var client = TelemetryClient<TelemetryData>.Create(logger, ibtOptions);
```

### Essential Event Handlers
```csharp
client.OnTelemetryUpdate += (sender, data) => { /* 60Hz telemetry data */ };
client.OnConnectStateChanged += (sender, args) => { /* Connection status */ };
client.OnError += (sender, args) => { /* Handle errors */ };
client.OnSessionInfoUpdate += (sender, info) => { /* Parsed YAML */ };
```

## Basic Usage

### 1. Define Required Telemetry Variables

Use the `[RequiredTelemetryVars]` attribute to specify which telemetry variables your application needs. The source generator will create a strongly-typed `TelemetryData` struct with these properties.

```csharp
using SVappsLAB.iRacingTelemetrySDK;

[RequiredTelemetryVars(["Speed", "RPM", "Gear"])]
public class Program
{
    // Your application code here
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

// Create client for live data
using var client = TelemetryClient<TelemetryData>.Create(logger);

// OR create client for IBT file playback
var ibtOptions = new IBTOptions("path/to/file.ibt", playBackSpeedMultiplier: 1);
using var client = TelemetryClient<TelemetryData>.Create(logger, ibtOptions);
```

### 3. Subscribe to Events

```csharp
// Subscribe to telemetry data updates
client.OnTelemetryUpdate += (sender, telemetryData) =>
{
    Console.WriteLine($"Speed: {telemetryData.Speed:F1} m/s, RPM: {telemetryData.RPM:F0}, Gear: {telemetryData.Gear}");
};

// Subscribe to connection state changes
client.OnConnectStateChanged += (sender, args) =>
{
    Console.WriteLine($"Connection state: {args.State}");
};

// Subscribe to session info updates
client.OnSessionInfoUpdate += (sender, sessionInfo) =>
{
    Console.WriteLine($"Track: {sessionInfo.WeekendInfo.TrackDisplayName}");
};

// Subscribe to raw session info (YAML format)
client.OnRawSessionInfoUpdate += (sender, yamlData) =>
{
    Console.WriteLine("Raw session info updated");
};

// Subscribe to errors
client.OnError += (sender, args) =>
{
    Console.WriteLine($"Error: {args.Exception.Message}");
};
```

### 4. Start Monitoring

```csharp
using var cts = new CancellationTokenSource();

// Start monitoring (this will run until cancelled)
await client.Monitor(cts.Token);
```

## Complete Examples

### Example 1: Basic Speed, RPM, and Gear Display

```csharp
using Microsoft.Extensions.Logging;
using SVappsLAB.iRacingTelemetrySDK;

namespace BasicTelemetryApp
{
    [RequiredTelemetryVars(["Speed", "RPM", "Gear", "IsOnTrackCar"])]
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

            var counter = 0;
            client.OnTelemetryUpdate += (sender, data) =>
            {
                // Limit logging output to once per second
                if ((counter++ % 60) != 0 || !data.IsOnTrackCar) return;

                var speedMph = data.Speed * 2.23694f; // Convert m/s to mph
                logger.LogInformation($"Gear: {data.Gear}, RPM: {data.RPM:F0}, Speed: {speedMph:F0} mph");
            };

            using var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

            await client.Monitor(cts.Token);
        }
    }
}
```

### Example 2: Track Position and Surface Analysis

```csharp
using Microsoft.Extensions.Logging;
using SVappsLAB.iRacingTelemetrySDK;

namespace TrackAnalysisApp
{
    [RequiredTelemetryVars(["IsOnTrack", "PlayerTrackSurface", "PlayerTrackSurfaceMaterial", "EngineWarnings", "PlayerIncidents", "LapDistPct"])]
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

                var trackSurface = Enum.GetName(data.PlayerTrackSurface) ?? "Unknown";
                var surfaceMaterial = Enum.GetName(data.PlayerTrackSurfaceMaterial) ?? "Unknown";
                var warnings = GetEngineWarningsList(data.EngineWarnings);
                var incidents = GetIncidentInfo(data.PlayerIncidents);

                logger.LogInformation($"Lap: {data.LapDistPct:P1}, OnTrack: {data.IsOnTrack}, " +
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
using SVappsLAB.iRacingTelemetrySDK.Models;

namespace DataExportApp
{
    [RequiredTelemetryVars(["Speed", "RPM", "SteeringWheelAngle", "Throttle", "Brake"])]
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

### ❌ DO NOT: Use TelemetryClient Without Generic Parameter
```csharp
// WRONG - Missing generic type parameter
var client = TelemetryClient.Create(logger);
```
**Correct**:
```csharp
var client = TelemetryClient<TelemetryData>.Create(logger);
```

### ❌ DO NOT: Perform Heavy Operations in Event Handlers
```csharp
// WRONG - Blocking telemetry processing thread
client.OnTelemetryUpdate += (sender, data) =>
{
    Thread.Sleep(100); // Blocks telemetry processing
    SaveToDatabase(data); // Synchronous I/O
    ComplexCalculation(); // CPU-intensive work
};
```
**Why**: Telemetry arrives at 60Hz. Heavy operations block the processing thread.

**Correct**:
```csharp
client.OnTelemetryUpdate += (sender, data) =>
{
    // Queue data for background processing
    telemetryQueue.Enqueue(data);
};
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
[RequiredTelemetryVars(["Speed", "RPM"])]
public class Program
{
    client.OnTelemetryUpdate += (sender, data) =>
    {
        var gear = data.Gear; // COMPILATION ERROR - Gear not declared
    };
}
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
3. **Event Handlers**: Keep event handlers lightweight to avoid blocking the telemetry processing loop
4. **IBT Playback**: Large IBT files can consume significant memory during processing

## Integration Patterns & Data Flow

### Database Integration Pattern
```csharp
[RequiredTelemetryVars(["Speed", "RPM", "Gear", "LapDistPct", "SessionTime"])]
public class DatabaseLogger
{
    private readonly ConcurrentQueue<TelemetryData> _dataQueue = new();
    private readonly CancellationTokenSource _backgroundCts = new();

    public async Task StartAsync()
    {
        using var client = TelemetryClient<TelemetryData>.Create(logger);

        // Queue telemetry data (non-blocking)
        client.OnTelemetryUpdate += (sender, data) => _dataQueue.Enqueue(data);

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

        await client.Monitor(cancellationToken);
        _backgroundCts.Cancel();
    }
}
```

### Real-Time Dashboard Pattern
```csharp
[RequiredTelemetryVars(["Speed", "RPM", "Gear", "Throttle", "Brake"])]
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

    public async Task StartTelemetry()
    {
        using var client = TelemetryClient<TelemetryData>.Create(logger);

        client.OnTelemetryUpdate += (sender, data) =>
        {
            _latestData = data; // Just store latest, don't process here
        };

        await client.Monitor(cancellationToken);
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
[RequiredTelemetryVars(["Speed", "RPM", "Gear", "IsOnTrack"])]
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
- **Case Insensitive**: `"speed"`, `"Speed"`, `"SPEED"` all resolve to the same variable
- **Exact Matching**: Variable names must match iRacing's internal names exactly
- **Type Inference**: Types are determined from iRacing's variable definitions
- **Array Support**: Array variables like `"CarIdxLapDistPct"` become `float[]` properties

### Compilation Requirements
- **Build Order**: Source generation happens during compilation, before your code is compiled
- **Clean Builds**: Sometimes required after changing `[RequiredTelemetryVars]` attributes
- **IDE Support**: Modern IDEs show generated code in "Dependencies > Analyzers"

### Source Generator Constraints
```csharp
// ✅ VALID: String array literals
[RequiredTelemetryVars(["Speed", "RPM", "Gear"])]

// ✅ VALID: Constant string arrays
private const string[] REQUIRED_VARS = ["Speed", "RPM"];
[RequiredTelemetryVars(REQUIRED_VARS)]

// ❌ INVALID: Runtime-determined arrays
[RequiredTelemetryVars(GetVariablesFromConfig())] // Compilation error

// ❌ INVALID: Variables from other assemblies
[RequiredTelemetryVars(ExternalClass.Variables)] // May not work
```

### Multiple Attribute Support
```csharp
// Each class can have its own set of variables
[RequiredTelemetryVars(["Speed", "RPM"])]
public class BasicMonitor { }

[RequiredTelemetryVars(["Speed", "RPM", "Gear", "Throttle", "Brake"])]
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
[RequiredTelemetryVars(["Speed"])] // Compiles successfully

// ❌ Invalid variable name
[RequiredTelemetryVars(["InvalidVar"])] // Compilation warning/error
```

## Threading and Async Patterns

The TelemetryClient is designed for async/await usage:

```csharp
// Proper async pattern
await client.Monitor(cancellationToken);

// Multiple concurrent operations
var monitorTask = client.Monitor(cancellationToken);
var keyboardTask = MonitorKeyboardInput();

await Task.WhenAny(monitorTask, keyboardTask);
```

## License

This SDK is licensed under the Apache License, Version 2.0. See the LICENSE file for details.
