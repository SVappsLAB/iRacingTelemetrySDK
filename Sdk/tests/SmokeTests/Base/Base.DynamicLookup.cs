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

using SVappsLAB.iRacingTelemetrySDK;

namespace SmokeTests;

public abstract partial class Base<T> where T : class
{
    /// <summary>
    /// verifies the string-based dynamic lookup (GetValue) surface. Requires <see cref="TelemetryDeliveryMode.Synchronous"/>.
    /// </summary>
    protected async Task BaseVerifyDynamicLookupShape(ITelemetryClient<TelemetryData> client, int timeoutSecs = 5)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSecs));

        var sampleChecked = false;

        await client.Monitor(
            new TelemetryHandlers<TelemetryData>
            {
                OnTelemetryUpdate = telemetryData =>
                {
                    // only need one sample
                    if (sampleChecked)
                        return Task.CompletedTask;
                    sampleChecked = true;

                    // scalar float - type only, plus case-insensitive name matching
                    Assert.IsType<float>(client.GetValue("RPM"));
                    Assert.IsType<float>(client.GetValue("rpm"));
                    Assert.IsType<float>(client.GetValue("Rpm"));

                    // scalar int
                    Assert.IsType<int>(client.GetValue("SessionNum"));

                    // array (enum-typed in the generated struct, plain int[] via dynamic lookup)
                    // CarIdxTrackSurface can legitimately be unavailable (null) - see Base.cs's BasicMonitoring check
                    var dynamicTrackSurface = client.GetValue("CarIdxTrackSurface");
                    if (telemetryData.CarIdxTrackSurface == null)
                    {
                        Assert.Null(dynamicTrackSurface);
                    }
                    else
                    {
                        var trackSurface = Assert.IsType<int[]>(dynamicTrackSurface);
                        Assert.Equal(telemetryData.CarIdxTrackSurface.Length, trackSurface.Length);
                    }

                    // unknown variable name
                    Assert.Null(client.GetValue("NotARealVariableName"));

                    cts.Cancel();
                    return Task.CompletedTask;
                },
            },
            cts.Token);

        Assert.True(sampleChecked, "Telemetry data was not received within the timeout period.");
    }

    /// <summary>
    /// verifies the string-based dynamic lookup (GetValue) returns values that exactly agree with the
    /// strongly-typed, source-generated struct for the same sample. Only valid under
    /// <see cref="TelemetryDeliveryMode.Synchronous"/>, which guarantees GetValue() reflects the sample
    /// delivered to the handler for the handler's full duration.
    /// </summary>
    protected async Task BaseVerifyDynamicLookupMatchesTypedValues(ITelemetryClient<TelemetryData> client, int timeoutSecs = 5)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSecs));

        var sampleChecked = false;

        await client.Monitor(
            new TelemetryHandlers<TelemetryData>
            {
                OnTelemetryUpdate = telemetryData =>
                {
                    // only need one sample
                    if (sampleChecked)
                        return Task.CompletedTask;
                    sampleChecked = true;

                    // scalar float
                    Assert.Equal(telemetryData.RPM, Assert.IsType<float>(client.GetValue("RPM")));

                    // scalar int
                    Assert.Equal(telemetryData.SessionNum, Assert.IsType<int>(client.GetValue("SessionNum")));

                    // array (enum-typed in the generated struct, plain int[] via dynamic lookup)
                    // CarIdxTrackSurface can legitimately be unavailable (null) - see Base.cs's BasicMonitoring check
                    var dynamicTrackSurface = client.GetValue("CarIdxTrackSurface");
                    if (telemetryData.CarIdxTrackSurface == null)
                    {
                        Assert.Null(dynamicTrackSurface);
                    }
                    else
                    {
                        var trackSurface = Assert.IsType<int[]>(dynamicTrackSurface);
                        Assert.Equal(telemetryData.CarIdxTrackSurface.Length, trackSurface.Length);
                        for (var i = 0; i < trackSurface.Length; i++)
                        {
                            Assert.Equal((int)telemetryData.CarIdxTrackSurface[i], trackSurface[i]);
                        }
                    }

                    // unknown variable name
                    Assert.Null(client.GetValue("NotARealVariableName"));

                    cts.Cancel();
                    return Task.CompletedTask;
                },
            },
            cts.Token);

        Assert.True(sampleChecked, "Telemetry data was not received within the timeout period.");
    }

    /// <summary>
    /// verifies that every variable's advertised <see cref="TelemetryVariable.Type"/> from
    /// <see cref="ITelemetryClient{T}.GetTelemetryVariables"/> agrees with the runtime type
    /// <see cref="ITelemetryClient{T}.GetValue"/> actually returns for that variable. Catches drift like
    /// advertising <c>string[]</c> while returning a single decoded <c>string</c>, or <c>uint</c> while
    /// returning <c>int</c>. Requires <see cref="TelemetryDeliveryMode.Synchronous"/>, since GetValue()
    /// throws <see cref="InvalidOperationException"/> in <see cref="TelemetryDeliveryMode.Async"/>.
    /// </summary>
    protected async Task BaseVerifyTelemetryVariableTypesMatchGetValue(ITelemetryClient<TelemetryData> client, int timeoutSecs = 5)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSecs));

        var sampleChecked = false;

        await client.Monitor(
            new TelemetryHandlers<TelemetryData>
            {
                OnTelemetryUpdate = _ =>
                {
                    // only need one sample
                    if (sampleChecked)
                        return Task.CompletedTask;
                    sampleChecked = true;

                    var variables = client.GetTelemetryVariables();
                    Assert.NotEmpty(variables);

                    var mismatches = new List<string>();
                    foreach (var v in variables)
                    {
                        // a variable can legitimately have no value yet for this sample - GetValue()
                        // returning null for it isn't a type mismatch
                        var value = client.GetValue(v.Name);
                        if (value == null)
                            continue;

                        var actualType = value.GetType();
                        if (actualType != v.Type)
                        {
                            mismatches.Add($"{v.Name}: advertised {v.Type?.Name ?? "null"}, actual {actualType.Name}");
                        }
                    }

                    if (mismatches.Count > 0)
                    {
                        Assert.Fail(
                            $"GetTelemetryVariables().Type disagreed with GetValue()'s runtime type for " +
                            $"{mismatches.Count} variable(s):\n{string.Join("\n", mismatches)}");
                    }

                    cts.Cancel();
                    return Task.CompletedTask;
                },
            },
            cts.Token);

        Assert.True(sampleChecked, "Telemetry data was not received within the timeout period.");
    }
}
