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
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace SVappsLAB.iRacingTelemetrySDK
{
    public record struct VarsToGenerate(VarType[] vars);
    public record struct VarType(string name, Type type);

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
                    private readonly string[]? _vars;

                    public RequiredTelemetryVarsAttribute(string[]? vars)
                    {
                        _vars = vars;
                    }
                } 
            }";

        static iRacingVars _iRacingData = new iRacingVars();

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
            // get our attribute
            var attr = context.Attributes[0];

            // get variable names from attribute
            var constArgs = attr.ConstructorArguments.SelectMany(x => x.Values);
            var varNames = constArgs.Select(x => x.Value!.ToString()).ToArray<string>();

            var varList = new VarType[varNames.Length];
            for (int i = 0; i < varNames.Length; i++)
            {
                VarType vt;
                var rawVariableName = varNames[i];
                if (_iRacingData.Vars.TryGetValue(rawVariableName, out var varItem))
                {
                    var type = varItem.Type switch
                    {
                        0 => varItem.Length == 1 ? typeof(byte) : typeof(byte[]),
                        1 => varItem.Length == 1 ? typeof(bool) : typeof(bool[]),
                        2 => varItem.Length == 1 ? typeof(int) : typeof(int[]),
                        3 => varItem.Length == 1 ? typeof(int) : typeof(int[]),
                        4 => varItem.Length == 1 ? typeof(float) : typeof(float[]),
                        5 => varItem.Length == 1 ? typeof(double) : typeof(double[]),
                        _ => throw new NotImplementedException(),
                    };
                    vt = new VarType(varItem.Name, type);
                }
                else
                {
                    // unknown variable name
                    vt = new VarType(rawVariableName, typeof(Exception));
                }

                varList[i] = vt;
            }

            return new VarsToGenerate(varList);
        }

        private static void Execute(SourceProductionContext spc, VarsToGenerate? vars)
        {
            if (vars is null)
                return;

            var values = vars.Value.vars;

            var sb = new StringBuilder();
            for (int i = 0; i < values.Length; i++)
            {
                var item = values[i];

                // for unknown variable names, ceatea a diagnostic error for user
                if (item.type == typeof(Exception))
                {
                    var diagnostic = Diagnostic.Create(
                               new DiagnosticDescriptor(
                                   id: "InvalidVarName",
                                   title: "InvalidVarName",
                                   messageFormat: "Invalid telemetry variable name:  '{0}'",
                                   category: "Usage",
                                   defaultSeverity: DiagnosticSeverity.Error,
                                   isEnabledByDefault: true),
                               Location.None,
                               item.name);

                    spc.ReportDiagnostic(diagnostic);
                }

                if (i > 0)
                    sb.Append(",");

                var varDeclaration = $"{item.type.Name} {item.name}";
                sb.Append(varDeclaration);
            }
            var varList = sb.ToString();

            // create source code block
            var code = $$"""
                        using System;

                        namespace SVappsLAB.iRacingTelemetrySDK
                        {
                            public record struct TelemetryData({{varList}});
                        }
                    """;

            // add source to compilation
            spc.AddSource("iRacingTelemetrySDK.g.cs", code);
        }
    }
}

