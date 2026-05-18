# iRacing Telemetry SDK - SDK Usage Guide

> For AI coding assistants generating applications that use `SVappsLAB.iRacingTelemetrySDK`.

Read this file first when building a consumer application. Use the pattern below unless the user explicitly asks for advanced stream handling.

## Default Pattern

```csharp
using Microsoft.Extensions.Logging;
using SVappsLAB.iRacingTelemetrySDK;

[RequiredTelemetryVars([TelemetryVar.Speed, TelemetryVar.RPM])]
public class Program
{
    public static async Task Main(string[] args)
    {
        var logger = LoggerFactory.Create(b => b.AddConsole()).CreateLogger("App");

        IBTOptions? ibtOptions = args.Length > 0 ? new IBTOptions(args[0]) : null;
        await using var client = TelemetryClient<TelemetryData>.Create(logger, ibtOptions);

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

        var counter = 0;

        var handlers = new TelemetryHandlers<TelemetryData>
        {
            OnTelemetryUpdate = data =>
            {
                // Telemetry arrives at 60 Hz. Avoid unthrottled console/file I/O.
                if (counter++ % 60 != 0)
                {
                    return Task.CompletedTask;
                }

                var speed = data.Speed?.ToString("F1") ?? "N/A";
                var rpm = data.RPM?.ToString("F0") ?? "N/A";
                Console.WriteLine($"Speed: {speed} m/s, RPM: {rpm}");

                return Task.CompletedTask;
            },
            OnSessionInfoUpdate = session =>
            {
                var track = session.WeekendInfo?.TrackDisplayName ?? "unknown";
                Console.WriteLine($"Track: {track}");
                return Task.CompletedTask;
            },
            OnConnectStateChanged = state =>
            {
                Console.WriteLine($"Connection: {state}");
                return Task.CompletedTask;
            },
            OnError = error =>
            {
                Console.Error.WriteLine(error.Message);
                return Task.CompletedTask;
            }
        };

        await client.Monitor(handlers, cts.Token);
    }
}
```

## Hard Rules

1. Do not manually define `TelemetryData`. The source generator creates it from `[RequiredTelemetryVars(...)]`.
2. Use `TelemetryVar` enum values, not strings: `[RequiredTelemetryVars([TelemetryVar.Speed])]`.
3. Every telemetry property you read must be listed in `[RequiredTelemetryVars(...)]`.
4. Telemetry properties are nullable. Use `?.`, `??`, `.HasValue`, or `== true` for `bool?`.
5. Create clients with `TelemetryClient<TelemetryData>.Create(logger, ...)`.
6. Dispose clients with `await using`.
7. For normal apps, define a `TelemetryHandlers<TelemetryData>` variable and pass it to `client.Monitor(handlers, ct)`.
8. `GetTelemetryVariables()` is synchronous. Do not `await` it.
9. Keep `OnTelemetryUpdate` under about 16 ms. At 60 Hz, slower handlers cause the SDK to drop oldest samples.
10. Live telemetry requires Windows and a running iRacing session. If unsure or running on Linux/macOS, default to IBT mode for portability.
11. Target .NET 8.0 or newer.
12. `OnError` is for SDK processing errors only. Exceptions thrown inside your handlers fault `Monitor(...)` directly; they are not sent to `OnError`. Wrap recoverable per-item work in `try/catch` inside the handler.

## Error Handling

`OnError` receives only SDK-side processing errors (for example a failed YAML parse or a read error). Exceptions thrown by your own handler code are NOT routed to `OnError` — they fault the `Monitor(...)` call directly, so bugs surface loudly instead of being silently swallowed. For recoverable per-item failures, wrap the work in a `try/catch` inside the handler. Keep `OnError` simple: log and return, and do not throw from `OnError`.

## Nullability Patterns

```csharp
var speed = data.Speed ?? 0f;
var display = data.Speed?.ToString("F1") ?? "N/A";
var speedMph = data.Speed * 2.23694f; // null stays null

if (data.IsOnTrackCar == true)
{
    // bool? checked explicitly
}
```

Avoid:

```csharp
var speed = data.Speed.Value; // throws when Speed is unavailable
if (data.IsOnTrackCar) { }    // does not compile because IsOnTrackCar is bool?
```

## Common Anti-Patterns

| Do not generate | Generate instead |
| --- | --- |
| `[RequiredTelemetryVars(["Speed"])]` | `[RequiredTelemetryVars([TelemetryVar.Speed])]` |
| `public struct TelemetryData { ... }` | Let the source generator create `TelemetryData` |
| `using var client = ...` | `await using var client = ...` |
| `TelemetryClient.Create(...)` | `TelemetryClient<TelemetryData>.Create(...)` |
| `await client.GetTelemetryVariables()` | `client.GetTelemetryVariables()` |
| `.Wait()`, `.Result`, or `Thread.Sleep` in handlers | Use `await`, throttling, or queue expensive work |

## When To Use Direct Streams

Use direct `IAsyncEnumerable` streams only when the user needs custom backpressure, separate consumers, or maximum IBT processing performance. For those patterns, read:

`docs/ai/SDK_REFERENCE.md`
