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

using Microsoft.Extensions.Logging;
using YamlDotNet.Core;
using YamlDotNet.Serialization;

namespace SVappsLAB.iRacingTelemetrySDK.YamlParsing
{
    internal record struct ParseResult<T>(T Model, int ParseAttemptsRequired);
    internal interface ISessionInfoParser
    {
        public ParseResult<T> Parse<T>(string sessionInfo);
    }

    internal class YamlParser : ISessionInfoParser
    {
        readonly IDeserializer _yamlDeserializer;
        readonly ILogger _logger;
        readonly YamlPreparationStrategy[] _parseStrategies;

        public YamlParser(ILogger logger)
        {
            _logger = logger;
            _yamlDeserializer = new DeserializerBuilder()
#if RELEASE
                .IgnoreUnmatchedProperties()
#endif
                .Build();
            _parseStrategies =
            [
                new NoOpYamlPreparationStrategy(),
                new QuoteValuesYamlPreparationStrategy()
            ];
        }

        public ParseResult<T> Parse<T>(string srcYaml)
        {
            YamlException? lastError = null;

            for (int i = 0; i < _parseStrategies.Length; i++)
            {
                try
                {
                    var preparedYaml = _parseStrategies[i].Prepare(srcYaml);
                    return new ParseResult<T>(_yamlDeserializer.Deserialize<T>(preparedYaml), i + 1);
                }
                catch (YamlException ex)
                {
                    lastError = ex;
                    _logger.LogWarning(ex, "YAML parse failed with strategy {StrategyName}. Attempt {attempt}/{numStrategies}.",
                        _parseStrategies[i].Name, i + 1, _parseStrategies.Length);
                }
            }

            throw lastError!;
        }

        private T ParseHelper<T>(string yaml)
        {
            var model = _yamlDeserializer.Deserialize<T>(yaml);
            return model;
        }
    }
}
