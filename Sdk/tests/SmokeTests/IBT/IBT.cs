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

    // resolve relative to the test assembly's own location
    private static readonly string IbtDataDirectory = Path.Combine(AppContext.BaseDirectory, "data", "ibt");

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

            var ibtFiles = Directory.GetFiles(IbtDataDirectory, "*.ibt");

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
    public async Task GetValueBeforeMonitorStarted_ReturnsNull()
    {
        // the provider hasn't read a header/var-buffer yet at this point (that only
        // happens once Monitor() starts pumping data), so GetValue() must return null instead of
        // throwing NullReferenceException. Requires Synchronous mode - GetValue() throws outright in
        // the default Async mode (see GetValueInAsyncMode_Throws).
        var ibtFile = Directory.GetFiles(IbtDataDirectory, "*.ibt").First();

        await using var client = TelemetryClient<TelemetryData>.Create(
            _logger,
            new IBTOptions(ibtFile),
            new ClientOptions { DeliveryMode = TelemetryDeliveryMode.Synchronous });

        Assert.Null(client.GetValue("RPM"));
    }

    [Fact]
    public async Task GetValueInAsyncMode_Throws()
    {
        // the SDK only works in one delivery mode at a time - GetValue() is unusable in the default
        // Async mode regardless of whether Monitor() has started, since production and consumption
        // run on independent tasks and there is no way to guarantee it reflects the delivered sample.
        var ibtFile = Directory.GetFiles(IbtDataDirectory, "*.ibt").First();

        await using var client = TelemetryClient<TelemetryData>.Create(_logger, new IBTOptions(ibtFile));

        Assert.Throws<InvalidOperationException>(() => client.GetValue("RPM"));
    }

    [Fact]
    public async Task GetValueAfterDispose_Throws()
    {
        var ibtFile = Directory.GetFiles(IbtDataDirectory, "*.ibt").First();

        var client = TelemetryClient<TelemetryData>.Create(_logger, new IBTOptions(ibtFile));
        await client.DisposeAsync();

        Assert.Throws<ObjectDisposedException>(() => client.GetValue("RPM"));
    }

    [Fact]
    public async Task GetTelemetryVariablesBeforeMonitorStarted_DoesNotPermanentlyCacheEmpty()
    {
        // calling GetTelemetryVariables() before the provider has read headers must
        // return an empty list without caching it - once Monitor() starts and headers become
        // available, later calls must return the real list, not the stale empty one.
        var ibtFile = Directory.GetFiles(IbtDataDirectory, "*.ibt").First();

        await using var client = TelemetryClient<TelemetryData>.Create(_logger, new IBTOptions(ibtFile));

        Assert.Empty(client.GetTelemetryVariables());

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(TIMEOUT_SECS));
        var sampleReceived = false;

        await client.Monitor(
            new TelemetryHandlers<TelemetryData>
            {
                OnTelemetryUpdate = _ =>
                {
                    sampleReceived = true;
                    cts.Cancel();
                    return Task.CompletedTask;
                }
            },
            cts.Token);

        Assert.True(sampleReceived);
        Assert.NotEmpty(client.GetTelemetryVariables());
    }

    [Fact]
    public async Task GetTelemetryVariablesAfterDispose_Throws()
    {
        var ibtFile = Directory.GetFiles(IbtDataDirectory, "*.ibt").First();

        var client = TelemetryClient<TelemetryData>.Create(_logger, new IBTOptions(ibtFile));
        await client.DisposeAsync();

        Assert.Throws<ObjectDisposedException>(() => client.GetTelemetryVariables());
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
    [MemberData(nameof(SyncTestModes))]
    public async Task VerifyDynamicLookupShape(string mode, Func<ILogger, ITelemetryClient<TelemetryData>> clientFactory)
    {
        _ = mode;   // used only to name the test cases in the test runner display

        // GetValue() only works in Synchronous mode - see GetValueInAsyncMode_Throws.
        await using var client = clientFactory(_logger);
        await BaseVerifyDynamicLookupShape(client, TIMEOUT_SECS);
    }

    [Theory]
    [MemberData(nameof(SyncTestModes))]
    public async Task VerifyTelemetryVariableTypesMatchGetValue(string mode, Func<ILogger, ITelemetryClient<TelemetryData>> clientFactory)
    {
        _ = mode;   // used only to name the test cases in the test runner display

        // GetValue() only works in Synchronous mode - see GetValueInAsyncMode_Throws.
        await using var client = clientFactory(_logger);
        await BaseVerifyTelemetryVariableTypesMatchGetValue(client, TIMEOUT_SECS);
    }

    /// <summary>
    /// same file set as <see cref="TestModes"/>, but configured for
    /// <see cref="TelemetryDeliveryMode.Synchronous"/> delivery, which guarantees GetValue() matches the
    /// sample delivered to the handler even under unthrottled (as-fast-as-possible) IBT playback.
    /// </summary>
    public static TheoryData<string, Func<ILogger, ITelemetryClient<TelemetryData>>> SyncTestModes
    {
        get
        {
            var testData = new TheoryData<string, Func<ILogger, ITelemetryClient<TelemetryData>>>();

            var ibtFiles = Directory.GetFiles(IbtDataDirectory, "*.ibt");

            foreach (var ibtFile in ibtFiles)
            {
                var fileName = Path.GetFileNameWithoutExtension(ibtFile);
                testData.Add(
                    $"IBT (sync) - {fileName}",
                    logger => TelemetryClient<TelemetryData>.Create(
                        logger,
                        new IBTOptions(ibtFile),
                        new ClientOptions { DeliveryMode = TelemetryDeliveryMode.Synchronous })
                );
            }

            return testData;
        }
    }

    [Theory]
    [MemberData(nameof(SyncTestModes))]
    public async Task VerifyDynamicLookupMatchesTypedValues_SynchronousMode(string mode, Func<ILogger, ITelemetryClient<TelemetryData>> clientFactory)
    {
        _ = mode;   // used only to name the test cases in the test runner display

        await using var client = clientFactory(_logger);
        await BaseVerifyDynamicLookupMatchesTypedValues(client, TIMEOUT_SECS);
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
    public async Task HandlerExceptionFaultsMonitor_SynchronousMode()
    {
        // a synchronous-mode handler exception must fault Monitor() directly,
        // exactly like Async mode, and must never be routed to OnError.
        var ibtFile = Directory.GetFiles(IbtDataDirectory, "*.ibt").First();

        await using var client = TelemetryClient<TelemetryData>.Create(
            _logger,
            new IBTOptions(ibtFile),
            new ClientOptions { DeliveryMode = TelemetryDeliveryMode.Synchronous });

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
    public async Task SynchronousModeWithoutTelemetryHandler_MonitorNoHandlersOverload_Throws()
    {
        var ibtFile = Directory.GetFiles(IbtDataDirectory, "*.ibt").First();

        await using var client = TelemetryClient<TelemetryData>.Create(
            _logger,
            new IBTOptions(ibtFile),
            new ClientOptions { DeliveryMode = TelemetryDeliveryMode.Synchronous });

        await Assert.ThrowsAsync<InvalidOperationException>(() => client.Monitor(CancellationToken.None));
    }

    [Fact]
    public async Task SynchronousModeWithoutTelemetryHandler_MonitorHandlersOverload_Throws()
    {
        var ibtFile = Directory.GetFiles(IbtDataDirectory, "*.ibt").First();

        await using var client = TelemetryClient<TelemetryData>.Create(
            _logger,
            new IBTOptions(ibtFile),
            new ClientOptions { DeliveryMode = TelemetryDeliveryMode.Synchronous });

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.Monitor(
                new TelemetryHandlers<TelemetryData>
                {
                    OnSessionInfoUpdate = _ => Task.CompletedTask
                },
                CancellationToken.None));
    }

    [Fact]
    public async Task MultipleHandlers_OneThrows_FaultsMonitorPromptly()
    {
        // with multiple handlers registered, a throwing handler must fault Monitor promptly.
        var ibtFile = Directory.GetFiles(IbtDataDirectory, "raygr22*").First();

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
        var ibtFile = Directory.GetFiles(IbtDataDirectory, "*.ibt").First();

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
        var ibtFile = Directory.GetFiles(IbtDataDirectory, "*.ibt").First();

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
