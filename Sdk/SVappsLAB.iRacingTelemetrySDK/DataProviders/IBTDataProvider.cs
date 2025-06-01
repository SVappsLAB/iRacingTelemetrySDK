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
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using SVappsLAB.iRacingTelemetrySDK.IBTPlayback;
using SVappsLAB.iRacingTelemetrySDK.irSDKDefines;

namespace SVappsLAB.iRacingTelemetrySDK.DataProviders
{
    internal unsafe class IBTDataProvider : DataProviderBase, IDataProvider
    {
        readonly IBTOptions _ibtOptions;
        readonly IPlaybackGovernor _governor;
        int _numRecords = 0;
        int _currentRecord = 0;

        public IBTDataProvider(ILogger logger, IBTOptions ibtOptions) : base(logger)
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
            _governor = new SimpleGovernor(_logger, _ibtOptions!.PlayBackSpeedMultiplier);
        }

        public override void OpenDataSource()
        {
            // open in shared mode so multiple processes (or tests) can access the same file
            _mmFile = MemoryMappedFile.CreateFromFile(_ibtOptions.IbtFilePath, FileMode.Open, null, 0, MemoryMappedFileAccess.Read);
            _viewAccessor = _mmFile.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
            _viewAccessor!.SafeMemoryMappedViewHandle.AcquirePointer(ref _dataPtr);

            // read header 
            _header = GetHeader();

            _numRecords = GetNumRecordsInIBTFile();

            _governor.StartPlayback();
        }

        public int GetNumRecordsInIBTFile()
        {
            var numRecs = GetDiskSubHeader().sessionRecordCount;
            return numRecs;
        }

        // wait for iRacing to signal there is new data
        public override bool WaitForDataReady(TimeSpan _timeSpan)
        {
            // throttle playback speed
            _governor.GovernSpeed(_currentRecord).Wait();

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
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
        }
    }
}
