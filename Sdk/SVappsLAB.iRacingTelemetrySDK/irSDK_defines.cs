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
using System.Runtime.InteropServices;
using SVappsLAB.iRacingTelemetrySDK.DataProviders;

namespace SVappsLAB.iRacingTelemetrySDK.irSDKDefines
{
    internal static class Constants
    {
        public const int IRSDK_MAX_BUFS = 4;
        public const int IRSDK_MAX_STRING = 32;
        // descriptions can be longer than max_string!
        public const int IRSDK_MAX_DESC = 64;

        // define markers for unlimited session lap and time
        public const int IRSDK_UNLIMITED_LAPS = 32767;
        public const float IRSDK_UNLIMITED_TIME = 604800.0f;

        // latest version of our telemetry headers
        public const int IRSDK_VER = 2;
    }

    internal enum irsdk_VarType : Int32
    {
        // 1 byte
        irsdk_char = 0,
        irsdk_bool,

        // 4 bytes
        irsdk_int,
        irsdk_bitField,
        irsdk_float,

        // 8 bytes
        irsdk_double,

        irsdk_ETCount
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    internal struct irsdk_varBuf
    {
        public int tickCount;       // used to detect changes in data (updated AFTER write completes)
        public int bufOffset;       // offset from header
        public int tickCountBegin;  // updated BEFORE write starts (for torn read detection)
        public int pad;             // (16 byte align)
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    internal unsafe struct irsdk_header
    {
        public int ver;                 // this api header version, see IRSDK_VER
        public irsdk_StatusField status;// bitfield using irsdk_StatusField
        public int tickRate;            // ticks per second (60 or 360 etc)

        // session information, updated periodically
        public int sessionInfoUpdate;   // Incremented when session info changes
        public int sessionInfoLen;      // Length in bytes of session info string
        public int sessionInfoOffset;   // Session info, encoded in YAML format

        // State data, output at tickRate
        public int numVars;             // length of array pointed to by varHeaderOffset
        public int varHeaderOffset;     // offset to irsdk_varHeader[numVars] array

        public int numBuf;              // <= IRSDK_MAX_BUFS (3 for now)
        public int bufLen;              // length in bytes for one line
        public int curBufTickCount;     // stashed copy of the current tickCount, can read this to see if new data is available
        public byte curBuf;             // index of the most recently written buffer (0 to IRSDK_MAX_BUFS-1)
        public fixed byte pad1[3];      // 16 byte align

        // if we don't use an array here. allows us to read this structure directly from unmanaged memory
        public irsdk_varBuf varBuf1;
        public irsdk_varBuf varBuf2;
        public irsdk_varBuf varBuf3;
        public irsdk_varBuf varBuf4;

        #region methods
        public irsdk_varBuf GetMostRecentBuffer()
        {
            // only the first numBuf buffers are active; the remainder are unused.
            // use numBuf rather than assuming all IRSDK_MAX_BUFS slots are valid.
            var activeBufs = Math.Min(numBuf, Constants.IRSDK_MAX_BUFS);

            var vb = varBuf1;
            for (int i = 1; i < activeBufs; i++)
            {
                var candidate = GetVarBuf(i);
                if (candidate.tickCount > vb.tickCount)
                    vb = candidate;
            }
            return vb;
        }

        // varBuf is exposed as discrete fields (so the header can be read directly
        // from unmanaged memory), so provide indexed access for iteration
        irsdk_varBuf GetVarBuf(int index) => index switch
        {
            0 => varBuf1,
            1 => varBuf2,
            2 => varBuf3,
            3 => varBuf4,
            _ => throw new ArgumentOutOfRangeException(nameof(index)),
        };
        #endregion
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public struct irsdk_diskSubHeader
    {
        public long sessionStartDate;   // seconds since epoch (Jan 1, 1970)
        public double sessionStartTime; // seconds since sessionStartDate
        public double sessionEndTime;   // seconds since sessionStartDate
        public int sessionLapCount;
        public int sessionRecordCount;  // num varBuff records in file
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal unsafe struct irsdk_varHeader
    {
        public irsdk_VarType type;  // irsdk_VarType
        public int offset;          // offset from start of buffer row
        public int count;           // number of entries (array)

        public bool countAsTime;    // 1-byte
        public fixed byte pad[3];   // (need 16 byte align)

        public fixed byte name[Constants.IRSDK_MAX_STRING];
        public fixed byte desc[Constants.IRSDK_MAX_DESC];
        public fixed byte unit[Constants.IRSDK_MAX_STRING];   // something like "kg/m^2"
    }
}
