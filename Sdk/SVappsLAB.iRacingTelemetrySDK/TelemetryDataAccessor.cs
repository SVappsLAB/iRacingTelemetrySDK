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
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection;
using Microsoft.Extensions.Logging;
using SVappsLAB.iRacingTelemetrySDK.DataProviders;

namespace SVappsLAB.iRacingTelemetrySDK;

/// <summary>
/// Builds telemetry data samples of type <typeparamref name="T"/> from the data provider.
/// </summary>
/// <typeparam name="T">The telemetry data struct type</typeparam>
internal sealed class TelemetryDataAccessor<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] T> where T : struct
{
    private readonly ILogger _logger;
    private TelemetryValueReader? _reader;
    private readonly IGeneratedTelemetryData<T>? _generated;
    private readonly PropertyAccessor[] _propertyAccessors;

    public TelemetryDataAccessor(ILogger logger)
    {
        _logger = logger;

        if (default(T) is IGeneratedTelemetryData<T> generated)
        {
            _generated = generated;
            _propertyAccessors = [];
        }
        else
        {
            _propertyAccessors = GetPropertyAccessors();
        }
    }

    /// <summary>
    /// Creates a new telemetry data sample from the provider's current values
    /// </summary>
    public T CreateTelemetryDataSample(IDataProvider dataProvider)
    {
        if (_generated != null)
        {
            // the provider is fixed for a client's lifetime, so the reader is too
            if (_reader?.DataProvider != dataProvider)
            {
                _reader = new TelemetryValueReader(dataProvider, _logger);
            }
            return _generated.Read(_reader);
        }

        // reflection fallback. properties are set on one boxed copy, then unboxed
        object telemetryData = default(T);

        foreach (var accessor in _propertyAccessors)
        {
            var rawValue = dataProvider.GetVarValue(accessor.PropertyName);
            if (rawValue == null) continue;

            try
            {
                accessor.SetValue(telemetryData, rawValue);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to set property {PropertyName} with value '{RawValue}': {Error}",
                    accessor.PropertyName, rawValue, ex.Message);
            }
        }

        return (T)telemetryData;
    }

    private static PropertyAccessor[] GetPropertyAccessors()
    {
        var properties = typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance);
        var accessors = new PropertyAccessor[properties.Length];

        for (int i = 0; i < properties.Length; i++)
        {
            accessors[i] = new PropertyAccessor(properties[i]);
        }

        return accessors;
    }

    private sealed class PropertyAccessor
    {
        private readonly PropertyInfo _property;
        private readonly Type _targetType;
        private readonly Type _underlyingType;

        public string PropertyName => _property.Name;

        public PropertyAccessor(PropertyInfo property)
        {
            _property = property;
            _targetType = property.PropertyType;
            _underlyingType = Nullable.GetUnderlyingType(_targetType) ?? _targetType;
        }

        public void SetValue(object boxedInstance, object rawValue) =>
            _property.SetValue(boxedInstance, ConvertValue(rawValue));

        private object ConvertValue(object value)
        {
            // enums (and flags) arrive as their int value
            if (_underlyingType.IsEnum)
                return Enum.ToObject(_underlyingType, (int)value);

            // arrays and object properties take the provider's value as is
            if (_targetType.IsArray || _targetType == typeof(object))
                return value;

            // nullable types box as their underlying value, so converting to that is enough
            return Convert.ChangeType(value, _underlyingType, CultureInfo.InvariantCulture);
        }
    }
}
