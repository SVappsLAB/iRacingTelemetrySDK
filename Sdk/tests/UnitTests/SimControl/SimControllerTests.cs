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

using Microsoft.Extensions.Logging;
using SVappsLAB.iRacingTelemetrySDK;
using SVappsLAB.iRacingTelemetrySDK.SimControl;

namespace UnitTests.SimControl;

// records the core sends so tests can assert the exact wire values
// (BroadcastMessageType, var1=wParam high word, var2=lParam)
internal sealed class FakeBroadcastMessageSender : IBroadcastMessageSender
{
    public List<(BroadcastMessageType msg, int var1, int var2)> Sent { get; } = [];

    public void Send(BroadcastMessageType msg, int var1, int var2) => Sent.Add((msg, var1, var2));

    public (BroadcastMessageType msg, int var1, int var2) Single()
    {
        Assert.Single(Sent);
        return Sent[0];
    }
}

// records log entries so tests can assert on level and message
internal sealed class CapturingLogger : ILogger
{
    public List<(LogLevel level, string message)> Entries { get; } = [];

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        => Entries.Add((logLevel, formatter(state, exception)));
}

public class ConnectionWarningSenderTests
{
    readonly FakeBroadcastMessageSender _inner = new();
    readonly CapturingLogger _logger = new();

    [Fact]
    public void Disconnected_LogsWarning_AndStillSends()
    {
        var sim = new SimController(new ConnectionWarningSender(_inner, () => false, _logger));

        sim.Pit.RequestFastRepair();

        // the command still goes out - broadcasts are connectionless
        var (msg, var1, _) = _inner.Single();
        Assert.Equal(BroadcastMessageType.PitCommand, msg);
        Assert.Equal(8, var1);

        var entry = Assert.Single(_logger.Entries);
        Assert.Equal(LogLevel.Warning, entry.level);
        Assert.Contains("not connected", entry.message);
    }

    [Fact]
    public void Connected_SendsWithoutLogging()
    {
        var sim = new SimController(new ConnectionWarningSender(_inner, () => true, _logger));

        sim.Pit.RequestFastRepair();

        Assert.Single(_inner.Sent);
        Assert.Empty(_logger.Entries);
    }
}

public class BroadcastPackingTests
{
    [Theory]
    [InlineData(0, 0, 0x00000000)]
    [InlineData(1, 2, 0x00020001)]
    [InlineData(0x1234, 0x5678, 0x56781234)]
    // negative words must not corrupt the other word (CameraFocus values are negative)
    [InlineData(2, -3, unchecked((int)0xFFFD0002))]
    [InlineData(-1, 1, 0x0001FFFF)]
    public void MakeLong_PacksLowAndHighWords(int low, int high, int expected)
    {
        Assert.Equal(expected, BroadcastPacking.MakeLong(low, high));
    }

    [Theory]
    [InlineData(0f, 0)]
    [InlineData(1f, 65536)]
    [InlineData(1.5f, 98304)]
    [InlineData(45.5f, 2981888)]
    [InlineData(-1f, -65536)]
    public void FloatToFixedPoint_Matches_16_16_FixedPoint(float value, int expected)
    {
        Assert.Equal(expected, BroadcastPacking.FloatToFixedPoint(value));
    }
}

public class PadCarNumberTests
{
    [Theory]
    [InlineData(1, 0, 1)]        // car "1"
    [InlineData(11, 0, 11)]      // car "11"
    [InlineData(123, 0, 123)]    // car "123"
    [InlineData(1, 2, 3001)]     // car "001" (official SDK doc example)
    [InlineData(1, 1, 2001)]     // car "01"
    [InlineData(11, 1, 3011)]    // car "011"
    public void PadCarNumber_MatchesOfficialAlgorithm(int num, int zeros, int expected)
    {
        Assert.Equal(expected, SimController.PadCarNumber(num, zeros));
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(1, -1)]
    public void PadCarNumber_NegativeInput_Throws(int num, int zeros)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => SimController.PadCarNumber(num, zeros));
    }
}

public class SimControllerTests
{
    readonly FakeBroadcastMessageSender _sender;
    readonly SimController _sim;

    public SimControllerTests()
    {
        _sender = new FakeBroadcastMessageSender();
        _sim = new SimController(_sender);
    }

    [Fact]
    public void Constructor_OnNonWindows_ThrowsPlatformNotSupported()
    {
        if (OperatingSystem.IsWindows())
            return; // the win32 sender is valid here; nothing to assert

        Assert.Throws<PlatformNotSupportedException>(() => new SimController(logger: null, isConnected: null));
    }

    // --- camera ---

    [Fact]
    public void Camera_SwitchToPosition_SendsPositionGroupAndCamera()
    {
        _sim.Camera.SwitchToPosition(3, cameraGroup: 1, camera: 2);

        var (msg, var1, var2) = _sender.Single();
        Assert.Equal(BroadcastMessageType.CamSwitchPos, msg);
        Assert.Equal(3, var1);
        Assert.Equal(BroadcastPacking.MakeLong(1, 2), var2);
    }

