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
using System.Collections.Generic;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.Extensions.Logging;
using SVappsLAB.iRacingTelemetrySDK.irSDKDefines;

namespace SVappsLAB.iRacingTelemetrySDK
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
    internal unsafe class irSDKMemoryAccessProvider : IDisposable
    {
        const string IRSDK_MemMapFileName = @"Local\IRSDKMemMapFileName";
        const string IRSDK_DataValidEventName = @"Local\IRSDKDataValidEvent";
        const int SYNCHRONIZE_ACCESS = 0x00100000;

        ILogger _logger;
        string? _ibtFileName;
        byte[]? _telemetryDataBuffer;
        byte* _dataPtr;
        irsdk_header _header;
        int _oldVarBufLen;
        VarHeaderDictionary? _varHeaders;
        int _lastTickCount = 0; // latest tick count iRacing wrote to
        int _lastSessionInfoUpdate = -1; // latest session info update counter

        int _dataDropCount = 0;
        DateTime _lastDropTime = DateTime.MinValue;

        MemoryMappedFile? _mmFile;
        MemoryMappedViewAccessor? _viewAccessor;
        AutoResetEvent? _dataReadyEvent;

        public irSDKMemoryAccessProvider(ILogger logger)
        {
            _logger = logger;
        }
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        public void OpenDataSource(string ibtFilename)
        {
            _ibtFileName = ibtFilename;

            // explicitly open in shared mode so multiple processes (or tests) can access the same file
            _mmFile = MemoryMappedFile.CreateFromFile(_ibtFileName, FileMode.Open, null, 0, MemoryMappedFileAccess.Read);
            _viewAccessor = _mmFile.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
            _viewAccessor!.SafeMemoryMappedViewHandle.AcquirePointer(ref _dataPtr);

            // read header 
            _header = GetHeader();
        }
        public void OpenDataSource()
        {
            _mmFile = MemoryMappedFile.OpenExisting(IRSDK_MemMapFileName);
            _viewAccessor = _mmFile.CreateViewAccessor();
            _viewAccessor!.SafeMemoryMappedViewHandle.AcquirePointer(ref _dataPtr);

            // read header 
            _header = GetHeader();

            // data ready event
            var rawEvent = PInvoke.OpenEvent(SYNCHRONIZE_ACCESS, false, IRSDK_DataValidEventName);
            _dataReadyEvent = new AutoResetEvent(false) { SafeWaitHandle = new Microsoft.Win32.SafeHandles.SafeWaitHandle(rawEvent, true) };
        }
        public void initCommon()
        {
            _viewAccessor!.SafeMemoryMappedViewHandle.AcquirePointer(ref _dataPtr);
        }
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
            var sessInfo = Marshal.PtrToStringAnsi(new IntPtr(_dataPtr + GetHeader().sessionInfoOffset), len);
            return sessInfo;
        }
        public int GetNumRecordsInIBTFile()
        {
            var numRecs = GetDiskSubHeader().sessionRecordCount;
            return numRecs;
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
                            //val = Marshal.ReadByte(new IntPtr(offset));
                            val = rosBuffer[vh.offset];
                        }
                        else
                        {
                            //val = Marshal.PtrToStringAnsi(new IntPtr(offset));
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
        public bool WaitForDataReady(TimeSpan timeSpan)
        {
            var signaled = _dataReadyEvent!.WaitOne(timeSpan);
            if (!signaled)
            {
                _logger.LogDebug("timeout waiting for data ready event");
                return false;
            }

            var latestTickCount = GetLatestVarBuff().tickCount;
            if (signaled && latestTickCount > _lastTickCount)
            {
                var tickDiff = latestTickCount - _lastTickCount - 1;
                if (_lastTickCount != 0 && tickDiff > 0)
                {
                    _dataDropCount += tickDiff;
                    _logger.LogWarning("dropped {count} data records. {total} total", tickDiff, _dataDropCount);
                }

                // we have new data - copy to the buffer for later reading
                CopyNewTelemetryDataToBuffer();
                _lastTickCount = latestTickCount;
                return true;
            }

            //// something wrong. disconnected?  need to reset
            //if (latestTickCount < _lastTickCount)
            //{
            //    _logger.LogWarning("new data is older than our last sample. lost connection?  resetting");
            //    _lastTickCount = Int32.MaxValue;
            //}

            return false;
        }

        // this is called by the IBT file processor
        // rather than waiting for live data, we just copy the specified data record to the buffer
        public bool WaitForDataReady(int recNum)
        {
            CopyNewTelemetryDataToBuffer(recNum);
            return true;
        }
        internal VarHeaderDictionary? GetVarHeaders()
        {
            return _varHeaders;
        }
        irsdk_varBuf GetLatestVarBuff()
        {
            var header = GetHeader();

            var vb = header.varBuf1;
            if (header.varBuf2.tickCount > vb.tickCount)
                vb = header.varBuf2;
            if (header.varBuf3.tickCount > vb.tickCount)
                vb = header.varBuf3;
            if (header.varBuf4.tickCount > vb.tickCount)
                vb = header.varBuf4;
            return vb;

        }
        irsdk_diskSubHeader GetDiskSubHeader()
        {
            // the disksubheader is located after the header
            var offset = sizeof(irsdk_header);

            var ros = new ReadOnlySpan<byte>(_dataPtr + offset, sizeof(irsdk_diskSubHeader));
            var diskSubHeader = MemoryMarshal.AsRef<irsdk_diskSubHeader>(ros);

            return diskSubHeader;
        }

        // with IBT files, the 'recNum' tells us which data record in the mmf we should read
        void CopyNewTelemetryDataToBuffer(int recNum = 0)
        {
            var offset = _header.GetMostRecentBuffer().bufOffset + (recNum * _header.bufLen);
            var ros = new ReadOnlySpan<byte>(_dataPtr + offset, _header.bufLen);
            ros.CopyTo(_telemetryDataBuffer);
        }

        protected virtual void Dispose(bool disposing)
        {
            // dispose managed resources
            if (disposing)
            {
                if (_dataReadyEvent != null)
                {
                    _dataReadyEvent.Dispose();
                    _dataReadyEvent = null;
                }
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
        ~irSDKMemoryAccessProvider()
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
                var name = Marshal.PtrToStringAnsi(new IntPtr(vh.name)) ?? String.Empty;

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
