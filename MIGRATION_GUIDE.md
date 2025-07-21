# iRacing Telemetry SDK Migration Guide

The iRacing Telemetry SDK underwent major architectural redesign to address performance, type safety, and maintainability concerns.

## ✨ What's New in v1.0

- **Strongly-typed Variables**: IntelliSense support with compile-time validation using `TelemetryVar` enums
- **Nullable Properties**: Better represents iRacing's dynamic variable availability
- **Modern Async Patterns**: Async stream-based data flows with asynchronous non-blocking processing
- **High Performance**: Lock-free architecture delivering 600,000+ telemetry records/second
- **Performance Monitoring**: Built-in metrics system for production diagnostics
- **Flow Control**: Pause/Resume functionality for temporary data suppression

## Before You Begin

Update your references to the latest version: `dotnet add package SVappsLAB.iRacingTelemetrySDK`

Check out the **MinimalExample** sample project in `Samples/MinimalExample/` for a complete working example of the new API.

Migration requires updating five key areas:
1. **Variable Declaration**: String arrays → Enum arrays
2. **Data Consumption**: Events → Async stream consumption or extension methods
3. **Disposal Pattern**: `using` → `await using`
4. **Namespaces**: Remove `.Models` from imports
5. **Null Handling**: Direct access → Nullable-aware patterns

The extension methods like `SubscribeToAllStreams` provide the smoothest migration path for simple applications,
while direct async stream consumption offer maximum performance and flexibility when speed is a concern.

## Breaking Changes Overview

### Variable Name Mapping

Most variables map directly from string to enum:

```csharp
"Speed" → TelemetryVar.Speed
"RPM" → TelemetryVar.RPM
"LFtempL" → TelemetryVar.LFtempL
"CarIdxLap" → TelemetryVar.CarIdxLap
```
### Breaking Changes Summary

| Component | Legacy | Modern | Migration Impact |
|-----------|--------|--------|------------------|
| Variable Declaration | `string[]` | `TelemetryVar[]` | Compile-time validation using enums |
| Type System | Non-nullable values | Nullable properties | Explicit null handling required |
| Data Delivery | .NET Events | Async streams (`IAsyncEnumerable<T>`) | Performance boost, backpressure handling |
| Async Patterns | Event handlers | `await foreach` | Modern async consumption patterns |
| Disposal Pattern | `IDisposable` | `IAsyncDisposable` | `using` → `await using` |
| Model Namespace | `SVappsLAB.iRacingTelemetrySDK.Models` | `SVappsLAB.iRacingTelemetrySDK` | Update using statements |
| GetTelemetryVariables | `Task<IEnumerable<TelemetryVariable>>` | `IReadOnlyList<TelemetryVariable>` | Remove await, update return type |

## Migration Workflow

1. Switch telemetry variable declarations to enums (`Migration Patterns → 1. Enums for Variables`).
2. Replace event-driven consumption with async streams or extension methods (`Migration Patterns → 2. Event Handlers to Async Stream Consumption`).
3. Move connection and error handling onto the provided streams (`Migration Patterns → 3. Connection and Error Handling Migration`).
4. Update disposal patterns and namespaces (`Migration Patterns → 4. Disposal Pattern and Namespace Changes`).
5. Update downstream code to respect nullable telemetry properties (`Nullable Property Handling`).

## Quick Migration Checklist

- [ ] Replace `[RequiredTelemetryVars(["Speed", "RPM"])]` with `[RequiredTelemetryVars([TelemetryVar.Speed, TelemetryVar.RPM])]`
- [ ] Replace event handlers with async stream consumption or `SubscribeToAllStreams`
- [ ] Update `using` to `await using` for client disposal
- [ ] Update namespace imports (remove `.Models` if used)
- [ ] Remove `await` from `GetTelemetryVariables()` calls
- [ ] Handle nullable properties explicitly (if needed)

## Migration Patterns

### 1. Enums for Variables

Replace string arrays with `TelemetryVar` enum arrays:

**Legacy:**
```csharp
[RequiredTelemetryVars(["Speed", "RPM", "Gear"])]
```
**Modern:**
```csharp
[RequiredTelemetryVars([TelemetryVar.Speed, TelemetryVar.RPM, TelemetryVar.Gear])]
```

### 2. Event Handlers to Async Stream Consumption

**Legacy (Events):**
```csharp
[RequiredTelemetryVars(["Speed", "RPM", "Gear"])]
public class SpeedMonitor
{
    public void Setup()
    {
        client.OnTelemetryUpdate += (sender, data) => {
            Console.WriteLine($"Speed: {data.Speed:F0}, RPM: {data.RPM:F0}");
        };

        client.OnSessionInfoUpdate += (sender, session) => {
            Console.WriteLine($"Track: {session.WeekendInfo.TrackName}");
        };
    }
}
```

**Modern (Async Streams - Extension Method Approach):**

Extension methods provide the easiest migration path. Call `SubscribeToAllStreams` with async Task delegates to handle updates.

```csharp
// Consume all streams with async Func<T, Task> delegate handlers
await client.SubscribeToAllStreams(
    onTelemetryUpdate: async data => {
        Console.WriteLine($"Speed: {data.Speed?.ToString("F0") ?? "N/A"}, RPM: {data.RPM?.ToString("F0") ?? "N/A"}");
    },
    onSessionInfoUpdate: async session => {
        Console.WriteLine($"Track: {session.WeekendInfo.TrackName}");
    },
    onRawSessionInfoUpdate: async yaml => {
        Console.WriteLine($"Session YAML updated: {yaml.Length} chars");
    },
    onConnectStateChanged: async state => {
        Console.WriteLine($"Connection: {state}");
    },
    onError: async error => {
        logger.LogError(error, "Error occurred");
    },
    cancellationToken: cancellationToken
);
```


