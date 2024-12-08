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

using System;

// bit fields
namespace SVappsLAB.iRacingTelemetrySDK.Flags
{
    [Flags]
    public enum irsdk_EngineWarnings
    {
        irsdk_waterTempWarning = 0x0001,
        irsdk_fuelPressureWarning = 0x0002,
        irsdk_oilPressureWarning = 0x0004,
        irsdk_engineStalled = 0x0008,
        irsdk_pitSpeedLimiter = 0x0010,
        irsdk_revLimiterActive = 0x0020,
        irsdk_oilTempWarning = 0x0040,
    };

    // global flags
    [Flags]
    public enum irsdk_Flags
    {
        // global flags
        irsdk_checkered = 0x00000001,
        irsdk_white = 0x00000002,
        irsdk_green = 0x00000004,
        irsdk_yellow = 0x00000008,
        irsdk_red = 0x00000010,
        irsdk_blue = 0x00000020,
        irsdk_debris = 0x00000040,
        irsdk_crossed = 0x00000080,
        irsdk_yellowWaving = 0x00000100,
        irsdk_oneLapToGreen = 0x00000200,
        irsdk_greenHeld = 0x00000400,
        irsdk_tenToGo = 0x00000800,
        irsdk_fiveToGo = 0x00001000,
        irsdk_randomWaving = 0x00002000,
        irsdk_caution = 0x00004000,
        irsdk_cautionWaving = 0x00008000,

        // drivers black flags
        irsdk_black = 0x00010000,
        irsdk_disqualify = 0x00020000,
        irsdk_servicible = 0x00040000, // car is allowed service (not a flag)
        irsdk_furled = 0x00080000,
        irsdk_repair = 0x00100000,

        // start lights
        irsdk_startHidden = 0x10000000,
        irsdk_startReady = 0x20000000,
        irsdk_startSet = 0x40000000,
        irsdk_startgo = unchecked((int)0x80000000),
    };

    [Flags]
    public enum irsdk_CameraState
    {
        irsdk_IsSessionScreen = 0x0001, // the camera tool can only be activated if viewing the session screen (out of car)
        irsdk_IsScenicActive = 0x0002, // the scenic camera is active (no focus car)

        //these can be changed with a broadcast message
        irsdk_CamToolActive = 0x0004,
        irsdk_UIHidden = 0x0008,
        irsdk_UseAutoShotSelection = 0x0010,
        irsdk_UseTemporaryEdits = 0x0020,
        irsdk_UseKeyAcceleration = 0x0040,
        irsdk_UseKey10xAcceleration = 0x0080,
        irsdk_UseMouseAimMode = 0x0100
    };

    [Flags]
    public enum irsdk_PitSvFlags
    {
        irsdk_LFTireChange = 0x0001,
        irsdk_RFTireChange = 0x0002,
        irsdk_LRTireChange = 0x0004,
        irsdk_RRTireChange = 0x0008,

        irsdk_FuelFill = 0x0010,
        irsdk_WindshieldTearoff = 0x0020,
        irsdk_FastRepair = 0x0040
    };

    [Flags]
    public enum irsdk_PaceFlags
    {
        irsdk_PaceFlagsEndOfLine = 0x0001,
        irsdk_PaceFlagsFreePass = 0x0002,
        irsdk_PaceFlagsWavedAround = 0x0004,
    };
}
