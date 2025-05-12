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
using System.Runtime.InteropServices;
using SVappsLAB.iRacingTelemetrySDK.DataProviders;

namespace SVappsLAB.iRacingTelemetrySDK.irSDKDefines
{
    internal static class Constants
    {
        public const int IRSDK_MAX_BUFS = 4;
        public const int IRSDK_MAX_STRING = 32;
        public const int IRSDK_MAX_DESC = 64;
    }

    internal enum irsdk_VarType : Int32
    {
        irsdk_char = 0,
        irsdk_bool,

        irsdk_int,
        irsdk_bitField,
        irsdk_float,

        irsdk_double,

        irsdk_ETCount
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    internal struct irsdk_varBuf
    {
        public int tickCount;
        public int bufOffset;
        public int pad1;
        public int pad2;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    internal struct irsdk_header
    {
        public int ver;
        public irsdk_StatusField status;
        public int tickRate;
        public int sessionInfoUpdate;
        public int sessionInfoLen;
        public int sessionInfoOffset;
        public int numVars;
        public int varHeaderOffset;
        public int numBuf;
        public int bufLen;
        // padding
        public int pad1;
        public int pad2;

        // if we don't use an array here. allows us to read this structure directly from unmanaged memory
        public irsdk_varBuf varBuf1;
        public irsdk_varBuf varBuf2;
        public irsdk_varBuf varBuf3;
        public irsdk_varBuf varBuf4;

        #region methods
        public irsdk_varBuf GetMostRecentBuffer()
        {
            var vb = varBuf1;
            if (varBuf2.tickCount > vb.tickCount)
                vb = varBuf2;
            if (varBuf3.tickCount > vb.tickCount)
                vb = varBuf3;
            if (varBuf4.tickCount > vb.tickCount)
                vb = varBuf4;
            return vb;
        }
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
