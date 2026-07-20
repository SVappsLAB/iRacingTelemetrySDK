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
/// Remote control of the iRacing simulator, grouped by feature area.
/// </summary>
public interface ISimController
{
    /// <summary>Camera control (switch focus, change camera group, camera tool state).</summary>
    ICameraCommands Camera { get; }

    /// <summary>Replay playback and tape search.</summary>
    IReplayCommands Replay { get; }

    /// <summary>Pit service requests (fuel, tires, repairs). Only work while in the car.</summary>
    IPitCommands Pit { get; }

    /// <summary>Chat window and chat macros.</summary>
    IChatCommands Chat { get; }

    /// <summary>Disk telemetry (IBT file) recording control.</summary>
    ITelemetryRecordingCommands TelemetryRecording { get; }

    /// <summary>Screenshot and video capture.</summary>
    IVideoCaptureCommands VideoCapture { get; }

    /// <summary>Force feedback configuration.</summary>
    IForceFeedbackCommands ForceFeedback { get; }

    /// <summary>Car texture reloading.</summary>
    ITextureCommands Textures { get; }
}
