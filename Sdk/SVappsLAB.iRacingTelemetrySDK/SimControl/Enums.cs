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

namespace SVappsLAB.iRacingTelemetrySDK.SimControl;

/// <summary>
/// Replay tape search operations (irsdk_RpySrchMode).
/// </summary>
public enum ReplaySearchMode
{
    /// <summary>Jump to the start of the tape.</summary>
    ToStart = 0,
    /// <summary>Jump to the end of the tape.</summary>
    ToEnd,
    /// <summary>Jump to the previous session.</summary>
    PreviousSession,
    /// <summary>Jump to the next session.</summary>
    NextSession,
    /// <summary>Jump to the previous lap.</summary>
    PreviousLap,
    /// <summary>Jump to the next lap.</summary>
    NextLap,
    /// <summary>Step back one frame.</summary>
    PreviousFrame,
    /// <summary>Step forward one frame.</summary>
    NextFrame,
    /// <summary>Jump to the previous incident.</summary>
    PreviousIncident,
    /// <summary>Jump to the next incident.</summary>
    NextIncident
}

/// <summary>
/// Reference point for replay frame positioning (irsdk_RpyPosMode).
/// </summary>
public enum ReplayPositionMode
{
    /// <summary>Frame number is relative to the start of the tape.</summary>
    Begin = 0,
    /// <summary>Frame number is relative to the current position.</summary>
    Current,
    /// <summary>Frame number is relative to the end of the tape.</summary>
    End
}

/// <summary>
/// Special camera focus targets (irsdk_csMode). Pass one of these instead of a
/// car position to have the camera system pick the target automatically.
/// </summary>
public enum CameraFocus
{
    /// <summary>Focus on the current incident.</summary>
    AtIncident = -3,
    /// <summary>Focus on the race leader.</summary>
    AtLeader = -2,
    /// <summary>Focus on the car exiting the pits.</summary>
    AtExiting = -1,
    /// <summary>Focus on your own driver.</summary>
    AtDriver = 0
}

/// <summary>
/// Identifies a tire for pit service commands.
/// </summary>
public enum TireLocation
{
    /// <summary>The left front tire.</summary>
    LeftFront = 0,
    /// <summary>The right front tire.</summary>
    RightFront,
    /// <summary>The left rear tire.</summary>
    LeftRear,
    /// <summary>The right rear tire.</summary>
    RightRear
}
