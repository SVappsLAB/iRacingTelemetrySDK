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

namespace SimControl;
// interactive keyboard demo for sim control. run it alongside a live iRacing
// session, pick a command category, then fire individual commands. commands
// are fire-and-forget win32 broadcast messages - watch the simulator to see
// the effect
// no telemetry variables needed - the empty list still generates the TelemetryData
// type required to create the client
[RequiredTelemetryVars([])]
internal class Program
{
    static ISimController _sim = null!;

    // camera group/camera used for all camera switches. change with 'g' in the camera menu
    static int _cameraGroup = 1;
    static int _camera = 1;

    static async Task Main()
    {
        Console.WriteLine("SimControl demo - remote control a running iRacing simulator");

        // create the telemetry client the same way as the other samples,
        // and get the sim controller from it
        var logger = LoggerFactory.Create(builder => builder.AddConsole())
                                  .CreateLogger("SimControl");
        await using var client = TelemetryClient<TelemetryData>.Create(logger);

        _sim = client.SimControl;

        // monitor in the background so the demo can show whether iRacing is running.
        // sim control itself does not need this - commands can be sent at any time
        using var cts = new CancellationTokenSource();
        var handlers = new TelemetryHandlers<TelemetryData>
        {
            OnConnectStateChanged = state =>
            {
                Console.WriteLine($"  * iRacing is {(state == ConnectState.Connected ? "running" : "not running")}");
                return Task.CompletedTask;
            }
        };
        var monitorTask = client.Monitor(handlers, cts.Token);

        MenuLoop();

        cts.Cancel();
        await monitorTask;
    }

    static void MenuLoop()
    {
        while (true)
        {
            Console.WriteLine("""

                == commands ==
                  c  camera
                  r  replay
                  p  pit service
                  h  chat
                  t  telemetry recording
                  v  video capture
                  f  force feedback
                  x  textures
                  q  quit
                """);

            var key = ReadKey();
            if (key.Key == ConsoleKey.Escape || key.KeyChar is 'q' or 'Q')
                return;

            switch (char.ToLowerInvariant(key.KeyChar))
            {
                case 'c': CameraMenu(); break;
                case 'r': ReplayMenu(); break;
                case 'p': PitMenu(); break;
                case 'h': ChatMenu(); break;
                case 't': TelemetryRecordingMenu(); break;
                case 'v': VideoCaptureMenu(); break;
                case 'f': ForceFeedbackMenu(); break;
                case 'x': TextureMenu(); break;
            }
        }
    }

    // camera commands only work when out of the car (spectating or in a replay)
    static void CameraMenu()
    {
        Console.WriteLine($"""

            == camera ==  (using group {_cameraGroup}, camera {_camera})
              l  focus on leader
              d  focus on my driver
              i  focus on incident
              e  focus on car exiting pits
              p  focus on race position...
              n  focus on car number...
              g  set camera group/camera...
              esc  back
            """);

        while (true)
        {
            var key = ReadKey();
            if (key.Key == ConsoleKey.Escape)
                return;

            switch (char.ToLowerInvariant(key.KeyChar))
            {
                case 'l':
                    _sim.Camera.SwitchToPosition(CameraFocus.AtLeader, _cameraGroup, _camera);
                    Sent("camera to leader");
                    break;
                case 'd':
                    _sim.Camera.SwitchToPosition(CameraFocus.AtDriver, _cameraGroup, _camera);
                    Sent("camera to my driver");
                    break;
                case 'i':
                    _sim.Camera.SwitchToPosition(CameraFocus.AtIncident, _cameraGroup, _camera);
                    Sent("camera to incident");
                    break;
                case 'e':
                    _sim.Camera.SwitchToPosition(CameraFocus.AtExiting, _cameraGroup, _camera);
                    Sent("camera to exiting car");
                    break;
                case 'p':
                    if (PromptInt("race position (1 = leader)") is int pos)
                    {
                        _sim.Camera.SwitchToPosition(pos, _cameraGroup, _camera);
                        Sent($"camera to position {pos}");
                    }
                    break;
                case 'n':
                    Console.Write("  car number (leading zeros matter, e.g. '001'): ");
                    var carNumber = Console.ReadLine();
                    if (!string.IsNullOrWhiteSpace(carNumber))
                    {
                        try
                        {
                            _sim.Camera.SwitchToCar(carNumber, _cameraGroup, _camera);
                            Sent($"camera to car #{carNumber.Trim()}");
                        }
                        catch (FormatException)
                        {
                            Console.WriteLine("  invalid car number");
                        }
                    }
                    break;
                case 'g':
                    if (PromptInt("camera group") is int group)
                        _cameraGroup = group;
                    if (PromptInt("camera within group") is int camera)
                        _camera = camera;
                    Console.WriteLine($"  now using group {_cameraGroup}, camera {_camera}");
                    break;
            }
        }
    }

