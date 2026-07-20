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
/// Screenshot and video capture commands (uses the simulator's built-in capture,
/// which must be enabled in the iRacing app settings).
/// </summary>
public interface IVideoCaptureCommands
{
    /// <summary>
    /// Saves a screenshot to disk.
    /// </summary>
    void CaptureScreenshot();

    /// <summary>
    /// Starts capturing video.
    /// </summary>
    void Start();

    /// <summary>
    /// Stops capturing video.
    /// </summary>
    void Stop();

    /// <summary>
    /// Toggles video capture on/off.
    /// </summary>
    void Toggle();

    /// <summary>
    /// Shows the video timer in the upper left corner of the display.
    /// </summary>
    void ShowTimer();

    /// <summary>
    /// Hides the video timer.
    /// </summary>
    void HideTimer();
}

/// <inheritdoc cref="IVideoCaptureCommands" />
public sealed class VideoCaptureCommands : IVideoCaptureCommands
{
    readonly IBroadcastMessageSender _sender;

    internal VideoCaptureCommands(IBroadcastMessageSender sender) => _sender = sender;

    /// <inheritdoc />
    public void CaptureScreenshot()
        => Send(VideoCaptureMode.TriggerScreenShot);

    /// <inheritdoc />
    public void Start()
        => Send(VideoCaptureMode.StartVideoCapture);

    /// <inheritdoc />
    public void Stop()
        => Send(VideoCaptureMode.EndVideoCapture);

    /// <inheritdoc />
    public void Toggle()
        => Send(VideoCaptureMode.ToggleVideoCapture);

    /// <inheritdoc />
    public void ShowTimer()
        => Send(VideoCaptureMode.ShowVideoTimer);

    /// <inheritdoc />
    public void HideTimer()
        => Send(VideoCaptureMode.HideVideoTimer);

    void Send(VideoCaptureMode mode)
        => _sender.Send(BroadcastMessageType.VideoCapture, (int)mode, 0);
}
