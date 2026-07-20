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
/// Controls the simulator's disk telemetry recording (the IBT files iRacing writes,
/// normally toggled with alt-L). Can be called at any time, but the simulator only
/// records while the driver is in the car.
/// </summary>
public interface ITelemetryRecordingCommands
{
    /// <summary>
    /// Turns disk telemetry recording on.
    /// </summary>
    void Start();

    /// <summary>
    /// Turns disk telemetry recording off.
    /// </summary>
    void Stop();

    /// <summary>
    /// Writes the current telemetry file to disk and starts a new one.
    /// </summary>
    void Restart();
}

/// <inheritdoc cref="ITelemetryRecordingCommands" />
public sealed class TelemetryRecordingCommands : ITelemetryRecordingCommands
{
    readonly IBroadcastMessageSender _sender;

    internal TelemetryRecordingCommands(IBroadcastMessageSender sender) => _sender = sender;

    /// <inheritdoc />
    public void Start()
        => Send(TelemetryCommandMode.Start);

    /// <inheritdoc />
    public void Stop()
        => Send(TelemetryCommandMode.Stop);

    /// <inheritdoc />
    public void Restart()
        => Send(TelemetryCommandMode.Restart);

    void Send(TelemetryCommandMode mode)
        => _sender.Send(BroadcastMessageType.TelemCommand, (int)mode, 0);
}
