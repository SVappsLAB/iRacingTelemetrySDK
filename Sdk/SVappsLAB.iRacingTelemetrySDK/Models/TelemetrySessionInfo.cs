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
 * limitations under the License.using Microsoft.CodeAnalysis;
**/

using System.Collections.Generic;

#nullable disable
namespace SVappsLAB.iRacingTelemetrySDK.Models
{
    public class TelemetrySessionInfo
    {
        public WeekendInfo WeekendInfo;
        public SessionInfo SessionInfo { get; set; }
        public QualifyResultsInfo QualifyResultsInfo { get; set; }
        public CameraInfo CameraInfo { get; set; }
        public RadioInfo RadioInfo { get; set; }
        public DriverInfo DriverInfo { get; set; }
        public SplitTimeInfo SplitTimeInfo { get; set; }
        //public dynamic CarSetup { get; set; }
        public Dictionary<string, object> CarSetup { get; set; }

    }

}
#nullable enable

