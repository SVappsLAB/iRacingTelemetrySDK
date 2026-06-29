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

using System.Collections.Generic;
using System.Text;
using SVappsLAB.iRacingTelemetrySDK.DataProviders;

namespace UnitTests.DataProviders
{
    public class SessionInfoDecoderTests
    {
        // "José Muñoz" differs between UTF-8 and Latin1; "René" round-trips through Latin1
        const string MultiByteName = "José Muñoz";
        const string Latin1Name = "René";

        static byte[] BuildSessionInfo(string? encodingTagValue, string driverName, Encoding payloadEncoding)
        {
            var sb = new StringBuilder();
            sb.Append("---\n");
            sb.Append("WeekendInfo:\n");
            if (encodingTagValue != null)
                sb.Append($" Encoding: {encodingTagValue}\n");
            sb.Append("DriverInfo:\n");
            sb.Append(" Drivers:\n");
            sb.Append($" - UserName: {driverName}\n");
            sb.Append("...\n");

            return payloadEncoding.GetBytes(sb.ToString());
        }

        [Fact]
        public void Utf8Tag_DecodesMultibyteCorrectly()
        {
            var bytes = BuildSessionInfo("UTF8", MultiByteName, Encoding.UTF8);

            Assert.True(SessionInfoDecoder.IsUtf8(bytes));

            var result = SessionInfoDecoder.Decode(bytes);
            Assert.Contains($"UserName: {MultiByteName}", result);
        }

        [Fact]
        public void NoTag_DefaultsToIso8859()
        {
            var bytes = BuildSessionInfo(encodingTagValue: null, Latin1Name, Encoding.Latin1);

            Assert.False(SessionInfoDecoder.IsUtf8(bytes));

            var result = SessionInfoDecoder.Decode(bytes);
            Assert.Contains($"UserName: {Latin1Name}", result);
        }
    }
}
