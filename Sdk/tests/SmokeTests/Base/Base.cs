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

namespace SmokeTests;

public abstract partial class Base<T> where T : class
{
    private const int TIMEOUT_SECS = 5;

    protected readonly ILogger _logger;
    protected readonly ITestOutputHelper _output;

    protected Base(ITestOutputHelper output)
    {
        _output = output;
        _logger = new TestLogger(_output, LogLevel.Information);
    }

    /// <summary>
    /// performs basic monitoring of telemetry and session sessionInfo streams to validate client functionality.
    /// </summary>
    public virtual async Task BasicMonitoring(string _mode, Func<ILogger, ITelemetryClient<TelemetryData>> clientFactory)
    {

        await using var client = clientFactory(_logger);
        Assert.NotNull(client);

        VariableSummary? _variableSummary = null;
        SessionSummary? _sessionInfoSummary = null;
        var connectionStateReceived = false;

        // monitor the data streams, until cancelled
        using var dataTasksCancellationSource = new CancellationTokenSource();

        var dataTasks = new[]
        {
            MonitorData<TelemetryData>(client.TelemetryData, async telemetryData =>
            {
                Assert.True(telemetryData.RPM.HasValue);
                Assert.True(telemetryData.RPM > 200);
                Assert.True(telemetryData.CarIdxTrackSurface == null || telemetryData.CarIdxTrackSurface.Length >= 64);

                // only need one sample
                if (_variableSummary is null) {
                    var telemetryVars = client.GetTelemetryVariables();
                    var variableSummary = VariableSummary.Create(telemetryVars);
                    _variableSummary = variableSummary;
                }
            }, dataTasksCancellationSource.Token),

            MonitorData<TelemetrySessionInfo>(client.SessionData, async sessionInfo =>
            {
                Assert.NotNull(sessionInfo);
                Assert.NotEmpty(sessionInfo.WeekendInfo.TrackName);

                // only need one sample
                if (_sessionInfoSummary is null)
                {
                    var sessionSummary = SessionSummary.Create(sessionInfo);
                    _sessionInfoSummary = sessionSummary;
                }
            }, dataTasksCancellationSource.Token),

            MonitorData<ConnectState>(client.ConnectStates, async connectState =>
            {
                Assert.True(client.IsConnected);

                connectionStateReceived = true;
            }, dataTasksCancellationSource.Token),

            MonitorData<Exception>(client.Errors, async error =>
            {
                Assert.NotNull(error);
                _output.WriteLine($"Error received: {error.Message}");
            }, dataTasksCancellationSource.Token),
        };

        // monitor for a few seconds then cancel the operation
        using var timeoutTokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(TIMEOUT_SECS));
        await client.Monitor(timeoutTokenSource.Token);


        // when the timeout occurs, we are done monitoring the data streams, so cancel them.
        dataTasksCancellationSource.Cancel();
        await Task.WhenAll(dataTasks);  // wait for them to complete

        Assert.NotNull(_variableSummary);
        _output.WriteLine($"Variables Summary: {_variableSummary}");

        Assert.NotNull(_sessionInfoSummary);
        _output.WriteLine($"Session Summary: {_sessionInfoSummary}");

        Assert.True(connectionStateReceived);
    }

    private async Task MonitorData<TData>(
        IAsyncEnumerable<TData> stream,
        Func<TData, Task> processItem,
        CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var data in stream.WithCancellation(cancellationToken))
            {
                await processItem(data);
            }
        }
        catch (OperationCanceledException e) when (e.CancellationToken.IsCancellationRequested)
        {
            // expected when the cancellation token is triggered
        }
        catch (Exception ex)
        {
            _output.WriteLine($"Error monitoring data streams: {ex.Message}");
        }
    }
}
