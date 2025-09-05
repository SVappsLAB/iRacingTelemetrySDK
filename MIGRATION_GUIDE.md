# Migration Guide: Legacy to v1.0+ Channel-Based Architecture

This guide will help you migrate your iRacing Telemetry SDK applications from the legacy string-based event system to the new enum-based channel architecture introduced in v1.0.

## Quick Migration Checklist

- [ ] Update `[RequiredTelemetryVars]` from strings to `TelemetryVar` enums
- [ ] Replace event handlers with channel stream consumption
- [ ] Use direct arithmetic with nullable types (preserves null semantics)
- [ ] Use null-conditional operators for string formatting  
- [ ] Change `if (data.BoolProperty)` to `if (data.BoolProperty == true)`
- [ ] Update async patterns to use channels instead of events
- [ ] Build and test

## Overview

### What Changed

The v1.0 release introduces two major architectural improvements:

1. **Enum-Based Variable System**: Replaces string-based telemetry variable identification with strongly-typed enums
2. **Channel-Based Architecture**: Replaces .NET events with high-performance `System.Threading.Channels`

These changes provide compile-time safety, IntelliSense support, better performance (~2x faster), and eliminate common runtime errors.

### Why These Changes Were Made

1. **Type Safety**: Compile-time validation prevents invalid variable names
2. **Performance**: 200% improvement, lock-free data structures
3. **IntelliSense Support**: IDE can suggest available telemetry variables
4. **Modern Async Patterns**: Native `await foreach` support with channels
5. **Better Backpressure**: Automatic handling of fast producers/slow consumers
6. **Developer Experience**: Better error detection and code completion
7. **Maintainability**: Centralized variable definitions reduce inconsistencies

### Breaking Changes Summary

- **Variable Declaration**: `string[]` → `TelemetryVar[]` in `[RequiredTelemetryVars]` attribute
- **Event System**: Events (`OnTelemetryUpdate`, etc.) → Channel streams (`TelemetryDataStream`, etc.)
- **Data Access**: Constructor-based → Property-based telemetry data access
- **Type System**: Record struct with constructor parameters → Record struct with nullable properties
- **Async Patterns**: Event handlers → `await foreach` channel consumption

## Step-by-Step Migration Instructions

### 1. Replace Events with Channel Streams

The most significant change is replacing .NET events with channel-based data streams.

**Available Channel Streams:**
- ✅ `TelemetryDataStream` - `ChannelReader<T>` (replaces `OnTelemetryUpdate`)
- ✅ `SessionDataStream` - `ChannelReader<TelemetrySessionInfo>` (replaces `OnSessionInfoUpdate`)
- ✅ `RawSessionDataStream` - `ChannelReader<string>` (replaces `OnRawSessionInfoUpdate`)
- ✅ `ConnectStateStream` - `ChannelReader<ConnectStateChangedEventArgs>` (replaces `OnConnectStateChanged`)
- ✅ `ErrorStream` - `ChannelReader<ExceptionEventArgs>` (replaces `OnError`)

**Before (Event-based):**
```csharp
client.OnTelemetryUpdate += (sender, data) => {
    Console.WriteLine($"Speed: {data.Speed}, RPM: {data.RPM}");
};

client.OnSessionInfoUpdate += (sender, session) => {
    Console.WriteLine($"Track: {session.WeekendInfo.TrackName}");
};

await client.Monitor(cancellationToken);
```

**After (Channel-based):**
```csharp
// Start telemetry consumption
var telemetryTask = Task.Run(async () => {
    await foreach (var data in client.TelemetryDataStream.ReadAllAsync(cancellationToken)) {
        Console.WriteLine($"Speed: {data.Speed}, RPM: {data.RPM}");
    }
}, cancellationToken);

// Start session consumption  
var sessionTask = Task.Run(async () => {
    await foreach (var session in client.SessionDataStream.ReadAllAsync(cancellationToken)) {
        Console.WriteLine($"Track: {session.WeekendInfo.TrackName}");
    }
}, cancellationToken);

// Start monitoring and wait for completion
var monitorTask = client.Monitor(cancellationToken);
await Task.WhenAny(monitorTask, telemetryTask, sessionTask);
```

**Simplified Alternative (Extension Methods):**
```csharp
// Use extension methods for simpler event-like pattern
await client.SubscribeToAllStreamsAsync(
    onTelemetryUpdate: data => Console.WriteLine($"Speed: {data.Speed}, RPM: {data.RPM}"),
    onSessionInfoUpdate: session => Console.WriteLine($"Track: {session.WeekendInfo.TrackName}"),
    cancellationToken: cancellationToken
);
```

