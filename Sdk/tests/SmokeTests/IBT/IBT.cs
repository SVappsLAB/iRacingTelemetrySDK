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
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SVappsLAB.iRacingTelemetrySDK;


namespace SmokeTests;


[Trait("Category", "ibt")]
public class IBT : Base<IBT>
{
    const int TIMEOUT_SECS = 5;

    public IBT(ITestOutputHelper output) : base(output)
    {
    }

    /// <summary>
    /// find all the IBT files and use them for testing
    /// </summary>
    /// <returns>
    /// a collection of test cases where each case contains:
    /// - test name based on the IBT file name
    /// - factory function that creates a TelemetryClient configured for IBT file playback
    /// </returns>
    public static TheoryData<string, Func<ILogger, ITelemetryClient<TelemetryData>>> TestModes
    {
        get
        {
            var testData = new TheoryData<string, Func<ILogger, ITelemetryClient<TelemetryData>>>();

            var ibtDirectory = @"data\ibt";
            var ibtFiles = Directory.GetFiles(ibtDirectory, "*.ibt");

            foreach (var ibtFile in ibtFiles)
            {
                var fileName = Path.GetFileNameWithoutExtension(ibtFile);
                testData.Add(
                    $"IBT - {fileName}",
                    logger => TelemetryClient<TelemetryData>.Create(logger, new IBTOptions(ibtFile))
                );
            }

            return testData;
        }
    }

    [Theory]
    [MemberData(nameof(TestModes))]
    public override async Task BasicMonitoring(string _mode, Func<ILogger, ITelemetryClient<TelemetryData>> clientFactory)
    {
        await base.BasicMonitoring(_mode, clientFactory);
    }

    [Fact]
    public async Task InvalidFileThrows()
    {
        await Assert.ThrowsAsync<FileNotFoundException>(async () =>
        {
            var ibtFile = @"no-such-file-name";
            await using var tc = TelemetryClient<TelemetryData>.Create(NullLogger.Instance, new IBTOptions(ibtFile));
        });
    }

    [Theory]
    [MemberData(nameof(TestModes))]
    public async Task VerifyModelMatchesRawYaml(string mode, Func<ILogger, ITelemetryClient<TelemetryData>> clientFactory)
    {
        _ = mode;   // used only to name the test cases in the test runner display

        await using var client = clientFactory(_logger);
        await BaseVerifyModelMatchesRawYaml(client, TIMEOUT_SECS);
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
    public async Task MonitorCancellationCompletesDirectStreams(string mode, Func<ILogger, ITelemetryClient<TelemetryData>> clientFactory)
    {
        _ = mode;   // used only to name the test cases in the test runner display

        await using var client = clientFactory(_logger);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(TIMEOUT_SECS));

        var telemetryReceived = false;
        var telemetryTask = Task.Run(async () =>
        {
            await foreach (var telemetryData in client.TelemetryData)
            {
                telemetryReceived = true;
                cts.Cancel();
                break;
            }
        }, TestContext.Current.CancellationToken);

        await client.Monitor(cts.Token);
        await telemetryTask.WaitAsync(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);

        Assert.True(telemetryReceived);
    }

    [Theory]
    [MemberData(nameof(TestModes))]
    public async Task HandlerExceptionFaultsMonitor(string mode, Func<ILogger, ITelemetryClient<TelemetryData>> clientFactory)
    {
        _ = mode;   // used only to name the test cases in the test runner display

        await using var client = clientFactory(_logger);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(TIMEOUT_SECS));

        var actual = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.Monitor(
                new TelemetryHandlers<TelemetryData>
                {
                    OnTelemetryUpdate = _ => throw new InvalidOperationException("handler failed")
                },
                cts.Token));

        Assert.Equal("handler failed", actual.Message);
    }

    [Fact]
    public async Task MultipleHandlers_OneThrows_FaultsMonitorPromptly()
    {
        // regression test: with multiple handlers registered, a throwing handler must fault Monitor promptly.
        var ibtFile = Directory.GetFiles(@"data\ibt", "raygr22*").First();

        await using var client = TelemetryClient<TelemetryData>.Create(
            _logger,
            new IBTOptions(ibtFile, playBackSpeedMultiplier: 1));

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        var sw = Stopwatch.StartNew();

        var actual = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.Monitor(
                new TelemetryHandlers<TelemetryData>
                {
                    OnTelemetryUpdate = _ => throw new InvalidOperationException("handler failed"),
                    OnSessionInfoUpdate = _ => Task.CompletedTask,
                    OnConnectStateChanged = _ => Task.CompletedTask
                },
                cts.Token));

        sw.Stop();

        Assert.Equal("handler failed", actual.Message);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5),
            $"Monitor should fault promptly when a handler throws, but took {sw.Elapsed.TotalSeconds:F1}s");
    }

    [Fact]
    public async Task HungHandlerFaultsMonitorAfterShutdownTimeout()
    {
        var ibtFile = Directory.GetFiles(@"data\ibt", "*.ibt").First();

        await using var client = TelemetryClient<TelemetryData>.Create(
            _logger,
            new IBTOptions(ibtFile));

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(TIMEOUT_SECS));

        var exception = await Assert.ThrowsAsync<TimeoutException>(() =>
            client.Monitor(
                new TelemetryHandlers<TelemetryData>
                {
                    OnTelemetryUpdate = async _ =>
                    {
                        cts.Cancel();
                        await Task.Delay(Timeout.InfiniteTimeSpan);
                    }
                },
                cts.Token));

        Assert.Contains("Telemetry handler did not complete within 5 seconds", exception.Message);
    }

    [Fact]
    public async Task HandlerMonitorRejectsCompletedClientBeforeStartingHandlers()
    {
        var ibtFile = Directory.GetFiles(@"data\ibt", "*.ibt").First();

        await using var client = TelemetryClient<TelemetryData>.Create(
            _logger,
            new IBTOptions(ibtFile));

        await client.Monitor(CancellationToken.None);

        var handlerCalled = false;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.Monitor(
                new TelemetryHandlers<TelemetryData>
                {
                    OnTelemetryUpdate = _ =>
                    {
                        handlerCalled = true;
                        return Task.CompletedTask;
                    }
                },
                CancellationToken.None));

        Assert.False(handlerCalled);
    }

    public static TheoryData<string, int> IBTPlaybackSpeeds =>
        new()
        {
            { "Normal Speed", 1 },
            { "Fast Playback", 10 },
            { "Maximum Speed", int.MaxValue }
        };
}
