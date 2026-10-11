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

using Microsoft.Extensions.Logging;
using SVappsLAB.iRacingTelemetrySDK.DataProviders;
using System.ComponentModel;
using System.Globalization;

namespace SVappsLAB.iRacingTelemetrySDK;

[EditorBrowsable(EditorBrowsableState.Never)]
public interface IGeneratedTelemetryData<T> where T : struct
{
    /// <summary>Builds a sample from the current telemetry values.</summary>
    T Read(TelemetryValueReader reader);
}

[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class TelemetryValueReader
{
    readonly IDataProvider _dataProvider;
    readonly ILogger _logger;

    internal TelemetryValueReader(IDataProvider dataProvider, ILogger logger)
    {
        _dataProvider = dataProvider;
        _logger = logger;
    }

    internal IDataProvider DataProvider => _dataProvider;

    public TValue? Value<TValue>(string name) where TValue : struct, IConvertible
    {
        var raw = _dataProvider.GetVarValue(name);
        if (raw is null) return null;
        if (raw is TValue value) return value;

        try
        {
            return (TValue)Convert.ChangeType(raw, typeof(TValue), CultureInfo.InvariantCulture);
        }
        catch (Exception ex)
        {
            LogConversionFailure(name, raw, ex);
            return null;
        }
    }

    public TEnum? Enum<TEnum>(string name) where TEnum : struct, Enum
    {
        var raw = _dataProvider.GetVarValue(name);
        if (raw is null) return null;

        try
        {
            return (TEnum)System.Enum.ToObject(typeof(TEnum), (int)raw);
        }
        catch (Exception ex)
        {
            LogConversionFailure(name, raw, ex);
            return null;
        }
    }

    public TElement[]? Array<TElement>(string name)
    {
        var raw = _dataProvider.GetVarValue(name);
        if (raw is null) return null;

        try
        {
            // an int[] casts to an array of an int-backed enum without copying
            return (TElement[])raw;
        }
        catch (Exception ex)
        {
            LogConversionFailure(name, raw, ex);
            return null;
        }
    }

    void LogConversionFailure(string name, object raw, Exception ex) =>
        _logger.LogWarning("Failed to set property {PropertyName} with value '{RawValue}': {Error}",
            name, raw, ex.Message);
}
