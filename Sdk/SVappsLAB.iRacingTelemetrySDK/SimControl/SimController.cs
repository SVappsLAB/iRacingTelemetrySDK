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

using System;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace SVappsLAB.iRacingTelemetrySDK.SimControl;

/// <summary>
/// Remote control of the iRacing simulator. Obtained from a telemetry client's
/// <c>SimControl</c> property. See <see cref="ISimController"/> for semantics.
/// </summary>
/// <example>
/// <code>
/// var sim = client.SimControl;
/// sim.Pit.AddFuel(30);
/// sim.Pit.ChangeTire(TireLocation.LeftFront);
/// sim.Replay.Search(ReplaySearchMode.NextIncident);
/// sim.Camera.SwitchToPosition(CameraFocus.AtLeader, cameraGroup: 1, camera: 1);
/// </code>
/// </example>
public sealed class SimController : ISimController
{
    // creates a controller for the running simulator. 'isConnected' is consulted
    // on each send to warn when iRacing is not connected. throws
    // PlatformNotSupportedException on non-windows platforms
    internal SimController(ILogger? logger, Func<bool>? isConnected)
        : this(CreateSender(logger ?? NullLogger.Instance, isConnected))
    {
    }

    static IBroadcastMessageSender CreateSender(ILogger logger, Func<bool>? isConnected)
    {
        IBroadcastMessageSender sender = new Win32BroadcastMessageSender(logger);
        return isConnected == null ? sender : new ConnectionWarningSender(sender, isConnected, logger);
    }

    internal SimController(IBroadcastMessageSender sender)
    {
        Camera = new CameraCommands(sender);
        Replay = new ReplayCommands(sender);
        Pit = new PitCommands(sender);
        Chat = new ChatCommands(sender);
        TelemetryRecording = new TelemetryRecordingCommands(sender);
        VideoCapture = new VideoCaptureCommands(sender);
        ForceFeedback = new ForceFeedbackCommands(sender);
        Textures = new TextureCommands(sender);
    }

    /// <inheritdoc />
    public ICameraCommands Camera { get; }
    /// <inheritdoc />
    public IReplayCommands Replay { get; }
    /// <inheritdoc />
    public IPitCommands Pit { get; }
    /// <inheritdoc />
    public IChatCommands Chat { get; }
    /// <inheritdoc />
    public ITelemetryRecordingCommands TelemetryRecording { get; }
    /// <inheritdoc />
    public IVideoCaptureCommands VideoCapture { get; }
    /// <inheritdoc />
    public IForceFeedbackCommands ForceFeedback { get; }
    /// <inheritdoc />
    public ITextureCommands Textures { get; }

    /// <summary>
    /// Encodes a car number that has leading zeros for use with
    /// <see cref="ICameraCommands.SwitchToCar(int, int, int)"/>. In iRacing,
    /// car "001" and car "1" are different cars. 
    /// </summary>
    /// <param name="carNumber">numeric value of the car number (1 for "001")</param>
    /// <param name="leadingZeros">count of leading zeros (2 for "001")</param>
    public static int PadCarNumber(int carNumber, int leadingZeros)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(carNumber);
        ArgumentOutOfRangeException.ThrowIfNegative(leadingZeros);

        if (leadingZeros == 0)
        {
            BroadcastValidation.Signed16(carNumber, nameof(carNumber));
            return carNumber;
        }

        var digits = carNumber > 99 ? 3 : carNumber > 9 ? 2 : 1;
        var encoded = checked(carNumber + 1000 * (digits + leadingZeros));
        BroadcastValidation.Signed16(encoded, nameof(carNumber));
        return encoded;
    }
}
