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

using System.Collections.Generic;

#nullable disable
namespace SVappsLAB.iRacingTelemetrySDK.Models
{

    public class CameraInfo
    {
        public List<Group> Groups { get; set; }

    }

    public class Group
    {
        public int GroupNum { get; set; }
        public string GroupName { get; set; }
        public bool IsScenic { get; set; } // Added for the Scenic group
        public List<Camera> Cameras { get; set; }

    }

    public class Camera
    {
        public int CameraNum { get; set; }
        public string CameraName { get; set; }

    }
}
#nullable enable
