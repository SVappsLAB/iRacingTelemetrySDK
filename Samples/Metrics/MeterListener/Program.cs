/**
 * Copyright (C) 2024-2026 Scott Velez
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 * http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 **/

using Microsoft.Extensions.Logging;
using SVappsLAB.iRacingTelemetrySDK;

namespace MeterListenerSample;

[RequiredTelemetryVars([TelemetryVar.Speed, TelemetryVar.RPM])]
internal class Program
{
    public static async Task Main(string[] args)
    {
        // === collection - the only part that differs from the OpenTelemetry sample ===

        // MetricsCollector (MetricsCollector.cs) is the MeterListener half of this sample.
        using var metrics = new MetricsCollector(reportInterval: TimeSpan.FromSeconds(5));

        // === client setup - identical in both samples ===

        var logger = LoggerFactory.Create(builder => builder.AddConsole())
                                  .CreateLogger("MeterListener");

        // no argument for live telemetry, an IBT filepath to replay a file. an optional second
        // argument is the playback speed - '1' replays at the speed it was recorded, which is the
        // only way playback.lag gets recorded. the default is max speed, which has no schedule
        IBTOptions? ibtOptions = args.Length switch
        {
            0 => null,
            1 => new IBTOptions(args[0]),
            _ => new IBTOptions(args[0], int.Parse(args[1]))
        };

        var clientOptions = new ClientOptions { DeliveryMode = TelemetryDeliveryMode.Synchronous };
        await using var client = TelemetryClient<TelemetryData>.Create(logger, ibtOptions, clientOptions);

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

        // synchronous delivery runs this handler inline, so its cost is the consumer's cost.
        // give it some work - await Task.Delay(50) - and watch handler.duration climb;
        // live telemetry will then report missed_tick as the read loop falls behind
        var handlers = new TelemetryHandlers<TelemetryData>
        {
            OnTelemetryUpdate = _ =>
            {
                client.GetValue("Speed");
                client.GetValue("RPM");
                return Task.CompletedTask;
            }
        };

        Console.WriteLine("press Ctrl-C to exit...");
        await client.Monitor(handlers, cts.Token);

        // one last report, for the run as a whole
        metrics.Print("final metrics");
    }
}
