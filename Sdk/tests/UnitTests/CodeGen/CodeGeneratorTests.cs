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
    static (Compilation output, ImmutableArray<Diagnostic> generatorDiagnostics) RunGenerator(string source)
    {
        // reference the runtime assemblies of this test process plus the
        // EnumsAndFlags assembly (for the TelemetryVar enum)
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))
            .Append(MetadataReference.CreateFromFile(typeof(TelemetryVar).Assembly.Location))
            .ToArray();

        var compilation = CSharpCompilation.Create(
            "GeneratorTestAssembly",
            [CSharpSyntaxTree.ParseText(source)],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var driver = CSharpGeneratorDriver.Create(new CodeGenerator());
        driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var diagnostics);

        return (outputCompilation, diagnostics);
    }

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
}
