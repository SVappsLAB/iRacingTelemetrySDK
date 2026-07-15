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

namespace SVappsLAB.iRacingTelemetrySDK.SimControl;

/// <summary>
/// Replay playback and search commands. These only work when you are out of your car.
/// </summary>
public interface IReplayCommands
{
    /// <summary>
    /// Sets the replay playback speed.
    /// </summary>
    /// <param name="speed">playback speed multiplier. 1 is normal speed, 0 pauses,
    /// negative values play in reverse, 2/3/4... fast-forward</param>
    /// <param name="slowMotion">when true, <paramref name="speed"/> is interpreted
    /// as a slow-motion divisor (2 = half speed)</param>
    /// <exception cref="ArgumentOutOfRangeException">speed cannot be represented by the broadcast protocol</exception>
    void SetPlaySpeed(int speed, bool slowMotion = false);

    /// <summary>
    /// Jumps playback to a frame number, relative to the given reference point.
    /// </summary>
    /// <param name="mode">reference point the frame number is relative to</param>
    /// <param name="frameNumber">frame number to jump to</param>
    /// <exception cref="ArgumentOutOfRangeException">mode is undefined</exception>
    void SetPlayPosition(ReplayPositionMode mode, int frameNumber);

    /// <summary>
    /// Searches the replay tape for an event (next lap, previous incident, ...).
    /// </summary>
    /// <param name="mode">the event to search for</param>
    /// <exception cref="ArgumentOutOfRangeException">mode is undefined</exception>
    void Search(ReplaySearchMode mode);

    /// <summary>
    /// Jumps playback to a session time within a specific session.
    /// </summary>
    /// <param name="sessionNumber">session number to search within</param>
    /// <param name="sessionTimeMilliseconds">session time, in milliseconds</param>
    /// <exception cref="ArgumentOutOfRangeException">a value is negative or the session number cannot be represented by the broadcast protocol</exception>
    void SearchSessionTime(int sessionNumber, int sessionTimeMilliseconds);

    /// <summary>
    /// Erases all data in the replay tape.
    /// </summary>
    void EraseTape();
}

/// <inheritdoc cref="IReplayCommands" />
public sealed class ReplayCommands : IReplayCommands
{
    readonly IBroadcastMessageSender _sender;

    internal ReplayCommands(IBroadcastMessageSender sender) => _sender = sender;

    /// <inheritdoc />
    public void SetPlaySpeed(int speed, bool slowMotion = false)
    {
        BroadcastValidation.Signed16(speed, nameof(speed));
        _sender.Send(BroadcastMessageType.ReplaySetPlaySpeed, speed, slowMotion ? 1 : 0);
    }

    /// <inheritdoc />
    public void SetPlayPosition(ReplayPositionMode mode, int frameNumber)
    {
        BroadcastValidation.Defined(mode, nameof(mode));
        _sender.Send(BroadcastMessageType.ReplaySetPlayPosition, (int)mode, frameNumber);
    }

    /// <inheritdoc />
    public void Search(ReplaySearchMode mode)
    {
        BroadcastValidation.Defined(mode, nameof(mode));
        _sender.Send(BroadcastMessageType.ReplaySearch, (int)mode, 0);
    }

    /// <inheritdoc />
    public void SearchSessionTime(int sessionNumber, int sessionTimeMilliseconds)
    {
        BroadcastValidation.NonNegative(sessionNumber, nameof(sessionNumber));
        BroadcastValidation.Signed16(sessionNumber, nameof(sessionNumber));
        BroadcastValidation.NonNegative(sessionTimeMilliseconds, nameof(sessionTimeMilliseconds));
        _sender.Send(BroadcastMessageType.ReplaySearchSessionTime, sessionNumber, sessionTimeMilliseconds);
    }

    /// <inheritdoc />
    public void EraseTape()
        => _sender.Send(BroadcastMessageType.ReplaySetState, (int)ReplayStateMode.EraseTape, 0);
}
