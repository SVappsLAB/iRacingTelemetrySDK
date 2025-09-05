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
 * limitations under the License.using Microsoft.CodeAnalysis;
**/

using Microsoft.Extensions.Logging.Abstractions;
using SVappsLAB.iRacingTelemetrySDK;

namespace SmokeTests;
public partial class Live : Base<Live>
{
    const int TIMEOUT_SECS = 5;

    [Fact]
    public async Task VerifyModelMatchesRawYaml()
    {
        var client = TelemetryClient<TelemetryData>.Create(NullLogger.Instance);
        await BaseVerifyModelMatchesRawYaml(client, TIMEOUT_SECS);
    }
}