    // replay commands only work when out of the car
    static void ReplayMenu()
    {
        Console.WriteLine("""

            == replay ==
              space  pause
              p  play (normal speed)
              f  fast-forward (4x)
              w  rewind (4x)
              s  slow motion (1/2 speed)
              [  previous lap        ]  next lap
              u  previous incident   i  next incident
              b  jump to start       e  jump to end
              j  jump to session time...
              esc  back
            """);

        while (true)
        {
            var key = ReadKey();
            if (key.Key == ConsoleKey.Escape)
                return;

            if (key.Key == ConsoleKey.Spacebar)
            {
                _sim.Replay.SetPlaySpeed(0);
                Sent("pause");
                continue;
            }

            switch (char.ToLowerInvariant(key.KeyChar))
            {
                case 'p':
                    _sim.Replay.SetPlaySpeed(1);
                    Sent("play");
                    break;
                case 'f':
                    _sim.Replay.SetPlaySpeed(4);
                    Sent("fast-forward 4x");
                    break;
                case 'w':
                    _sim.Replay.SetPlaySpeed(-4);
                    Sent("rewind 4x");
                    break;
                case 's':
                    _sim.Replay.SetPlaySpeed(2, slowMotion: true);
                    Sent("slow motion 1/2 speed");
                    break;
                case '[':
                    _sim.Replay.Search(ReplaySearchMode.PreviousLap);
                    Sent("previous lap");
                    break;
                case ']':
                    _sim.Replay.Search(ReplaySearchMode.NextLap);
                    Sent("next lap");
                    break;
                case 'u':
                    _sim.Replay.Search(ReplaySearchMode.PreviousIncident);
                    Sent("previous incident");
                    break;
                case 'i':
                    _sim.Replay.Search(ReplaySearchMode.NextIncident);
                    Sent("next incident");
                    break;
                case 'b':
                    _sim.Replay.Search(ReplaySearchMode.ToStart);
                    Sent("jump to start");
                    break;
                case 'e':
                    _sim.Replay.Search(ReplaySearchMode.ToEnd);
                    Sent("jump to end");
                    break;
                case 'j':
                    if (PromptInt("session number") is int session &&
                        PromptInt("session time (seconds)") is int seconds)
                    {
                        _sim.Replay.SearchSessionTime(session, seconds * 1000);
                        Sent($"jump to session {session} at {seconds}s");
                    }
                    break;
            }
        }
    }

    // pit commands set/clear the pit service checkboxes and only work while in the car
    static void PitMenu()
    {
        Console.WriteLine("""

            == pit service ==
              f  add fuel...         g  cancel fuel
              1  change left front   2  change right front
              3  change left rear    4  change right rear
              a  change all tires    x  cancel tire changes
              w  clean windshield    e  cancel windshield
              r  fast repair         d  cancel fast repair
              o  tire compound...
              c  clear all
              esc  back
            """);

        while (true)
        {
            var key = ReadKey();
            if (key.Key == ConsoleKey.Escape)
                return;

            switch (char.ToLowerInvariant(key.KeyChar))
            {
                case 'f':
                    if (PromptInt("liters to add (0 = existing amount)") is int liters)
                    {
                        _sim.Pit.AddFuel(liters);
                        Sent(liters == 0 ? "add fuel (existing amount)" : $"add {liters} liters of fuel");
                    }
                    break;
                case 'g':
                    _sim.Pit.CancelFuel();
                    Sent("cancel fuel");
                    break;
                case '1':
                    _sim.Pit.ChangeTire(TireLocation.LeftFront);
                    Sent("change left front tire");
                    break;
                case '2':
                    _sim.Pit.ChangeTire(TireLocation.RightFront);
                    Sent("change right front tire");
                    break;
                case '3':
                    _sim.Pit.ChangeTire(TireLocation.LeftRear);
                    Sent("change left rear tire");
                    break;
                case '4':
                    _sim.Pit.ChangeTire(TireLocation.RightRear);
                    Sent("change right rear tire");
                    break;
                case 'a':
                    _sim.Pit.ChangeTire(TireLocation.LeftFront);
                    _sim.Pit.ChangeTire(TireLocation.RightFront);
                    _sim.Pit.ChangeTire(TireLocation.LeftRear);
                    _sim.Pit.ChangeTire(TireLocation.RightRear);
                    Sent("change all four tires");
                    break;
                case 'x':
                    _sim.Pit.CancelTireChanges();
                    Sent("cancel tire changes");
                    break;
                case 'w':
                    _sim.Pit.CleanWindshield();
                    Sent("clean windshield");
                    break;
                case 'e':
                    _sim.Pit.CancelCleanWindshield();
                    Sent("cancel windshield clean");
                    break;
                case 'r':
                    _sim.Pit.RequestFastRepair();
                    Sent("fast repair");
                    break;
                case 'd':
                    _sim.Pit.CancelFastRepair();
                    Sent("cancel fast repair");
                    break;
                case 'o':
                    if (PromptInt("tire compound index") is int compound)
                    {
                        _sim.Pit.ChangeTireCompound(compound);
                        Sent($"tire compound {compound}");
                    }
                    break;
                case 'c':
                    _sim.Pit.ClearAll();
                    Sent("clear all pit service");
                    break;
            }
        }
    }