### 2. Update RequiredTelemetryVars Attribute

**Before (String-based):**
```csharp
[RequiredTelemetryVars(["Speed", "RPM", "Gear"])]
public class Program
{
    // Your code here
}
```

**After (Enum-based):**
```csharp
[RequiredTelemetryVars([TelemetryVar.Speed, TelemetryVar.RPM, TelemetryVar.Gear])]
public class Program
{
    // Your code here
}
```

### 3. Update Telemetry Data Access

The generated `TelemetryData` struct has changed from constructor-based to property-based access.

**Before (Constructor-based):**
```csharp
client.OnTelemetryUpdate += (sender, data) =>
{
    // Data was accessed through constructor parameters
    // Values were non-nullable and passed to constructor
    Console.WriteLine($"Speed: {data.Speed}, RPM: {data.RPM}");
};
```

**After (Property-based):**
```csharp
client.OnTelemetryUpdate += (sender, data) =>
{
    // Data is now accessed through nullable properties
    Console.WriteLine($"Speed: {data.Speed}, RPM: {data.RPM}");
};
```

### 4. Handle Nullable Properties

All telemetry properties are now nullable to handle cases where data might not be available.

**Before:**
```csharp
// Properties were always available (non-nullable)
var speedMph = data.Speed * 2.23694f; // Direct conversion
```

**After:**
```csharp
// Properties are nullable - check for null or use null-conditional operators
var speedMph = data.Speed * 2.23694f; // Works with nullable float
// Or be explicit:
var speedMph = data.Speed.HasValue ? data.Speed.Value * 2.23694f : 0f;
// Or use null-conditional:
var speedMph = data.Speed?.ToString("F1") ?? "N/A";
```

## Common Migration Scenarios

### Scenario 1: Basic Speed/RPM/Gear Application

**Before (Event-based with strings):**
```csharp
[RequiredTelemetryVars(["Speed", "RPM", "Gear"])]
public class SpeedMonitor
{
    public void SetupTelemetry()
    {
        client.OnTelemetryUpdate += (sender, data) =>
        {
            var speedMph = data.Speed * 2.23694f;
            Console.WriteLine($"Gear: {data.Gear}, RPM: {data.RPM:F0}, Speed: {speedMph:F0} mph");
        };
    }
}
```

**After (Channel-based with enums):**
```csharp
[RequiredTelemetryVars([TelemetryVar.Speed, TelemetryVar.RPM, TelemetryVar.Gear])]
public class SpeedMonitor
{
    public async Task SetupTelemetryAsync(CancellationToken cancellationToken)
    {
        var telemetryTask = Task.Run(async () =>
        {
            await foreach (var data in client.TelemetryDataStream.ReadAllAsync(cancellationToken))
            {
                var speedMph = data.Speed * 2.23694f;
                Console.WriteLine($"Gear: {data.Gear}, RPM: {data.RPM?.ToString("F0") ?? "N/A"}, Speed: {speedMph?.ToString("F0") ?? "N/A"} mph");
            }
        }, cancellationToken);

        await Task.WhenAny(client.Monitor(cancellationToken), telemetryTask);
    }
}
```

**Simplified Alternative:**
```csharp
// Use extension method for simpler migration
await client.SubscribeToAllStreamsAsync(
    onTelemetryUpdate: data => 
    {
        var speedMph = data.Speed * 2.23694f;
        Console.WriteLine($"Gear: {data.Gear}, RPM: {data.RPM?.ToString("F0") ?? "N/A"}, Speed: {speedMph?.ToString("F0") ?? "N/A"} mph");
    },
    cancellationToken: cancellationToken
);
```

### Scenario 2: Complex Telemetry Application

**Before (Event-based with strings):**
```csharp
[RequiredTelemetryVars([
    "Speed", "RPM", "Gear", "Throttle", "Brake", 
    "SteeringWheelAngle", "LapDistPct", "FuelLevel"
])]
public class AdvancedTelemetry
{
    public void SetupTelemetry()
    {
        client.OnTelemetryUpdate += (sender, data) =>
        {
            // Process telemetry data
            ProcessTelemetryData(data);
        };

        client.OnSessionInfoUpdate += (sender, session) =>
        {
            // Process session updates
            ProcessSessionInfo(session);
        };
    }
}
```

