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

using Microsoft.Extensions.Logging.Abstractions;
using SVappsLAB.iRacingTelemetrySDK;
using SVappsLAB.iRacingTelemetrySDK.DataProviders;
using SVappsLAB.iRacingTelemetrySDK.YamlParsing;
using YamlDotNet.Serialization;

namespace UnitTests.YamlParsing;

public class YamlStaticContextTests
{
    static readonly IDeserializer ReflectionDeserializer = new DeserializerBuilder().Build();
    static readonly IDeserializer StaticDeserializer = new StaticDeserializerBuilder(new SessionInfoYamlContext()).Build();

    // reflection serializer, used to turn both object graphs into comparable text
    static readonly ISerializer Serializer = new SerializerBuilder().Build();

    [Fact]
    public void StaticDeserializer_ValidYaml_MatchesReflection() =>
        AssertSameModel(File.ReadAllText("data/valid.yaml"));

    [Fact]
    public void StaticDeserializer_QuotedValuesYaml_MatchesReflection() =>
        AssertSameModel(new QuoteValuesYamlPreparationStrategy().Prepare(File.ReadAllText("data/invalid-unescapedChars.yaml")));

    [Fact]
    public async Task StaticDeserializer_IbtSessionInfo_MatchesReflection()
    {
        await using var provider = new IBTDataProvider(NullLogger.Instance, new IBTOptions("data/ibt/metrics-playback.ibt"));
        provider.OpenDataSource();

        AssertSameModel(provider.GetSessionInfoYaml());
    }

#if DEBUG
    // flag unmatched properties
    [Theory]
    [InlineData("WeekendInfo:\n  TrackName: spa up\n  BrandNewWeekendProperty: 1\n")]
    [InlineData("BrandNewSection:\n  Value: 1\n")]
    [InlineData("DriverInfo:\n  Drivers:\n  - CarIdx: 0\n    BrandNewDriverProperty: x\n")]
    public void Parse_UnknownProperty_ThrowsInDebug(string yaml)
    {
        var parser = new YamlParser(NullLogger.Instance);

        var ex = Assert.ThrowsAny<YamlDotNet.Core.YamlException>(() => parser.Parse<TelemetrySessionInfo>(yaml));
        Assert.Contains("BrandNew", ex.Message);
    }
#endif

    static void AssertSameModel(string yaml)
    {
        var expected = ReflectionDeserializer.Deserialize<TelemetrySessionInfo>(yaml);
        var actual = StaticDeserializer.Deserialize<TelemetrySessionInfo>(yaml);

        Assert.NotNull(actual.WeekendInfo);
        Assert.NotNull(actual.DriverInfo);
        Assert.Equal(Serializer.Serialize(expected), Serializer.Serialize(actual));
    }
}
