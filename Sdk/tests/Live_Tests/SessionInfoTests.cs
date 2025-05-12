
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

using SVappsLAB.iRacingTelemetrySDK;

namespace Live_Tests
{
    public class SessionInfo : IClassFixture<Fixture>

    {
        Fixture _fixture;
        ITelemetryClient<TelemetryData> _telemetryClient;
        public SessionInfo(Fixture fixture)
        {
            _fixture = fixture;
            _telemetryClient = fixture.TelemetryClient;
        }

        [Theory]
        [InlineData("WeekendInfo", true)]
        [InlineData("SessionInfo", true)]
        [InlineData("CameraInfo", true)]
        [InlineData("RadioInfo", true)]
        [InlineData("DriverInfo", true)]
        [InlineData("SplitTimeInfo", true)]
        [InlineData("CarSetup", true)]
        [InlineData("_NoSuchProperty_", false)]
        public void EnsureRawYamlContainsSpecifiedStrings(string str, bool shouldExist)
        {
            var isValid = _telemetryClient.GetRawTelemetrySessionInfoYaml().Contains(str) == shouldExist;
            Assert.True(isValid, $"{str} not found in TelemetrySessionInfo");
        }

        [Fact]
        public void EnsureSessionInfoHasExpectedValues()
        {
            // to ensure this test has a chance of passing, lets pick values that are likely to find in ANY online session

            var si = _fixture.TelemetrySessionInfo;

            // WeekendInfo
            Assert.Equal("full", si.WeekendInfo.SimMode);
            Assert.Equal("Release", si.WeekendInfo.BuildType);
            Assert.Equal("Members", si.WeekendInfo.BuildTarget);

            // SessionInfo
            Assert.True(si.SessionInfo.Sessions.Count > 0);

            // CameraInfo
            Assert.True(si.CameraInfo.Groups.Count > 0);
            Assert.True(si.CameraInfo.Groups.Exists(g => g.GroupName == "Gearbox"));

            // RadioInfo
            Assert.True(si.RadioInfo.Radios.Count > 0);
            Assert.True(si.RadioInfo.Radios[0].NumFrequencies > 0);
            Assert.True(si.RadioInfo.Radios[0].Frequencies.Exists(f => f.FrequencyName == "@CLUB"));

            // DriverInfo
            Assert.True(si.DriverInfo.DriverCarIdleRPM > 0);
            Assert.True(si.DriverInfo.DriverCarIdleRPM < si.DriverInfo.DriverCarRedLine);

            // CarSetup
#pragma warning disable CS8604 // Possible null reference
            var updateCount = int.Parse(si.CarSetup["UpdateCount"] as string);
            Assert.True(updateCount > 0);
        }

    }
}
