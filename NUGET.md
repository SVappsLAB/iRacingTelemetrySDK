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
        using var cts = new CancellationTokenSource();

        // 5. Subscribe to telemetry and sessionInfo streams
        var subscriptionTask = client.SubscribeToAllStreams(
            onTelemetryUpdate: async data =>
            {
                Console.WriteLine($"Speed: {data.Speed}, RPM: {data.RPM}");
            },
            onSessionInfoUpdate: async session =>
            {
                Console.WriteLine($"Track: {session.WeekendInfo.TrackName}, Drivers: {session.DriverInfo.Drivers.Count}");
            },
            onConnectStateChanged: async state =>
            {
                Console.WriteLine($"Connection state: {state}"); // Connected, Disconnected
            },
            cancellationToken: cts.Token
        );

        // 6. Start monitoring (iRacing data will be processed by your subscription handlers)
        var monitorTask = client.Monitor(cts.Token);

        await Task.WhenAny(monitorTask, subscriptionTask);
    }
}
```

## Requirements

- **.NET 8.0+**

## Documentation & Examples

- **[Getting Started Guide](https://github.com/SVappsLAB/iRacingTelemetrySDK#readme)** - Setup and basic usage
- **[Sample Projects](https://github.com/SVappsLAB/iRacingTelemetrySDK/tree/main/Samples)** - Basic monitoring, data export, track analysis
- **[Implementation Guide](https://github.com/SVappsLAB/iRacingTelemetrySDK/blob/main/Sdk/SVappsLAB.iRacingTelemetrySDK/contents/docs/AI_USAGE.md)** - Deep dive into telemetry APIs and code-generation requirements
- **[Migration Guide](https://github.com/SVappsLAB/iRacingTelemetrySDK/blob/main/MIGRATION_GUIDE.md)** - Upgrading from previous versions
- **[GitHub Repository](https://github.com/SVappsLAB/iRacingTelemetrySDK)** - Source code and releases

## License

Apache License 2.0 - See [LICENSE](https://github.com/SVappsLAB/iRacingTelemetrySDK/blob/main/LICENSE) for details.
