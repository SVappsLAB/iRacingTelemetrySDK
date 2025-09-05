using System.Collections;
using SVappsLAB.iRacingTelemetrySDK;

namespace SmokeTests
{
    internal class SessionSummary() : SummaryBase
    {

        public string? TrackName { get; init; }
        public int NumberOfDrivers { get; init; }
        public string? WeatherConditions { get; init; }
        public string? SessionType { get; init; }
        public int NumSessions { get; init; }


        public static SessionSummary Create(TelemetrySessionInfo si)
        {
            return new SessionSummary
            {
                TrackName = si.WeekendInfo?.TrackDisplayShortName ?? UNKNOWN,
                NumberOfDrivers = si.DriverInfo?.Drivers?.Count ?? -1,
                WeatherConditions = $"{si.WeekendInfo?.TrackSkies ?? UNKNOWN}, {si.WeekendInfo?.TrackPrecipitation ?? UNKNOWN} Rain",
                SessionType = si.WeekendInfo?.EventType ?? UNKNOWN,
                NumSessions = (si.SessionInfo.Sessions?.Count ?? -1),

                // TODO: track number of drivers currently active in the session
            };
        }
    }

    internal class VariableSummary() : SummaryBase
    {
        public int NumVariables { get; init; }
        public int VarTypes { get; init; }
        public Dictionary<int, int> LengthCounts { get; init; } = new();

        public static VariableSummary Create(IEnumerable<TelemetryVariable> vars)
        {

            return new VariableSummary
            {
                NumVariables = vars.Count(),
                VarTypes = vars.Select(v => v.Type).Distinct().Count(),
                LengthCounts = vars.GroupBy(v => v.Length)
                                  .ToDictionary(g => g.Key, g => g.Count()),
            };
        }
    }

    internal class SummaryBase
    {
        protected const string UNKNOWN = "<null>";
        public override string ToString()
        {
            var properties = GetType().GetProperties()
                .Where(p => p.Name != nameof(ToString))
                .Select(p =>
                {
                    var value = p.GetValue(this);
                    if (value is IDictionary dict)
                    {
                        var pairs = dict.Cast<dynamic>()
                            .Select(kvp => $"{kvp.Key}={kvp.Value}")
                            .ToArray();
                        return $"{p.Name}: {{{string.Join(", ", pairs)}}}";
                    }
                    return $"{p.Name}: {value ?? UNKNOWN}";
                });
            return string.Join(", ", properties);
        }
    }
}

