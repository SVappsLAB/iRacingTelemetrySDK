/**
 * Copyright (C)2024 Scott Velez
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

using System;
using YamlDotNet.Core;
using YamlDotNet.Serialization;

namespace SVappsLAB.iRacingTelemetrySDK
{
    public record struct ParseResult<T>(T Model, int ParseAttemptsRequired);
    public interface ISessionInfoParser
    {
        public ParseResult<T> Parse<T>(string sessionInfo);
    }

    public class YamlParser : ISessionInfoParser
    {
        readonly IDeserializer _yamlDeserializer;

        public YamlParser()
        {
            _yamlDeserializer = new DeserializerBuilder()
#if !DEBUG
                .IgnoreUnmatchedProperties()
#endif
                //.WithNamingConvention(UnderscoredNamingConvention.Instance)
                .Build();
        }

        public ParseResult<T> Parse<T>(string srcYaml)
        {
            const int MAX_ATTEMPTS = 10;
            string tmpYaml = srcYaml;

            for (int attempts = 0; attempts < MAX_ATTEMPTS; attempts++)
            {
                try
                {
                    return new ParseResult<T>(ParseHelper<T>(tmpYaml), attempts + 1);
                }
                catch (YamlException ex)
                {
                    tmpYaml = TryToFixYaml(tmpYaml, ex);
                }
            }

            // If all attempts fail, throw an exception or handle the error accordingly
            throw new Exception("Failed to parse YAML after multiple attempts.");
        }
        private T ParseHelper<T>(string yaml)
        {
            var model = _yamlDeserializer.Deserialize<T>(yaml);
            return model;
        }
        private string TryToFixYaml(string yaml, YamlException ex)
        {
            // hack: find the problem area and escape it
            // this seems to be the most common issue with the iRacing yaml

            var badDataStart = ex.Start.Index;
            var badDataEnd = FindEndOfBadData(yaml, badDataStart, ["\r\n", "\n", "#"]);

            // create new string with the bad data escaped
            var escapedBadData = $"\"{yaml.Substring(badDataStart, badDataEnd - badDataStart)}\"";
            // patch it all back togther.. rinse and repeat
            var fixedYaml = $"{yaml.Substring(0, badDataStart)}{escapedBadData}{yaml.Substring(badDataEnd)}";

            return fixedYaml;
        }
        static int FindEndOfBadData(string text, int startIndex, string[] terminationStrings)
        {
            // hack: yuk, yuk, yuk
            // find the end of the bad data by looking for any of the termination strings

            for (int i = startIndex; i < text.Length; i++)
            {
                foreach (var term in terminationStrings)
                {
                    if (i + term.Length > text.Length)
                        continue;
                    var subStr = text.Substring(i, term.Length);
                    if (subStr == term)
                        return i;
                }
            }

            throw new Exception("Unable to find termination char");
        }
    }
}