**After (Channel-based with enums):**
```csharp
[RequiredTelemetryVars([
    TelemetryVar.Speed, TelemetryVar.RPM, TelemetryVar.Gear, 
    TelemetryVar.Throttle, TelemetryVar.Brake, 
    TelemetryVar.SteeringWheelAngle, TelemetryVar.LapDistPct, TelemetryVar.FuelLevel
])]
public class AdvancedTelemetry
{
    public async Task SetupTelemetryAsync(CancellationToken cancellationToken)
    {
        // Use extension method for comprehensive stream consumption
        await client.SubscribeToAllStreamsAsync(
            onTelemetryUpdate: ProcessTelemetryData,
            onSessionInfoUpdate: ProcessSessionInfo,
            cancellationToken: cancellationToken
        );
    }

    private void ProcessTelemetryData(TelemetryData data)
    {
        // Process telemetry data with nullable properties
    }

    private void ProcessSessionInfo(TelemetrySessionInfo session)
    {
        // Process session updates
    }
}
```

### Scenario 3: Conditional Data Processing

**Before (Event-based):**
```csharp
client.OnTelemetryUpdate += (sender, data) =>
{
    if (data.IsOnTrackCar && data.Speed > 50)
    {
        ProcessHighSpeedData(data.Speed, data.RPM);
    }
};
```

**After (Channel-based):**
```csharp
var telemetryTask = Task.Run(async () =>
{
    await foreach (var data in client.TelemetryDataStream.ReadAllAsync(cancellationToken))
    {
        if (data.IsOnTrackCar == true && (data.Speed ?? 0) > 50)
        {
            ProcessHighSpeedData(data.Speed, data.RPM);
        }
    }
}, cancellationToken);

await Task.WhenAny(client.Monitor(cancellationToken), telemetryTask);
```

**Simplified Alternative:**
```csharp
await client.SubscribeToAllStreamsAsync(
    onTelemetryUpdate: data =>
    {
        if (data.IsOnTrackCar == true && (data.Speed ?? 0) > 50)
        {
            ProcessHighSpeedData(data.Speed, data.RPM);
        }
    },
    cancellationToken: cancellationToken
);
```

## Variable Name Mapping

Most variable names remain the same between string and enum versions. Here are the key patterns:

| String Format | Enum Format | Notes |
|---------------|-------------|-------|
| `"Speed"` | `TelemetryVar.Speed` | Direct mapping |
| `"RPM"` | `TelemetryVar.RPM` | Direct mapping |
| `"LFtempL"` | `TelemetryVar.LFtempL` | Tire temperature variables |
| `"CarIdxLap"` | `TelemetryVar.CarIdxLap` | Car index variables |

### Finding Variable Names

1. **IntelliSense**: Type `TelemetryVar.` and browse available options
2. **Source Code**: Check `TelemetryVar.cs` for complete list
3. **Documentation**: Refer to iRacing SDK documentation for variable descriptions

## Breaking Changes and Solutions

### 1. Compilation Errors

**Error**: `Cannot convert from 'string[]' to 'TelemetryVar[]'`

**Solution**: Replace string literals with `TelemetryVar` enum values:
```csharp
// Change this:
[RequiredTelemetryVars(["Speed", "RPM"])]

// To this:
[RequiredTelemetryVars([TelemetryVar.Speed, TelemetryVar.RPM])]
```

### 2. Null Reference Warnings

**Error**: `Possible null reference return`

**Solution**: Handle nullable properties appropriately:
```csharp
// Before:
var speed = data.Speed;

// After - use null-conditional or explicit checks:
var speed = data.Speed ?? 0f;
// Or:
var speed = data.Speed.GetValueOrDefault();
```

### 3. String Formatting Issues

**Error**: Formatting methods expecting non-nullable values

**Solution**: Use null-conditional operators:
```csharp
// Before:
Console.WriteLine($"RPM: {data.RPM:F0}");

// After:
Console.WriteLine($"RPM: {data.RPM?.ToString("F0") ?? "N/A"}");
```

### 4. Boolean Logic Changes

**Error**: Boolean variables now nullable

**Solution**: Explicitly check for true/false:
```csharp
// Before:
if (data.IsOnTrackCar)

// After:
if (data.IsOnTrackCar == true)
// Or:
if (data.IsOnTrackCar.GetValueOrDefault())
```

