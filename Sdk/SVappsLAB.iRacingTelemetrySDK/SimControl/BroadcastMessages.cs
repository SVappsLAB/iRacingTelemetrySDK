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

// wire-level values from the official iRacing SDK (irsdk_defines.h, v1.20).
// these are internal. the public API exposes strongly-typed methods instead

namespace SVappsLAB.iRacingTelemetrySDK.SimControl;

// irsdk_BroadcastMsg
internal enum BroadcastMessageType
{
    CamSwitchPos = 0,           // car position, group, camera
    CamSwitchNum,               // padded car number, group, camera
    CamSetState,                // CameraState, unused, unused
    ReplaySetPlaySpeed,         // speed, slowMotion, unused
    ReplaySetPlayPosition,      // ReplayPositionMode, frame number
    ReplaySearch,               // ReplaySearchMode, unused, unused
    ReplaySetState,             // ReplayStateMode, unused, unused
    ReloadTextures,             // ReloadTexturesMode, carIdx, unused
    ChatCommand,                // ChatCommandMode, subCommand, unused
    PitCommand,                 // PitCommandMode, parameter
    TelemCommand,               // TelemetryCommandMode, unused, unused
    FFBCommand,                 // FFBCommandMode, value (16.16 fixed-point float)
    ReplaySearchSessionTime,    // sessionNum, sessionTimeMS
    VideoCapture,               // VideoCaptureMode, unused, unused
    Last                        // unused placeholder
}

// irsdk_ChatCommandMode
internal enum ChatCommandMode
{
    Macro = 0,
    BeginChat,
    Reply,
    Cancel
}

// irsdk_PitCommandMode
internal enum PitCommandMode
{
    Clear = 0,
    CleanWindshield,
    Fuel,
    LeftFrontTire,
    RightFrontTire,
    LeftRearTire,
    RightRearTire,
    ClearTires,
    FastRepair,
    ClearWindshield,
    ClearFastRepair,
    ClearFuel,
    TireCompound
}

// irsdk_TelemCommandMode
internal enum TelemetryCommandMode
{
    Stop = 0,
    Start,
    Restart
}

// irsdk_RpyStateMode
internal enum ReplayStateMode
{
    EraseTape = 0
}

// irsdk_ReloadTexturesMode
internal enum ReloadTexturesMode
{
    All = 0,
    CarIdx
}

// irsdk_FFBCommandMode
internal enum FFBCommandMode
{
    MaxForce = 0
}

// irsdk_VideoCaptureMode
internal enum VideoCaptureMode
{
    TriggerScreenShot = 0,
    StartVideoCapture,
    EndVideoCapture,
    ToggleVideoCapture,
    ShowVideoTimer,
    HideVideoTimer
}
