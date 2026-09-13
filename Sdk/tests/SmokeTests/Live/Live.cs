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

using System.IO.MemoryMappedFiles;
using Microsoft.Extensions.Logging;
using SVappsLAB.iRacingTelemetrySDK;

namespace SmokeTests;

[Trait("Category", "live")]
public class Live : Base<Live>
{
    const int TIMEOUT_SECS = 5;

    public Live(ITestOutputHelper output) : base(output)
    {
        Assert.SkipUnless(IsIRacingConnected(), "iRacing is not running with an active session");
    }

    static bool IsIRacingConnected()
    {
        const int STATUS_OFFSET = 4;    // irsdk_header.status follows the int 'ver' field
        const int STATUS_CONNECTED = 1;

        if (!OperatingSystem.IsWindows())
            return false;

        try
        {
            using var mmFile = MemoryMappedFile.OpenExisting(@"Local\IRSDKMemMapFileName");
            using var accessor = mmFile.CreateViewAccessor(0, STATUS_OFFSET + sizeof(int), MemoryMappedFileAccess.Read);
            return (accessor.ReadInt32(STATUS_OFFSET) & STATUS_CONNECTED) != 0;
        }
        catch (FileNotFoundException)
        {
            return false;
        }
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
    public async Task VerifyAllVariablesAreCovered(string mode, Func<ILogger, ITelemetryClient<TelemetryData>> clientFactory)
    {
        _ = mode;   // used only to name the test cases in the test runner display

        await using var client = clientFactory(_logger);
        await BaseVerifyAllVariablesCovered(client, TIMEOUT_SECS);
    }

    [Theory]
    [MemberData(nameof(TestModes))]
    public async Task VerifyModelMatchesRawYaml(string mode, Func<ILogger, ITelemetryClient<TelemetryData>> clientFactory)
    {
        _ = mode;   // used only to name the test cases in the test runner display

        await using var client = clientFactory(_logger);
        await BaseVerifyModelMatchesRawYaml(client, TIMEOUT_SECS);
    }

    [Fact]
    public async Task VerifyTelemetryVariableTypesMatchGetValue()
    {
        // GetValue() only works in Synchronous mode
        await using var client = TelemetryClient<TelemetryData>.Create(
            _logger,
            ibtOptions: null,
            clientOptions: new ClientOptions { DeliveryMode = TelemetryDeliveryMode.Synchronous });
        await BaseVerifyTelemetryVariableTypesMatchGetValue(client, TIMEOUT_SECS);
    }

    [Fact]
    public async Task HandlerExceptionFaultsMonitor_SynchronousMode()
    {
        // a synchronous-mode handler exception must fault Monitor() directly,
        // exactly like Async mode, and must never be routed to OnError. Requires iRacing running
        // with an active session sending telemetry (see Sdk/tests/README.md).
        await using var client = TelemetryClient<TelemetryData>.Create(
            _logger,
            ibtOptions: null,
            clientOptions: new ClientOptions { DeliveryMode = TelemetryDeliveryMode.Synchronous });

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(TIMEOUT_SECS));

        var errorsReceived = new List<Exception>();

        var actual = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.Monitor(
                new TelemetryHandlers<TelemetryData>
                {
                    OnTelemetryUpdate = _ => throw new InvalidOperationException("handler failed"),
                    OnError = e =>
                    {
                        errorsReceived.Add(e);
                        return Task.CompletedTask;
                    }
                },
                cts.Token));

        Assert.Equal("handler failed", actual.Message);
        Assert.Empty(errorsReceived);
    }

    [Fact]
    public async Task SynchronousModeWithoutTelemetryHandler_Throws()
    {
        await using var client = TelemetryClient<TelemetryData>.Create(
            _logger,
            ibtOptions: null,
            clientOptions: new ClientOptions { DeliveryMode = TelemetryDeliveryMode.Synchronous });

        await Assert.ThrowsAsync<InvalidOperationException>(() => client.Monitor(CancellationToken.None));
    }
}
