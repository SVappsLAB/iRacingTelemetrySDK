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
    public class RadioInfo
    {
        public int SelectedRadioNum { get; set; }
        public List<Radio> Radios { get; set; }

    }

    public class Radio
    {
        public int RadioNum { get; set; }
        public int HopCount { get; set; }
        public int NumFrequencies { get; set; }
        public int TunedToFrequencyNum { get; set; }
        public int ScanningIsOn { get; set; }
        public List<Frequency> Frequencies { get; set; }

    }

    public class Frequency
    {
        public int FrequencyNum { get; set; }
        public string FrequencyName { get; set; }
        public int Priority { get; set; }
        public int CarIdx { get; set; }
        public int EntryIdx { get; set; }
        public int ClubID { get; set; }
        public int CanScan { get; set; }
        public int CanSquawk { get; set; }
        public int Muted { get; set; }
        public int IsMutable { get; set; }
        public int IsDeletable { get; set; }

    }
}
#nullable enable
