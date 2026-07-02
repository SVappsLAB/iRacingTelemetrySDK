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

namespace UnitTests
{
    // TelemetryVar enum and the iRacingVars dictionary are maintained by hand.
    // the compiler guarantees every dictionary entry has an enum key, but not the
    // reverse - an enum member without a dictionary entry has no codegen metadata.
    // these tests keep the two in sync
    public class TelemetryVarDictionaryTests
    {
        [Fact]
        public void EveryEnumValueHasDictionaryEntry()
        {
            var vars = new iRacingVars().Vars;

            var missing = Enum.GetValues<TelemetryVar>()
                .Where(v => !vars.ContainsKey(v))
                .Select(v => v.ToString())
                .ToList();

            Assert.True(missing.Count == 0,
                $"found {missing.Count} TelemetryVar enum values missing from the iRacingVars dictionary: {string.Join(", ", missing)}");
        }

        [Fact]
        public void EveryDictionaryEntryNameMatchesItsEnumKey()
        {
            var vars = new iRacingVars().Vars;

            // the VarItem name string is what's used at runtime to look up the
            // variable in the iRacing data, so a typo would silently break the lookup
            var mismatched = vars
                .Where(kvp => kvp.Key.ToString() != kvp.Value.Name)
                .Select(kvp => $"key={kvp.Key}, name=\"{kvp.Value.Name}\"")
                .ToList();

            Assert.True(mismatched.Count == 0,
                $"found {mismatched.Count} iRacingVars entries whose Name doesn't match their enum key: {string.Join("; ", mismatched)}");
        }
    }
}
