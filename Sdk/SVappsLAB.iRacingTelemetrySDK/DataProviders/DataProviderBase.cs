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
using System.Collections.Generic;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SVappsLAB.iRacingTelemetrySDK.irSDKDefines;

namespace SVappsLAB.iRacingTelemetrySDK.DataProviders;

internal enum irsdk_StatusField
{
    irsdk_stNotConnected = 0,
    irsdk_stConnected = 1
};


internal class VarHeaderDictionary : Dictionary<string, irsdk_varHeader>
{
    public VarHeaderDictionary() : base(StringComparer.OrdinalIgnoreCase)
    {
    }
}

internal abstract unsafe class DataProviderBase : IAsyncDisposable
{
    private static readonly Encoding TelemetryEncoding = Encoding.GetEncoding("ISO-8859-1");

    protected ILogger _logger;
    byte[]? _telemetryDataBuffer;
    protected byte* _dataPtr;
    protected irsdk_header _header;
    int _oldVarBufLen;
    VarHeaderDictionary? _varHeaders;
    int _lastSessionInfoUpdate = -1; // latest session info update counter

    // last layout we logged. used to detect when a region moves or resizes, so
    // we only log the layout when it actually changes (see LogLayoutIfChanged)
    int _loggedNumVars = -1;
    int _loggedBufLen = -1;
    int _loggedVarHeaderOffset = -1;
    int _loggedSessionInfoOffset = -1;
    int _loggedSessionInfoLen = -1;
    int _loggedFirstBufOffset = -1;

    protected MemoryMappedFile? _mmFile;
    protected MemoryMappedViewAccessor? _viewAccessor;

    public DataProviderBase(ILogger logger)
    {
        _logger = logger;
        _logger.LogDebug($"Initializing {GetType().Name}.");
    }

    public virtual ValueTask DisposeAsync()
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

        GC.SuppressFinalize(this);
        return ValueTask.CompletedTask;
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

        if (_logger.IsEnabled(LogLevel.Debug))
            LogLayoutIfChanged();

