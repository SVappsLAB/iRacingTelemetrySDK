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
using System.Globalization;

namespace SVappsLAB.iRacingTelemetrySDK.SimControl;

/// <summary>
/// Camera control commands. These only work when you are out of your car
/// (spectating, or watching a replay).
/// </summary>
public interface ICameraCommands
{
    /// <summary>
    /// Switches the camera to the car currently running in the given race position.
    /// </summary>
    /// <param name="position">race position (1 = leader), or a <see cref="CameraFocus"/> value cast to int</param>
    /// <param name="cameraGroup">camera group number to activate</param>
    /// <param name="camera">camera number within the group</param>
    /// <exception cref="ArgumentOutOfRangeException">a value is negative or cannot be represented by the broadcast protocol</exception>
    void SwitchToPosition(int position, int cameraGroup, int camera);

    /// <summary>
    /// Switches the camera to a special focus target (leader, incident, ...).
    /// </summary>
    /// <param name="focus">the focus target the camera system should follow</param>
    /// <param name="cameraGroup">camera group number to activate</param>
    /// <param name="camera">camera number within the group</param>
    /// <exception cref="ArgumentOutOfRangeException">the focus is undefined, or a camera value is negative or cannot be represented by the broadcast protocol</exception>
    void SwitchToPosition(CameraFocus focus, int cameraGroup, int camera);

    /// <summary>
    /// Switches the camera to the car with the given car number.
    /// </summary>
    /// <param name="paddedCarNumber">the car number, pre-encoded with <see cref="SimController.PadCarNumber"/>
    /// if the number has leading zeros (e.g. car "001")</param>
    /// <param name="cameraGroup">camera group number to activate</param>
    /// <param name="camera">camera number within the group</param>
    /// <exception cref="ArgumentOutOfRangeException">a value is negative or cannot be represented by the broadcast protocol</exception>
    void SwitchToCar(int paddedCarNumber, int cameraGroup, int camera);

    /// <summary>
    /// Switches the camera to the car with the given car number string.
    /// Leading zeros are significant (car "001" is different from car "1") and are encoded automatically.
    /// </summary>
    /// <param name="carNumber">the car number as displayed in the sim (e.g. "1", "001")</param>
    /// <param name="cameraGroup">camera group number to activate</param>
    /// <param name="camera">camera number within the group</param>
    void SwitchToCar(string carNumber, int cameraGroup, int camera);

    /// <summary>
    /// Sets the camera tool state (show/hide UI, auto shot selection, ...).
    /// Only the flags marked as broadcast-changeable in <see cref="CameraState"/> can be modified.
    /// </summary>
    /// <param name="state">the camera state flags to apply</param>
    void SetState(CameraState state);
}

/// <inheritdoc cref="ICameraCommands" />
public sealed class CameraCommands : ICameraCommands
{
    readonly IBroadcastMessageSender _sender;

    internal CameraCommands(IBroadcastMessageSender sender) => _sender = sender;

    /// <inheritdoc />
    public void SwitchToPosition(int position, int cameraGroup, int camera)
    {
        BroadcastValidation.NonNegative(position, nameof(position));
        ValidateCameraSelection(position, nameof(position), cameraGroup, camera);
        _sender.Send(BroadcastMessageType.CamSwitchPos, position, cameraGroup, camera);
    }

    /// <inheritdoc />
    public void SwitchToPosition(CameraFocus focus, int cameraGroup, int camera)
    {
        BroadcastValidation.Defined(focus, nameof(focus));
        ValidateCameraSelection((int)focus, nameof(focus), cameraGroup, camera);
        _sender.Send(BroadcastMessageType.CamSwitchPos, (int)focus, cameraGroup, camera);
    }

    /// <inheritdoc />
    public void SwitchToCar(int paddedCarNumber, int cameraGroup, int camera)
    {
        BroadcastValidation.NonNegative(paddedCarNumber, nameof(paddedCarNumber));
        ValidateCameraSelection(paddedCarNumber, nameof(paddedCarNumber), cameraGroup, camera);
        _sender.Send(BroadcastMessageType.CamSwitchNum, paddedCarNumber, cameraGroup, camera);
    }

    /// <inheritdoc />
    public void SwitchToCar(string carNumber, int cameraGroup, int camera)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(carNumber);

        var trimmed = carNumber.AsSpan().Trim();
        var value = int.Parse(trimmed, NumberStyles.None, CultureInfo.InvariantCulture);

        // count zeros preceding the first non-zero digit. for an all-zero
        // number ("0", "00") every digit but the last counts as padding
        var leadingZeros = 0;
        while (leadingZeros < trimmed.Length - 1 && trimmed[leadingZeros] == '0')
            leadingZeros++;

        SwitchToCar(SimController.PadCarNumber(value, leadingZeros), cameraGroup, camera);
    }

    /// <inheritdoc />
    public void SetState(CameraState state)
        => _sender.Send(BroadcastMessageType.CamSetState, (int)state, 0);

    static void ValidateCameraSelection(int target, string targetParamName, int cameraGroup, int camera)
    {
        BroadcastValidation.Signed16(target, targetParamName);
        BroadcastValidation.Signed16(cameraGroup, nameof(cameraGroup));
        BroadcastValidation.Signed16(camera, nameof(camera));
        BroadcastValidation.NonNegative(cameraGroup, nameof(cameraGroup));
        BroadcastValidation.NonNegative(camera, nameof(camera));
    }
}
