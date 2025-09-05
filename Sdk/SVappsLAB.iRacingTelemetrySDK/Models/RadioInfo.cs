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
namespace SVappsLAB.iRacingTelemetrySDK
{
    public class RadioInfo
    {
        public int SelectedRadioNum { get; set; } // 0
        public List<Radio> Radios { get; set; }

    }

    public class Radio
    {
        public int RadioNum { get; set; } // 0
        public int HopCount { get; set; } // 1
        public int NumFrequencies { get; set; } // 6
        public int TunedToFrequencyNum { get; set; } // 0
        public int ScanningIsOn { get; set; } // 1
        public List<Frequency> Frequencies { get; set; }

    }

    public class Frequency
    {
        public int FrequencyNum { get; set; } // 0
        public string FrequencyName { get; set; } // "@ALLTEAMS"
        public int Priority { get; set; } // 12
        public int CarIdx { get; set; } // -1
        public int EntryIdx { get; set; } // -1
        public int ClubID { get; set; } // 0
        public int CanScan { get; set; } // 1
        public int CanSquawk { get; set; } // 1
        public int Muted { get; set; } // 0
        public int IsMutable { get; set; } // 1
        public int IsDeletable { get; set; } // 0

    }
}
#nullable enable
