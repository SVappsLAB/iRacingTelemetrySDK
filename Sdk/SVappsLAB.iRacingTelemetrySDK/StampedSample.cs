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

namespace SVappsLAB.iRacingTelemetrySDK;

// what travels through the internal telemetry channel: one record plus when it was acquired.
// TData is the caller's source-generated struct. the timestamp rides with it and is
// unwrapped before delivery - the public stream still yields plain TData.
// AcquiredTimestamp is TelemetryMeters.NotTiming when no pipeline instrument is being collected
internal readonly record struct StampedSample<TData>(TData Sample, long AcquiredTimestamp);
