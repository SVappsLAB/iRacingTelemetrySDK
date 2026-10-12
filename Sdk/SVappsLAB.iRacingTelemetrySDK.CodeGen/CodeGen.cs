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

using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace SVappsLAB.iRacingTelemetrySDK;

// the pipeline model. each type is value-equatable
internal record struct TelemetryProperty(string Name, string TypeName, string ReaderCall);

// a Location that compares by value. Location itself holds a SyntaxTree reference changes with each edit
internal record struct LocationInfo(string FilePath, TextSpan Span, LinePositionSpan LineSpan)
{
    public Location ToLocation() => Location.Create(FilePath, Span, LineSpan);

    public static LocationInfo? From(Location? location) =>
        location is null || location.SourceTree is null
            ? null
            : new LocationInfo(location.SourceTree.FilePath, location.SourceSpan, location.GetLineSpan().Span);
}

internal record struct DiagnosticInfo(DiagnosticDescriptor Descriptor, LocationInfo? Location, string? Argument)
{
    public Diagnostic ToDiagnostic() =>
        Diagnostic.Create(Descriptor, Location?.ToLocation(), Argument is null ? [] : [Argument]);
}

// properties and diagnostics are kept apart so that an edit which only moves the attribute
internal record struct TelemetryModel(EquatableArray<TelemetryProperty> Properties, EquatableArray<DiagnosticInfo> Diagnostics, LocationInfo? AttributeLocation);

// immutable array that compares by contents
internal readonly struct EquatableArray<T> : IEquatable<EquatableArray<T>>, IEnumerable<T> where T : IEquatable<T>
{
    readonly T[]? _items;

    public EquatableArray(T[] items) => _items = items;

    public int Length => _items?.Length ?? 0;

    public bool Equals(EquatableArray<T> other) => AsSpan().SequenceEqual(other.AsSpan());
    public override bool Equals(object? obj) => obj is EquatableArray<T> other && Equals(other);

    public override int GetHashCode()
    {
        var hash = 17;
        foreach (var item in AsSpan())
            hash = unchecked(hash * 31 + item.GetHashCode());
        return hash;
    }

    public ReadOnlySpan<T> AsSpan() => _items.AsSpan();

    public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)(_items ?? [])).GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

[Generator(LanguageNames.CSharp)]
public class CodeGenerator : IIncrementalGenerator
{
    const string AttributeMetadataName = "SVappsLAB.iRacingTelemetrySDK.RequiredTelemetryVarsAttribute";

    // tracking names, so tests can check which steps Roslyn reused
    public const string ModelStep = "TelemetryModel";
    public const string PropertiesStep = "TelemetryProperties";

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

    static readonly Dictionary<TelemetryVar, VarItem> _catalog = new iRacingVars().Vars;

