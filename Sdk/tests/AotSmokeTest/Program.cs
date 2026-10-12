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
using Microsoft.Extensions.Logging.Abstractions;
using SVappsLAB.iRacingTelemetrySDK;
using SVappsLAB.iRacingTelemetrySDK.DataProviders;
using SVappsLAB.iRacingTelemetrySDK.irSDKDefines;

namespace AotSmokeTest;

// one variable of each kind the generated reader handles
[RequiredTelemetryVars([
    TelemetryVar.IsOnTrack,              // bool
    TelemetryVar.SessionNum,             // int
    TelemetryVar.SessionState,           // enum
    TelemetryVar.EngineWarnings,         // flags
    TelemetryVar.CarIdxTrackSurface,     // enum array
    TelemetryVar.SteeringWheelTorque_ST, // float array
    TelemetryVar.RPM,                    // float
    TelemetryVar.SessionTime,            // double
    ])]
internal class Program
{
    // manual (not source generated), populated through the reflection fallback
    public struct ManualTelemetryData
    {
        public float RPM { get; set; }
        public double? SessionTime { get; set; }
        public SessionState? SessionState { get; set; }
        public TrackLocation[]? CarIdxTrackSurface { get; set; }
    }

    const string ConversionFailureMessage = "Failed to set property";
    const int MaxReportedMismatches = 10;

    static readonly List<string> Failures = [];

    // where telemetry comes from: an IBT file played to the end, or live iRacing for a fixed time
    sealed record Source(IBTOptions? IbtOptions, TimeSpan LiveDuration)
    {
        public CancellationTokenSource CreateRunToken() =>
            IbtOptions is null ? new CancellationTokenSource(LiveDuration) : new CancellationTokenSource();
    }

