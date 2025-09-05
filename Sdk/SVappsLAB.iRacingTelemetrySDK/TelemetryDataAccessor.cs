/**
 * Copyright (C) 2024-2025 Scott Velez
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
using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using SVappsLAB.iRacingTelemetrySDK.DataProviders;

namespace SVappsLAB.iRacingTelemetrySDK
{
    /// <summary>
    /// High-performance accessor for telemetry data that eliminates boxing/unboxing overhead
    /// through ref-based operations and compiled expression trees.
    /// </summary>
    /// <typeparam name="T">The telemetry data struct type</typeparam>
    internal sealed class TelemetryDataAccessor<T> where T : struct
    {
        private readonly ILogger _logger;
        private readonly PropertyAccessor[] _propertyAccessors;

        public TelemetryDataAccessor(ILogger logger)
        {
            _logger = logger;
            _propertyAccessors = CompilePropertyAccessors();
        }

        /// <summary>
        /// Creates a new telemetry data sample with high performance, avoiding boxing/unboxing
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public T CreateTelemetryDataSample(IDataProvider dataProvider)
        {
            var telemetryData = default(T);

            foreach (var accessor in _propertyAccessors)
            {
                var rawValue = dataProvider.GetVarValue(accessor.PropertyName);
                if (rawValue == null) continue;

                try
                {
                    var convertedValue = accessor.ConvertValue(rawValue);
                    accessor.SetValue(ref telemetryData, convertedValue);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("Failed to set property {PropertyName} with value '{RawValue}': {Error}", 
                        accessor.PropertyName, rawValue, ex.Message);
                }
            }

            return telemetryData;
        }


        private static PropertyAccessor[] CompilePropertyAccessors()
        {
            var properties = typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance);
            var accessors = new PropertyAccessor[properties.Length];

            for (int i = 0; i < properties.Length; i++)
            {
                accessors[i] = new PropertyAccessor(properties[i]);
            }

            return accessors;
        }

        private delegate void RefSetter<TStruct>(ref TStruct instance, object value) where TStruct : struct;

        private sealed class PropertyAccessor
        {
            private static readonly ConcurrentDictionary<Type, Func<object, object>> _converterCache
                = new ConcurrentDictionary<Type, Func<object, object>>();

            public string PropertyName { get; }
            public Type PropertyType { get; }
            public Type UnderlyingType { get; }
            public bool IsEnum { get; }
            public bool IsNullable { get; }

            private readonly RefSetter<T> _refSetter;
            private readonly Func<object, object> _converter;

            public PropertyAccessor(PropertyInfo property)
            {
                PropertyName = property.Name;
                PropertyType = property.PropertyType;
                UnderlyingType = Nullable.GetUnderlyingType(PropertyType) ?? PropertyType;
                IsEnum = UnderlyingType.IsEnum;
                IsNullable = PropertyType != UnderlyingType;

                _refSetter = CompileRefPropertySetter(property);
                _converter = GetOrCompileConverter(PropertyType, UnderlyingType, IsEnum);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void SetValue(ref T instance, object value)
            {
                _refSetter(ref instance, value);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public object ConvertValue(object value)
            {
                return _converter(value);
            }

            private static RefSetter<T> CompileRefPropertySetter(PropertyInfo property)
            {
                var instanceParam = Expression.Parameter(typeof(T).MakeByRefType(), "instance");
                var valueParam = Expression.Parameter(typeof(object), "value");

                var valueCast = Expression.Convert(valueParam, property.PropertyType);
                var propertyAccess = Expression.Property(instanceParam, property);
                var assignment = Expression.Assign(propertyAccess, valueCast);

                var lambda = Expression.Lambda<RefSetter<T>>(
                    assignment, instanceParam, valueParam);

                return lambda.Compile();
            }

            private static Func<object, object> GetOrCompileConverter(Type targetType, Type underlyingType, bool isEnum)
            {
                return _converterCache.GetOrAdd(targetType, _ => CompileConverter(targetType, underlyingType, isEnum));
            }

            private static Func<object, object> CompileConverter(Type targetType, Type underlyingType, bool isEnum)
            {
                var valueParam = Expression.Parameter(typeof(object), "value");
                Expression conversionExpr;

                if (isEnum)
                {
                    // Handle enum conversion: if value is int, convert to enum
                    var valueAsInt = Expression.Convert(valueParam, typeof(int));
                    conversionExpr = Expression.Call(
                        typeof(Enum).GetMethod(nameof(Enum.ToObject), new[] { typeof(Type), typeof(object) })!,
                        Expression.Constant(underlyingType),
                        Expression.Convert(valueAsInt, typeof(object)));
                    conversionExpr = Expression.Convert(conversionExpr, targetType);
                }
                else if (targetType == typeof(object))
                {
                    // No conversion needed
                    conversionExpr = valueParam;
                }
                else if (Nullable.GetUnderlyingType(targetType) != null)
                {
                    // Handle nullable types: convert to underlying type first, then to nullable
                    var underlyingConvertMethod = typeof(Convert).GetMethod(nameof(Convert.ChangeType), new[] { typeof(object), typeof(Type) });
                    var underlyingConversion = Expression.Call(underlyingConvertMethod!, valueParam, Expression.Constant(underlyingType));
                    conversionExpr = Expression.Convert(underlyingConversion, targetType);
                }
                else
                {
                    // Standard type conversion
                    var convertMethod = typeof(Convert).GetMethod(nameof(Convert.ChangeType), new[] { typeof(object), typeof(Type) });
                    conversionExpr = Expression.Call(convertMethod!, valueParam, Expression.Constant(targetType));
                    conversionExpr = Expression.Convert(conversionExpr, targetType);
                }

                var lambda = Expression.Lambda<Func<object, object>>(
                    Expression.Convert(conversionExpr, typeof(object)), valueParam);

                return lambda.Compile();
            }
        }
    }
}
