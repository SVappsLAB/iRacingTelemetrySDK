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

using System.Collections.Generic;

#nullable disable
namespace SVappsLAB.iRacingTelemetrySDK.Models
{
    public class DriverInfo
    {
        public int DriverCarIdx { get; set; }
        public int DriverUserID { get; set; }
        public int PaceCarIdx { get; set; }
        public float DriverHeadPosX { get; set; } // in units of length (mm, m, etc.)
        public float DriverHeadPosY { get; set; } // in units of length (mm, m, etc.)
        public float DriverHeadPosZ { get; set; } // in units of length (mm, m, etc.)
        public int DriverCarIsElectric { get; set; } // 0
        public float DriverCarIdleRPM { get; set; }
        public float DriverCarRedLine { get; set; }
        public int DriverCarEngCylinderCount { get; set; }
        public float DriverCarFuelKgPerLtr { get; set; }
        public float DriverCarFuelMaxLtr { get; set; }
        public float DriverCarMaxFuelPct { get; set; } // in percentage
        public int DriverCarGearNumForward { get; set; } // 6
        public int DriverCarGearNeutral { get; set; } // 1
        public int DriverCarGearReverse { get; set; } // 1
        public float DriverCarSLFirstRPM { get; set; }
        public float DriverCarSLShiftRPM { get; set; }
        public float DriverCarSLLastRPM { get; set; }
        public float DriverCarSLBlinkRPM { get; set; }
        public string DriverCarVersion { get; set; }
        public float DriverPitTrkPct { get; set; } // in percentage
        public float DriverCarEstLapTime { get; set; }
        public string DriverSetupName { get; set; }
        public int DriverSetupIsModified { get; set; }
        public string DriverSetupLoadTypeName { get; set; }
        public int DriverSetupPassedTech { get; set; }
        public int DriverIncidentCount { get; set; }
        public List<Driver> Drivers { get; set; }

    }

    public class Driver
    {
        public int CarIdx { get; set; }
        public string UserName { get; set; }
        public string AbbrevName { get; set; }
        public string Initials { get; set; }
        public int UserID { get; set; }
        public int TeamID { get; set; }
        public string TeamName { get; set; }
        public string CarNumber { get; set; }
        public int CarNumberRaw { get; set; }
        public string CarPath { get; set; }
        public int CarClassID { get; set; }
        public int CarID { get; set; }
        public int CarIsPaceCar { get; set; }
        public int CarIsAI { get; set; }
        public int CarIsElectric { get; set; }
        public string CarScreenName { get; set; }
        public string CarScreenNameShort { get; set; }
        public string CarClassShortName { get; set; }
        public int CarClassRelSpeed { get; set; }
        public int CarClassLicenseLevel { get; set; }
        public string CarClassMaxFuelPct { get; set; } // in percentage
        public string CarClassWeightPenalty { get; set; } // in kilograms
        public string CarClassPowerAdjust { get; set; } // in percentage
        public string CarClassDryTireSetLimit { get; set; } // in percentage
        public int CarClassColor { get; set; }
        public string CarClassEstLapTime { get; set; } // safety pcporsche911cup
        public int IRating { get; set; }
        public int LicLevel { get; set; }
        public int LicSubLevel { get; set; }
        public string LicString { get; set; }
        public string LicColor { get; set; }
        public int IsSpectator { get; set; }
        public string CarDesignStr { get; set; }
        public string HelmetDesignStr { get; set; }
        public string SuitDesignStr { get; set; }
        public int BodyType { get; set; } // 0
        public int FaceType { get; set; } // 0
        public int HelmetType { get; set; } // 0
        public string CarNumberDesignStr { get; set; }
        public int CarSponsor_1 { get; set; }
        public int CarSponsor_2 { get; set; }
        public string ClubName { get; set; }
        public string ClubID { get; set; } // 0
        public string DivisionName { get; set; }
        public string DivisionID { get; set; } // 0
        public int CurDriverIncidentCount { get; set; }
        public int TeamIncidentCount { get; set; }

    }

}
#nullable enable
