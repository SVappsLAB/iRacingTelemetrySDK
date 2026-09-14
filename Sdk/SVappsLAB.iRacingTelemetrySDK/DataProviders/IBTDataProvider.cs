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
using SVappsLAB.iRacingTelemetrySDK.IBTPlayback;
using SVappsLAB.iRacingTelemetrySDK.irSDKDefines;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;

namespace SVappsLAB.iRacingTelemetrySDK.DataProviders;

internal unsafe class IBTDataProvider : DataProviderBase, IDataProvider
{
    // the irsdk header version has been 1 or 2 forever. 
    // anything far outside is garbage
    const int MAX_SANE_HEADER_VER = 3;

    readonly IBTOptions _ibtOptions;
    readonly IPlaybackGovernor _governor;
    int _numRecords = 0;
    int _currentRecord = 0;

    // onPlaybackLag is called for each record of a paced playback with how far behind its
    // scheduled time the record is being read
    public IBTDataProvider(ILogger logger, IBTOptions ibtOptions, Action<TimeSpan>? onPlaybackLag = null) : base(logger)
    {

        if (!File.Exists(ibtOptions.IbtFilePath))
        {
            throw new FileNotFoundException($"IBT file [{ibtOptions.IbtFilePath}] not found", ibtOptions.IbtFilePath);
        }
        if (!ibtOptions.IbtFilePath.EndsWith(".ibt"))
        {
            throw new ArgumentException($"File [{ibtOptions.IbtFilePath}] is not an IBT file", ibtOptions.IbtFilePath);
        }
        _ibtOptions = ibtOptions;
        _governor = new SimpleGovernor(_logger, _ibtOptions!.PlayBackSpeedMultiplier, onPlaybackLag);
    }

    public override void OpenDataSource()
    {
        var fileLength = new FileInfo(_ibtOptions.IbtFilePath).Length;

        // open in shared mode so multiple processes (or tests) can access the same file
        _mmFile = MemoryMappedFile.CreateFromFile(_ibtOptions.IbtFilePath, FileMode.Open, null, 0, MemoryMappedFileAccess.Read);
        _viewAccessor = _mmFile.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
        _viewAccessor!.SafeMemoryMappedViewHandle.AcquirePointer(ref _dataPtr);

        // validate before the first read
        ValidateHeader(fileLength);

        // read header
        _header = GetHeader();

        _numRecords = GetNumRecordsInIBTFile(fileLength);

        if (_logger.IsEnabled(LogLevel.Debug))
            LogIBTFileLayout();

        _governor.StartPlayback();
    }

    // validate
    void ValidateHeader(long fileLength)
    {
        var minLength = sizeof(irsdk_header) + sizeof(irsdk_diskSubHeader);
        if (fileLength < minLength)
            throw Invalid($"file is {fileLength} bytes, too small to hold the {minLength} byte header");

        // read the raw header rather than calling GetHeader(), which would go on to read the
        // var headers
        var header = MemoryMarshal.AsRef<irsdk_header>(new ReadOnlySpan<byte>(_dataPtr, sizeof(irsdk_header)));

        // a sanity band
        if (header.ver < 1 || header.ver > MAX_SANE_HEADER_VER)
            throw Invalid($"header version {header.ver} is not a valid irsdk version");
        if (header.ver != Constants.IRSDK_VER)
            _logger.LogWarning("IBT file [{file}] has header version {ver}, expected {expectedVer}. attempting to read it anyway",
                _ibtOptions.IbtFilePath, header.ver, Constants.IRSDK_VER);

        if (header.numVars <= 0)
            throw Invalid($"numVars is {header.numVars}");
        if (header.bufLen <= 0)
            throw Invalid($"bufLen is {header.bufLen}");
        if (header.numBuf < 1 || header.numBuf > Constants.IRSDK_MAX_BUFS)
            throw Invalid($"numBuf is {header.numBuf}");

        // the var header array must sit past the header, and end inside the file
        var varHeaderEnd = (long)header.varHeaderOffset + (long)header.numVars * sizeof(irsdk_varHeader);
        if (header.varHeaderOffset < sizeof(irsdk_header) || varHeaderEnd > fileLength)
            throw Invalid($"the {header.numVars} var headers at offset {header.varHeaderOffset} end at {varHeaderEnd}, past the end of the {fileLength} byte file");

        // session info yaml
        var sessionInfoEnd = (long)header.sessionInfoOffset + header.sessionInfoLen;
        if (header.sessionInfoOffset < sizeof(irsdk_header) || header.sessionInfoLen < 0 || sessionInfoEnd > fileLength)
            throw Invalid($"session info at offset {header.sessionInfoOffset} with length {header.sessionInfoLen} ends at {sessionInfoEnd}, past the end of the {fileLength} byte file");

        // the record region starts at the first (only) buffer. one full record has to fit
        var firstRecordOffset = header.GetVarBuf(0).bufOffset;
        if (firstRecordOffset < sizeof(irsdk_header) || (long)firstRecordOffset + header.bufLen > fileLength)
            throw Invalid($"the first {header.bufLen} byte record at offset {firstRecordOffset} runs past the end of the {fileLength} byte file");
    }

