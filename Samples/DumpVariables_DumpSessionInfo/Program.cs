/**
 * Copyright (C) 2024-2025 Scott Velez
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

namespace DumpVariables_DumpSessionInfo
{
    [RequiredTelemetryVars([TelemetryVar.RPM])]
    internal class Program
    {

        static async Task Main(string[] args)
        {
            string timeStamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            string VARIABLES_FILENAME = $"iRacingVariables-{timeStamp}.csv";
            string SESSIONINFO_FILENAME = $"IRacingSessionInfo-{timeStamp}.yaml";
            // amount of time to wait for data before giving up
            const int WAIT_FOR_DATA_SECS = 30;

            IEnumerable<TelemetryVariable>? telemetryVariables = null;
            string? rawSessionInfoYaml = null;

            // if you pass in a IBT filename, we'll use that, otherwise default to LIVE mode
            var ibtOptions = args.Length == 1 ? new IBTOptions(args[0]) : null;

            var logger = LoggerFactory
                    .Create(builder => builder
                    .SetMinimumLevel(LogLevel.Debug)
                    .AddConsole())
                    .CreateLogger("logger");

            logger.LogInformation("pulling data from \'{source}\'", ibtOptions != null ? "IBT file session" : "Live iRacing session");

            // create telemetry client
            await using var tc = TelemetryClient<TelemetryData>.Create(logger, ibtOptions);

            // give up if we don't receive the session info in time
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(WAIT_FOR_DATA_SECS));

            // collect the session info and variables list from the first session info update, then exit
            var handlers = new TelemetryHandlers<TelemetryData>
            {
                OnRawSessionInfoUpdate = rawYaml =>
                {
                    rawSessionInfoYaml = rawYaml;

                    // the variables list is available once we're connected, which is true by the
                    // time session info arrives. note: GetTelemetryVariables() is synchronous
                    telemetryVariables = tc.GetTelemetryVariables();

                    // we have everything we need, so stop monitoring
                    cts.Cancel();
                    return Task.CompletedTask;
                }
            };

            // start monitoring - returns when we cancel (data received) or the timeout elapses
            await tc.Monitor(handlers, cts.Token);

            // if we have data, save it
            if (telemetryVariables != null && rawSessionInfoYaml != null)
            {
                // save telemetryVariables to file
                writeVariablesFile(telemetryVariables);

                // save sessionInfo yaml to file
                writeSessionInfoFile(rawSessionInfoYaml);

                logger.LogInformation("Done. Status: successful");
            }
            else
            {
                logger.LogWarning("Done. Status: timeout - no session info received within {secs} secs", WAIT_FOR_DATA_SECS);
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
