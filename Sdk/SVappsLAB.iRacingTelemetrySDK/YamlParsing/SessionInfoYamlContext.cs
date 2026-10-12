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

using System.ComponentModel;
using YamlDotNet.Serialization;

namespace SVappsLAB.iRacingTelemetrySDK.YamlParsing;

// compile-time (de)serialization metadata for the session info models
// include each type. a type that's missing fails at runtime
[YamlStaticContext]
[YamlSerializable(typeof(TelemetrySessionInfo))]
[YamlSerializable(typeof(WeekendInfo))]
[YamlSerializable(typeof(WeekendOptions))]
[YamlSerializable(typeof(TelemetryOptions))]
[YamlSerializable(typeof(SessionInfo))]
[YamlSerializable(typeof(Session))]
[YamlSerializable(typeof(Session.QualifyPosition))]
[YamlSerializable(typeof(Session.ResultPosition))]
[YamlSerializable(typeof(Session.ResultFastestLap))]
[YamlSerializable(typeof(QualifyResultsInfo))]
[YamlSerializable(typeof(QualifyResultsInfo.Result))]
[YamlSerializable(typeof(CameraInfo))]
[YamlSerializable(typeof(Group))]
[YamlSerializable(typeof(Camera))]
[YamlSerializable(typeof(RadioInfo))]
[YamlSerializable(typeof(Radio))]
[YamlSerializable(typeof(Frequency))]
[YamlSerializable(typeof(DriverInfo))]
[YamlSerializable(typeof(Driver))]
[YamlSerializable(typeof(DriverTire))]
[YamlSerializable(typeof(SplitTimeInfo))]
[YamlSerializable(typeof(Sector))]
[EditorBrowsable(EditorBrowsableState.Never)]
public partial class SessionInfoYamlContext : YamlDotNet.Serialization.StaticContext
{
}
