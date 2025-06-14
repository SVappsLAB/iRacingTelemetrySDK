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

using System.Collections.Generic;

#nullable disable
namespace SVappsLAB.iRacingTelemetrySDK.Models
{
    public class DriverInfo
    {
        public int DriverCarIdx { get; set; } // 0
        public int DriverUserID { get; set; } // 22176
        public int PaceCarIdx { get; set; } // -1
        public float DriverHeadPosX { get; set; } // in units of length (mm, m, etc.)
        public float DriverHeadPosY { get; set; } // in units of length (mm, m, etc.)
        public float DriverHeadPosZ { get; set; } // in units of length (mm, m, etc.)
        public int DriverCarIsElectric { get; set; } // 0
        public float DriverCarIdleRPM { get; set; } // 875.000
        public float DriverCarRedLine { get; set; } // 7525.000
        public int DriverCarEngCylinderCount { get; set; } // 4
        public float DriverCarFuelKgPerLtr { get; set; } // 0.750
        public float DriverCarFuelMaxLtr { get; set; } // 44.987
        public float DriverCarMaxFuelPct { get; set; } // in percentage
        public int DriverCarGearNumForward { get; set; } // 6
        public int DriverCarGearNeutral { get; set; } // 1
        public int DriverCarGearReverse { get; set; } // 1
        public float DriverCarSLFirstRPM { get; set; } // 5600.000
        public float DriverCarSLShiftRPM { get; set; } // 7200.000
        public float DriverCarSLLastRPM { get; set; } // 7200.000
        public float DriverCarSLBlinkRPM { get; set; } // 7700.000
        public string DriverCarVersion { get; set; } // "2025.06.11.02"
        public float DriverPitTrkPct { get; set; } // in percentage
        public float DriverCarEstLapTime { get; set; } // 19.6722
        public string DriverSetupName { get; set; } // "baseline.sto"
        public int DriverSetupIsModified { get; set; } // 0
        public string DriverSetupLoadTypeName { get; set; } // "user"
        public int DriverSetupPassedTech { get; set; } // 1
        public int DriverIncidentCount { get; set; } // 0
        public float DriverBrakeCurvingFactor { get; set; } // 0.001
        public List<Driver> Drivers { get; set; }
        public List<DriverTire> DriverTires { get; set; }
    }

    public class Driver
    {
        public int CarIdx { get; set; } // 0
        public string UserName { get; set; } // "Scott Velez"
        public string AbbrevName { get; set; } // ""
        public string Initials { get; set; } // ""
        public int UserID { get; set; } // 22176
        public int TeamID { get; set; } // 0
        public string TeamName { get; set; } // "Scott Velez"
        public string CarNumber { get; set; } // "222"
        public int CarNumberRaw { get; set; } // 222
        public string CarPath { get; set; } // "mx5 mx52016"
        public int CarClassID { get; set; } // 0
        public int CarID { get; set; } // 67
        public int CarIsPaceCar { get; set; } // 0
        public int CarIsAI { get; set; } // 0
        public int CarIsElectric { get; set; } // 0
        public string CarScreenName { get; set; } // "Mazda MX-5 Cup"
        public string CarScreenNameShort { get; set; } // "MX-5 Cup"
        public int CarCfg { get; set; } // -1
        public string CarCfgName { get; set; } // ""
        public string CarCfgCustomPaintExt { get; set; } // ""
        public string CarClassShortName { get; set; } // ""
        public int CarClassRelSpeed { get; set; } // 0
        public int CarClassLicenseLevel { get; set; } // 0
        public string CarClassMaxFuelPct { get; set; } // in percentage
        public string CarClassWeightPenalty { get; set; } // in kilograms
        public string CarClassPowerAdjust { get; set; } // in percentage
        public string CarClassDryTireSetLimit { get; set; } // in percentage
        public int CarClassColor { get; set; } // 0xffffff
        public float CarClassEstLapTime { get; set; } // 19.6722
        public int IRating { get; set; } // 1
        public int LicLevel { get; set; } // 1
        public int LicSubLevel { get; set; } // 1
        public string LicString { get; set; } // "R 0.01"
        public string LicColor { get; set; } // "0xundefined"
        public int IsSpectator { get; set; } // 0
        public string CarDesignStr { get; set; } // "3,000000,12bfca,fd35d5,5a5a5a"
        public string HelmetDesignStr { get; set; } // "4,000000,fff100,ff0000"
        public string SuitDesignStr { get; set; } // "19,000000,ffda01,ff0000"
        public int BodyType { get; set; } // 0
        public int FaceType { get; set; } // 6
        public int HelmetType { get; set; } // 0
        public string CarNumberDesignStr { get; set; } // "0,0,ffffff,777777,000000"
        public int CarSponsor_1 { get; set; } // 2
        public int CarSponsor_2 { get; set; } // 2
        public string ClubName { get; set; } // None
        public string ClubID { get; set; } // 0
        public string DivisionName { get; set; } // Division 1 
        public string DivisionID { get; set; } // 0
        public string FlairName { get; set; } // Brazil
        public string FlairID { get; set; } // 0
        public int CurDriverIncidentCount { get; set; } // 0
        public int TeamIncidentCount { get; set; } // 0

    }

    public class DriverTire
    {
        public int TireIndex { get; set; } // 0
        public string TireCompoundType { get; set; } // "Hard"
    }
}
#nullable enable