    static readonly DiagnosticDescriptor DuplicateVarName = new(
        id: "DuplicateVarName",
        title: "Duplicate Variable Name",
        messageFormat: "Duplicate telemetry variable name found: \"{0}\"",
        category: "Usage",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    static readonly DiagnosticDescriptor EmptyVarList = new(
        id: "EmptyVarList",
        title: "Empty Variable List",
        messageFormat: "No telemetry variables requested. The generated 'TelemetryData' type will have no properties.",
        category: "Usage",
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true);

    static readonly DiagnosticDescriptor UnknownVarName = new(
        id: "UnknownVarName",
        title: "Unknown Variable Name",
        messageFormat: "Unknown telemetry variable name: \"{0}\".  Check spelling.",
        category: "Usage",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    static readonly DiagnosticDescriptor MultipleAttributes = new(
        id: "MultipleRequiredTelemetryVars",
        title: "Multiple RequiredTelemetryVars Attributes",
        messageFormat: "Only one class per project may use [RequiredTelemetryVars]; it defines the project's single 'TelemetryData' type",
        category: "Usage",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // add the marker attribute
        context.RegisterPostInitializationOutput(static ctx => ctx.AddSource(
            "MarkerAttribute.g.cs", SourceText.From(MarkerAttribute, Encoding.UTF8)));

        // one model per annotated class
        var models = context.SyntaxProvider.ForAttributeWithMetadataName(
                AttributeMetadataName,
                // a class or record class
                predicate: static (node, _) => node is ClassDeclarationSyntax or RecordDeclarationSyntax,
                transform: static (ctx, ct) => CreateModel(ctx, ct))
            .WithTrackingName(ModelStep)
            .Collect();

        // diagnostics, from every annotated class
        context.RegisterSourceOutput(models, static (spc, models) => ReportDiagnostics(spc, models));

        // the source, from the first annotated class only. only one TelemetryData type allowed
        var properties = models
            .Select(static (models, _) => models.IsEmpty ? (EquatableArray<TelemetryProperty>?)null : models[0].Properties)
            .WithTrackingName(PropertiesStep);

        context.RegisterSourceOutput(properties, static (spc, properties) =>
        {
            if (properties is { } props)
                spc.AddSource("iRacingTelemetrySDK.g.cs", GenerateSourceCode(props));
        });
    }

    static TelemetryModel CreateModel(GeneratorAttributeSyntaxContext context, CancellationToken ct)
    {
        var attribute = context.Attributes[0];
        var attributeSyntax = attribute.ApplicationSyntaxReference?.GetSyntax(ct) as AttributeSyntax;
        var attributeLocation = LocationInfo.From(attributeSyntax?.GetLocation());

        var properties = new List<TelemetryProperty>();
        var diagnostics = new List<DiagnosticInfo>();

        // a missing or unbindable argument is already a compiler error. generate an empty TelemetryData
        if (attribute.ConstructorArguments.Length != 1 || attribute.ConstructorArguments[0].Kind != TypedConstantKind.Array)
            return new TelemetryModel(new([]), new([]), attributeLocation);

        // [RequiredTelemetryVars(null)] is treated as an empty list
        var arg = attribute.ConstructorArguments[0];
        var values = arg.IsNull ? ImmutableArray<TypedConstant>.Empty : arg.Values;

        // an empty list is valid but often an accident, so surface an informational diagnostic
        if (values.IsEmpty)
            diagnostics.Add(new DiagnosticInfo(EmptyVarList, attributeLocation, null));

        var elements = GetElementSyntax(attributeSyntax);
        var seen = new HashSet<TelemetryVar>();
        var duplicatesReported = new HashSet<TelemetryVar>();

        for (int i = 0; i < values.Length; i++)
        {
            var location = i < elements.Count ? LocationInfo.From(elements[i].GetLocation()) : attributeLocation;

            // an undefined enum value (e.g. (TelemetryVar)999999), or one the catalog doesn't know
            if (values[i].Value is not int raw || !_catalog.TryGetValue((TelemetryVar)raw, out var item))
            {
                var name = values[i].Value is int r && Enum.IsDefined(typeof(TelemetryVar), r)
                    ? ((TelemetryVar)r).ToString()
                    : values[i].Value?.ToString() ?? "null";
                diagnostics.Add(new DiagnosticInfo(UnknownVarName, location, name));
                continue;
            }

            var variable = (TelemetryVar)raw;
            if (!seen.Add(variable))
            {
                if (duplicatesReported.Add(variable))
                    diagnostics.Add(new DiagnosticInfo(DuplicateVarName, location, variable.ToString()));
                continue;
            }

            properties.Add(CreateProperty(variable, item));
        }

        return new TelemetryModel(new(properties.ToArray()), new(diagnostics.ToArray()), attributeLocation);
    }

    // the syntax for each element of the attribute's array argument, used to place diagnostics
    static IReadOnlyList<SyntaxNode> GetElementSyntax(AttributeSyntax? attributeSyntax)
    {
        var expression = attributeSyntax?.ArgumentList?.Arguments.FirstOrDefault()?.Expression;
        return expression switch
        {
            // [TelemetryVar.A, TelemetryVar.B]
            CollectionExpressionSyntax collection => collection.Elements,
            // new[] { TelemetryVar.A, TelemetryVar.B }
            ImplicitArrayCreationExpressionSyntax implicitArray => implicitArray.Initializer.Expressions,
            // new TelemetryVar[] { TelemetryVar.A, TelemetryVar.B }
            ArrayCreationExpressionSyntax { Initializer: { } initializer } => initializer.Expressions,
            _ => []
        };
    }

    static TelemetryProperty CreateProperty(TelemetryVar variable, VarItem item)
    {
        // the wire type (irsdk_VarType) decides the storage; some int and bitfield
        // vars have semantic enum. bitfields without one fall back to int
        var enumType = item.Type switch
        {
            2 => GetEnumTypeForVariable(item.Name),
            3 => GetFlagTypeForVariable(item.Name),
            _ => null
        };
        var elementType = enumType ?? item.Type switch
        {
            0 => "byte",
            1 => "bool",
            2 or 3 => "int",
            4 => "float",
            5 => "double",
            _ => throw new NotSupportedException($"Unknown irsdk_VarType {item.Type} for {item.Name}")
        };

        if (item.Length > 1)
            return new TelemetryProperty(variable.ToString(), $"{elementType}[]", $"Array<{elementType}>");

        return new TelemetryProperty(variable.ToString(), elementType, enumType is null ? $"Value<{elementType}>" : $"Enum<{elementType}>");
    }

    static string? GetEnumTypeForVariable(string varName) => varName switch
    {
        "CarIdxTrackSurface" => nameof(TrackLocation),
        "CarIdxTrackSurfaceMaterial" => nameof(TrackSurface),
        "CarLeftRight" => nameof(CarLeftRight),
        "PaceMode" => nameof(PaceMode),
        "PlayerCarPitSvStatus" => nameof(PitServiceStatus),
        "PlayerIncidents" => nameof(IncidentFlags),
        "PlayerTrackSurface" => nameof(TrackLocation),
        "PlayerTrackSurfaceMaterial" => nameof(TrackSurface),
        "SessionState" => nameof(SessionState),
        "TrackWetness" => nameof(TrackWetness),
        _ => null
    };

    static string? GetFlagTypeForVariable(string varName) => varName switch
    {
        "CamCameraState" => nameof(CameraState),
        "CarIdxPaceFlags" => nameof(PaceFlags),
        "CarIdxSessionFlags" => nameof(SessionFlags),
        "EngineWarnings" => nameof(EngineWarnings),
        "PitSvFlags" => nameof(PitServiceFlags),
        "SessionFlags" => nameof(SessionFlags),
        _ => null
    };

    static void ReportDiagnostics(SourceProductionContext spc, ImmutableArray<TelemetryModel> models)
    {
        for (int i = 0; i < models.Length; i++)
        {
            if (i > 0)
            {
                // the extra class's own diagnostics would be noise, its list is never generated
                spc.ReportDiagnostic(new DiagnosticInfo(MultipleAttributes, models[i].AttributeLocation, null).ToDiagnostic());
                continue;
            }

            foreach (var diagnostic in models[i].Diagnostics)
                spc.ReportDiagnostic(diagnostic.ToDiagnostic());
        }
    }

    static string GenerateSourceCode(EquatableArray<TelemetryProperty> properties)
    {
        // nullable properties: a var missing from the current session/ibt reads as null
        var varList = string.Join("\n",
            properties.Select(p => $"        public {p.TypeName}? {p.Name} {{ get; init; }}"));

        // property initializers for IGeneratedTelemetryData<T>.Read. populates
        // TelemetryData without reflection, so it works with AOT
        var readList = string.Join("\n",
            properties.Select(p => $"                {p.Name} = reader.{p.ReaderCall}(\"{p.Name}\"),"));

        return $$"""
            #nullable enable
            using System;

            namespace SVappsLAB.iRacingTelemetrySDK
            {
                /// <summary>
                /// Represents iRacing telemetry data with strongly-typed properties.
                /// </summary>
                public record struct TelemetryData : IGeneratedTelemetryData<TelemetryData>
                {
            {{varList}}

                    TelemetryData IGeneratedTelemetryData<TelemetryData>.Read(TelemetryValueReader reader) => new TelemetryData
                    {
            {{readList}}
                    };
                }
            }
            """;
    }
}
