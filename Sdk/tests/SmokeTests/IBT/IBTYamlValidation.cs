using Microsoft.Extensions.Logging.Abstractions;
using SVappsLAB.iRacingTelemetrySDK;
using SVappsLAB.iRacingTelemetrySDK.DataProviders;
using SVappsLAB.iRacingTelemetrySDK.YamlParsing;

namespace SmokeTests;

public class IBTYamlValidation
{
    private readonly ITestOutputHelper _output;
    private static string TelemetryDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "iRacing", "telemetry");

    public IBTYamlValidation(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task ValidateYamlInAllIbtFiles()
    {
        // skip if directory doesn't exist
        if (!Directory.Exists(TelemetryDir))
        {
            _output.WriteLine($"skipping: telemetry directory not found: {TelemetryDir}");
            return;
        }

        var ibtFiles = Directory.GetFiles(TelemetryDir, "*.ibt");
        Assert.True(ibtFiles.Length > 0, "no IBT files found");

        var parser = new YamlParser(new XunitLogger(_output));
        var failures = new List<(string file, string error)>();
        var successes = new List<(string file, int attempts)>();

        foreach (var ibtFile in ibtFiles)
        {
            var fileName = Path.GetFileName(ibtFile);
            try
            {
                await using var provider = new IBTDataProvider(NullLogger.Instance, new IBTOptions(ibtFile));
                provider.OpenDataSource();

                var rawYaml = provider.GetSessionInfoYaml();
                var result = parser.Parse<TelemetrySessionInfo>(rawYaml);

                successes.Add((fileName, result.ParseAttemptsRequired));
                _output.WriteLine($"OK ({result.ParseAttemptsRequired} attempts): {fileName}");
            }
            catch (Exception ex)
            {
                failures.Add((fileName, ex.Message));
                _output.WriteLine($"FAIL: {fileName} — {ex.Message}");
            }
        }

        // summary
        _output.WriteLine($"\n--- summary ---");
        _output.WriteLine($"total: {ibtFiles.Length}, passed: {successes.Count}, failed: {failures.Count}");

        if (successes.Count > 0)
        {
            var multiAttempt = successes.Where(s => s.attempts > 1).ToList();
            if (multiAttempt.Count > 0)
            {
                _output.WriteLine($"\nfiles requiring yaml fixes ({multiAttempt.Count}):");
                foreach (var (file, attempts) in multiAttempt)
                    _output.WriteLine($"  {file}: {attempts} attempts");
            }
        }

        if (failures.Count > 0)
        {
            var failureReport = string.Join("\n", failures.Select(f => $"  {f.file}: {f.error}"));
            Assert.Fail($"{failures.Count} file(s) failed YAML parsing:\n{failureReport}");
        }
    }
}
