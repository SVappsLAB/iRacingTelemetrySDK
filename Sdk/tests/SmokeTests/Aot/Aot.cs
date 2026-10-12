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

using System.Diagnostics;
using System.Reflection;

namespace SmokeTests;

// publishes native AOT then run the exe against IBT file and live iRacing
[Trait("Category", "aot")]
public class Aot
{
    const int LIVE_SECS = 5;
    static readonly TimeSpan PublishTimeout = TimeSpan.FromMinutes(10);
    static readonly TimeSpan RunTimeout = TimeSpan.FromMinutes(5);

    static readonly string IbtDataDirectory = Path.Combine(AppContext.BaseDirectory, "data", "ibt");
    static readonly string PublishDirectory = Path.Combine(AppContext.BaseDirectory, "aot-publish");

    // native AOT needs vswhere.exe
    static readonly string VsInstallerDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft Visual Studio", "Installer");

    // published once per test run, shared by every test
    static readonly Lazy<Task<string>> PublishedExe = new(PublishAsync);

    readonly ITestOutputHelper _output;

    public Aot(ITestOutputHelper output)
    {
        _output = output;
        Assert.SkipUnless(OperatingSystem.IsWindows(), "native AOT smoke tests publish for win-x64");
    }

    public static TheoryData<string> IbtFiles =>
        new(Directory.GetFiles(IbtDataDirectory, "*.ibt").Select(Path.GetFileName).Order()!);

    [Theory]
    [MemberData(nameof(IbtFiles))]
    public async Task NativeAot_Ibt(string ibtFile)
    {
        var exe = await PublishedExe.Value;
        await RunAsync(exe, Path.Combine(IbtDataDirectory, ibtFile));
    }

    [Fact]
    public async Task NativeAot_Live()
    {
        Assert.SkipUnless(Live.IsIRacingConnected(), "iRacing is not running with an active session");

        var exe = await PublishedExe.Value;
        await RunAsync(exe, "--live", LIVE_SECS.ToString());
    }

    async Task RunAsync(string exe, params string[] args)
    {
        var (exitCode, output) = await RunProcessAsync(exe, args, RunTimeout);
        _output.WriteLine(output);

        Assert.True(exitCode == 0, $"AotSmokeTest exited with {exitCode}\n{output}");
    }

    static async Task<string> PublishAsync()
    {
        var project = typeof(Aot).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(a => a.Key == "AotSmokeTestProject").Value!;

        var (exitCode, output) = await RunProcessAsync("dotnet",
            ["publish", project, "-c", "Release", "-r", "win-x64", "-o", PublishDirectory],
            PublishTimeout);

        // the project treats warnings as errors, so a trim/AOT warning fails the publish too
        Assert.True(exitCode == 0, $"native AOT publish failed ({exitCode})\n{output}");

        return Path.Combine(PublishDirectory, "AotSmokeTest.exe");
    }

    static async Task<(int ExitCode, string Output)> RunProcessAsync(string fileName, IEnumerable<string> args, TimeSpan timeout)
    {
        var startInfo = new ProcessStartInfo(fileName)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }
        if (Directory.Exists(VsInstallerDirectory))
        {
            startInfo.Environment["PATH"] = VsInstallerDirectory + Path.PathSeparator + startInfo.Environment["PATH"];
        }

        using var process = Process.Start(startInfo)!;
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cts.CancelAfter(timeout);

        var stdout = process.StandardOutput.ReadToEndAsync(cts.Token);
        var stderr = process.StandardError.ReadToEndAsync(cts.Token);
        try
        {
            await process.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw;
        }

        return (process.ExitCode, (await stdout) + (await stderr));
    }
}