## Benefits of the New Approach

### 1. Channel-Based Architecture Benefits

**Automatic Backpressure Handling:**
- Channels are bounded with a default limit to prevent memory issues
- Uses `BoundedChannelFullMode.DropOldest` to automatically drop old data
- No manual buffering or queue management needed

**Better Performance:**
- Lock-free data structures
- ~650K records/sec vs ~325K previously  
- More efficient than events for high-frequency data
- Reduced memory allocations

**Modern Async Patterns:**
- Native support for `await foreach`
- Integrates with `IAsyncEnumerable<T>`
- Better cancellation token support
- Consistent API across all data streams

### 2. Compile-Time Safety
```csharp
// This will cause a compilation error (good!):
// [RequiredTelemetryVars([TelemetryVar.InvalidVariable])]

// This would have been a runtime error before:
// [RequiredTelemetryVars(["Speeed"])] // Typo would cause runtime issues
```

### 2. IntelliSense Support
- Type `TelemetryVar.` to see all available variables
- IDE shows descriptions and helps prevent typos
- Refactoring tools work properly with enum values

### 3. Performance Improvements
- Eliminates string-based lookups
- Reduces boxing/unboxing overhead
- Compiled expression trees for faster data access

### 4. Better Error Messages
```csharp
// Old: Runtime error about missing variable "Speeed"
// New: Compile-time error "TelemetryVar does not contain a definition for 'Speeed'"
```

## Troubleshooting Common Issues

### Issue: Can't find equivalent enum value for string

**Problem**: You have a string variable name but can't find the corresponding enum value.

**Solution**:
1. Check the `TelemetryVar.cs` file for the complete list
2. Variable names should match exactly (case-sensitive)
3. If a variable doesn't exist, it may have been deprecated or renamed

### Issue: Null values in telemetry data

**Problem**: Getting null values for telemetry properties that you expect to have data.

**Solution**:
1. Verify the variable exists in your iRacing session
2. Check that you're connected to iRacing or using a valid IBT file
3. Some variables are only available in certain conditions (e.g., on track)

### Issue: Performance degradation after migration

**Problem**: Application seems slower after migration.

**Solution**:
1. The first access to telemetry data includes expression compilation overhead
2. Performance should improve after the initial warmup period
3. Overall performance should be better than the string-based system

### Issue: Build errors after migration

**Problem**: Project won't compile after updating attribute syntax.

**Solution**:
1. Clean and rebuild your solution: `dotnet clean && dotnet build`
2. Ensure you're referencing the correct version of the SDK
3. Check that all required NuGet packages are updated

## Advanced Migration Patterns

### Dynamic Variable Lists

**Before:**
```csharp
var variables = new[] { "Speed", "RPM", "Gear" };
// Could not use dynamic lists with attributes
```

**After:**
```csharp
// Enum arrays can be defined as constants
private static readonly TelemetryVar[] BASIC_VARS = [
    TelemetryVar.Speed, TelemetryVar.RPM, TelemetryVar.Gear
];

[RequiredTelemetryVars(BASIC_VARS)]
public class Program { }
```

### Conditional Variable Selection

**Before:**
```csharp
// Had to use different classes or complex string logic
```

**After:**
```csharp
// Can use conditional compilation or separate constants
#if DEBUG
private static readonly TelemetryVar[] DEBUG_VARS = [
    TelemetryVar.Speed, TelemetryVar.RPM, TelemetryVar.Gear,
    TelemetryVar.EngineWarnings, TelemetryVar.SessionFlags
];
[RequiredTelemetryVars(DEBUG_VARS)]
#else
[RequiredTelemetryVars([TelemetryVar.Speed, TelemetryVar.RPM, TelemetryVar.Gear])]
#endif
public class Program { }
```

## Testing Your Migration

### 1. Compilation Test
```bash
dotnet build
```
Should complete without errors.

### 2. Runtime Test with IBT File
```bash
dotnet run path/to/test.ibt
```
Verify telemetry data is correctly populated.

### 3. Live Test
Start iRacing and run your application to ensure live data works correctly.

### 4. Unit Tests
Update any unit tests that relied on the old string-based system:

**Before:**
```csharp
// Testing with mock string data
```

**After:**
```csharp
// Testing with enum-based data and nullable properties
Assert.Equal(100f, telemetryData.Speed);
Assert.True(telemetryData.IsOnTrackCar);
```

## Complete Migration Example