    InvalidDataException Invalid(string reason) =>
        new($"IBT file [{_ibtOptions.IbtFilePath}] is not valid: {reason}");

    // the base class logs the regions common to live and IBT data.
    // add the the disk sub header, and the record region till end of the file
    void LogIBTFileLayout()
    {
        var diskSubHeader = GetDiskSubHeader();

        _logger.LogDebug("IBT layout: file={file}", _ibtOptions.IbtFilePath);

        // the disk sub header sits immediately after the header
        _logger.LogDebug("IBT layout: diskSubHeaderOffset={diskSubHeaderOffset}, diskSubHeaderEnd={diskSubHeaderEnd}, sessionStartDate={sessionStartDate}, sessionStartTime={sessionStartTime}, sessionEndTime={sessionEndTime}, sessionLapCount={sessionLapCount}, sessionRecordCount={sessionRecordCount}",
            sizeof(irsdk_header), sizeof(irsdk_header) + sizeof(irsdk_diskSubHeader),
            diskSubHeader.sessionStartDate, diskSubHeader.sessionStartTime, diskSubHeader.sessionEndTime,
            diskSubHeader.sessionLapCount, diskSubHeader.sessionRecordCount);

        // records are written one after the other, starting at the first (only) buffer offset
        var firstRecordOffset = _header.GetVarBuf(0).bufOffset;
        var recordBytes = (long)diskSubHeader.sessionRecordCount * _header.bufLen;
        var computedFileLength = firstRecordOffset + recordBytes;
        var actualFileLength = new FileInfo(_ibtOptions.IbtFilePath).Length;

        _logger.LogDebug("IBT layout: firstRecordOffset={firstRecordOffset}, recordCount={recordCount}, bufLen={bufLen}, recordBytes={recordBytes}, computedFileLength={computedFileLength}, actualFileLength={actualFileLength}, matches={matches}",
            firstRecordOffset, diskSubHeader.sessionRecordCount, _header.bufLen, recordBytes,
            computedFileLength, actualFileLength, computedFileLength == actualFileLength);
    }

    public int GetNumRecordsInIBTFile(long fileLength)
    {
        var claimedRecs = GetDiskSubHeader().sessionRecordCount;

        var firstRecordOffset = _header.GetVarBuf(0).bufOffset;
        var availableRecs = (int)Math.Min((fileLength - firstRecordOffset) / _header.bufLen, int.MaxValue);

        if (claimedRecs > availableRecs)
        {
            _logger.LogWarning("IBT file [{file}] claims {claimedRecs} records, but only {availableRecs} fit in its {fileLength} bytes. the file looks truncated - playing back what is there",
                _ibtOptions.IbtFilePath, claimedRecs, availableRecs, fileLength);
            return availableRecs;
        }

        return claimedRecs;
    }

    // wait for iRacing to signal there is new data
    public override Task<bool> WaitForDataReady(TimeSpan _timeSpan, CancellationToken cancellationToken = default)
    {
        return IBTDataProviderAsyncHelper.WaitForDataReady(_governor, _currentRecord, this);
    }

    internal bool ProcessNextRecord()
    {
        CopyNewTelemetryDataToBuffer(_currentRecord);

        _currentRecord++;

        // return true if there is more data to process
        return _currentRecord < _numRecords;
    }

    irsdk_diskSubHeader GetDiskSubHeader()
    {
        // the disksubheader is located after the header
        var offset = sizeof(irsdk_header);

        var ros = new ReadOnlySpan<byte>(_dataPtr + offset, sizeof(irsdk_diskSubHeader));
        var diskSubHeader = MemoryMarshal.AsRef<irsdk_diskSubHeader>(ros);

        return diskSubHeader;
    }
    public override ValueTask DisposeAsync()
    {
        // IBTDataProvider doesn't have additional resources to dispose
        return base.DisposeAsync();
    }
}

// Helper class to handle async operations outside unsafe context
internal static class IBTDataProviderAsyncHelper
{
    public static async Task<bool> WaitForDataReady(IPlaybackGovernor governor, int currentRecord, IBTDataProvider provider)
    {
        // throttle playback speed
        await governor.GovernSpeed(currentRecord).ConfigureAwait(false);

        // measured after the wait, so any delay in the read loop shows up as lag
        governor.RecordLag(currentRecord);

        return provider.ProcessNextRecord();
    }
}
