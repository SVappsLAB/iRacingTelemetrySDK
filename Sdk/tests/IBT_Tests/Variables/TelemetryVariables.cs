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

using IBT_Tests.SessionInfo;

namespace IBT_Tests.Variables
{
    public class TelemetryVariables : IClassFixture<TestSession_Fixture>
    {
        private readonly TestSession_Fixture _fixture;


        public TelemetryVariables(TestSession_Fixture fixture)
        {
            _fixture = fixture;
        }

        [Theory]
        [InlineData(true, "IsOnTrackCar", "Car on track", typeof(bool), false)]
        [InlineData(true, "SessionTick", "Current update number", typeof(int), false)]
        [InlineData(true, "SessionTime", "Seconds since session start", typeof(double), false)]
        [InlineData(true, "EngineWarnings", "Bitfield for warning lights", typeof(uint), false)]
        [InlineData(true, "RPM", "Engine rpm", typeof(float), false)]
        [InlineData(true, "Lat", "Latitude in decimal degrees", typeof(double), false)]
        [InlineData(false, "no_such_var", "", typeof(object), false)]
        public async Task Variables(bool validVar, string varName, string desc, Type type, bool isTimeValue)
        {
            var vars = await _fixture.TelemetryClient.GetTelemetryVariables();

            var v = vars.FirstOrDefault(v => v.Name == varName);
            if (validVar)
            {
                Assert.NotNull(v);
                Assert.Contains(desc, v.Desc);
                Assert.Equal(type, v.Type);
                Assert.Equal(isTimeValue, v.IsTimeValue);
            }
            else
            {
                Assert.Null(v);
            }
        }
    }
}
