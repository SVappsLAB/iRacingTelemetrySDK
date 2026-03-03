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

using System.Diagnostics;
using SVappsLAB.iRacingTelemetrySDK;

namespace SmokeTests;

public abstract partial class Base<T> where T : class
{
    protected async Task BaseVerifyAllVariablesCovered(ITelemetryClient<TelemetryData> client, int timeoutSecs = 5)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSecs));

        bool variablesReceived = false;
        List<string>? allMissingVariables = null;

        // start telemetry data consumption
        var telemetryTask = Task.Run(async () =>
        {
            await foreach (var telemetryData in client.TelemetryData.WithCancellation(cts.Token))
            {
                variablesReceived = true;

                // get all available variable definitions from iRacing
                var availableVariables = client.GetTelemetryVariables();
                var availableVariableNames = availableVariables.Select(v => v.Name).ToHashSet();

                // get all TelemetryVar enum values
                var enumVariables = Enum.GetValues<TelemetryVar>()
                    .Select(e => e.ToString())
                    .ToHashSet();

                // find variables that exist in iRacing but not in our enum
                allMissingVariables = availableVariableNames
                    .Where(varName => !enumVariables.Contains(varName))
                    .OrderBy(varName => varName)
                    .ToList();

                cts.Cancel();
                break; // exit after first item
            }
        }, cts.Token);

        // start monitoring
        var monitorTask = client.Monitor(cts.Token);

        // wait for either task to complete
        await Task.WhenAny(telemetryTask, monitorTask);

        Assert.True(variablesReceived, "Telemetry data was not received within the timeout period.");

        if (allMissingVariables != null && allMissingVariables.Count > 0)
        {
            Assert.Fail($"Found {allMissingVariables.Count} variables in iRacing telemetry that are missing from TelemetryVar enum: {string.Join(", ", allMissingVariables)}");
        }
    }
}
