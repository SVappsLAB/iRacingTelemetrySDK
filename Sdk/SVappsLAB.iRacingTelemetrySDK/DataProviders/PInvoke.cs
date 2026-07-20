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

using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace SVappsLAB.iRacingTelemetrySDK.DataProviders;

[SupportedOSPlatform("windows")]
internal static class PInvoke
{
    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern nint OpenEvent(uint dwDesiredAccess, bool bInheritHandle, string lpName);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool CloseHandle(nint hObject);
}
