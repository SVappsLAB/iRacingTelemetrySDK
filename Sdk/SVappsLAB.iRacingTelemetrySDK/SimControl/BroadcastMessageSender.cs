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
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;

namespace SVappsLAB.iRacingTelemetrySDK.SimControl;

internal interface IBroadcastMessageSender
{
    // core send. 'var1' is a 16-bit value packed into the high word of wParam,
    // 'var2' occupies the full 32 bits of lParam
    void Send(BroadcastMessageType msg, int var1, int var2);
}

internal static class BroadcastMessageSenderExtensions
{
    // var2 and var3 are 16-bit values packed into the low/high words of lParam
    public static void Send(this IBroadcastMessageSender sender, BroadcastMessageType msg, int var1, int var2, int var3)
        => sender.Send(msg, var1, BroadcastPacking.MakeLong(var2, var3));

    // var2 is a float carried as 16.16 fixed-point in lParam
    public static void Send(this IBroadcastMessageSender sender, BroadcastMessageType msg, int var1, float var2)
        => sender.Send(msg, var1, BroadcastPacking.FloatToFixedPoint(var2));
}

internal static class BroadcastPacking
{
    // equivalent of the win32 MAKELONG macro. masking keeps negative
    // 16-bit values (e.g. CameraFocus.AtIncident == -3) from corrupting the other word
    public static int MakeLong(int lowWord, int highWord)
        => unchecked((int)(((uint)(ushort)highWord << 16) | (ushort)lowWord));

    // the official SDK transmits floats by shifting the fractional part
    // into the integer (irsdk_utils.cpp: "(int)(var2 * 65536.0f)")
    public static int FloatToFixedPoint(float value)
        => (int)(value * 65536.0f);
}

// wraps a sender to warn when commands are sent while iRacing is not connected.
// the command is still sent - broadcasts are connectionless and iRacing may be
// mid-startup - the warning just explains why it likely had no effect
internal sealed class ConnectionWarningSender : IBroadcastMessageSender
{
    readonly IBroadcastMessageSender _inner;
    readonly Func<bool> _isConnected;
    readonly ILogger _logger;

    public ConnectionWarningSender(IBroadcastMessageSender inner, Func<bool> isConnected, ILogger logger)
    {
        _inner = inner;
        _isConnected = isConnected;
        _logger = logger;
    }

    public void Send(BroadcastMessageType msg, int var1, int var2)
    {
        if (!_isConnected())
            _logger.LogWarning("iRacing is not connected. sim control command {command} will likely be ignored", msg);

        _inner.Send(msg, var1, var2);
    }
}

/// <summary>
/// Sends broadcast messages to the iRacing simulator using the same mechanism as the
/// official SDK: a registered "IRSDK_BROADCASTMSG" window message delivered via
/// SendNotifyMessage(HWND_BROADCAST, ...). Sends are fire-and-forget; if the simulator
/// is not running the message is simply ignored.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class Win32BroadcastMessageSender : IBroadcastMessageSender
{
    const string BROADCAST_MESSAGE_NAME = "IRSDK_BROADCASTMSG";
    const nint HWND_BROADCAST = 0xFFFF;

    // RegisterWindowMessage returns the same id for all callers in the process,
    // so register once and cache
    static readonly Lazy<uint> _messageId = new(() => PInvoke.RegisterWindowMessage(BROADCAST_MESSAGE_NAME));

    readonly ILogger _logger;

    public Win32BroadcastMessageSender(ILogger logger)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("iRacing sim control requires Windows. Broadcast messages are delivered via win32 window messages.");

        _logger = logger;
    }

    public void Send(BroadcastMessageType msg, int var1, int var2)
    {
        // same range guard as the official SDK
        if (msg < 0 || msg >= BroadcastMessageType.Last)
        {
            _logger.LogWarning("sim control command {command} is out of range. not sent", msg);
            return;
        }

        var messageId = _messageId.Value;
        if (messageId == 0)
        {
            _logger.LogWarning("unable to register the iRacing broadcast window message. command {command} not sent", msg);
            return;
        }

        // the official SDK ignores the send result (with HWND_BROADCAST a failure from
        // any unrelated window is meaningless), so we do too
        PInvoke.SendNotifyMessage(HWND_BROADCAST, messageId, BroadcastPacking.MakeLong((int)msg, var1), var2);

        if (_logger.IsEnabled(LogLevel.Debug))
            _logger.LogDebug("sent sim control command {command} (var1={var1}, var2={var2})", msg, var1, var2);
    }
}
