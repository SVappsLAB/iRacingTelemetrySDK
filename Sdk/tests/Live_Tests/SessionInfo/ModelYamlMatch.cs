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

using System.Reflection;
using Microsoft.Extensions.Logging.Abstractions;
using SVappsLAB.iRacingTelemetrySDK;
using SVappsLAB.iRacingTelemetrySDK.Models;
using YamlDotNet.Serialization;

namespace Live_Tests
{
    public class ModelYamlMatch
    {
        const int TIMEOUT_SECS = 5;
        ITestOutputHelper _output;

        public ModelYamlMatch(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public async Task ModelShouldMatchYaml()
        {
            CancellationTokenSource cts = new CancellationTokenSource(TimeSpan.FromSeconds(TIMEOUT_SECS));

            var client = TelemetryClient<TelemetryData>.Create(NullLogger.Instance);

            bool sessionInfoReceived = false;

            client.OnRawSessionInfoUpdate += (object? sender, string rawYaml) =>
            {
                sessionInfoReceived = true;

                // save rawYaml to a file for debugging
                //File.WriteAllText("rawyaml.yml", rawYaml);

                var allMissingProperties = ValidateModelAgainstYaml<TelemetrySessionInfo>(rawYaml);

                // skip 'CarSetup' properties since they are dynamic and can vary widely
                var missingProperties = allMissingProperties
                    .Where(prop => !prop.StartsWith("CarSetup"))
                    .ToList();

                cts.Cancel();

                Assert.True(missingProperties.Count == 0, $"missing properties in model: {string.Join(", ", missingProperties)}");
            };

            await client.Monitor(cts.Token);

            Assert.True(sessionInfoReceived, "Session info was not received within the timeout period.");
        }

        List<string> ValidateModelAgainstYaml<T>(string rawYaml)
        {
            var deserializer = new DeserializerBuilder().Build();
            var rawSessionInfo = deserializer.Deserialize<Dictionary<object, object>>(rawYaml);

            // check if all YAML keys exist in the model
            var missingProperties = new List<string>();
            RecursiveMatcher(rawSessionInfo, typeof(T), "", missingProperties);

            return missingProperties;
        }

        bool RecursiveMatcher(Dictionary<object, object> yamlObject, Type modelType, string currentPath, List<string> missingProperties)
        {
            bool allPropertiesFound = true;
            var properties = modelType.GetProperties(BindingFlags.Public | BindingFlags.Instance);
            var propertyNames = properties.Select(p => p.Name).ToList();

            foreach (var entry in yamlObject)
            {
                string? key = entry.Key?.ToString();
                if (key == null) continue;

                string propertyPath = string.IsNullOrEmpty(currentPath) ? key : $"{currentPath}.{key}";

                // check if the property exists in the model
                var matchingProperty = properties.FirstOrDefault(p =>
                    p.Name.Equals(key, StringComparison.OrdinalIgnoreCase));
#if DEBUG
                var debugMsg = $"checking property '{propertyPath}' against model type '{modelType.Name}', matched: {matchingProperty != null}";
                _output.WriteLine(debugMsg);
#endif
                if (matchingProperty == null)
                {
                    missingProperties.Add(propertyPath);
                    allPropertiesFound = false;
                    continue;
                }

                // if this is a nested object, recurse into it
                if (entry.Value is Dictionary<object, object> nestedDict)
                {
                    Type propertyType = matchingProperty.PropertyType;

                    // if property is a collection or dictionary type, get the element type
                    if (propertyType.IsGenericType)
                    {
                        Type[] genericArgs = propertyType.GetGenericArguments();
                        if (genericArgs.Length > 0)
                        {
                            // use the value type for dictionaries or the element type for collections
                            propertyType = genericArgs[genericArgs.Length - 1];
                        }
                    }

                    bool nestedResult = RecursiveMatcher(nestedDict, propertyType, propertyPath, missingProperties);
                    allPropertiesFound = allPropertiesFound && nestedResult;
                }
                else if (entry.Value is List<object> list)
                {
                    foreach (var item in list)
                    {
                        if (item is Dictionary<object, object> itemDict)
                        {
                            Type elementType = matchingProperty.PropertyType.GetElementType() ??
                                              (matchingProperty.PropertyType.IsGenericType ?
                                               matchingProperty.PropertyType.GetGenericArguments()[0] :
                                               typeof(object));

                            bool listItemResult = RecursiveMatcher(itemDict, elementType, $"{propertyPath}[item]", missingProperties);
                            allPropertiesFound = allPropertiesFound && listItemResult;
                        }
                    }
                }
            }

            return allPropertiesFound;
        }
    }
}
