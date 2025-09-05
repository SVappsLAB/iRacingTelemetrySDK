# iRacing Telemetry SDK for C# .NET

High-performance .NET SDK for accessing **live telemetry data** from iRacing simulator and **IBT file playback**. Features compile-time code generation for strongly-typed telemetry access with lock-free performance optimizations.

## Why Use This SDK?

- **Live Telemetry**: Real-time access to speed, RPM, tire data, and 200+ other variables during iRacing sessions
- **IBT File Support**: Full support for telemetry data saved in iRacing IBT files. Work with historical data using the same API as live telemetry
- **Type Safety**: Enum-based telemetry variables with IntelliSense support and compile-time validation
- **High Performance**: Processes over half a million telemetry records/second with lock-free channel architecture  
- **Modern API**: Real-time channel-based data streams with async patterns and automatic backpressure handling

Perfect for building **dashboards**, **data analysis tools**, **race engineering applications**, and **telemetry visualizations**.

## ✨ What's New in v1.0

- **2x Performance Boost**: Lock-free channels delivering over 600K records/sec 
- **Strongly-typed Variables**: IntelliSense support with compile-time validation using `TelemetryVar` enums
- **Nullable Properties**: Better represents iRacing's dynamic variable availability
- **Modern Async Patterns**: Channel-based streams allow asynchronous non-blocking processing of the real-time data streams

## Installation

```bash
dotnet add package SVappsLAB.iRacingTelemetrySDK
```

## Quick Start

```csharp
using Microsoft.Extensions.Logging;
using SVappsLAB.iRacingTelemetrySDK;

// 1. Define the telemetry variables you want to track
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
        using var client = TelemetryClient<TelemetryData>.Create(logger, ibtOptions);
        using var cts = new CancellationTokenSource();

        // 5. Subscribe to telemetry data stream (60Hz rate)
        var telemetryTask = Task.Run(async () =>
        {
            await foreach (var data in client.TelemetryDataStream.ReadAllAsync(cts.Token))
            {
                Console.WriteLine($"Speed: {data.Speed}, RPM: {data.RPM}");
            }
        }, cts.Token);

        // 6. Subscribe to session info updates
        var sessionTask = Task.Run(async () =>
        {
            await foreach (var session in client.SessionDataStream.ReadAllAsync(cts.Token))
            {
                Console.WriteLine($"Track: {session.WeekendInfo.TrackName}, Drivers: {session.DriverInfo.Drivers.Count}");
            }
        }, cts.Token);

        // 7. Start monitoring
        var monitorTask = client.Monitor(cts.Token);
        await Task.WhenAny(monitorTask, telemetryTask, sessionTask);
    }
}
```

## Requirements

- **.NET 8.0+**

## Documentation & Examples

- **[Getting Started Guide](https://github.com/SVappsLAB/iRacingTelemetrySDK/blob/main/README.md)** - Quick setup and basic usage walkthrough
- **[Sample Projects](https://github.com/SVappsLAB/iRacingTelemetrySDK/tree/main/Samples)** - Ready-to-run examples: basic monitoring, data export, track analysis
- **[AI Context Documentation](https://github.com/SVappsLAB/iRacingTelemetrySDK/blob/main/AI_CONTEXT.md)** - Comprehensive reference for AI coding assistants
- **[GitHub Repository](https://github.com/SVappsLAB/iRacingTelemetrySDK)** - Full source code and releases

## License

Apache License 2.0 - See [LICENSE](https://github.com/SVappsLAB/iRacingTelemetrySDK/blob/main/LICENSE) for details.