        return _header;
    }

    // log where each region of the memory mapped data lives
    protected void LogLayoutIfChanged()
    {
        var firstBufOffset = _header.GetVarBuf(0).bufOffset;

        var changed =
            _header.numVars != _loggedNumVars ||
            _header.bufLen != _loggedBufLen ||
            _header.varHeaderOffset != _loggedVarHeaderOffset ||
            _header.sessionInfoOffset != _loggedSessionInfoOffset ||
            _header.sessionInfoLen != _loggedSessionInfoLen ||
            firstBufOffset != _loggedFirstBufOffset;

        if (!changed)
            return;

        var isFirstLog = _loggedNumVars == -1;

        _loggedNumVars = _header.numVars;
        _loggedBufLen = _header.bufLen;
        _loggedVarHeaderOffset = _header.varHeaderOffset;
        _loggedSessionInfoOffset = _header.sessionInfoOffset;
        _loggedSessionInfoLen = _header.sessionInfoLen;
        _loggedFirstBufOffset = firstBufOffset;

        LogLayout(isFirstLog ? "initial" : "changed");
    }

    void LogLayout(string reason)
    {
        var source = GetType().Name;

        _logger.LogDebug("{source} layout ({reason}): ver={ver}, status={status}, tickRate={tickRate}, sessionInfoUpdate={sessionInfoUpdate}",
            source, reason, _header.ver, _header.status, _header.tickRate, _header.sessionInfoUpdate);

        _logger.LogDebug("{source} layout ({reason}): sizeof(header)={headerSize}, sizeof(diskSubHeader)={diskSubHeaderSize}, sizeof(varHeader)={varHeaderSize}, sizeof(varBuf)={varBufSize}",
            source, reason, sizeof(irsdk_header), sizeof(irsdk_diskSubHeader), sizeof(irsdk_varHeader), sizeof(irsdk_varBuf));

        // varHeader array: numVars entries, each sizeof(irsdk_varHeader) bytes
        var varHeaderBytes = (long)_header.numVars * sizeof(irsdk_varHeader);
        _logger.LogDebug("{source} layout ({reason}): varHeaderOffset={varHeaderOffset}, numVars={numVars}, varHeaderBytes={varHeaderBytes}, varHeaderEnd={varHeaderEnd}",
            source, reason, _header.varHeaderOffset, _header.numVars, varHeaderBytes, _header.varHeaderOffset + varHeaderBytes);

        // session info yaml. 'sessionInfoLen' is the length in use, which may be smaller than what the irsdk reserved
        _logger.LogDebug("{source} layout ({reason}): sessionInfoOffset={sessionInfoOffset}, sessionInfoLen={sessionInfoLen}, sessionInfoEnd={sessionInfoEnd}",
            source, reason, _header.sessionInfoOffset, _header.sessionInfoLen, (long)_header.sessionInfoOffset + _header.sessionInfoLen);

        // telemetry buffers
        var numBuf = Math.Min(_header.numBuf, irSDKDefines.Constants.IRSDK_MAX_BUFS);
        _logger.LogDebug("{source} layout ({reason}): numBuf={numBuf}, bufLen={bufLen}, curBuf={curBuf}, curBufTickCount={curBufTickCount}",
            source, reason, _header.numBuf, _header.bufLen, _header.curBuf, _header.curBufTickCount);

        for (var i = 0; i < numBuf; i++)
        {
            var varBuf = _header.GetVarBuf(i);
            _logger.LogDebug("{source} layout ({reason}): varBuf[{index}] bufOffset={bufOffset}, bufEnd={bufEnd}, tickCount={tickCount}, tickCountBegin={tickCountBegin}, pad={pad}",
                source, reason, i, varBuf.bufOffset, (long)varBuf.bufOffset + _header.bufLen, varBuf.tickCount, varBuf.tickCountBegin, varBuf.pad);
        }
    }
    public string GetSessionInfoYaml()
    {
        var header = GetHeader();
        var offSet = header.sessionInfoOffset;
        var maxLen = header.sessionInfoLen;

        var span = new ReadOnlySpan<byte>(_dataPtr + offSet, maxLen);
        return SessionInfoDecoder.Decode(span);
    }

    public object? GetVarValue(string varName)
    {
        // headers/buffer are populated lazily by GetHeader() once the provider has read at least one
        // header from the data source
        if (_varHeaders == null || _telemetryDataBuffer == null)
        {
            _logger.LogDebug("Telemetry data not yet available; ignoring lookup for [{varName}]", varName);
            return null;
        }

        if (!_varHeaders.TryGetValue(varName, out irsdk_varHeader vh))
        {
            _logger.LogDebug("Telemetry variable [{varName}] not found in data provider", varName);
            return null;
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
                        var span = _telemetryDataBuffer.AsSpan(vh.offset, vh.count);
                        val = ExtractNullTerminatedString(span, vh.count);
                    }
                }
                break;
            case irsdk_VarType.irsdk_bool:
                {
                    val = GetValue<bool>(rosBuffer, vh.offset, vh.count, vh.type);
                }
                break;
            case irsdk_VarType.irsdk_int:
            case irsdk_VarType.irsdk_bitField:
                {
                    val = GetValue<int>(rosBuffer, vh.offset, vh.count, vh.type);
                }
                break;
            case irsdk_VarType.irsdk_float:
                {
                    val = GetValue<float>(rosBuffer, vh.offset, vh.count, vh.type);
                }
                break;
            case irsdk_VarType.irsdk_double:
                {
                    val = GetValue<double>(rosBuffer, vh.offset, vh.count, vh.type);
                }
                break;
            default:
                throw new NotImplementedException($"{vh.type}, not implemented");
        }

        return val;
    }

    // maps irsdk_VarType -> CLR Type mapping
    internal static Type GetClrType(irsdk_VarType type, int count) => type switch
    {
        // a single char returns the raw byte; a char buffer decodes to one string (a text field, not
        // an array of separate strings)
        irsdk_VarType.irsdk_char => count > 1 ? typeof(string) : typeof(byte),
        irsdk_VarType.irsdk_bool => count > 1 ? typeof(bool[]) : typeof(bool),
        // bitField is packed into the same int/int[] path as irsdk_int above
        irsdk_VarType.irsdk_int or irsdk_VarType.irsdk_bitField => count > 1 ? typeof(int[]) : typeof(int),
        irsdk_VarType.irsdk_float => count > 1 ? typeof(float[]) : typeof(float),
        irsdk_VarType.irsdk_double => count > 1 ? typeof(double[]) : typeof(double),
        _ => throw new NotImplementedException($"{type} not implemented")
    };

    // wait for iRacing to signal there is new data
    public abstract Task<bool> WaitForDataReady(TimeSpan timeSpan, CancellationToken cancellationToken = default);

    public VarHeaderDictionary? GetVarHeaders()
    {
        return _varHeaders;
    }


    // with IBT files, the 'recNum' tells us which data record in the mmf we should read
    //
    // IBT files always have a single buffer, and don't populate 'curBuf' (it is
    // only written by the sim, for live data). read varBuf[0] directly, rather
    // than resolving through the live-only field
    protected void CopyNewTelemetryDataToBuffer(int recNum = 0)
    {
        var offset = _header.GetVarBuf(0).bufOffset + recNum * _header.bufLen;
        var ros = new ReadOnlySpan<byte>(_dataPtr + offset, _header.bufLen);
        ros.CopyTo(_telemetryDataBuffer);
    }

    // live telemetry can be overwritten by the sim while we read it. mirror the
    // official sdk's torn-read detection: 'tickCountBegin' is updated before a
    // write starts and 'tickCount' after it completes. if the tickCount read
    // before the copy matches tickCountBegin after, no write was in progress
    protected bool TryCopyLiveTelemetryDataToBuffer(out int validTickCount)
    {
        const int MAX_ATTEMPTS = 2;

        // try a few times to get the data out
        for (var attempt = 0; attempt < MAX_ATTEMPTS; attempt++)
        {
            var header = GetHeader();
            var bufIndex = header.GetMostRecentBufferIndex();
            var varBuf = header.GetVarBuf(bufIndex);

            var curTickCount = varBuf.tickCount;
            Thread.MemoryBarrier();

            var ros = new ReadOnlySpan<byte>(_dataPtr + varBuf.bufOffset, header.bufLen);
            ros.CopyTo(_telemetryDataBuffer);

            Thread.MemoryBarrier();

            // re-read from shared memory to see if a write was in progress
            if (curTickCount == GetHeader().GetVarBuf(bufIndex).tickCountBegin)
            {
                validTickCount = curTickCount;
                return true;
            }
        }

        // the data changed out from under us
        validTickCount = 0;
        return false;
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

    /// <summary>
    /// Extract a null-terminated string from a byte span using the specified encoding
    /// </summary>
    /// <param name="data">The byte span containing the string data</param>
    /// <param name="expectedLength">The maximum expected length of the string</param>
    /// <returns>Decoded string up to the first null byte or end of span</returns>
    private string ExtractNullTerminatedString(Span<byte> data, int expectedLength)
    {
        // Scan for null terminator
        int actualLength = data.Length;
        for (int i = 0; i < data.Length; i++)
        {
            if (data[i] == 0)
            {
                actualLength = i;
                if (actualLength < expectedLength)
                {
                    _logger.LogDebug("String length is {actualLength}, but expected length was {expectedLength}", actualLength, expectedLength);
                }
                break;
            }
        }
        return TelemetryEncoding.GetString(data.Slice(0, actualLength));
    }

    object GetValue<T>(ReadOnlySpan<byte> span, int offset, int count, irsdk_VarType type) where T : struct
    {
        int elementSizeInBytes = type switch
        {
            irsdk_VarType.irsdk_bool => sizeof(bool),
            irsdk_VarType.irsdk_int => sizeof(int),
            irsdk_VarType.irsdk_bitField => sizeof(int),
            irsdk_VarType.irsdk_float => sizeof(float),
            irsdk_VarType.irsdk_double => sizeof(double),
            _ => throw new NotImplementedException($"{type} size not implemented")
        };

        var ros = MemoryMarshal.Cast<byte, T>(span.Slice(offset, count * elementSizeInBytes));

        // optimize memory allocation: avoid array allocation for single values
        if (count == 1)
            return ros[0];
        else
            return ros.ToArray();
    }

}

