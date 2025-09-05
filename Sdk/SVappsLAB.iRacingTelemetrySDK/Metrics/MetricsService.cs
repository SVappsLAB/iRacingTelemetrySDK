/**
 * Copyright (C) 2024-2025 Scott Velez
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
using System.Collections.Generic;
using System.Diagnostics.Metrics;

namespace SVappsLAB.iRacingTelemetrySDK.Metrics
{
    public class TelemetryMeters
    {
        public TelemetryMeters(Meter meter)
        {
            Processed = meter.CreateCounter<long>(
                "telemetry_records_processed_total",
                description: "Total number of telemetry records processed");

            Dropped = meter.CreateCounter<long>(
                "telemetry_records_dropped_total",
                description: "Total number of telemetry records dropped");

            Duration = meter.CreateHistogram<double>(
                "telemetry_processing_duration_microseconds",
                unit: "us",
                description: "Time spent processing individual telemetry records");
        }
        public Counter<long> Processed { get; private set; }
        public Counter<long> Dropped { get; private set; }
        public Histogram<double> Duration { get; private set; }

        internal void RecordsProcessed(long count) => Processed.Add(count);
        internal void RecordsDropped(long count) => Dropped.Add(count);
        internal void ProcessingDuration(TimeSpan ts) => Duration.Record(ts.TotalMicroseconds);
    }
    public class SessionInfoMeters
    {
        public SessionInfoMeters(Meter meter)
        {
            Processed = meter.CreateCounter<long>(
                "sessioninfo_records_processed_total",
                description: "Total number of session info records processed");

            Duration = meter.CreateHistogram<double>(
                "sessioninfo_processing_duration_milliseconds",
                unit: "ms",
                description: "Time spent processing individual session info records");
        }
        public Counter<long> Processed { get; private set; }
        public Histogram<double> Duration { get; private set; }

        internal void RecordsProcessed(long count) => Processed.Add(count);
        internal void ProcessingDuration(TimeSpan ts) => Duration.Record(ts.TotalMilliseconds);
    }

    public interface IMetricsService : IDisposable
    {
        TelemetryMeters Telemetry { get; }
        SessionInfoMeters SessionInfo { get; }
    }

    public class MetricsService : IMetricsService
    {
        private readonly Meter _meter;
        public TelemetryMeters Telemetry { get; private set; }
        public SessionInfoMeters SessionInfo { get; private set; }

        public MetricsService(IMeterFactory? meterFactory, string dataSourceType)
        {
            var tags = new List<KeyValuePair<string, object?>>
            {
                new("data_source", dataSourceType)
            };

            _meter = meterFactory != null ?
                meterFactory.Create(Constants.SDK_NAME, null, tags) :
               new Meter(Constants.SDK_NAME, null, tags);

            Telemetry = new TelemetryMeters(_meter);
            SessionInfo = new SessionInfoMeters(_meter);

            //// Connection metrics
            //_connectionsTotal = _meter.CreateCounter<long>(
            //    "telemetry_connections_total",
            //    description: "Total number of telemetry connections established");

            //_disconnectionsTotal = _meter.CreateCounter<long>(
            //    "telemetry_disconnections_total",
            //    description: "Total number of telemetry disconnections");

            //_connectionStatus = _meter.CreateGauge<int>(
            //    "telemetry_connection_status",
            //    description: "Current connection status (1 = connected, 0 = disconnected)");

            //_connectionDuration = _meter.CreateHistogram<double>(
            //    "telemetry_connection_duration_seconds",
            //    unit: "s",
            //    description: "Duration of telemetry connections",
            //    advice: new InstrumentAdvice<double> { HistogramBucketBoundaries = [1, 5, 10, 30, 60, 300, 600, 1800, 3600] });


            //// Observable metrics for current state
            //_meter.CreateObservableGauge<double>(
            //    "telemetry_connection_uptime_seconds",
            //    () => _isConnected && _connectionStartTime.HasValue ?
            //          (DateTime.UtcNow - _connectionStartTime.Value).TotalSeconds : 0.0,
            //    unit: "s",
            //    description: "Current connection uptime in seconds");
        }

        public void Dispose()
        {
            _meter.Dispose();
        }
    }
}
