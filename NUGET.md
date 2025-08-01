# iRacing Telemetry SDK for C# .NET

High-performance .NET SDK for accessing **live telemetry data** from iRacing simulator and **IBT file playback**. Features compile-time code generation for strongly-typed telemetry access with zero-allocation performance optimizations.

## Why Use This SDK?

- **Live Telemetry**: Real-time access to speed, RPM, tire data, and 200+ other variables during iRacing sessions
- **IBT File Support**: Analyze historical telemetry data from saved iRacing IBT files  
- **Type Safety**: Source code generation creates strongly-typed telemetry structs at compile time
- **High Performance**: Processes 300,000+ telemetry records/second with zero-allocation techniques
- **Simple API**: Event-driven architecture with easy-to-use async patterns

Perfect for building **dashboards**, **data analysis tools**, **race engineering applications**, and **telemetry visualizations**.

## Installation

```bash
dotnet add package SVappsLAB.iRacingTelemetrySDK
```

## Quick Start

```csharp
using Microsoft.Extensions.Logging;
using SVappsLAB.iRacingTelemetrySDK;

// 1. Define the telemetry variables you want to track
[RequiredTelemetryVars(["Speed", "RPM", "Gear"])]
public class Program
{
    static async Task Main(string[] args)
    {
        // 2. Create logger
        var logger = LoggerFactory.Create(builder => builder.AddConsole())
                                  .CreateLogger("TelemetryApp");
        
        // 3. Use IBT file if provided, otherwise connect to live iRacing session
        // If using an IBT file, you can specify the playback speed from 1x to max (depends on hardware)
        // e.g. to specify 10x playback speed: new IBTOptions(args[0], playBackSpeedMultiplier: 10)
        var ibtOptions = args.Length == 1 ? new IBTOptions(args[0]) : null;

        // 4. Create telemetry client
        using var client = TelemetryClient<TelemetryData>.Create(logger, ibtOptions);
        
        // 5. Subscribe to telemetry updates (60Hz)
        client.OnTelemetryUpdate += (sender, data) =>
        {
            var speedMph = data.Speed * 2.23694f; // Convert m/s to mph
            Console.WriteLine($"Speed: {speedMph:F0} mph, RPM: {data.RPM:F0}, Gear: {data.Gear}");
        };
        
        // 6. Start monitoring
        using var cts = new CancellationTokenSource();
        await client.Monitor(cts.Token);
    }
}
```

## Requirements

- **.NET 8.0+**
- **Windows** (for live telemetry) - IBT playback works cross-platform
- **Microsoft.Extensions.Logging** package

## Documentation & Examples

- **[Getting Started Guide](https://github.com/SVappsLAB/iRacingTelemetrySDK/blob/main/README.md)** - Quick setup and basic usage walkthrough
- **[AI Context Documentation](https://github.com/SVappsLAB/iRacingTelemetrySDK/blob/main/AI_CONTEXT.md)** - Comprehensive reference for AI coding assistants
- **[Sample Projects](https://github.com/SVappsLAB/iRacingTelemetrySDK/tree/main/Samples)** - Ready-to-run examples: basic monitoring, data export, track analysis
- **[GitHub Repository](https://github.com/SVappsLAB/iRacingTelemetrySDK)** - Full source code and releases

## License

Apache License 2.0 - See [LICENSE](https://github.com/SVappsLAB/iRacingTelemetrySDK/blob/main/LICENSE) for details.
