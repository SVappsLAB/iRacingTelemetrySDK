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

namespace SmokeTests;

public class Live : Base<Live>
{
    const int TIMEOUT_SECS = 5;

    public Live(ITestOutputHelper output) : base(output)
    {
    }

    public static TheoryData<string, Func<ILogger, ITelemetryClient<TelemetryData>>> TestModes =>
        new()
        {
        {
            "Live",
            logger => TelemetryClient<TelemetryData>.Create(logger)
        },
        };
    [Theory]
    [MemberData(nameof(TestModes))]
    public override async Task BasicMonitoring(string _mode, Func<ILogger, ITelemetryClient<TelemetryData>> clientFactory)
    {
        await base.BasicMonitoring(_mode, clientFactory);
    }

    [Theory]
    [MemberData(nameof(TestModes))]
    public async Task VerifyAllVariablesAreCovered(string _mode, Func<ILogger, ITelemetryClient<TelemetryData>> clientFactory)
    {
        await using var client = clientFactory(_logger);
        await BaseVerifyAllVariablesCovered(client, TIMEOUT_SECS);
    }

    [Theory]
    [MemberData(nameof(TestModes))]
    public async Task VerifyModelMatchesRawYaml(string _mode, Func<ILogger, ITelemetryClient<TelemetryData>> clientFactory)
    {
        await using var client = clientFactory(_logger);
        await BaseVerifyModelMatchesRawYaml(client, TIMEOUT_SECS);
    }
}
