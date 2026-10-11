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

using SVappsLAB.iRacingTelemetrySDK;

namespace UnitTests.CodeGen;

// the generator runs inside the consumer's compiler, which must be at least the Roslyn
// version the generator was built against. an older compiler skips the generator with
// CS9057 and TelemetryData is never generated. 4.8 is the compiler in the .NET 8.0.100 SDK
public class GeneratorRoslynVersionTests
{
    static readonly Version MaxRoslynVersion = new(4, 8, 0, 0);

    [Theory]
    [InlineData("Microsoft.CodeAnalysis")]
    [InlineData("Microsoft.CodeAnalysis.CSharp")]
    public void Generator_ReferencesRoslynNoNewerThanNet8Sdk(string assemblyName)
    {
        var reference = Assert.Single(typeof(CodeGenerator).Assembly.GetReferencedAssemblies(), a => a.Name == assemblyName);

        Assert.True(reference.Version <= MaxRoslynVersion,
            $"the generator references {assemblyName} {reference.Version}, newer than {MaxRoslynVersion}. " +
            "consumers on an older .NET SDK would fail with CS9057 - keep the CodeGen project's " +
            "Microsoft.CodeAnalysis.CSharp reference at the oldest version that has the APIs it needs");
    }
}
