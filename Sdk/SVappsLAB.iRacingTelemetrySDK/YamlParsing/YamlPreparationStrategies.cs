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

using System.Text.RegularExpressions;

namespace SVappsLAB.iRacingTelemetrySDK.YamlParsing
{
    internal abstract class YamlPreparationStrategy
    {
        public abstract string Name { get; }
        public abstract string Prepare(string srcYaml);
    }

    // this the optimum strategy for well-formed yaml (no-op)
    // and will be the first option we try
    internal class NoOpYamlPreparationStrategy : YamlPreparationStrategy
    {
        public override string Name => "No-Op";
        public override string Prepare(string srcYaml) => srcYaml;
    }

    // regex can be slow, but the parsing is running on a separate Task, off the critical path
    internal partial class QuoteValuesYamlPreparationStrategy : YamlPreparationStrategy
    {
        public override string Name => "Quote-Values";

        public override string Prepare(string srcYaml)
        {
            return KnownStringKeysRegex().Replace(srcYaml, match =>
            {
                var keyPart = match.Groups[1].Value;
                var valuePart = match.Groups[2].Value;

                // preserve trailing \r
                var hasCR = valuePart.EndsWith('\r');
                if (hasCR)
                    valuePart = valuePart[..^1];

                var value = valuePart.TrimStart();

                // skip already-quoted values
                if (value.Length >= 2 &&
                    ((value[0] == '\'' && value[^1] == '\'') ||
                     (value[0] == '"' && value[^1] == '"')))
                    return match.Value;

                // wrap in single quotes, escaping embedded single quotes
                var escaped = value.Replace("'", "''");
                return $"{keyPart} '{escaped}'{(hasCR ? "\r" : "")}";
            });
        }

        [GeneratedRegex(@"^(\s*(?:AbbrevName|TeamName|UserName|Initials|DriverSetupName):)([ \t]+\S.*)$", RegexOptions.Multiline)]
        private static partial Regex KnownStringKeysRegex();
    }
}
