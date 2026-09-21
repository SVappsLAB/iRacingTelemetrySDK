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

using SVappsLAB.iRacingTelemetrySDK;
using System.Collections.Concurrent;
using System.Diagnostics.Metrics;

namespace MeterListenerSample;

/// <summary>
/// Collects the SDK's metrics with a <see cref="System.Diagnostics.Metrics.MeterListener"/>,
/// which is built into the runtime and needs no packages.
///
/// A MeterListener hands you raw measurements - one callback per recorded value - and stops
/// there, so everything below is the aggregation OpenTelemetry would otherwise do for you:
/// the dictionary keyed by instrument-plus-tags, the <see cref="Stat"/> rows, the reporting
/// timer, and the printing. It is all here rather than in Program.cs so the sample's client
/// setup reads the same as the OpenTelemetry sample's.
/// </summary>
internal sealed class MetricsCollector : IDisposable
{
    // one row per instrument, per set of tags - so the drop reasons stay apart
    readonly ConcurrentDictionary<string, Stat> _stats = new();
    readonly object _consoleLock = new();
    readonly MeterListener _listener;
    readonly Timer _timer;

    /// <summary>
    /// Starts listening. Measurements are printed every <paramref name="reportInterval"/>,
    /// and whenever <see cref="Print"/> is called.
    /// </summary>
    public MetricsCollector(TimeSpan reportInterval)
    {
        // subscribe to the SDK's meter by name; the SDK creates the meter itself, so
        // nothing has to be passed to the client
        _listener = new MeterListener
        {
            InstrumentPublished = (instrument, theListener) =>
            {
                if (instrument.Meter.Name == TelemetryMetrics.MeterName)
                    theListener.EnableMeasurementEvents(instrument);
            }
        };

        // raw measurements arrive one at a time, and we aggregate them ourselves.
        // the SDK's counters are long, its histograms are double
        _listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) => Record(instrument, value, tags));
        _listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) => Record(instrument, value, tags));
        _listener.Start();

        _timer = new Timer(_ => Print("metrics"), null, reportInterval, reportInterval);
    }

    public void Dispose()
    {
        _timer.Dispose();
        _listener.Dispose();
    }

    void Record(Instrument instrument, double value, ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        // the meter-level 'data_source' tag is not repeated here; these are the per-measurement
        // tags - drop.reason, error.type, session_info.stream - and each combination gets its own row
        var key = instrument.Name;
        foreach (var tag in tags)
            key += $" [{tag.Key}={tag.Value}]";

        _stats.GetOrAdd(key, _ => new Stat(instrument.Unit, instrument is Counter<long>)).Add(value);
    }

    public void Print(string title)
    {
        lock (_consoleLock)
        {
            Console.WriteLine($"{Environment.NewLine}--- {title} ---");

            foreach (var (name, stat) in _stats.OrderBy(kvp => kvp.Key))
            {
                var value = stat switch
                {
                    // counters are totals
                    { IsCounter: true } => $"{stat.Sum,10:N0} {stat.Unit}",
                    // durations read better scaled than in raw seconds
                    { Unit: "s" } => $"{stat.Count,10:N0} recorded   avg {Duration(stat.Sum / stat.Count)}   max {Duration(stat.Max)}",
                    // everything else - byte sizes, parse attempts
                    _ => $"{stat.Count,10:N0} recorded   avg {stat.Sum / stat.Count,10:N0} {stat.Unit}   max {stat.Max,10:N0} {stat.Unit}"
                };

                // trim the meter prefix the instruments all share
                Console.WriteLine($"  {name.Replace("svappslab.iracingsdk.", ""),-62} {value}");
            }
        }
    }

    // most of these land in microseconds, so scale rather than print a row of zeros
    static string Duration(double seconds) =>
        seconds < 0.001 ? $"{seconds * 1_000_000,8:N1} us" : $"{seconds * 1_000,8:N3} ms";

    // count/sum/max is all a MeterListener gives you without writing a bucketing histogram
    // yourself - no percentiles, and the SDK's bucket 'advice' is not applied
    class Stat(string? unit, bool isCounter)
    {
        public string? Unit => unit;
        public bool IsCounter => isCounter;
        public long Count { get; private set; }
        public double Sum { get; private set; }
        public double Max { get; private set; }

        public void Add(double value)
        {
            // measurements arrive on the SDK's threads
            lock (this)
            {
                Count++;
                Sum += value;
                Max = Math.Max(Max, value);
            }
        }
    }
}
