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

        using var client = clientFactory(_logger);
        Assert.NotNull(client);

        VariableSummary? _variableSummary = null;
        SessionSummary? _sessionInfoSummary = null;
        var connectionStateReceived = false;

        // monitor the data streams, until cancelled
        using var dataTasksCancellationSource = new CancellationTokenSource();

        var dataTasks = new[]
        {
            MonitorDataAsync(client.TelemetryDataStream.ReadAllAsync(dataTasksCancellationSource.Token),  async telemetryData =>
            {
                Assert.True(telemetryData.RPM.HasValue);
                Assert.True(telemetryData.RPM > 200);

                // only need one sample
                if (_variableSummary is null) {
                    var telemetryVars = await client.GetTelemetryVariables();
                    var variableSummary = VariableSummary.Create(telemetryVars);
                    _variableSummary = variableSummary;
                }
            }),

            MonitorDataAsync(client.SessionDataStream.ReadAllAsync(dataTasksCancellationSource.Token),  async sessionInfo =>
            {
                Assert.NotNull(sessionInfo);
                Assert.NotEmpty(sessionInfo.WeekendInfo.TrackName);

                // only need one sample
                if (_sessionInfoSummary is null)
                {
                    var sessionSummary = SessionSummary.Create(sessionInfo);
                    _sessionInfoSummary = sessionSummary;
                }
            }),

            MonitorDataAsync(client.ConnectStateStream.ReadAllAsync(dataTasksCancellationSource.Token),  async connectState =>
            {
                connectionStateReceived = true;
            }),

            MonitorDataAsync(client.ErrorStream.ReadAllAsync(dataTasksCancellationSource.Token),  async error =>
            {
                Assert.NotNull(error.Exception);
                _output.WriteLine($"Error received: {error.Exception.Message}");
            }),
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

    private async Task MonitorDataAsync<TData>(
        IAsyncEnumerable<TData> stream,
        Func<TData, Task> processItem)
    {
        try
        {
            await foreach (var data in stream)
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
            _output.WriteLine($"Error monitoring data channels: {ex.Message}");
        }
    }

    // [Theory]
    // [MemberData(nameof(IBTPlaybackSpeeds))]
    // public void IBTPlayback_DifferentSpeeds_ShouldWork(string speedDescription, int playbackSpeed)
    // {
    //     // Arrange
    //     var ibtOptions = new IBTOptions("../IBT_Tests/sessionInfo/race_test/lamborghinievogt3_spa up.ibt", playbackSpeed);
    //
    //     // Act & Assert
    //     using var client = TelemetryClient<TelemetryData>.Create(_logger, ibtOptions);
    //     Assert.NotNull(client);
    //     Assert.True(true, $"IBT playback at {speedDescription} completed successfully");
    // }
    //
    // public async Task LiveMode_GetTelemetryVariables_ShouldThrowWhenNotConnected()
    // {
    //     // Arrange
    //     using var client = TelemetryClient<TelemetryData>.Create(_logger);
    //
    //     // Act & Assert - Should throw when not connected to iRacing
    //     await Assert.ThrowsAsync<NullReferenceException>(async () =>
    //     {
    //         await client.GetTelemetryVariables();
    //     });
    // }

}
