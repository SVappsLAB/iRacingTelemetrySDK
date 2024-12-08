/**
 * Copyright (C)2024 Scott Velez
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

namespace IBT_Tests.SessionInfo
{
    public class TestSession_Fixture : Fixture
    {
        public TestSession_Fixture() : base(Path.Combine(Directory.GetCurrentDirectory(), @"data/race_test/lamborghinievogt3_spa up.ibt"))
        {
        }
    }
    public class RoadRaceSession_Fixture : Fixture
    {
        public RoadRaceSession_Fixture() : base(Path.Combine(Directory.GetCurrentDirectory(), @"data/race_road/audir8lmsevo2gt3_spa up.ibt"))
        {
        }
    }
    public class OvalRaceSession_Fixture : Fixture
    {
        public OvalRaceSession_Fixture() : base(Path.Combine(Directory.GetCurrentDirectory(), @"data/race_oval/latemodel_southboston.ibt"))
        {
        }
    }

    // tests common to all IBT fixtures
    public abstract class SessionInfoTestBase
    {
        protected Fixture _fixture;
        public SessionInfoTestBase(Fixture fixture)
        {
            _fixture = fixture;
        }
        public object? GetActualSessionValue(string key)
        {
            var si = _fixture.TelemetrySessionInfo;

            var val = key switch
            {
                // WeekendInfo
                "WeekendInfo.TrackName" => si.WeekendInfo.TrackName,
                "WeekendInfo.TrackID" => si.WeekendInfo.TrackID,
                "WeekendInfo.EventType" => si.WeekendInfo.EventType,
                "WeekendInfo.Category" => si.WeekendInfo.Category,
                // WeekendInfo.WeekendOptions
                "WeekendInfo.WeekendOptions.NumStarters" => si.WeekendInfo.WeekendOptions.NumStarters,
                // WeekendInfo.TelemetryOptions
                "WeekendInfo.TelemetryOptions.TelemetryDiskFile" => si.WeekendInfo.TelemetryOptions.TelemetryDiskFile,
                // SessionInfo
                "SessionInfo.SessionsCount" => si.SessionInfo.Sessions.Count,
                "SessionInfo.SessionType" => si.SessionInfo.Sessions[0].SessionType,
                // CameraInfo
                "CameraInfo.GroupsCount" => si.CameraInfo.Groups.Count,
                // RadioInfo
                "RadioInfo.RadiosCount" => si.RadioInfo.Radios.Count,
                "RadioInfo.NumFrequencies" => si.RadioInfo.Radios[0].NumFrequencies,
                // DriverInfo
                "DriverInfo.DriverCarIdx" => si.DriverInfo.DriverCarIdx,
                "DriverInfo.DriverCarSLShiftRPM" => si.DriverInfo.DriverCarSLShiftRPM,
                "DriverInfo.CarNumber" => si.DriverInfo.Drivers[0].CarNumber,
                // SplitTimeInfo
                "SplitTimeInfo.SectorStartPct" => si.SplitTimeInfo.Sectors[1].SectorStartPct,
                // CarSetup
                "CarSetup.UpdateCount" => si.CarSetup["UpdateCount"],
                _ => throw new NotImplementedException()
            };
            return val;
        }
        public virtual void VerifySessionValue(string key, object expected)
        {
            var actualVal = GetActualSessionValue(key);

            Assert.Equal(expected, actualVal);
        }



    }

    public class SessionInfo_Test : SessionInfoTestBase, IClassFixture<TestSession_Fixture>
    {
        public SessionInfo_Test(TestSession_Fixture fixture) : base(fixture) { }

        [Theory]
        [InlineData("WeekendInfo.TrackName", "spa up")]
        [InlineData("WeekendInfo.TrackID", 163)]
        [InlineData("WeekendInfo.EventType", "Test")]
        [InlineData("WeekendInfo.Category", "Road")]
        [InlineData("WeekendInfo.WeekendOptions.NumStarters", 0)]
        [InlineData("WeekendInfo.TelemetryOptions.TelemetryDiskFile", "")]
        [InlineData("SessionInfo.SessionsCount", 1)]
        [InlineData("SessionInfo.SessionType", "Offline Testing")]
        [InlineData("CameraInfo.GroupsCount", 22)]
        [InlineData("RadioInfo.RadiosCount", 1)]
        [InlineData("RadioInfo.NumFrequencies", 7)]
        [InlineData("DriverInfo.DriverCarIdx", 0)]
        [InlineData("DriverInfo.DriverCarSLShiftRPM", 8050.000f)]
        [InlineData("DriverInfo.CarNumber", "52")]
        [InlineData("SplitTimeInfo.SectorStartPct", 0.310613f)]
        [InlineData("CarSetup.UpdateCount", "1")]
        public override void VerifySessionValue(string key, object expected) => base.VerifySessionValue(key, expected);
    }
    public class SessionInfo_Race_Road : SessionInfoTestBase, IClassFixture<RoadRaceSession_Fixture>
    {
        public SessionInfo_Race_Road(RoadRaceSession_Fixture fixture) : base(fixture) { }

        [Theory]
        [InlineData("WeekendInfo.TrackName", "spa up")]
        [InlineData("WeekendInfo.TrackID", 163)]
        [InlineData("WeekendInfo.EventType", "Race")]
        [InlineData("WeekendInfo.Category", "Road")]
        [InlineData("WeekendInfo.WeekendOptions.NumStarters", 60)]
        [InlineData("WeekendInfo.TelemetryOptions.TelemetryDiskFile", "")]
        [InlineData("SessionInfo.SessionsCount", 4)]
        [InlineData("SessionInfo.SessionType", "Practice")]
        [InlineData("CameraInfo.GroupsCount", 22)]
        [InlineData("RadioInfo.RadiosCount", 1)]
        [InlineData("RadioInfo.NumFrequencies", 6)]
        [InlineData("DriverInfo.DriverCarIdx", 10)]
        [InlineData("DriverInfo.DriverCarSLShiftRPM", 8050.000f)]
        [InlineData("DriverInfo.CarNumber", "0")]
        [InlineData("SplitTimeInfo.SectorStartPct", 0.310613f)]
        [InlineData("CarSetup.UpdateCount", "10")]
        public override void VerifySessionValue(string key, object expected) => base.VerifySessionValue(key, expected);
    }
    public class SessionInfo_Oval_Road : SessionInfoTestBase, IClassFixture<OvalRaceSession_Fixture>
    {
        public SessionInfo_Oval_Road(OvalRaceSession_Fixture fixture) : base(fixture) { }

        [Theory]
        [InlineData("WeekendInfo.TrackName", "southboston")]
        [InlineData("WeekendInfo.TrackID", 14)]
        [InlineData("WeekendInfo.EventType", "Race")]
        [InlineData("WeekendInfo.Category", "Oval")]
        [InlineData("WeekendInfo.WeekendOptions.NumStarters", 20)]
        [InlineData("WeekendInfo.TelemetryOptions.TelemetryDiskFile", "")]
        [InlineData("SessionInfo.SessionsCount", 3)]
        [InlineData("SessionInfo.SessionType", "Practice")]
        [InlineData("CameraInfo.GroupsCount", 20)]
        [InlineData("RadioInfo.RadiosCount", 1)]
        [InlineData("RadioInfo.NumFrequencies", 7)]
        [InlineData("DriverInfo.DriverCarIdx", 1)]
        [InlineData("DriverInfo.DriverCarSLShiftRPM", 7050.000f)]
        [InlineData("DriverInfo.CarNumber", "0")]
        [InlineData("SplitTimeInfo.SectorStartPct", 0.500000f)]
        [InlineData("CarSetup.UpdateCount", "1")]
        public override void VerifySessionValue(string key, object expected) => base.VerifySessionValue(key, expected);
    }
}