Here's a complete before/after example:

**Before (Event-based with strings):**
```csharp
using Microsoft.Extensions.Logging;
using SVappsLAB.iRacingTelemetrySDK;

[RequiredTelemetryVars(["Speed", "RPM", "Gear", "IsOnTrackCar"])]
public class Program
{
    public static async Task Main(string[] args)
    {
        var logger = LoggerFactory.Create(builder => builder.AddConsole())
                                  .CreateLogger("Program");

        using var client = TelemetryClient<TelemetryData>.Create(logger);

        client.OnTelemetryUpdate += (sender, data) =>
        {
            if (data.IsOnTrackCar)
            {
                var speedMph = data.Speed * 2.23694f;
                Console.WriteLine($"Gear: {data.Gear}, RPM: {data.RPM:F0}, Speed: {speedMph:F0}");
            }
        };

        using var cts = new CancellationTokenSource();
        await client.Monitor(cts.Token);
    }
}
```

**After (Channel-based with enums):**
```csharp
using Microsoft.Extensions.Logging;
using SVappsLAB.iRacingTelemetrySDK;

[RequiredTelemetryVars([TelemetryVar.Speed, TelemetryVar.RPM, TelemetryVar.Gear, TelemetryVar.IsOnTrackCar])]
public class Program
{
    public static async Task Main(string[] args)
    {
        var logger = LoggerFactory.Create(builder => builder.AddConsole())
                                  .CreateLogger("Program");

        using var client = TelemetryClient<TelemetryData>.Create(logger);
        using var cts = new CancellationTokenSource();

        // Enable graceful shutdown with Ctrl+C
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

        // Subscribe to telemetry stream using extension method
        await client.SubscribeToAllStreamsAsync(
            onTelemetryUpdate: data =>
            {
                if (data.IsOnTrackCar == true)
                {
                    var speedMph = data.Speed * 2.23694f;
                    Console.WriteLine($"Gear: {data.Gear}, RPM: {data.RPM?.ToString("F0") ?? "N/A"}, Speed: {speedMph?.ToString("F0") ?? "N/A"}");
                }
            },
            cancellationToken: cts.Token
        );
    }
}
```

**Alternative Manual Channel Approach:**
```csharp
using Microsoft.Extensions.Logging;
using SVappsLAB.iRacingTelemetrySDK;

[RequiredTelemetryVars([TelemetryVar.Speed, TelemetryVar.RPM, TelemetryVar.Gear, TelemetryVar.IsOnTrackCar])]
public class Program
{
    public static async Task Main(string[] args)
    {
        var logger = LoggerFactory.Create(builder => builder.AddConsole())
                                  .CreateLogger("Program");

        using var client = TelemetryClient<TelemetryData>.Create(logger);
        using var cts = new CancellationTokenSource();

        // Start telemetry consumption
        var telemetryTask = Task.Run(async () =>
        {
            await foreach (var data in client.TelemetryDataStream.ReadAllAsync(cts.Token))
            {
                if (data.IsOnTrackCar == true)
                {
                    var speedMph = data.Speed * 2.23694f;
                    Console.WriteLine($"Gear: {data.Gear}, RPM: {data.RPM?.ToString("F0") ?? "N/A"}, Speed: {speedMph?.ToString("F0") ?? "N/A"}");
                }
            }
        }, cts.Token);

        // Start monitoring and wait for completion
        var monitorTask = client.Monitor(cts.Token);
        await Task.WhenAny(monitorTask, telemetryTask);
    }
}
```

## Summary

The migration from the legacy event-based system to the v1.0+ channel-based architecture requires attention to:

1. **Event Migration**: Replace event handlers with channel stream consumption or extension methods
2. **Attribute Syntax**: Replace string arrays with `TelemetryVar` enum arrays  
3. **Async Patterns**: Update to use `await foreach` or the provided extension methods
4. **Nullable Properties**: Handle nullable telemetry data appropriately
5. **Boolean Logic**: Explicitly check boolean values against true/false
6. **String Formatting**: Use null-conditional operators for formatting

The benefits of improved performance (~2x faster), type safety, IntelliSense support, and modern async patterns make this migration worthwhile. The provided extension methods (`SubscribeToAllStreamsAsync`) offer a simplified migration path that closely resembles the old event-based pattern.


Most applications can be migrated using the extension methods with minimal code changes, while applications needing fine control can use the direct channel APIs for maximum performance.