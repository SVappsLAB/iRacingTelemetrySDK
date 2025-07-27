/**
 * Copyright (C)2024 Scott Velez
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
 * limitations under the License.using Microsoft.CodeAnalysis;
**/

using Microsoft.Extensions.Logging;
using SVappsLAB.iRacingTelemetrySDK;
using SVappsLAB.iRacingTelemetrySDK.Models;

namespace DumpVariables_DumpSessionInfo
{
    [RequiredTelemetryVars(["rpm"])]
    internal class Program
    {

        static async Task Main(string[] args)
        {
            string timeStamp = DateTime.Now.ToString("yyyyMMdd-HHMMss");
            string VARIABLES_FILENAME = $"iRacingVariables-{timeStamp}.csv";
            string SESSIONINFO_FILENAME = $"IRacingSessionInfo-{timeStamp}.yaml";
            // amount of time to wait for data
            const int WAIT_FOR_DATA_SECS = 30;

            IEnumerable<TelemetryVariable>? telemetryVariables = null;
            string? rawSessionInfoYaml = null;
            CancellationTokenSource cts = new CancellationTokenSource();

            // if you pass in a IBT filename, we'll use that, otherwise default to LIVE mode
            var ibtOptions = args.Length == 1 ? new IBTOptions(args[0]) : null;

            var logger = LoggerFactory
                    .Create(builder => builder
                    .SetMinimumLevel(LogLevel.Debug)
                    .AddConsole())
                    .CreateLogger("logger");

            logger.LogInformation("pulling data from \'{source}\'", ibtOptions != null ? "IBT file session" : "Live iRacing session");

            // create telemetry client  and subscribe
            using var tc = TelemetryClient<TelemetryData>.Create(logger, ibtOptions);
            tc.OnConnectStateChanged += OnConnectStateChanged;
            tc.OnRawSessionInfoUpdate += OnRawSessionInfoUpdate;

            // startTime monitoring - exit when we receive the session info event 
            var monitorTask = tc.Monitor(cts.Token);

            DateTime startTime = DateTime.Now;
            while (DateTime.Now < startTime + TimeSpan.FromSeconds(WAIT_FOR_DATA_SECS))
            {
                if (telemetryVariables != null && rawSessionInfoYaml != null)
                {
                    // save telemetryVariables to file
                    writeVariablesFile(telemetryVariables);

                    // save sessinInfo yaml to file
                    writeSessionInfoFile(rawSessionInfoYaml);

                    // now that we have both 'telemetryVariables' and 'sessionInfo'
                    // we can cancel the monitoring
                    cts.Cancel();
                    break;
                }

                logger.LogInformation("waiting for telemetry data... {elapsed} secs", (DateTime.Now - startTime).TotalSeconds.ToString("F1"));
                await Task.Delay(TimeSpan.FromSeconds(1));
            }

            // wait for 2 seconds to exit
            bool success = monitorTask.Wait(2 * 1000);
            logger.LogInformation("Done. Status: {status}", success ? "successful" : "timeout-" + monitorTask.Status);


            // connection event handler
            void OnConnectStateChanged(object? _sender, ConnectStateChangedEventArgs e)
            {
                // if we already have the telemetryVariables, no more work to do
                if (telemetryVariables != null)
                    return;

                telemetryVariables = tc.GetTelemetryVariables().Result;
            }

            // raw sessionInfo event handler
            void OnRawSessionInfoUpdate(object? sender, string sessionInfoYaml)
            {
                // if we already have the rawSessionInfoYaml, no more work to do
                if (rawSessionInfoYaml != null)
                    return;

                rawSessionInfoYaml = sessionInfoYaml;
            }

            void writeVariablesFile(IEnumerable<TelemetryVariable> variables)
            {
                // open telemetryVariables file and write
                using (var writer = new StreamWriter(VARIABLES_FILENAME))
                {
                    // header
                    writer.WriteLine("name,type,length,isTimeValue,desc,units");

                    // data
                    foreach (var v in variables)
                    {
                        var line = $"{v.Name},{v.Type.Name},{v.Length},{v.IsTimeValue},{v.Desc},{v.Units}";
                        writer.WriteLine(line);
                    }
                }
                logger.LogInformation("telemetry variables saved to \"{filename}\"", VARIABLES_FILENAME);
            }
            void writeSessionInfoFile(string sessionInfoYaml)
            {
                // open sessionInfo file and write
                using (var writer = new StreamWriter(SESSIONINFO_FILENAME))
                {
                    writer.WriteLine(sessionInfoYaml);
                }
                logger.LogInformation("raw sessionInfo yml saved to \"{filename}\"", SESSIONINFO_FILENAME);
            }
        }

    }
}

