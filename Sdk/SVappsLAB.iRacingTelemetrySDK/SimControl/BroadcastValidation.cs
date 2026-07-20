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

namespace SVappsLAB.iRacingTelemetrySDK.SimControl;

internal static class BroadcastValidation
{
    public static void Signed16(int value, string paramName)
    {
        if (value < short.MinValue || value > short.MaxValue)
            throw new ArgumentOutOfRangeException(paramName, value, "Broadcast values packed into a word must fit in a signed 16-bit integer.");
    }

    public static void NonNegative(int value, string paramName)
        => ArgumentOutOfRangeException.ThrowIfNegative(value, paramName);

    public static void Defined<TEnum>(TEnum value, string paramName) where TEnum : struct, Enum
    {
        if (!Enum.IsDefined(value))
            throw new ArgumentOutOfRangeException(paramName, value, $"Unknown {typeof(TEnum).Name} value.");
    }

    public static void FixedPoint16_16(float value, string paramName)
    {
        // official C lang SDK multiplies by 65536 and casts to a signed int
        if (!float.IsFinite(value) || value < -32768f || value >= 32768f)
            throw new ArgumentOutOfRangeException(paramName, value, "Value must be finite and fit in signed 16.16 fixed-point format.");
    }
}
