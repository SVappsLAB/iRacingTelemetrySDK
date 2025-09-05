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

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace SVappsLAB.iRacingTelemetrySDK
{

    public record struct VarsToGenerate(VarType[] vars, (TelemetryVar variable, Location location)[] duplicates);
    public record struct VarType(TelemetryVar variable, Type type, Location location);

    [Generator(LanguageNames.CSharp)]
    public class CodeGenerator : IIncrementalGenerator
    {
        // marker attribute
        const string MarkerAttribute = @"
            #nullable enable
            using System;

            namespace SVappsLAB.iRacingTelemetrySDK
            {
                [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
                internal sealed class RequiredTelemetryVarsAttribute : Attribute
                {
                    private readonly TelemetryVar[]? _vars;

                    public RequiredTelemetryVarsAttribute(TelemetryVar[]? vars)
                    {
                        _vars = vars;
                    }
                }
            }";

        static readonly Lazy<iRacingVars> _iRacingData = new Lazy<iRacingVars>(() => new iRacingVars());
        static readonly ConcurrentDictionary<string, Type> _typeCache = new ConcurrentDictionary<string, Type>();

        // performance telemetry
        static readonly ConcurrentDictionary<string, long> _performanceCounters = new ConcurrentDictionary<string, long>();
        static readonly ConcurrentDictionary<string, TimeSpan> _performanceTimes = new ConcurrentDictionary<string, TimeSpan>();

        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            // add the marker attribute
            context.RegisterPostInitializationOutput(static ctx => ctx.AddSource(
                "MarkerAttribute.g.cs", SourceText.From(MarkerAttribute, Encoding.UTF8)));

            // find our marker attribute
            var pipeline = context.SyntaxProvider.ForAttributeWithMetadataName<VarsToGenerate?>(
                "SVappsLAB.iRacingTelemetrySDK.RequiredTelemetryVarsAttribute",
                predicate: predMatcher,
                transform: tranformer)
                .Where(static m => m is not null);

            context.RegisterSourceOutput(pipeline,
                static (spc, source) => Execute(spc, source));
        }

        private bool predMatcher(SyntaxNode sn, CancellationToken _ct)
        {
            var x = sn is ClassDeclarationSyntax;
            return x;
        }
        private VarsToGenerate? tranformer(GeneratorAttributeSyntaxContext context, CancellationToken _ct)
        {
            var stopwatch = Stopwatch.StartNew();
            IncrementCounter(TelemetryConstants.COUNTER_TRANSFORMER_CALLS);

            try
            {
                var attribute = ValidateAndGetAttribute(context);
                if (attribute == null)
                    return null;

                var variableLocations = ExtractVariableLocations(context, attribute);
                if (variableLocations.Count == 0)
                    return null;

                var duplicatesWithLocations = FindDuplicateVariables(variableLocations);
                var varList = ProcessVariables(variableLocations);

                IncrementCounter(TelemetryConstants.COUNTER_VARIABLES_PROCESSED, varList.Length);
                return new VarsToGenerate(varList, duplicatesWithLocations);
            }
            finally
            {
                stopwatch.Stop();
                RecordTime(TelemetryConstants.TIME_TRANSFORMER_DURATION, stopwatch.Elapsed);
            }
        }

        private AttributeData? ValidateAndGetAttribute(GeneratorAttributeSyntaxContext context)
        {
            if (context.Attributes.Length == 0)
                return null;

            var attr = context.Attributes[0];
            if (attr.ConstructorArguments.Length == 0)
                return null;

            return attr;
        }

        private List<(TelemetryVar variable, Location location)> ExtractVariableLocations(GeneratorAttributeSyntaxContext context, AttributeData attribute)
        {
            var variableLocations = new List<(TelemetryVar variable, Location location)>();

            for (int argIndex = 0; argIndex < attribute.ConstructorArguments.Length; argIndex++)
            {
                var arg = attribute.ConstructorArguments[argIndex];
                for (int valueIndex = 0; valueIndex < arg.Values.Length; valueIndex++)
                {
                    var value = arg.Values[valueIndex];
                    if (value.Value is int enumValue && Enum.IsDefined(typeof(TelemetryVar), enumValue))
                    {
                        var telemetryVar = (TelemetryVar)enumValue;
                        var location = GetVariableLocation(context, argIndex, valueIndex);
                        variableLocations.Add((telemetryVar, location));
                    }
                }
            }

            return variableLocations;
        }

        private (TelemetryVar variable, Location location)[] FindDuplicateVariables(List<(TelemetryVar variable, Location location)> variableLocations)
        {
            var duplicateGroups = variableLocations
                .GroupBy(x => x.variable)
                .Where(g => g.Count() > 1)
                .ToArray();

            // only report the first occurrence of each duplicate variable name
            var duplicatesWithLocations = duplicateGroups
                .Select(g => g.First())
                .ToArray();

            if (duplicatesWithLocations.Length > 0)
            {
                IncrementCounter(TelemetryConstants.COUNTER_DUPLICATE_VARIABLES_FOUND);
            }

            return duplicatesWithLocations;
        }

        private VarType[] ProcessVariables(List<(TelemetryVar variable, Location location)> variableLocations)
        {
            var varList = new VarType[variableLocations.Count];

            for (int i = 0; i < variableLocations.Count; i++)
            {
                var (variable, location) = variableLocations[i];
                varList[i] = ProcessSingleVariable(variable, location);
            }

            return varList;
        }

        private VarType ProcessSingleVariable(TelemetryVar variable, Location location)
        {
            if (_iRacingData.Value.Vars.TryGetValue(variable, out var varItem))
            {
                var type = GetVariableType(varItem);
                return new VarType(variable, type, location);
            }
            else
            {
                IncrementCounter(TelemetryConstants.COUNTER_UNKNOWN_VARIABLES);
                return new VarType(variable, typeof(Exception), location);
            }
        }

        private Type GetVariableType(object varItem)
        {
            // use reflection to get properties since we can't use dynamic in source generators
            var nameProperty = varItem.GetType().GetProperty("Name");
            var typeProperty = varItem.GetType().GetProperty("Type");
            var lengthProperty = varItem.GetType().GetProperty("Length");

            var name = nameProperty?.GetValue(varItem)?.ToString() ?? "";
            var type = (int)(typeProperty?.GetValue(varItem) ?? 0);
            var length = (int)(lengthProperty?.GetValue(varItem) ?? 1);

            var cacheKey = $"{name}_{type}_{length}";

            // track cache hits/misses
            bool cacheHit = _typeCache.ContainsKey(cacheKey);
            if (cacheHit) IncrementCounter(TelemetryConstants.COUNTER_CACHE_HITS);
            else IncrementCounter(TelemetryConstants.COUNTER_CACHE_MISSES);

            return _typeCache.GetOrAdd(cacheKey, _ => type switch
            {
                0 => length == 1 ? typeof(byte) : typeof(byte[]),
                1 => length == 1 ? typeof(bool) : typeof(bool[]),
                2 => GetIntOrEnumType(name, length),
                3 => GetFlagType(name, length),
                4 => length == 1 ? typeof(float) : typeof(float[]),
                5 => length == 1 ? typeof(double) : typeof(double[]),
                _ => throw new NotImplementedException(),
            });
        }

        private static void Execute(SourceProductionContext spc, VarsToGenerate? vars)
        {
            var stopwatch = Stopwatch.StartNew();
            IncrementCounter(TelemetryConstants.COUNTER_EXECUTE_CALLS);

            try
            {
                if (vars is null)
                    return;

                var values = vars.Value.vars;
                var duplicates = vars.Value.duplicates;

                ReportDuplicateVariableDiagnostics(spc, duplicates);
                ReportUnknownVariableDiagnostics(spc, values);

                // filter out duplicates and invalid variables before generating code
                var validValues = FilterValidVariables(values, duplicates);

                // only generate code if we have valid variables
                if (validValues.Length > 0)
                {
                    var varList = GenerateVariableList(validValues);
                    var code = GenerateSourceCode(varList);

                    spc.AddSource("iRacingTelemetrySDK.g.cs", code);
                    IncrementCounter(TelemetryConstants.COUNTER_SOURCES_GENERATED);
                }
            }
            finally
            {
                stopwatch.Stop();
                RecordTime(TelemetryConstants.TIME_EXECUTE_DURATION, stopwatch.Elapsed);
                LogMetricsToDebug();
            }
        }

        private static VarType[] FilterValidVariables(VarType[] values, (TelemetryVar variable, Location location)[] duplicates)
        {
            var duplicateVariables = new HashSet<TelemetryVar>(duplicates.Select(d => d.variable));
            var seenVariables = new HashSet<TelemetryVar>();
            var validVariables = new List<VarType>();

            foreach (var variable in values)
            {
                // skip if it's a duplicate or unknown variable
                if (duplicateVariables.Contains(variable.variable) || variable.type == typeof(Exception))
                    continue;

                // skip if we've already seen this variable
                if (!seenVariables.Add(variable.variable))
                    continue;

                validVariables.Add(variable);
            }

            return validVariables.ToArray();
        }

        private static void ReportDuplicateVariableDiagnostics(SourceProductionContext spc, (TelemetryVar variable, Location location)[] duplicates)
        {
            foreach (var (variable, location) in duplicates)
            {
                var diagnostic = CreateDuplicateVariableDiagnostic(variable.ToString(), location);
                spc.ReportDiagnostic(diagnostic);
            }
        }

        private static void ReportUnknownVariableDiagnostics(SourceProductionContext spc, VarType[] values)
        {
            foreach (var item in values)
            {
                if (item.type == typeof(Exception))
                {
                    var diagnostic = CreateUnknownVariableDiagnostic(item.variable.ToString(), item.location);
                    spc.ReportDiagnostic(diagnostic);
                }
            }
        }

        private static string GenerateVariableList(VarType[] values)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < values.Length; i++)
            {
                var item = values[i];

                if (i > 0)
                    sb.AppendLine();

                // Generate property declarations with nullable types
                var nullableFriendlyTypeName = GetNullableFriendlyTypeName(item.type);
                var propertyDeclaration = $"        public {nullableFriendlyTypeName} {item.variable} {{ get; init; }}";
                sb.Append(propertyDeclaration);
            }
            return sb.ToString();
        }

        private static string GenerateSourceCode(string varList)
        {
            return $$"""
                #nullable enable
                using System;

                namespace SVappsLAB.iRacingTelemetrySDK
                {
                    /// <summary>
                    /// Represents iRacing telemetry data with strongly-typed properties.
                    /// This record is generated based on the RequiredTelemetryVarsAttribute usage.
                    /// Properties return null when the corresponding telemetry variable is not available.
                    /// </summary>
                    public record struct TelemetryData
                    {
                {{varList}}
                    }
                }
                """;
        }

        private static Diagnostic CreateDuplicateVariableDiagnostic(string name, Location location)
        {
            return Diagnostic.Create(
                new DiagnosticDescriptor(
                    id: "DuplicateVarName",
                    title: "Duplicate Variable Name",
                    messageFormat: "Duplicate telemetry variable name found: \"{0}\".",
                    category: "Usage",
                    defaultSeverity: DiagnosticSeverity.Error,
                    isEnabledByDefault: true),
                location,
                name);
        }

        private static Diagnostic CreateUnknownVariableDiagnostic(string name, Location location)
        {
            return Diagnostic.Create(
                new DiagnosticDescriptor(
                    id: "UnknownVarName",
                    title: "Unknown Variable Name",
                    messageFormat: "Unknown telemetry variable name: \"{0}\".  Check spelling.",
                    category: "Usage",
                    defaultSeverity: DiagnosticSeverity.Error,
                    isEnabledByDefault: true),
                location,
                name);
        }

        private static Location GetVariableLocation(GeneratorAttributeSyntaxContext context, int argIndex, int valueIndex)
        {
            var attributeSyntax = context.TargetNode.DescendantNodes()
                .OfType<AttributeSyntax>()
                .FirstOrDefault(a => a.Name.ToString().Contains("RequiredTelemetryVars"));

            if (attributeSyntax?.ArgumentList?.Arguments == null || argIndex >= attributeSyntax.ArgumentList.Arguments.Count)
                return Location.None;

            var argument = attributeSyntax.ArgumentList.Arguments[argIndex];

            // handle array initializer syntax like new string[] { "var1", "var2" }
            if (argument.Expression is ImplicitArrayCreationExpressionSyntax implicitArray)
            {
                if (implicitArray.Initializer?.Expressions != null && valueIndex < implicitArray.Initializer.Expressions.Count)
                {
                    var expression = implicitArray.Initializer.Expressions[valueIndex];
                    return expression.GetLocation();
                }
            }
            // handle explicit array creation syntax like new string[] { "var1", "var2" }
            else if (argument.Expression is ArrayCreationExpressionSyntax arrayCreation)
            {
                if (arrayCreation.Initializer?.Expressions != null && valueIndex < arrayCreation.Initializer.Expressions.Count)
                {
                    var expression = arrayCreation.Initializer.Expressions[valueIndex];
                    return expression.GetLocation();
                }
            }
            // handle collection expression syntax like ["var1", "var2"]
            else if (argument.Expression is CollectionExpressionSyntax collectionExpression)
            {
                if (collectionExpression.Elements != null && valueIndex < collectionExpression.Elements.Count)
                {
                    var element = collectionExpression.Elements[valueIndex];
                    if (element is ExpressionElementSyntax expressionElement)
                    {
                        return expressionElement.Expression.GetLocation();
                    }
                }
            }
            // handle direct string literal
            else if (valueIndex == 0)
            {
                return argument.Expression.GetLocation();
            }

            return Location.None;
        }

        private Type GetIntOrEnumType(string varName, int length)
        {
            var enumType = GetEnumTypeForVariable(varName);
            return CreateTypeForLength(enumType ?? typeof(int), length);
        }

        private Type GetFlagType(string varName, int length)
        {
            var flagType = GetFlagTypeForVariable(varName);
            if (flagType == null)
                throw new NotImplementedException($"Unknown flag type: {varName}");

            return CreateTypeForLength(flagType, length);
        }

        private static Type? GetEnumTypeForVariable(string varName) => varName switch
        {
            "CarIdxTrackSurface" => typeof(TrackLocation),
            "CarIdxTrackSurfaceMaterial" => typeof(TrackSurface),
            "CarLeftRight" => typeof(CarLeftRight),
            "PaceMode" => typeof(PaceMode),
            "PlayerCarPitSvStatus" => typeof(PitServiceStatus),
            "PlayerTrackSurface" => typeof(TrackLocation),
            "PlayerTrackSurfaceMaterial" => typeof(TrackSurface),
            "SessionState" => typeof(SessionState),
            "TrackWetness" => typeof(TrackWetness),
            _ => null
        };

        private static Type? GetFlagTypeForVariable(string varName) => varName switch
        {
            "CamCameraState" => typeof(CameraState),
            "CarIdxPaceFlags" => typeof(PaceFlags),
            "CarIdxSessionFlags" => typeof(SessionFlags),
            "EngineWarnings" => typeof(EngineWarnings),
            "PitSvFlags" => typeof(PitServiceFlags),
            "PlayerIncidents" => typeof(IncidentFlags),
            "SessionFlags" => typeof(SessionFlags),
            _ => null
        };

        private static Type CreateTypeForLength(Type baseType, int length)
        {
            return length == 1 ? baseType : baseType.MakeArrayType();
        }

        private static readonly Dictionary<string, string> TypeNameMappings = new()
        {
            ["Int32"] = "int",
            ["Single"] = "float",
            ["Double"] = "double",
            ["Boolean"] = "bool",
            ["Byte"] = "byte",
            ["String"] = "string"
        };

        private static string GetFriendlyTypeName(Type type)
        {
            if (type.IsArray)
            {
                var elementType = type.GetElementType();
                return elementType != null ? $"{GetFriendlyTypeName(elementType)}[]" : "object[]";
            }

            return TypeNameMappings.TryGetValue(type.Name, out var friendlyName) ? friendlyName : type.Name;
        }

        private static string GetNullableFriendlyTypeName(Type type)
        {
            var baseName = GetFriendlyTypeName(type);
            
            // Value types (except arrays) need the ? suffix for nullable
            return type.IsValueType && !type.IsArray ? $"{baseName}?" : baseName;
        }

        // telemetry helpers
        private static void IncrementCounter(string key, long increment = 1) => _performanceCounters.AddOrUpdate(key, increment, (k, v) => v + increment);
        private static void RecordTime(string key, TimeSpan elapsed) => _performanceTimes.AddOrUpdate(key, elapsed, (k, v) => v + elapsed);

        // dump performance metrics (debugging/monitoring)
        internal static Dictionary<string, object> GetPerformanceMetrics()
        {
            var metrics = new Dictionary<string, object>();

            // add counter metrics
            foreach (var counter in _performanceCounters)
            {
                metrics[$"counter_{counter.Key}"] = counter.Value;
            }

            // add timing metrics
            foreach (var time in _performanceTimes)
            {
                metrics[$"time_{time.Key}_ms"] = time.Value.TotalMilliseconds;
            }

            // add system metrics
            metrics["cache_size"] = _typeCache.Count;
            metrics["data_loaded"] = _iRacingData.IsValueCreated;

            return metrics;
        }
        private static void LogMetricsToDebug()
        {
            var metrics = GetPerformanceMetrics();
            foreach (var metric in metrics)
            {
                System.Diagnostics.Debug.WriteLine($"CodeGen Metric: {metric.Key} = {metric.Value}");
            }
        }
    }
    internal static class TelemetryConstants
    {
        // counter metrics
        public const string COUNTER_TRANSFORMER_CALLS = "transformer_calls";
        public const string COUNTER_EXECUTE_CALLS = "execute_calls";
        public const string COUNTER_VARIABLES_PROCESSED = "variables_processed";
        public const string COUNTER_CACHE_HITS = "cache_hits";
        public const string COUNTER_CACHE_MISSES = "cache_misses";
        public const string COUNTER_UNKNOWN_VARIABLES = "unknown_variables";
        public const string COUNTER_DUPLICATE_VARIABLES_FOUND = "duplicate_variables_found";
        public const string COUNTER_SOURCES_GENERATED = "sources_generated";

        // time metrics
        public const string TIME_TRANSFORMER_DURATION = "transformer_duration";
        public const string TIME_EXECUTE_DURATION = "execute_duration";
    }

}

