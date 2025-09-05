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

using System;

#nullable disable
namespace SVappsLAB.iRacingTelemetrySDK
{
    public class WeekendInfo
    {
        public string TrackName { get; set; } // spa up
        public int TrackID { get; set; } // 143
        public string TrackLength { get; set; } // 6.93 km
        public string TrackLengthOfficial { get; set; } // 7.00 km
        public string TrackDisplayName { get; set; } // Circuit de Spa-Francorchamps
        public string TrackDisplayShortName { get; set; } // Spa
        public string TrackConfigName { get; set; } // Grand Prix Pit
        public string TrackCity { get; set; } // Francorchamps
        public string TrackState { get; set; } // MA - Maine
        public string TrackCountry { get; set; } // Belgium
        public string TrackAltitude { get; set; } // 414.45 m
        public string TrackLatitude { get; set; } // 50.444061 m
        public string TrackLongitude { get; set; } // 5.965178 m
        public string TrackNorthOffset { get; set; } // 5.8076 rad
        public int TrackNumTurns { get; set; } // 1
        public string TrackPitSpeedLimit { get; set; } // 60.00 kph
        public string TrackPaceSpeed { get; set; } // 60.00 kph
        public int TrackNumPitStalls { get; set; } // 1
        public string TrackType { get; set; } // road course
        public string TrackDirection { get; set; } // neutral
        public string TrackWeatherType { get; set; } // Generated / Dynamic Sky
        public string TrackSkies { get; set; } // Partly Cloudy
        public string TrackSurfaceTemp { get; set; } // 32.53 C
        public string TrackSurfaceTempCrew { get; set; } // 32.53 C
        public string TrackAirTemp { get; set; } // 18.28 C
        public string TrackAirPressure { get; set; } // 28.48 Hg
        public string TrackAirDensity { get; set; } // 1.13 kg/m^3
        public string TrackWindVel { get; set; } // 1.15 m/s
        public string TrackWindDir { get; set; } // 4.10 rad
        public string TrackRelativeHumidity { get; set; } // 53 %
        public string TrackFogLevel { get; set; } // 0 %
        public string TrackPrecipitation { get; set; } // 0 %
        public int TrackCleanup { get; set; } // 0
        public int TrackDynamicTrack { get; set; } // 0
        public string TrackVersion { get; set; } // 2020.11.28.01
        public int SeriesID { get; set; } // 0
        public int SeasonID { get; set; } // 0
        public int SessionID { get; set; } // 0
        public int SubSessionID { get; set; } // 0
        public int LeagueID { get; set; } // 0
        public int Official { get; set; } // 0
        public int RaceWeek { get; set; } // 0
        public string EventType { get; set; } // Test
        public string Category { get; set; } // Road
        public string SimMode { get; set; } // full
        public int TeamRacing { get; set; } // 0
        public int MinDrivers { get; set; } // 0
        public int MaxDrivers { get; set; } // 0
        public string DCRuleSet { get; set; } // None
        public int QualifierMustStartRace { get; set; } // 0
        public int NumCarClasses { get; set; } // 1
        public int NumCarTypes { get; set; } // 1
        public string AIRosterName { get; set; } // ""
        public int HeatRacing { get; set; } // 0
        public string BuildType { get; set; } // Release
        public string BuildTarget { get; set; } // Members
        public string BuildVersion { get; set; } // 2020.12.08.06
        public string RaceFarm { get; set; } // US-Bos

        public WeekendOptions WeekendOptions { get; set; }
        public TelemetryOptions TelemetryOptions { get; set; }

    }

    public class WeekendOptions
    {
        public int NumStarters { get; set; } // 0
        public string StartingGrid { get; set; } // single file
        public string QualifyScoring { get; set; } // best lap
        public string CourseCautions { get; set; } // off
        public int StandingStart { get; set; } // 0
        public int ShortParadeLap { get; set; } // 0
        public string Restarts { get; set; } // single file
        public string WeatherType { get; set; } // Generated / Dynamic Sky
        public string Skies { get; set; } // Partly Cloudy
        public string WindDirection { get; set; } // N
        public string WindSpeed { get; set; } // 3.22 km/h
        public string WeatherTemp { get; set; } // 25.56 C
        public string RelativeHumidity { get; set; } // 55 %
        public string FogLevel { get; set; } // 0 %
        public string TimeOfDay { get; set; } // 12:55 pm
        public DateTime Date { get; set; } // 2021-03-01
        public int EarthRotationSpeedupFactor { get; set; } // 1
        public int Unofficial { get; set; } // 1
        public string CommercialMode { get; set; } // consumer
        public string NightMode { get; set; } // variable
        public int IsFixedSetup { get; set; } // 0
        public string StrictLapsChecking { get; set; } // default
        public int HasOpenRegistration { get; set; } // 0
        public int HardcoreLevel { get; set; } // 1
        public int NumJokerLaps { get; set; } // 0
        public string IncidentLimit { get; set; } // unlimited
        public string FastRepairsLimit { get; set; } // unlimited
        public int GreenWhiteCheckeredLimit { get; set; } // 0

    }

    public class TelemetryOptions
    {
        public string TelemetryDiskFile { get; set; } // ""

    }
}
#nullable enable
