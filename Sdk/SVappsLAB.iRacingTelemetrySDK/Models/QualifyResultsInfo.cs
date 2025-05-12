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
    public class QualifyResultsInfo
    {
        public List<Result> Results { get; set; }


        public class Result
        {
            public int Position { get; set; } // 0
            public int ClassPosition { get; set; } // 0
            public int CarIdx { get; set; } // 2
            public int Lap { get; set; } // 3
            public float Time { get; set; } // 139.3070
            public int FastestLap { get; set; } // 3
            public float FastestTime { get; set; } // 139.3070
            public float LastTime { get; set; } // 139.3070
            public int LapsLed { get; set; } // 0
            public int LapsComplete { get; set; } // 3
            public int JokerLapsComplete { get; set; } // 0
            public float LapsDriven { get; set; } // 4.400
            public int Incidents { get; set; } // 6
            public int ReasonOutId { get; set; } // 0
            public string ReasonOutStr { get; set; } // Running

        }
    }
}
#nullable enable
