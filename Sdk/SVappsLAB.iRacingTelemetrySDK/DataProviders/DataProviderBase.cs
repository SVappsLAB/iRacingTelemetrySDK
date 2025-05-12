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
 * limitations under the License.using Microsoft.CodeAnalysis;
**/

using System;
using System.Collections.Generic;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Extensions.Logging;
using SVappsLAB.iRacingTelemetrySDK.irSDKDefines;

namespace SVappsLAB.iRacingTelemetrySDK.DataProviders
{
    internal enum irsdk_StatusField
    {
        irsdk_stNotConnected = 0,
        irsdk_stConnected = 1
    };


    internal class VarHeaderDictionary : Dictionary<string, irsdk_varHeader>
    {
        public VarHeaderDictionary() : base(StringComparer.InvariantCultureIgnoreCase)
        {
        }
    }

    internal abstract unsafe class DataProviderBase : IDisposable
    {

        protected ILogger _logger;
        byte[]? _telemetryDataBuffer;
        protected byte* _dataPtr;
        protected irsdk_header _header;
        int _oldVarBufLen;
        VarHeaderDictionary? _varHeaders;
        int _lastSessionInfoUpdate = -1; // latest session info update counter

        protected MemoryMappedFile? _mmFile;
        protected MemoryMappedViewAccessor? _viewAccessor;

        public DataProviderBase(ILogger logger)
        {
            _logger = logger;
            _logger.LogInformation($"Initializing {GetType().Name}.");
        }
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        public void OpenDataSource(string ibtFilename)
        {
        }
        public abstract void OpenDataSource();
        public bool IsConnected => (GetHeader().status & irsdk_StatusField.irsdk_stConnected) > 0;
        public bool IsSessionInfoUpdated()
        {
            var siUpdateCount = GetHeader().sessionInfoUpdate;
            // if nothing has changed
            if (siUpdateCount == _lastSessionInfoUpdate)
                return false;

            // new data. update our marker
            _lastSessionInfoUpdate = siUpdateCount;
            return true;
        }
        public irsdk_header GetHeader()
        {
            var ros = new ReadOnlySpan<byte>(_dataPtr, sizeof(irsdk_header));
            _header = MemoryMarshal.AsRef<irsdk_header>(ros);

            // varbuff changed?
            if (_oldVarBufLen != _header.bufLen)
            {
                _logger.LogDebug("buffLen changed ({oldLength} to {newLength}), updating headers and buffer", _oldVarBufLen, _header.bufLen);

                _varHeaders = ReadVarHeaders();
                _oldVarBufLen = _header.bufLen;

                // allocate new data buffer
                _telemetryDataBuffer = new byte[_header.bufLen];
            }

            return _header;
        }
        public string GetSessionInfoYaml()
        {
            var header = GetHeader();
            var offSet = header.sessionInfoOffset;

            // this is the maximum length
            var len = header.sessionInfoLen;

            // but the actual length may be shorter, so we need
            // to scan and find the null terminator, if there is one
            Span<byte> span = new Span<byte>(_dataPtr + offSet, len);
            for (int i = 0; i < span.Length; i++)
            {
                if (span[i] == 0)
                {
                    _logger.LogDebug("SessionInfo: length is {len}, but found null terminator at {i}", len, i);

                    // adjust length
                    len = i;
                    break;
                }
            }

            // convert buffer (from 'offSet' to 'len') to a string
            var sessInfo = Marshal.PtrToStringAnsi(new nint(_dataPtr + GetHeader().sessionInfoOffset), len);
            return sessInfo;
        }

        public object GetVarValue(string varName)
        {
            if (!_varHeaders!.TryGetValue(varName, out irsdk_varHeader vh))
            {
                throw new Exception($"the telemetry value '{varName}' does not exist");
            }

            var rosBuffer = _telemetryDataBuffer.AsSpan();

            object val = 0;

            switch (vh.type)
            {
                case irsdk_VarType.irsdk_char:
                    {
                        if (vh.count == 1)
                        {
                            // read the byte value at the offset
                            val = rosBuffer[vh.offset];
                        }
                        else
                        {
                            val = Encoding.ASCII.GetString(_telemetryDataBuffer!, vh.offset, vh.count);
                        }
                    }
                    break;
                case irsdk_VarType.irsdk_bool:
                    {
                        val = GetValue<bool>(rosBuffer, vh.offset, vh.count);
                    }
                    break;
                case irsdk_VarType.irsdk_int:
                case irsdk_VarType.irsdk_bitField:
                    {
                        val = GetValue<int>(rosBuffer, vh.offset, vh.count);
                    }
                    break;
                case irsdk_VarType.irsdk_float:
                    {
                        val = GetValue<float>(rosBuffer, vh.offset, vh.count);
                    }
                    break;
                case irsdk_VarType.irsdk_double:
                    {
                        val = GetValue<double>(rosBuffer, vh.offset, vh.count);
                    }
                    break;
                default:
                    throw new NotImplementedException($"{vh.type}, not implemented");
            }

            return val;
        }

        // wait for iRacing to signal there is new data
        public abstract bool WaitForDataReady(TimeSpan timeSpan);

        public VarHeaderDictionary? GetVarHeaders()
        {
            return _varHeaders;
        }


        // with IBT files, the 'recNum' tells us which data record in the mmf we should read
        protected void CopyNewTelemetryDataToBuffer(int recNum = 0)
        {
            var offset = _header.GetMostRecentBuffer().bufOffset + recNum * _header.bufLen;
            var ros = new ReadOnlySpan<byte>(_dataPtr + offset, _header.bufLen);
            ros.CopyTo(_telemetryDataBuffer);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_viewAccessor != null)
                {
                    _viewAccessor.SafeMemoryMappedViewHandle.ReleasePointer();
                    _viewAccessor.Dispose();
                    _viewAccessor = null;
                }
                if (_mmFile != null)
                {
                    _mmFile.Dispose();
                    _mmFile = null;
                }
            }
        }
        ~DataProviderBase()
        {
            Dispose(false);
        }
        VarHeaderDictionary ReadVarHeaders()
        {
            var ros = new ReadOnlySpan<irsdk_varHeader>(_dataPtr + _header.varHeaderOffset, _header.numVars);

            var dict = new VarHeaderDictionary();
            for (int i = 0; i < _header.numVars; i++)
            {
                var vh = ros[i];
                var name = Marshal.PtrToStringAnsi(new nint(vh.name)) ?? string.Empty;

                dict.Add(name, vh);
            }

            return dict;
        }
        object GetValue<T>(ReadOnlySpan<byte> span, int offset, int count) where T : struct
        {
            var ros = MemoryMarshal.Cast<byte, T>(span.Slice(offset, count * Marshal.SizeOf(typeof(T))));
            var data = ros.ToArray();
            object val = count == 1 ? data[0] : data;
            return val;
        }

    }

}

