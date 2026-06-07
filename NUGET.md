# iRacing Telemetry SDK for C# .NET

High-performance .NET SDK for accessing **live telemetry data** from iRacing simulator and **IBT file playback**. Features compile-time code generation for strongly-typed telemetry access with lock-free performance optimizations.

## Why Use This SDK?

- **Type Safety**: Enum-based telemetry variables with IntelliSense/Copilot support and compile-time validation
- **High Performance**: Processes 600,000+ telemetry records/second with lock-free data streaming architecture
- **Background Processing**: Dedicated threads for telemetry collection and processing - your app's processing speed never blocks the streaming telemetry data
- **Modern Async API**: Async data streams with async/await patterns and automatic backpressure handling
- **Live Telemetry**: Real-time access to speed, RPM, tire data, and 200+ variables during iRacing sessions
- **IBT File Support**: Cross-platform playback of historical telemetry using the same strongly-typed API
- **Robust Buffering**: 60-sample ring buffer (1 second at 60Hz) ensures reliable data delivery even when your app can't keep pace

## Architecture Benefits

**Production-Ready Data Streaming:**
- **60-sample ring buffer** provides 1 second of buffering at iRacing's 60Hz update rate
- **FIFO with drop-oldest** strategy automatically handles backpressure when your processing can't keep up
- **Never blocks** the iRacing data stream - always prioritizes the most recent telemetry
- **Prevents memory exhaustion** - bounded buffers protect against slow consumer scenarios
- **Zero data loss** when your app processes faster than 60Hz (~16ms per sample)

This architecture means your dashboard or analysis tool stays responsive and current, even during CPU-intensive operations like rendering charts or writing to disk.

## Use Cases

Perfect for building:
- **Real-time Dashboards** - Display live telemetry on secondary screens
- **Data Analysis Tools** - Analyze racing performance from IBT files
- **Race Engineering Apps** - Track tire wear, fuel consumption, lap times
- **Telemetry Visualizations** - Create charts and graphs from historical data


## Support for AI-Assisted Development

Point your AI coding agent to these repository docs:

- **[SDK usage guide for agents](https://github.com/SVappsLAB/iRacingTelemetrySDK/blob/main/docs/ai/SDK_USAGE.md)** - Recommended usage for consumer applications.
- **[Advanced SDK reference for agents](https://github.com/SVappsLAB/iRacingTelemetrySDK/blob/main/docs/ai/SDK_REFERENCE.md)** - Advanced stream, metrics, DI, and troubleshooting patterns.

These files are hosted in the SDK repository and are not copied into consuming projects by the NuGet package.

To have your agent use them automatically, add a line like this to your project's `AGENTS.md`, `CLAUDE.md`, or `.cursorrules` (your agent needs the ability to fetch URLs):

```
When working with the iRacing Telemetry SDK, read
https://raw.githubusercontent.com/SVappsLAB/iRacingTelemetrySDK/main/docs/ai/SDK_USAGE.md first,
and https://raw.githubusercontent.com/SVappsLAB/iRacingTelemetrySDK/main/docs/ai/SDK_REFERENCE.md for advanced patterns.
```


## Installation

```bash
dotnet add package SVappsLAB.iRacingTelemetrySDK
```

## Quick Start

```csharp
using Microsoft.Extensions.Logging;
using SVappsLAB.iRacingTelemetrySDK;

// 1. Define the telemetry variables you want to track
// Source generator creates strongly-typed TelemetryData struct at compile-time
[RequiredTelemetryVars([TelemetryVar.Speed, TelemetryVar.RPM])]

public class Program
{
    public static async Task Main(string[] args)
    {
        // 2. Create logger
        var logger = LoggerFactory.Create(builder => builder.AddConsole())
                                  .CreateLogger("Simple.Program");

        // 3. Choose data source
        IBTOptions? ibtOptions = null;  // null for live telemetry from iRacing
                                        // = new IBTOptions("gt3_spa.ibt");  // IBT file path for playback

        // 4. Create telemetry client
        await using var client = TelemetryClient<TelemetryData>.Create(logger, ibtOptions);

        // 5. Define handlers (what to do with the telemetry data when it arrives)
        var handlers = new TelemetryHandlers<TelemetryData>
        {
            OnTelemetryUpdate = data =>
            {
                Console.WriteLine($"Speed: {data.Speed?.ToString("F1") ?? "N/A"}, RPM: {data.RPM?.ToString("F0") ?? "N/A"}");
                return Task.CompletedTask;
            },
            OnSessionInfoUpdate = session =>
            {
                var track = session.WeekendInfo?.TrackDisplayName ?? "unknown";
                var drivers = session.DriverInfo?.Drivers?.Count ?? 0;
                Console.WriteLine($"Track: {track}, Drivers: {drivers}");
                return Task.CompletedTask;
            },
            OnConnectStateChanged = state =>
            {
                Console.WriteLine($"Connection state: {state}");
                return Task.CompletedTask;
            }
        };

        // Cancellation token to cancel monitoring when needed (e.g. on app shutdown)
        using var cts = new CancellationTokenSource();

        // 6. Monitor telemetry data stream
        await client.Monitor(handlers, cts.Token);
    }
}
```

## Requirements

- **.NET 8.0+** — the library targets `net8.0` and is fully compatible with .NET versions 8, 9 and 10

## Documentation & Examples

- **[Getting Started Guide](https://github.com/SVappsLAB/iRacingTelemetrySDK#readme)** - Setup and basic usage
- **[Sample Projects](https://github.com/SVappsLAB/iRacingTelemetrySDK/tree/main/Samples)** - Basic monitoring, data export, track analysis
- **[Advanced Usage](https://github.com/SVappsLAB/iRacingTelemetrySDK/blob/main/docs/ADVANCED.md)** - Direct stream access, multiple consumers, and cancellation behavior
- **[Migration Guide](https://github.com/SVappsLAB/iRacingTelemetrySDK/blob/main/docs/MIGRATION_GUIDE.md)** - Upgrading from previous versions
- **[GitHub Repository](https://github.com/SVappsLAB/iRacingTelemetrySDK)** - Source code and releases

## License

Apache License 2.0 - See [LICENSE](https://github.com/SVappsLAB/iRacingTelemetrySDK/blob/main/LICENSE) for details.