**Modern (Async Streams - Manual Approach):**

Use async foreach to consume streaming data, or use Tasks with modern async patterns to consume channels.

```csharp
public async Task SetupAsync(CancellationToken cancellationToken)
{
    var telemetryTask = Task.Run(async () => {
        await foreach (var data in client.TelemetryData.WithCancellation(cancellationToken)) {
            Console.WriteLine($"Speed: {data.Speed?.ToString("F0") ?? "N/A"}, RPM: {data.RPM?.ToString("F0") ?? "N/A"}");
        }
    }, cancellationToken);

    var sessionTask = Task.Run(async () => {
        await foreach (var session in client.SessionData.WithCancellation(cancellationToken)) {
            Console.WriteLine($"Track: {session.WeekendInfo.TrackName}");
        }
    }, cancellationToken);

    var monitorTask = client.Monitor(cancellationToken);

    await Task.WhenAny(monitorTask, telemetryTask, sessionTask);
}
```

### 3. Connection and Error Handling Migration

Connection state changes and runtime exceptions are streamed through channels, just like telemetry and session data.

**Legacy:**
```csharp
client.OnError += (sender, args) => {
    logger.LogError(args.Exception, "Telemetry error occurred");
};
```

**Modern:**

Errors are streamed via `Errors`.

```csharp
var errorTask = Task.Run(async () => {
    await foreach (var error in client.Errors.WithCancellation(cancellationToken)) {
        logger.LogError(error, "Telemetry error occurred");
    }
}, cancellationToken);
```

### 4. Disposal Pattern and Namespace Changes

**Disposal Pattern Change:**
```csharp
// ❌ Legacy: IDisposable
using var client = TelemetryClient<TelemetryData>.Create(logger);

// ✅ Modern: IAsyncDisposable
await using var client = TelemetryClient<TelemetryData>.Create(logger);
```

**Namespace Changes:**
```csharp
// ❌ Legacy: Models in separate namespace
using SVappsLAB.iRacingTelemetrySDK.Models;

// ✅ Modern: Models in main namespace
using SVappsLAB.iRacingTelemetrySDK; // All types now in main namespace
```

**GetTelemetryVariables Change:**
```csharp
// ❌ Legacy: Async method
var variables = await client.GetTelemetryVariables();

// ✅ Modern: Synchronous method
var variables = client.GetTelemetryVariables();
```

## Events to Async Streams Reference

| Legacy Event | Modern Async Stream | Type |
|--------------|----------------------|------|
| `OnTelemetryUpdate` | `TelemetryData` | `IAsyncEnumerable<TelemetryData>` |
| `OnSessionInfoUpdate` | `SessionData` | `IAsyncEnumerable<TelemetrySessionInfo>` |
| `OnRawSessionInfoUpdate` | `SessionDataYaml` | `IAsyncEnumerable<string>` |
| `OnConnectStateChanged` | `ConnectStates` | `IAsyncEnumerable<ConnectState>` |
| `OnError` | `Errors` | `IAsyncEnumerable<Exception>` |

## Nullable Property Handling

Some iRacing variables are only available in certain sessions. Rather than throwing a runtime error when a variable is unavailable, the SDK returns null values using nullable types.

```csharp
// ✅ Direct arithmetic (preserves null semantics)
var speedMph = data.Speed * 2.23694f;

// ✅ Null-conditional formatting
var display = $"RPM: {data.RPM?.ToString("F0") ?? "N/A"}";

// ✅ Explicit null handling
var speed = data.Speed ?? 0f;
var speed = data.Speed.GetValueOrDefault();

// ✅ Boolean checks
if (data.IsOnTrackCar == true) { /* ... */ }
if (data.IsOnTrackCar.GetValueOrDefault()) { /* ... */ }

// ❌ Avoid - potential NullReferenceException
var display = $"RPM: {data.RPM.Value:F0}";
```

## Common Migration Issues & Solutions

### Issue: Compilation Error - String to Enum Conversion
```csharp
// ❌ Error: Cannot convert from 'string[]' to 'TelemetryVar[]'
[RequiredTelemetryVars(["Speed", "RPM"])]

// ✅ Solution: Use enum values
[RequiredTelemetryVars([TelemetryVar.Speed, TelemetryVar.RPM])]
```

### Issue: Null Reference Warnings
```csharp
// ❌ Warning: Possible null reference
var speed = data.Speed;

// ✅ Solution: Handle nullability
var speed = data.Speed ?? 0f;
var speedText = data.Speed?.ToString("F1") ?? "N/A";
```

### Issue: Boolean Logic Changes
```csharp
// ❌ Error: Cannot implicitly convert 'bool?' to 'bool'
if (data.IsOnTrackCar) { }

// ✅ Solution: Explicit boolean check
if (data.IsOnTrackCar == true) { }
```

### Issue: Disposal Pattern Changes
```csharp
// ❌ Error: Cannot convert from 'using' to 'await using'
using var client = TelemetryClient<TelemetryData>.Create(logger);

// ✅ Solution: Use async disposal
await using var client = TelemetryClient<TelemetryData>.Create(logger);
```

### Issue: Namespace Resolution
```csharp
// ❌ Error: Type 'SessionInfo' not found
using SVappsLAB.iRacingTelemetrySDK.Models;

// ✅ Solution: Use main namespace
using SVappsLAB.iRacingTelemetrySDK;
```
