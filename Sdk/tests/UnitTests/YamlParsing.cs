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

// disable for file
#pragma warning disable CS8602

using SVappsLAB.iRacingTelemetrySDK;
using SVappsLAB.iRacingTelemetrySDK.Models;
using Xunit;

namespace UnitTests
{
    public class YamlParsing
    {
        static string yaml_with_no_issues = @"
DriverInfo:
  DriverSetupName: mySetup.sto
";
        static string unescapedYaml_with_1_issues = @"
DriverInfo:
  DriverSetupName: - X
";
        static string unescapedYaml_with_5_issues = @"
DriverInfo:
  DriverSetupName: - x.sto
Drivers:
  - CarIdx: 0
    UserName: `myName`
    AbbrevName: $
    Initials: [
    TeamName: @ALL
";

        public static TheoryData<string, Type, int> Data =>
    new TheoryData<string, Type, int>
        {
        {  yaml_with_no_issues, typeof(object), 1 },
        {  unescapedYaml_with_1_issues, typeof(object), 2 },
        {  unescapedYaml_with_5_issues, typeof(object), 5 },
        {  File.ReadAllText(@"data/valid.yaml"), typeof(TelemetrySessionInfo), 1 },
        {  File.ReadAllText(@"data/invalid-unescapedChars.yaml"), typeof(TelemetrySessionInfo), 6 }
        };

        [Theory]
        [MemberData(nameof(Data))]
        public void ParseYaml(string yaml, Type modelType, int parseAttempts)
        {
            var parser = new YamlParser();

            // need to jump through some hoops to call the generic method and get a generic result
            //
            // all this to do this:
            //      var result = parser.Parse<T>(yaml);

            var methodInfo = typeof(YamlParser).GetMethod(nameof(YamlParser.Parse));
            var genericMethodInfo = methodInfo.MakeGenericMethod(modelType);

            var result = genericMethodInfo.Invoke(parser, new object[] { yaml });

            var modelValue = result.GetType().GetProperty("Model").GetValue(result);
            var parseAttemptsValue = result.GetType().GetProperty("ParseAttemptsRequired").GetValue(result);

            // and finally, the tests
            Assert.IsAssignableFrom(modelType, modelValue);
            Assert.Equal(parseAttempts, parseAttemptsValue);
        }
    }
}
