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
using System.IO.MemoryMappedFiles;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SVappsLAB.iRacingTelemetrySDK.irSDKDefines;

namespace SVappsLAB.iRacingTelemetrySDK.DataProviders
{
    internal unsafe class LiveDataProvider : DataProviderBase, IDataProvider
    {
        const string IRSDK_MemMapFileName = @"Local\IRSDKMemMapFileName";
        const string IRSDK_DataValidEventName = @"Local\IRSDKDataValidEvent";
        const int SYNCHRONIZE = 0x00100000; // access right for synchronization

        int _dataDropCount = 0;
        int _lastTickCount = 0; // latest tick count iRacing wrote to
        AutoResetEvent? _dataReadyEvent;

        public LiveDataProvider(ILogger logger) : base(logger)
        {
        }

        public override void OpenDataSource()
        {
            _mmFile = MemoryMappedFile.OpenExisting(IRSDK_MemMapFileName);
            _viewAccessor = _mmFile.CreateViewAccessor();
            _viewAccessor!.SafeMemoryMappedViewHandle.AcquirePointer(ref _dataPtr);

            // read header 
            _header = GetHeader();

            // data ready event
            var rawEvent = PInvoke.OpenEvent(SYNCHRONIZE, false, IRSDK_DataValidEventName);
            _dataReadyEvent = new AutoResetEvent(false) { SafeWaitHandle = new Microsoft.Win32.SafeHandles.SafeWaitHandle(rawEvent, true) };
        }

        public override Task<bool> WaitForDataReady(TimeSpan timeSpan, CancellationToken cancellationToken = default)
        {
            return LiveDataProviderAsyncHelper.WaitForDataReady(_dataReadyEvent!, timeSpan, cancellationToken, _logger, this);
        }

        internal bool ProcessNewData()
        {
            var latestTickCount = GetLatestVarBuff().tickCount;

            // if we missed any telemetry data, log that it happened
            if (latestTickCount > _lastTickCount)
            {
                var tickDiff = latestTickCount - _lastTickCount - 1;
                if (_lastTickCount != 0 && tickDiff > 0)
                {
                    _dataDropCount += tickDiff;
                    _logger.LogWarning("dropped {count} data records. {total} total. last tick: {lastTick}, current tick: {currentTick}", tickDiff, _dataDropCount, _lastTickCount, latestTickCount);
                }
            }

            // did we loose sync?  perhaps we disconnected or a new session started
            // log that it happened. we will resync below
            if (latestTickCount < _lastTickCount)
            {
                _logger.LogDebug("new data is older than our last sample. lost connection?  will resync");
            }

            // copy new data to the access buffer for later reading
            CopyNewTelemetryDataToBuffer();
            // resync - update our last tick count
            _lastTickCount = latestTickCount;

            return true;
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
        public override ValueTask DisposeAsync()
        {
            if (_dataReadyEvent != null)
            {
                _dataReadyEvent.Dispose();
                _dataReadyEvent = null;
            }
            return base.DisposeAsync();
        }
    }

    // Helper class to handle async operations outside unsafe context
    internal static class LiveDataProviderAsyncHelper
    {
        public static async Task<bool> WaitForDataReady(AutoResetEvent dataReadyEvent, TimeSpan timeSpan, CancellationToken cancellationToken, ILogger logger, LiveDataProvider provider)
        {
            var signaled = await Task.Run(() => dataReadyEvent.WaitOne(timeSpan), cancellationToken).ConfigureAwait(false);
            if (!signaled)
            {
                logger.LogDebug("timeout waiting for data ready event");
                return false;
            }

            return provider.ProcessNewData();
        }
    }
}
