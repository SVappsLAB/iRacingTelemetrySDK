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
    public class SessionInfo
    {
        public int CurrentSessionNum { get; set; } // 0
        public List<Session> Sessions { get; set; }

    }

    public class Session
    {
        public int SessionNum { get; set; } // 0
        public string SessionLaps { get; set; } // unlimited
        public string SessionTime { get; set; } // unlimited
        public int SessionNumLapsToAvg { get; set; } // 0
        public string SessionType { get; set; } // Offline Testing
        public string SessionTrackRubberState { get; set; } // moderate usage
        public string SessionName { get; set; } // TESTING
        public string SessionSubType { get; set; } // ""
        public int SessionSkipped { get; set; } // 0
        public int SessionRunGroupsUsed { get; set; } // 0
        public int SessionEnforceTireCompoundChange { get; set; } // 0
        public List<ResultPosition> ResultsPositions { get; set; }
        public List<ResultFastestLap> ResultsFastestLap { get; set; }
        public float ResultsAverageLapTime { get; set; } // -1.0000
        public int ResultsNumCautionFlags { get; set; } // 0
        public int ResultsNumCautionLaps { get; set; } // 0
        public int ResultsNumLeadChanges { get; set; } // 0
        public int ResultsLapsComplete { get; set; } // -1
        public int ResultsOfficial { get; set; } // 0



        public class ResultPosition
        {
            public int Position { get; set; } // 1
            public int ClassPosition { get; set; } // 1
            public int CarIdx { get; set; } // 0
            public int Lap { get; set; } // 1
            public float Time { get; set; } // 120.5
            public int FastestLap { get; set; } // 1
            public float FastestTime { get; set; } // 89.234
            public float LastTime { get; set; } // 89.456
            public int LapsLed { get; set; } // 0
            public int LapsComplete { get; set; } // 5
            public int JokerLapsComplete { get; set; } // 0
            public float LapsDriven { get; set; } // 5.0
            public int Incidents { get; set; } // 0
            public int ReasonOutId { get; set; } // 0
            public string ReasonOutStr { get; set; } // ""

        }

        public class ResultFastestLap

        {
            public int CarIdx { get; set; } // 255
            public int FastestLap { get; set; } // 0
            public float FastestTime { get; set; } // -1.0000

        }

    }
}
#nullable enable
