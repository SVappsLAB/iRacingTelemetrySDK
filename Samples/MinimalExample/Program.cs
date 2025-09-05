using Microsoft.Extensions.Logging;
using SVappsLAB.iRacingTelemetrySDK;

namespace MinimalExample
{
    // 1. Define the telemetry variables you want to track
    [RequiredTelemetryVars([TelemetryVar.Speed, TelemetryVar.RPM])]
    internal class Program
    {
        public static async Task Main(string[] args)
        {
            // 2. Create logger
            var logger = LoggerFactory.Create(builder => builder.AddConsole())
                                      .CreateLogger("MinimalExample");

            // 3. Choose data source
            IBTOptions? ibtOptions = null;  // null for live telemetry from iRacing
                                            // = new IBTOptions("gt3_spa.ibt");  IBT filepath for file playback
            ibtOptions = new IBTOptions(args[0]);

            // 4. Create telemetry client
            using var client = TelemetryClient<TelemetryData>.Create(logger, ibtOptions);

            // 5. Use cancellation token for proper shutdown
            using var cts = new CancellationTokenSource();

            // 6. Subscribe to all data streams using event delegate methods for simplified consumption
            var subscriptionTask = client.SubscribeToAllStreamsAsync(
                onTelemetryUpdate: OnTelemetryUpdate,
                onSessionInfoUpdate: OnSessionInfoUpdate,
                onConnectStateChanged: OnConnectStateChanged,
                onError: OnError,
                cancellationToken: cts.Token);

            // 7. Enable graceful shutdown with Ctrl+C
            Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

            // 8. Start monitoring and wait for completion
            await Task.WhenAny(client.Monitor(cts.Token), subscriptionTask);
        }

        static void OnTelemetryUpdate(TelemetryData data)
        {
            Console.WriteLine($"Speed: {data.Speed}, RPM: {data.RPM}");
        }
        static void OnSessionInfoUpdate(TelemetrySessionInfo session)
        {
            var driverCount = session.DriverInfo?.Drivers?.Count ?? 0;
            Console.WriteLine($"Drivers: {driverCount}");
        }
        static void OnConnectStateChanged(ConnectStateChangedEventArgs state)
        {
            Console.WriteLine($"Connection: {state.State}");
        }
        static void OnError(ExceptionEventArgs error)
        {
            Console.WriteLine($"Error: {error.Exception.Message}");
        }
    }
}
