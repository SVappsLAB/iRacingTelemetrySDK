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
 * limitations under the License.
**/

using SVappsLAB.iRacingTelemetrySDK;
using SVappsLAB.iRacingTelemetrySDK.YamlParsing;

namespace UnitTests.YamlParsing
{
    public class YamlFileParsing
    {
        private readonly ITestOutputHelper _output;
        public YamlFileParsing(ITestOutputHelper output) => _output = output;

        [Fact]
        public void ParseYaml_ValidFile_ParsesOnFirstAttempt()
        {
            var parser = new YamlParser(new XunitLogger(_output));
            var yaml = File.ReadAllText(@"data/valid.yaml");

            var result = parser.Parse<TelemetrySessionInfo>(yaml);

            Assert.NotNull(result.Model);
            Assert.Equal(1, result.ParseAttemptsRequired);
        }

        [Fact]
        public void ParseYaml_InvalidFile_ParsesOnSecondAttempt()
        {
            var parser = new YamlParser(new XunitLogger(_output));
            var yaml = File.ReadAllText(@"data/invalid-unescapedChars.yaml");

            var result = parser.Parse<TelemetrySessionInfo>(yaml);

            Assert.NotNull(result.Model);
            Assert.Equal(2, result.ParseAttemptsRequired);
        }
    }
}
