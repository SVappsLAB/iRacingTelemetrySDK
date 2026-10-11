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

using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SVappsLAB.iRacingTelemetrySDK;

namespace UnitTests.CodeGen;

// runs the source generator in-process against small consumer programs and
// asserts on the generated TelemetryData type and reported diagnostics
public class CodeGeneratorTests
{
    // reference the runtime assemblies of this test process plus the
    // EnumsAndFlags assembly (for the TelemetryVar enum)
    static readonly MetadataReference[] References = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
        .Split(Path.PathSeparator)
        .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))
        .Append(MetadataReference.CreateFromFile(typeof(TelemetryVar).Assembly.Location))
        .ToArray();

    static CSharpCompilation CreateCompilation(params string[] sources) => CSharpCompilation.Create(
        "GeneratorTestAssembly",
        sources.Select(s => CSharpSyntaxTree.ParseText(s)),
        References,
        new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

    static (Compilation output, ImmutableArray<Diagnostic> generatorDiagnostics) RunGenerator(params string[] sources)
    {
        var driver = CSharpGeneratorDriver.Create(new CodeGenerator());
        driver.RunGeneratorsAndUpdateCompilation(CreateCompilation(sources), out var outputCompilation, out var diagnostics, TestContext.Current.CancellationToken);

        return (outputCompilation, diagnostics);
    }

    static void AssertCompilesWithoutErrors(Compilation output) =>
        Assert.Empty(output.GetDiagnostics(TestContext.Current.CancellationToken).Where(d => d.Severity == DiagnosticSeverity.Error));

    [Fact]
    public void EmptyVariableList_GeneratesEmptyTelemetryData()
    {
        var (output, diagnostics) = RunGenerator("""
            using SVappsLAB.iRacingTelemetrySDK;

            [RequiredTelemetryVars([])]
            public class Program { }
            """);

        var telemetryData = output.GetTypeByMetadataName("SVappsLAB.iRacingTelemetrySDK.TelemetryData");
        Assert.NotNull(telemetryData);
        Assert.Empty(telemetryData.GetMembers().OfType<IPropertySymbol>());

        // the consumer program itself must compile cleanly
        Assert.Empty(output.GetDiagnostics(TestContext.Current.CancellationToken).Where(d => d.Severity == DiagnosticSeverity.Error));

        // an informational diagnostic flags the (possibly accidental) empty list
        var info = Assert.Single(diagnostics, d => d.Id == "EmptyVarList");
        Assert.Equal(DiagnosticSeverity.Info, info.Severity);
    }

    [Fact]
    public void PopulatedVariableList_GeneratesMatchingProperties()
    {
        var (output, diagnostics) = RunGenerator("""
            using SVappsLAB.iRacingTelemetrySDK;

            [RequiredTelemetryVars([TelemetryVar.Speed, TelemetryVar.RPM])]
            public class Program { }
            """);

        var telemetryData = output.GetTypeByMetadataName("SVappsLAB.iRacingTelemetrySDK.TelemetryData");
        Assert.NotNull(telemetryData);

        var properties = telemetryData.GetMembers().OfType<IPropertySymbol>().Select(p => p.Name).ToList();
        Assert.Contains("Speed", properties);
        Assert.Contains("RPM", properties);

        Assert.Empty(output.GetDiagnostics(TestContext.Current.CancellationToken).Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.DoesNotContain(diagnostics, d => d.Id == "EmptyVarList");
    }

    // irsdk_int vars (wire type 2) map through the enum table, irsdk_bitField
    // vars (wire type 3) through the flag table, mirroring irsdk_defines.h
    [Theory]
    [InlineData(nameof(TelemetryVar.PlayerIncidents), typeof(IncidentFlags))]
    [InlineData(nameof(TelemetryVar.SessionState), typeof(SessionState))]
    [InlineData(nameof(TelemetryVar.EngineWarnings), typeof(EngineWarnings))]
    [InlineData(nameof(TelemetryVar.Gear), typeof(int))]
    public void Variable_GeneratesExpectedPropertyType(string varName, Type expectedType)
    {
        var (output, _) = RunGenerator($$"""
            using SVappsLAB.iRacingTelemetrySDK;

            [RequiredTelemetryVars([TelemetryVar.{{varName}}])]
            public class Program { }
            """);

        var telemetryData = output.GetTypeByMetadataName("SVappsLAB.iRacingTelemetrySDK.TelemetryData");
        Assert.NotNull(telemetryData);

        var property = Assert.Single(telemetryData.GetMembers(varName).OfType<IPropertySymbol>());
        var propertyType = property.Type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable
            ? nullable.TypeArguments[0]
            : property.Type;
        var expected = output.GetTypeByMetadataName(expectedType.FullName!);

        Assert.Equal(expected, propertyType, SymbolEqualityComparer.Default);
    }

    // per-car (length 64) vars generate arrays of the same element type
    [Theory]
    [InlineData(nameof(TelemetryVar.CarIdxLapDistPct), typeof(float))]
    [InlineData(nameof(TelemetryVar.CarIdxTrackSurface), typeof(TrackLocation))]
    [InlineData(nameof(TelemetryVar.CarIdxSessionFlags), typeof(SessionFlags))]
    public void ArrayVariable_GeneratesExpectedElementType(string varName, Type expectedElementType)
    {
        var (output, _) = RunGenerator($$"""
            using SVappsLAB.iRacingTelemetrySDK;

            [RequiredTelemetryVars([TelemetryVar.{{varName}}])]
            public class Program { }
            """);

        var telemetryData = output.GetTypeByMetadataName("SVappsLAB.iRacingTelemetrySDK.TelemetryData");
        Assert.NotNull(telemetryData);

        var property = Assert.Single(telemetryData.GetMembers(varName).OfType<IPropertySymbol>());
        var arrayType = Assert.IsAssignableFrom<IArrayTypeSymbol>(property.Type);
        var expected = output.GetTypeByMetadataName(expectedElementType.FullName!);

        Assert.Equal(expected, arrayType.ElementType, SymbolEqualityComparer.Default);
        AssertCompilesWithoutErrors(output);
    }

    [Fact]
    public void RecordClass_GeneratesTelemetryData()
    {
        var (output, _) = RunGenerator("""
            using SVappsLAB.iRacingTelemetrySDK;

            [RequiredTelemetryVars([TelemetryVar.Speed])]
            public record Program;
            """);

        var telemetryData = output.GetTypeByMetadataName("SVappsLAB.iRacingTelemetrySDK.TelemetryData");
        Assert.NotNull(telemetryData);
        Assert.Single(telemetryData.GetMembers("Speed").OfType<IPropertySymbol>());
        AssertCompilesWithoutErrors(output);
    }

    [Fact]
    public void MultipleAnnotatedClasses_GeneratesFromFirstAndReportsTheRest()
    {
        var first = """
            using SVappsLAB.iRacingTelemetrySDK;

            [RequiredTelemetryVars([TelemetryVar.Speed])]
            public class First { }
            """;
        var second = """
            using SVappsLAB.iRacingTelemetrySDK;

            [RequiredTelemetryVars([TelemetryVar.RPM])]
            public class Second { }
            """;

        var driver = CSharpGeneratorDriver.Create(new CodeGenerator())
            .RunGeneratorsAndUpdateCompilation(CreateCompilation(first, second), out var output, out var diagnostics, TestContext.Current.CancellationToken);

        // no generator exception (previously CS8785, a duplicate hint name)
        Assert.Null(driver.GetRunResult().Results[0].Exception);

        var error = Assert.Single(diagnostics, d => d.Id == "MultipleRequiredTelemetryVars");
        Assert.Equal(DiagnosticSeverity.Error, error.Severity);
        Assert.Equal(second.IndexOf("RequiredTelemetryVars(", StringComparison.Ordinal), error.Location.SourceSpan.Start);

        // TelemetryData comes from the first class only, so nothing else fails to compile
        var telemetryData = output.GetTypeByMetadataName("SVappsLAB.iRacingTelemetrySDK.TelemetryData");
        Assert.NotNull(telemetryData);
        var property = Assert.Single(telemetryData.GetMembers().OfType<IPropertySymbol>());
        Assert.Equal("Speed", property.Name);
        AssertCompilesWithoutErrors(output);
    }

    [Fact]
    public void NullVariableList_GeneratesEmptyTelemetryData()
    {
        var driver = CSharpGeneratorDriver.Create(new CodeGenerator())
            .RunGeneratorsAndUpdateCompilation(CreateCompilation("""
                using SVappsLAB.iRacingTelemetrySDK;

                [RequiredTelemetryVars(null)]
                public class Program { }
                """), out var output, out var diagnostics, TestContext.Current.CancellationToken);

        Assert.Null(driver.GetRunResult().Results[0].Exception);
        Assert.Single(diagnostics, d => d.Id == "EmptyVarList");

        var telemetryData = output.GetTypeByMetadataName("SVappsLAB.iRacingTelemetrySDK.TelemetryData");
        Assert.NotNull(telemetryData);
        Assert.Empty(telemetryData.GetMembers().OfType<IPropertySymbol>());
        AssertCompilesWithoutErrors(output);
    }

    [Fact]
    public void UndefinedEnumValue_ReportsUnknownVarNameAtTheElement()
    {
        var source = """
            using SVappsLAB.iRacingTelemetrySDK;

            [RequiredTelemetryVars([(TelemetryVar)999999, TelemetryVar.Speed])]
            public class Program { }
            """;
        var (output, diagnostics) = RunGenerator(source);

        var error = Assert.Single(diagnostics, d => d.Id == "UnknownVarName");
        Assert.Contains("999999", error.GetMessage());
        Assert.Equal(source.IndexOf("(TelemetryVar)999999", StringComparison.Ordinal), error.Location.SourceSpan.Start);

        // the valid var is still generated
        var telemetryData = output.GetTypeByMetadataName("SVappsLAB.iRacingTelemetrySDK.TelemetryData");
        Assert.NotNull(telemetryData);
        Assert.Single(telemetryData.GetMembers("Speed").OfType<IPropertySymbol>());
    }

    [Fact]
    public void DuplicateVariable_ReportsOnceAndGeneratesOneProperty()
    {
        var source = """
            using SVappsLAB.iRacingTelemetrySDK;

            [RequiredTelemetryVars([TelemetryVar.Speed, TelemetryVar.RPM, TelemetryVar.Speed, TelemetryVar.Speed])]
            public class Program { }
            """;
        var (output, diagnostics) = RunGenerator(source);

        var error = Assert.Single(diagnostics, d => d.Id == "DuplicateVarName");
        Assert.Equal(source.LastIndexOf("TelemetryVar.Speed, TelemetryVar.Speed", StringComparison.Ordinal), error.Location.SourceSpan.Start);

        var telemetryData = output.GetTypeByMetadataName("SVappsLAB.iRacingTelemetrySDK.TelemetryData");
        Assert.NotNull(telemetryData);
        Assert.Single(telemetryData.GetMembers("Speed").OfType<IPropertySymbol>());
        Assert.Single(telemetryData.GetMembers("RPM").OfType<IPropertySymbol>());
    }

    [Fact]
    public void AliasedAttribute_ReportsDiagnosticsAtTheElement()
    {
        var source = """
            using SVappsLAB.iRacingTelemetrySDK;
            using Vars = SVappsLAB.iRacingTelemetrySDK.RequiredTelemetryVarsAttribute;

            [Vars([(TelemetryVar)999999])]
            public class Program { }
            """;
        var (_, diagnostics) = RunGenerator(source);

        var error = Assert.Single(diagnostics, d => d.Id == "UnknownVarName");
        Assert.Equal(source.IndexOf("(TelemetryVar)999999", StringComparison.Ordinal), error.Location.SourceSpan.Start);
    }

    // incremental generation: edits that don't change the requested vars must not re-emit the source

    const string IncrementalSource = """
        using SVappsLAB.iRacingTelemetrySDK;

        [RequiredTelemetryVars([TelemetryVar.Speed, TelemetryVar.RPM])]
        public class Program
        {
            public int Method() => 1;
        }
        """;

    static GeneratorRunResult RunTwice(string source, Func<string, string> edit, string? otherFile = null, Func<string, string>? otherEdit = null)
    {
        string[] Sources(string s, string? o) => o is null ? [s] : [s, o];

        var compilation = CreateCompilation(Sources(source, otherFile));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new CodeGenerator().AsSourceGenerator()],
            driverOptions: new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None, trackIncrementalGeneratorSteps: true));
        driver = driver.RunGenerators(compilation, TestContext.Current.CancellationToken);

        // replace the trees in place so unchanged trees keep their identity, like an IDE edit
        var edited = compilation.ReplaceSyntaxTree(compilation.SyntaxTrees.First(),
            CSharpSyntaxTree.ParseText(edit(source), cancellationToken: TestContext.Current.CancellationToken));
        if (otherFile is not null && otherEdit is not null)
            edited = edited.ReplaceSyntaxTree(edited.SyntaxTrees.Last(),
                CSharpSyntaxTree.ParseText(otherEdit(otherFile), cancellationToken: TestContext.Current.CancellationToken));

        return driver.RunGenerators(edited, TestContext.Current.CancellationToken).GetRunResult().Results[0];
    }

    static void AssertSourceNotRegenerated(GeneratorRunResult result)
    {
        var properties = Assert.Single(result.TrackedSteps[CodeGenerator.PropertiesStep]);
        Assert.All(properties.Outputs, o => Assert.Contains(o.Reason, new[] { IncrementalStepRunReason.Cached, IncrementalStepRunReason.Unchanged }));

        Assert.All(result.TrackedOutputSteps.SelectMany(s => s.Value).SelectMany(s => s.Outputs),
            o => Assert.Equal(IncrementalStepRunReason.Cached, o.Reason));
    }

    [Fact]
    public void Incremental_EditInAnnotatedClassBody_ReusesModel()
    {
        var result = RunTwice(IncrementalSource, s => s.Replace("=> 1;", "=> 2;"));

        // the transform reran (its file changed) but produced an equal model
        var model = Assert.Single(result.TrackedSteps[CodeGenerator.ModelStep]);
        Assert.All(model.Outputs, o => Assert.Equal(IncrementalStepRunReason.Unchanged, o.Reason));
        AssertSourceNotRegenerated(result);
    }

    [Fact]
    public void Incremental_EditInOtherFile_ReusesEverything()
    {
        var result = RunTwice(IncrementalSource, s => s,
            otherFile: "public class Other { public int M() => 1; }",
            otherEdit: s => s.Replace("=> 1;", "=> 2;"));

        AssertSourceNotRegenerated(result);
    }

    [Fact]
    public void Incremental_AttributeMovedOnly_DoesNotRegenerateSource()
    {
        // shifting the attribute changes diagnostic locations but not the generated source
        var result = RunTwice(IncrementalSource, s => "\n\n" + s);

        var properties = Assert.Single(result.TrackedSteps[CodeGenerator.PropertiesStep]);
        Assert.All(properties.Outputs, o => Assert.Equal(IncrementalStepRunReason.Unchanged, o.Reason));
    }

    [Fact]
    public void Incremental_VariableAdded_RegeneratesSource()
    {
        var result = RunTwice(IncrementalSource, s => s.Replace("TelemetryVar.RPM", "TelemetryVar.RPM, TelemetryVar.Gear"));

        var properties = Assert.Single(result.TrackedSteps[CodeGenerator.PropertiesStep]);
        Assert.All(properties.Outputs, o => Assert.Equal(IncrementalStepRunReason.Modified, o.Reason));
    }
}