    public static async Task<int> Main(string[] args)
    {
        Source source;
        var liveSeconds = 5;
        if (args is ["--live"] || (args is ["--live", var seconds] && int.TryParse(seconds, out liveSeconds)))
        {
            source = new Source(null, TimeSpan.FromSeconds(liveSeconds));
        }
        else if (args is [var ibtFile] && !ibtFile.StartsWith("--"))
        {
            source = new Source(new IBTOptions(ibtFile), TimeSpan.Zero);
        }
        else
        {
            Console.WriteLine("usage: AotSmokeTest <file.ibt>");
            Console.WriteLine("       AotSmokeTest --live [seconds]   (default 5)");
            return 2;
        }

        using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Warning));
        var logger = new CountingLogger(loggerFactory.CreateLogger("AotSmokeTest"));
        // synchronous to let a handler read the raw provider values (GetValue)
        var clientOptions = new ClientOptions { DeliveryMode = TelemetryDeliveryMode.Synchronous };

        CheckReaderConversions();
        await CheckGeneratedTelemetryData(logger, source, clientOptions);
        await CheckManualTelemetryData(logger, source, clientOptions);

        // the SDK logs a warning and leaves the property null when a value can't be converted
        var conversionFailures = logger.Count(ConversionFailureMessage);
        Check(conversionFailures == 0, $"telemetry: {conversionFailures} conversion failure warning(s) logged");

        foreach (var failure in Failures)
        {
            Console.WriteLine($"FAIL: {failure}");
        }
        Console.WriteLine(Failures.Count == 0 ? "PASS" : $"{Failures.Count} check(s) failed");
        return Failures.Count == 0 ? 0 : 1;
    }

    // compares every field of every sample with the provider's raw value for that record
    static async Task CheckGeneratedTelemetryData(ILogger logger, Source source, ClientOptions clientOptions)
    {
        await using var client = TelemetryClient<TelemetryData>.Create(logger, source.IbtOptions, clientOptions);
        using var cts = source.CreateRunToken();

        var samples = 0;
        var compare = new ProviderComparison("generated", () => client.GetTelemetryVariables());
        TelemetryData last = default;
        TelemetrySessionInfo? session = null;
        var errors = 0;

        await client.Monitor(new TelemetryHandlers<TelemetryData>
        {
            OnTelemetryUpdate = data =>
            {
                samples++;
                last = data;

                compare.Scalar(samples, "IsOnTrack", data.IsOnTrack, client.GetValue("IsOnTrack"));
                compare.Scalar(samples, "SessionNum", data.SessionNum, client.GetValue("SessionNum"));
                compare.Enum(samples, "SessionState", data.SessionState, client.GetValue("SessionState"));
                compare.Enum(samples, "EngineWarnings", data.EngineWarnings, client.GetValue("EngineWarnings"));
                compare.Array(samples, "CarIdxTrackSurface", data.CarIdxTrackSurface, client.GetValue("CarIdxTrackSurface"));
                compare.Array(samples, "SteeringWheelTorque_ST", data.SteeringWheelTorque_ST, client.GetValue("SteeringWheelTorque_ST"));
                compare.Scalar(samples, "RPM", data.RPM, client.GetValue("RPM"));
                compare.Scalar(samples, "SessionTime", data.SessionTime, client.GetValue("SessionTime"));
                return Task.CompletedTask;
            },
            OnSessionInfoUpdate = info =>
            {
                session = info;
                return Task.CompletedTask;
            },
            OnError = ex =>
            {
                errors++;
                Console.WriteLine($"error: {ex}");
                return Task.CompletedTask;
            },
        }, cts.Token);

        Console.WriteLine($"generated: {samples} samples, last RPM={last.RPM} SessionTime={last.SessionTime} " +
            $"SessionState={last.SessionState} EngineWarnings={last.EngineWarnings} IsOnTrack={last.IsOnTrack} SessionNum={last.SessionNum}");
        Console.WriteLine($"generated: present: {string.Join(", ", compare.Present.Order())}");
        Console.WriteLine($"session info: track={session?.WeekendInfo?.TrackName} drivers={session?.DriverInfo?.Drivers?.Count} " +
            $"sessions={session?.SessionInfo?.Sessions?.Count}");

        Check(samples > 0, "generated: no telemetry samples");
        Check(compare.Declared.Contains("RPM") && compare.Declared.Contains("SessionTime"),
            "generated: the source doesn't declare RPM and SessionTime, so there's nothing to compare");
        compare.Report();

        Check(session?.WeekendInfo?.TrackName is { Length: > 0 }, "session info: WeekendInfo not parsed");
        Check(session?.DriverInfo?.Drivers is { Count: > 0 }, "session info: DriverInfo.Drivers not parsed");
        Check(session?.SessionInfo?.Sessions is { Count: > 0 }, "session info: SessionInfo.Sessions not parsed");
        Check(errors == 0, $"generated: {errors} errors reported");
    }

    static async Task CheckManualTelemetryData(ILogger logger, Source source, ClientOptions clientOptions)
    {
        await using var client = TelemetryClient<ManualTelemetryData>.Create(logger, source.IbtOptions, clientOptions);
        using var cts = source.CreateRunToken();

        var samples = 0;
        var compare = new ProviderComparison("manual", () => client.GetTelemetryVariables());

        await client.Monitor(new TelemetryHandlers<ManualTelemetryData>
        {
            OnTelemetryUpdate = data =>
            {
                samples++;

                // RPM isn't nullable, so a missing value reads as 0
                compare.Scalar(samples, "RPM", (float?)data.RPM, client.GetValue("RPM"), missingAs: 0f);
                compare.Scalar(samples, "SessionTime", data.SessionTime, client.GetValue("SessionTime"));
                compare.Enum(samples, "SessionState", data.SessionState, client.GetValue("SessionState"));
                compare.Array(samples, "CarIdxTrackSurface", data.CarIdxTrackSurface, client.GetValue("CarIdxTrackSurface"));
                return Task.CompletedTask;
            },
        }, cts.Token);

        Console.WriteLine($"manual: {samples} samples, present: {string.Join(", ", compare.Present.Order())}");

        Check(samples > 0, "manual: no telemetry samples");
        compare.Report();
    }

    // generated Read
    static void CheckReaderConversions()
    {
        var logger = new CountingLogger(NullLogger.Instance);
        var provider = new FakeDataProvider(new()
        {
            ["IsOnTrack"] = true,
            ["SessionNum"] = (short)2,                   // converted to int
            ["SessionState"] = (int)SessionState.Racing,
            ["EngineWarnings"] = (int)(EngineWarnings.WaterTempWarning | EngineWarnings.PitSpeedLimiter),
            ["CarIdxTrackSurface"] = new[] { -1, 3, 1 }, // int[] read as TrackLocation[]
            ["SteeringWheelTorque_ST"] = new[] { 1.5f, -2.25f },
            ["RPM"] = 7250.5d,                           // converted to float
            ["SessionTime"] = "not a number",            // can't convert: null and a warning
        });

        var data = Read(new TelemetryValueReader(provider, logger));
        Console.WriteLine($"reader: {data}");

        Check(data.IsOnTrack == true, $"reader: IsOnTrack = {data.IsOnTrack}, expected True");
        Check(data.SessionNum == 2, $"reader: SessionNum = {data.SessionNum}, expected 2 (short converted to int)");
        Check(data.SessionState == SessionState.Racing, $"reader: SessionState = {data.SessionState}, expected Racing");
        Check(data.EngineWarnings == (EngineWarnings.WaterTempWarning | EngineWarnings.PitSpeedLimiter),
            $"reader: EngineWarnings = {data.EngineWarnings}, expected WaterTempWarning, PitSpeedLimiter");
        Check(data.CarIdxTrackSurface is [TrackLocation.NotInWorld, TrackLocation.OnTrack, TrackLocation.InPitStall],
            $"reader: CarIdxTrackSurface = [{Join(data.CarIdxTrackSurface)}], expected [NotInWorld, OnTrack, InPitStall]");
        Check(data.SteeringWheelTorque_ST is [1.5f, -2.25f],
            $"reader: SteeringWheelTorque_ST = [{Join(data.SteeringWheelTorque_ST)}], expected [1.5, -2.25]");
        Check(data.RPM == 7250.5f, $"reader: RPM = {data.RPM}, expected 7250.5 (double converted to float)");
        Check(data.SessionTime is null, $"reader: SessionTime = {data.SessionTime}, expected null for an unconvertible value");
        var conversionFailures = logger.Count(ConversionFailureMessage);
        Check(conversionFailures == 1, $"reader: {conversionFailures} conversion failure warning(s), expected 1 (SessionTime)");

        // with nothing in the provider, every variable is missing and every property is null
        var empty = Read(new TelemetryValueReader(new FakeDataProvider([]), logger));
        Check(empty == default, $"reader: expected every property null when no variables are present, got {empty}");
    }

    static TelemetryData Read(TelemetryValueReader reader) =>
        ((IGeneratedTelemetryData<TelemetryData>)default(TelemetryData)).Read(reader);

    static string Join<T>(T[]? values) => values is null ? "null" : string.Join(", ", values);

    static void Check(bool condition, string failure)
    {
        if (!condition) Failures.Add(failure);
    }

    // finds differences between struct property and the provider's raw value
    sealed class ProviderComparison(string label, Func<IReadOnlyList<TelemetryVariable>> getDeclaredVariables)
    {
        int _mismatches;
        readonly List<string> _examples = [];
        readonly HashSet<string> _compared = [];
        HashSet<string>? _declared;

        // variables the source declares, read on the first sample (once the headers are available)
        public IReadOnlySet<string> Declared => _declared ?? [];

        // variables the provider had a value for in at least one sample
        public HashSet<string> Present { get; } = [];

        // missingAs: what a non-nullable property holds when the variable is missing
        public void Scalar<T>(int sample, string name, T? actual, object? raw, T? missingAs = null) where T : struct
        {
            Track(name, raw);
            T? expected = raw is null ? missingAs : (T)Convert.ChangeType(raw, typeof(T));
            if (!EqualityComparer<T?>.Default.Equals(actual, expected))
                Add(sample, name, actual?.ToString(), expected?.ToString());
        }

        // enums and flags arrive as their int value
        public void Enum<TEnum>(int sample, string name, TEnum? actual, object? raw) where TEnum : struct, System.Enum
        {
            Track(name, raw);
            int? actualValue = actual is null ? null : Convert.ToInt32(actual.Value);
            var expected = (int?)raw;
            if (actualValue != expected)
                Add(sample, name, actual?.ToString(), expected?.ToString());
        }

        public void Array<T>(int sample, string name, T[]? actual, object? raw)
        {
            Track(name, raw);
            var expected = (System.Array?)raw;
            var same = (actual, expected) switch
            {
                (null, null) => true,
                (null, _) or (_, null) => false,
                // an enum array element compares by its int value
                _ => actual.Length == expected.Length &&
                     actual.Select((value, i) => Convert.ToDouble(value).Equals(Convert.ToDouble(expected.GetValue(i)))).All(equal => equal),
            };
            if (!same)
                Add(sample, name, actual is null ? null : $"[{Join(actual)}]",
                    expected is null ? null : $"[{string.Join(", ", expected.Cast<object>())}]");
        }

        void Track(string name, object? raw)
        {
            _declared ??= getDeclaredVariables().Select(v => v.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            _compared.Add(name);
            if (raw is not null) Present.Add(name);
        }

        void Add(int sample, string name, string? actual, string? expected)
        {
            _mismatches++;
            if (_examples.Count < MaxReportedMismatches)
                _examples.Add($"sample {sample}: {name} = {actual ?? "null"}, provider has {expected ?? "null"}");
        }

        public void Report()
        {
            foreach (var example in _examples)
                Console.WriteLine($"{label} mismatch: {example}");
            Check(_mismatches == 0, $"{label}: {_mismatches} value(s) differ from the provider");

            foreach (var name in _compared)
            {
                if (Declared.Contains(name))
                    Check(Present.Contains(name), $"{label}: {name} is declared by the source but never had a value");
                else
                    Check(!Present.Contains(name), $"{label}: {name} isn't declared by the source but had a value");
            }
        }
    }

    // counts log messages, so silent conversion failures (logged as warnings) can be detected
    sealed class CountingLogger(ILogger inner) : ILogger
    {
        readonly List<string> _messages = [];

        public int Count(string text)
        {
            lock (_messages) return _messages.Count(m => m.Contains(text));
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => inner.BeginScope(state);
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (_messages) _messages.Add(formatter(state, exception));
            inner.Log(logLevel, eventId, state, exception, formatter);
        }
    }

    // serves fixed variable values to the reader. only GetVarValue is used
    sealed class FakeDataProvider(Dictionary<string, object> values) : IDataProvider
    {
        public object? GetVarValue(string varName) => values.GetValueOrDefault(varName);

        public bool IsConnected => true;
        public void OpenDataSource() { }
        public bool IsSessionInfoUpdated() => false;
        public irsdk_header GetHeader() => throw new NotSupportedException();
        public string GetSessionInfoYaml() => throw new NotSupportedException();
        public VarHeaderDictionary? GetVarHeaders() => null;
        public Task<bool> WaitForDataReady(TimeSpan timeout, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
