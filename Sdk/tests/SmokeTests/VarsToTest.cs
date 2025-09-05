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

using SVappsLAB.iRacingTelemetrySDK;

namespace SmokeTests
{
    // this class is only used as a convenient place to define a
    // set of shared variables that the live and ibt test classes can use
    [RequiredTelemetryVars([
        TelemetryVar.dcPushToPass,      // type 1 - boolean
        TelemetryVar.SessionNum,        // type 2 - int
        TelemetryVar.EngineWarnings,    // type 3 - bitfield (flags)
        TelemetryVar.RPM,               // type 4 - float
        TelemetryVar.SessionTime,       // type 5 - double
        TelemetryVar.CarDistAhead,      // new var, doesn't exist in old ibt files
        ])]
    public class VarsToTest
    {
    }
}
