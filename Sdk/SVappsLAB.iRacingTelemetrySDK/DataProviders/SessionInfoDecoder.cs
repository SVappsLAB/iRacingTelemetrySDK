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
using System.Text;

namespace SVappsLAB.iRacingTelemetrySDK.DataProviders;

// Decodes the raw session-info bytes from the memory-mapped file / IBT file
// into a string, depending on the WeekendInfo:Encoding: tag (UTF8).
internal static class SessionInfoDecoder
{
    private static readonly byte[] EncodingKey = Encoding.ASCII.GetBytes("Encoding:");
    private static readonly byte[] Utf8Value = Encoding.ASCII.GetBytes("UTF8");

    /// <summary>
    /// Decode using the encoding declared by the WeekendInfo:Encoding: tag
    /// Older session info without "UTF8" use ISO-8859-1
    /// </summary>
    public static string Decode(ReadOnlySpan<byte> raw)
    {
        // trim at the first null terminator
        var nullIdx = raw.IndexOf((byte)0);
        if (nullIdx >= 0)
            raw = raw.Slice(0, nullIdx);

        if (IsUtf8(raw))
            return Encoding.UTF8.GetString(raw);

        return Encoding.Latin1.GetString(raw);
    }

    public static bool IsUtf8(ReadOnlySpan<byte> raw)
    {
        var keyIdx = raw.IndexOf(EncodingKey);
        if (keyIdx < 0)
            return false;

        // the value is on the same line, immediately after the key
        var value = raw.Slice(keyIdx + EncodingKey.Length);
        var eol = value.IndexOf((byte)'\n');
        if (eol >= 0)
            value = value.Slice(0, eol);

        return value.IndexOf(Utf8Value) >= 0;
    }
}
