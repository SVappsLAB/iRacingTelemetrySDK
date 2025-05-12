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

// bit fields
namespace SVappsLAB.iRacingTelemetrySDK
{
    [Flags]
    public enum EngineWarnings
    {
        WaterTempWarning = 0x0001,
        FuelPressureWarning = 0x0002,
        OilPressureWarning = 0x0004,
        EngineStalled = 0x0008,
        PitSpeedLimiter = 0x0010,
        RevLimiterActive = 0x0020,
        OilTempWarning = 0x0040,
    };

    // global flags
    [Flags]
    public enum SessionFlags
    {
        // global flags
        Checkered = 0x00000001,
        White = 0x00000002,
        Green = 0x00000004,
        Yellow = 0x00000008,
        Red = 0x00000010,
        Blue = 0x00000020,
        Debris = 0x00000040,
        Crossed = 0x00000080,
        YellowWaving = 0x00000100,
        OneLapToGreen = 0x00000200,
        GreenHeld = 0x00000400,
        TenToGo = 0x00000800,
        FiveToGo = 0x00001000,
        RandomWaving = 0x00002000,
        Caution = 0x00004000,
        CautionWaving = 0x00008000,

        // drivers black flags
        Black = 0x00010000,
        Disqualify = 0x00020000,
        Serviceable = 0x00040000, // car is allowed service (not a flag)
        Furled = 0x00080000,
        Repair = 0x00100000,

        // start lights
        StartHidden = 0x10000000,
        StartReady = 0x20000000,
        StartSet = 0x40000000,
        StartGo = unchecked((int)0x80000000),
    };

    [Flags]
    public enum CameraState
    {
        IsSessionScreen = 0x0001, // the camera tool can only be activated if viewing the session screen (out of car)
        IsScenicActive = 0x0002, // the scenic camera is active (no focus car)

        //these can be changed with a broadcast message
        CamToolActive = 0x0004,
        UIHidden = 0x0008,
        UseAutoShotSelection = 0x0010,
        UseTemporaryEdits = 0x0020,
        UseKeyAcceleration = 0x0040,
        UseKey10xAcceleration = 0x0080,
        UseMouseAimMode = 0x0100
    };

    [Flags]
    public enum PitServiceFlags
    {
        LFTireChange = 0x0001,
        RFTireChange = 0x0002,
        LRTireChange = 0x0004,
        RRTireChange = 0x0008,

        FuelFill = 0x0010,
        WindshieldTearoff = 0x0020,
        FastRepair = 0x0040
    };

    [Flags]
    public enum PaceFlags
    {
        EndOfLine = 0x0001,
        FreePass = 0x0002,
        WavedAround = 0x0004,
    };
}