    [Fact]
    public void Camera_SwitchToPosition_WithFocus_SendsNegativeFocusValue()
    {
        _sim.Camera.SwitchToPosition(CameraFocus.AtIncident, cameraGroup: 0, camera: 0);

        var (msg, var1, _) = _sender.Single();
        Assert.Equal(BroadcastMessageType.CamSwitchPos, msg);
        Assert.Equal(-3, var1);
    }

    [Theory]
    [InlineData("1", 1)]
    [InlineData("64", 64)]
    [InlineData("001", 3001)]   // leading zeros are significant
    [InlineData("01", 2001)]
    [InlineData("0", 0)]
    public void Camera_SwitchToCar_String_EncodesLeadingZeros(string carNumber, int expectedVar1)
    {
        _sim.Camera.SwitchToCar(carNumber, cameraGroup: 1, camera: 1);

        var (msg, var1, _) = _sender.Single();
        Assert.Equal(BroadcastMessageType.CamSwitchNum, msg);
        Assert.Equal(expectedVar1, var1);
    }

    [Fact]
    public void Camera_SwitchToCar_NonNumericString_ThrowsFormatException()
    {
        Assert.Throws<FormatException>(() => _sim.Camera.SwitchToCar("abc", 1, 1));
    }

    [Fact]
    public void Camera_SwitchToCar_BlankString_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => _sim.Camera.SwitchToCar("  ", 1, 1));
    }

    [Fact]
    public void Camera_SetState_SendsFlags()
    {
        _sim.Camera.SetState(CameraState.CamToolActive | CameraState.UIHidden);

        var (msg, var1, var2) = _sender.Single();
        Assert.Equal(BroadcastMessageType.CamSetState, msg);
        Assert.Equal(0x000C, var1);
        Assert.Equal(0, var2);
    }

    // --- replay ---

    [Fact]
    public void Replay_SetPlaySpeed_SendsSpeedAndSlowMotionFlag()
    {
        _sim.Replay.SetPlaySpeed(-2, slowMotion: true);

        var (msg, var1, var2) = _sender.Single();
        Assert.Equal(BroadcastMessageType.ReplaySetPlaySpeed, msg);
        Assert.Equal(-2, var1);
        Assert.Equal(1, var2);
    }

    [Fact]
    public void Replay_SetPlayPosition_SendsFullFrameNumberInVar2()
    {
        _sim.Replay.SetPlayPosition(ReplayPositionMode.Begin, 123_456);

        var (msg, var1, var2) = _sender.Single();
        Assert.Equal(BroadcastMessageType.ReplaySetPlayPosition, msg);
        Assert.Equal(0, var1);
        Assert.Equal(123_456, var2); // frame number uses the full 32 bits
    }

    [Theory]
    [InlineData(ReplaySearchMode.ToStart, 0)]
    [InlineData(ReplaySearchMode.NextLap, 5)]
    [InlineData(ReplaySearchMode.NextIncident, 9)]
    public void Replay_Search_SendsOfficialModeValues(ReplaySearchMode mode, int expectedVar1)
    {
        _sim.Replay.Search(mode);

        var (msg, var1, _) = _sender.Single();
        Assert.Equal(BroadcastMessageType.ReplaySearch, msg);
        Assert.Equal(expectedVar1, var1);
    }

    [Fact]
    public void Replay_SearchSessionTime_SendsSessionAndMilliseconds()
    {
        _sim.Replay.SearchSessionTime(sessionNumber: 2, sessionTimeMilliseconds: 90_000);

        var (msg, var1, var2) = _sender.Single();
        Assert.Equal(BroadcastMessageType.ReplaySearchSessionTime, msg);
        Assert.Equal(2, var1);
        Assert.Equal(90_000, var2);
    }

    [Fact]
    public void Replay_EraseTape_SendsReplaySetState()
    {
        _sim.Replay.EraseTape();

        var (msg, var1, _) = _sender.Single();
        Assert.Equal(BroadcastMessageType.ReplaySetState, msg);
        Assert.Equal(0, var1);
    }

    // --- pit ---

    [Theory]
    [InlineData(TireLocation.LeftFront, 3)]
    [InlineData(TireLocation.RightFront, 4)]
    [InlineData(TireLocation.LeftRear, 5)]
    [InlineData(TireLocation.RightRear, 6)]
    public void Pit_ChangeTire_MapsToOfficialCommandValues(TireLocation tire, int expectedVar1)
    {
        _sim.Pit.ChangeTire(tire, pressureKPa: 150);

        var (msg, var1, var2) = _sender.Single();
        Assert.Equal(BroadcastMessageType.PitCommand, msg);
        Assert.Equal(expectedVar1, var1);
        Assert.Equal(150, var2);
    }

    [Fact]
    public void Pit_Commands_SendOfficialModeValues()
    {
        _sim.Pit.ClearAll();
        _sim.Pit.CleanWindshield();
        _sim.Pit.AddFuel(30);
        _sim.Pit.CancelTireChanges();
        _sim.Pit.RequestFastRepair();
        _sim.Pit.CancelCleanWindshield();
        _sim.Pit.CancelFastRepair();
        _sim.Pit.CancelFuel();
        _sim.Pit.ChangeTireCompound(2);

        Assert.All(_sender.Sent, s => Assert.Equal(BroadcastMessageType.PitCommand, s.msg));
        Assert.Equal([(0, 0), (1, 0), (2, 30), (7, 0), (8, 0), (9, 0), (10, 0), (11, 0), (12, 2)],
            _sender.Sent.Select(s => (s.var1, s.var2)).ToArray());
    }

    // --- chat ---

    [Fact]
    public void Chat_SendMacro_SendsMacroNumberAsSubCommand()
    {
        _sim.Chat.SendMacro(5);

        var (msg, var1, var2) = _sender.Single();
        Assert.Equal(BroadcastMessageType.ChatCommand, msg);
        Assert.Equal(0, var1); // ChatCommandMode.Macro
        Assert.Equal(5, var2);
    }

    [Fact]
    public void Chat_Commands_SendOfficialModeValues()
    {
        _sim.Chat.Open();
        _sim.Chat.ReplyToPrivateChat();
        _sim.Chat.Close();

        Assert.Equal([1, 2, 3], _sender.Sent.Select(s => s.var1).ToArray());
    }

    // --- telemetry recording ---

    [Fact]
    public void TelemetryRecording_Commands_SendOfficialModeValues()
    {
        _sim.TelemetryRecording.Stop();
        _sim.TelemetryRecording.Start();
        _sim.TelemetryRecording.Restart();

        Assert.All(_sender.Sent, s => Assert.Equal(BroadcastMessageType.TelemCommand, s.msg));
        Assert.Equal([0, 1, 2], _sender.Sent.Select(s => s.var1).ToArray());
    }

    // --- video capture ---

    [Fact]
    public void VideoCapture_Commands_SendOfficialModeValues()
    {
        _sim.VideoCapture.CaptureScreenshot();
        _sim.VideoCapture.Start();
        _sim.VideoCapture.Stop();
        _sim.VideoCapture.Toggle();
        _sim.VideoCapture.ShowTimer();
        _sim.VideoCapture.HideTimer();

        Assert.All(_sender.Sent, s => Assert.Equal(BroadcastMessageType.VideoCapture, s.msg));
        Assert.Equal([0, 1, 2, 3, 4, 5], _sender.Sent.Select(s => s.var1).ToArray());
    }

    // --- force feedback ---

    [Fact]
    public void ForceFeedback_SetMaxForce_SendsFixedPointValue()
    {
        _sim.ForceFeedback.SetMaxForce(45.5f);

        var (msg, var1, var2) = _sender.Single();
        Assert.Equal(BroadcastMessageType.FFBCommand, msg);
        Assert.Equal(0, var1); // FFBCommandMode.MaxForce
        Assert.Equal(2_981_888, var2); // 45.5 * 65536
    }

    // --- textures ---

    [Fact]
    public void Textures_Commands_SendOfficialModeValues()
    {
        _sim.Textures.ReloadAll();
        _sim.Textures.ReloadForCar(7);

        Assert.All(_sender.Sent, s => Assert.Equal(BroadcastMessageType.ReloadTextures, s.msg));
        Assert.Equal([(0, 0), (1, 7)], _sender.Sent.Select(s => (s.var1, s.var2)).ToArray());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(16)]
    public void Chat_SendMacro_OutsideOfficialRange_Throws(int macroNumber)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => _sim.Chat.SendMacro(macroNumber));
        Assert.Empty(_sender.Sent);
    }

    [Fact]
    public void Camera_IntPosition_RejectsFocusValues_UseTypedOverloadInstead()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => _sim.Camera.SwitchToPosition(-1, 1, 1));
        Assert.Empty(_sender.Sent);
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, -1)]
    public void Pit_NegativeQuantity_Throws(int liters, int pressureKPa)
    {
        if (liters < 0)
            Assert.Throws<ArgumentOutOfRangeException>(() => _sim.Pit.AddFuel(liters));
        else
            Assert.Throws<ArgumentOutOfRangeException>(() => _sim.Pit.ChangeTire(TireLocation.LeftFront, pressureKPa));
        Assert.Empty(_sender.Sent);
    }

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(32768f)]
    public void ForceFeedback_UnrepresentableValue_Throws(float value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => _sim.ForceFeedback.SetMaxForce(value));
        Assert.Empty(_sender.Sent);
    }

    [Fact]
    public void ForceFeedback_NegativeOneSentinel_RemainsSupported()
    {
        _sim.ForceFeedback.SetMaxForce(-1f);
        Assert.Equal(-65536, _sender.Single().var2);
    }

    [Fact]
    public void Replay_UndefinedMode_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => _sim.Replay.Search((ReplaySearchMode)99));
        Assert.Empty(_sender.Sent);
    }
}