    static void ChatMenu()
    {
        Console.WriteLine("""

            == chat ==
              o  open chat window
              r  reply to last private chat
              x  close chat window
              m  send chat macro...
              esc  back
            """);

        while (true)
        {
            var key = ReadKey();
            if (key.Key == ConsoleKey.Escape)
                return;

            switch (char.ToLowerInvariant(key.KeyChar))
            {
                case 'o':
                    _sim.Chat.Open();
                    Sent("open chat");
                    break;
                case 'r':
                    _sim.Chat.ReplyToPrivateChat();
                    Sent("reply to private chat");
                    break;
                case 'x':
                    _sim.Chat.Close();
                    Sent("close chat");
                    break;
                case 'm':
                    if (PromptInt("macro number (1-15)") is int macro)
                    {
                        _sim.Chat.SendMacro(macro);
                        Sent($"chat macro {macro}");
                    }
                    break;
            }
        }
    }

    // controls the IBT files iRacing writes to disk (normally toggled with alt-L)
    static void TelemetryRecordingMenu()
    {
        Console.WriteLine("""

            == telemetry recording ==
              s  start recording
              x  stop recording
              r  restart (save current file, start a new one)
              esc  back
            """);

        while (true)
        {
            var key = ReadKey();
            if (key.Key == ConsoleKey.Escape)
                return;

            switch (char.ToLowerInvariant(key.KeyChar))
            {
                case 's':
                    _sim.TelemetryRecording.Start();
                    Sent("start telemetry recording");
                    break;
                case 'x':
                    _sim.TelemetryRecording.Stop();
                    Sent("stop telemetry recording");
                    break;
                case 'r':
                    _sim.TelemetryRecording.Restart();
                    Sent("restart telemetry recording");
                    break;
            }
        }
    }

    // uses the simulator's built-in capture - must be enabled in the iRacing app settings
    static void VideoCaptureMenu()
    {
        Console.WriteLine("""

            == video capture ==
              c  capture screenshot
              s  start video capture
              x  stop video capture
              t  toggle video capture
              m  show video timer
              h  hide video timer
              esc  back
            """);

        while (true)
        {
            var key = ReadKey();
            if (key.Key == ConsoleKey.Escape)
                return;

            switch (char.ToLowerInvariant(key.KeyChar))
            {
                case 'c':
                    _sim.VideoCapture.CaptureScreenshot();
                    Sent("screenshot");
                    break;
                case 's':
                    _sim.VideoCapture.Start();
                    Sent("start video capture");
                    break;
                case 'x':
                    _sim.VideoCapture.Stop();
                    Sent("stop video capture");
                    break;
                case 't':
                    _sim.VideoCapture.Toggle();
                    Sent("toggle video capture");
                    break;
                case 'm':
                    _sim.VideoCapture.ShowTimer();
                    Sent("show video timer");
                    break;
                case 'h':
                    _sim.VideoCapture.HideTimer();
                    Sent("hide video timer");
                    break;
            }
        }
    }

    static void ForceFeedbackMenu()
    {
        Console.WriteLine("""

            == force feedback ==
              m  set max force...
              esc  back
            """);

        while (true)
        {
            var key = ReadKey();
            if (key.Key == ConsoleKey.Escape)
                return;

            if (char.ToLowerInvariant(key.KeyChar) == 'm')
            {
                if (PromptFloat("max force (newton-meters)") is float maxForce)
                {
                    _sim.ForceFeedback.SetMaxForce(maxForce);
                    Sent($"max force {maxForce} Nm");
                }
            }
        }
    }

    // useful after editing custom paint files
    static void TextureMenu()
    {
        Console.WriteLine("""

            == textures ==
              a  reload textures for all cars
              c  reload textures for one car...
              esc  back
            """);

        while (true)
        {
            var key = ReadKey();
            if (key.Key == ConsoleKey.Escape)
                return;

            switch (char.ToLowerInvariant(key.KeyChar))
            {
                case 'a':
                    _sim.Textures.ReloadAll();
                    Sent("reload all car textures");
                    break;
                case 'c':
                    if (PromptInt("carIdx") is int carIdx)
                    {
                        _sim.Textures.ReloadForCar(carIdx);
                        Sent($"reload textures for carIdx {carIdx}");
                    }
                    break;
            }
        }
    }

    static ConsoleKeyInfo ReadKey() => Console.ReadKey(intercept: true);

    static void Sent(string message) => Console.WriteLine($"  > sent: {message}");

    static int? PromptInt(string label)
    {
        Console.Write($"  {label}: ");
        if (int.TryParse(Console.ReadLine(), out var value))
            return value;

        Console.WriteLine("  invalid number");
        return null;
    }

    static float? PromptFloat(string label)
    {
        Console.Write($"  {label}: ");
        if (float.TryParse(Console.ReadLine(), out var value))
            return value;

        Console.WriteLine("  invalid number");
        return null;
    }
}
