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

using SVappsLAB.iRacingTelemetrySDK.YamlParsing;

namespace UnitTests.YamlParsing
{
    public class YamlStrategyTests
    {
        private readonly QuoteValuesYamlPreparationStrategy _quoteStrategy = new();

        [Fact]
        public void NoOp_LeavesValuesUnchanged()
        {
            var strategy = new NoOpYamlPreparationStrategy();
            var src =
@"
 DriverInfo:
  DriverSetupName: x.sto
  Drivers:
    - CarIdx: 0
      UserName: myName
";

            var result = strategy.Prepare(src);

            Assert.Multiple(
                () => Assert.Contains("DriverSetupName: x.sto", result),
                () => Assert.Contains("UserName: myName", result)
            );
        }

        [Theory]
        [InlineData("  DriverSetupName: - x.sto", "DriverSetupName: '- x.sto'")]
        [InlineData("  UserName: `myName`", "UserName: '`myName`'")]
        [InlineData("  AbbrevName: $", "AbbrevName: '$'")]
        [InlineData("  Initials: [", "Initials: '['")]
        [InlineData("  TeamName: @ALL", "TeamName: '@ALL'")]
        [InlineData("  UserName: O'Neil", "UserName: 'O''Neil'")]
        public void QuoteValues_QuotesProblematicValues(string inputLine, string expectedOutput)
        {
            var result = _quoteStrategy.Prepare(inputLine);

            Assert.Contains(expectedOutput, result);
        }

        [Fact]
        public void QuoteValues_LeavesAlreadyQuotedValuesUnchanged()
        {
            var src =
@"
DriverInfo:
  Drivers:
    - CarIdx: 0
      UserName: 'Already Single'
      TeamName: ""Already Double""
";

            var result = _quoteStrategy.Prepare(src);

            Assert.Multiple(
                () => Assert.Contains("UserName: 'Already Single'", result),
                () => Assert.Contains("TeamName: \"Already Double\"", result),
                () => Assert.DoesNotContain("''Already Single''", result)
            );
        }
    }
}
