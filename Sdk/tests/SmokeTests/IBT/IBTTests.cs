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
using Microsoft.Extensions.Logging.Abstractions;
using SVappsLAB.iRacingTelemetrySDK;


namespace SmokeTests;


public class IBTTests : Base<IBTTests>
{
    const int TIMEOUT_SECS = 5;

    public IBTTests(ITestOutputHelper output) : base(output)
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
    public void InvalidFileThrows()
    {
        Assert.Throws<FileNotFoundException>(() =>
        {
            var ibtFile = @"no-such-file-name";
            using var tc = TelemetryClient<TelemetryData>.Create(NullLogger.Instance, new IBTOptions(ibtFile));
        });
    }

    [Theory]
    [MemberData(nameof(TestModes))]
    public async Task VerifyModelMatchesRawYaml(string mode, Func<ILogger, ITelemetryClient<TelemetryData>> clientFactory)
    {
        using var client = clientFactory(_logger);
        await BaseVerifyModelMatchesRawYaml(client, TIMEOUT_SECS);
    }

    public static TheoryData<string, int> IBTPlaybackSpeeds =>
        new()
        {
            { "Normal Speed", 1 },
            { "Fast Playback", 10 },
            { "Maximum Speed", int.MaxValue }
        };


    // [Theory]
    // [MemberData(nameof(IBTPlaybackSpeeds))]
    // public void IBTPlayback_DifferentSpeeds_ShouldWork(string speedDescription, int playbackSpeed)
    // {
    //     // Arrange
    //     var ibtOptions = new IBTOptions("../IBT_Tests/data/race_test/lamborghinievogt3_spa up.ibt", playbackSpeed);
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
