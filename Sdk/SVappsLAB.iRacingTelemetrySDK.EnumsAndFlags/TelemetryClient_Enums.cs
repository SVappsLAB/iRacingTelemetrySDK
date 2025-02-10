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


// enums
namespace SVappsLAB.iRacingTelemetrySDK
{
    public enum irsdk_TrkLoc
    {
        irsdk_NotInWorld = -1,
        irsdk_OffTrack = 0,
        irsdk_InPitStall,
        // This indicates the lead in to pit road, as well as the pit road itself (where speed limits are enforced)
        // if you just want to know that your on the pit road surface look at the live value 'OnPitRoad'
        irsdk_AproachingPits,
        irsdk_OnTrack
    };

    public enum irsdk_TrkSurf
    {
        irsdk_SurfaceNotInWorld = -1,
        irsdk_UndefinedMaterial = 0,

        irsdk_Asphalt1Material,
        irsdk_Asphalt2Material,
        irsdk_Asphalt3Material,
        irsdk_Asphalt4Material,
        irsdk_Concrete1Material,
        irsdk_Concrete2Material,
        irsdk_RacingDirt1Material,
        irsdk_RacingDirt2Material,
        irsdk_Paint1Material,
        irsdk_Paint2Material,
        irsdk_Rumble1Material,
        irsdk_Rumble2Material,
        irsdk_Rumble3Material,
        irsdk_Rumble4Material,

        irsdk_Grass1Material,
        irsdk_Grass2Material,
        irsdk_Grass3Material,
        irsdk_Grass4Material,
        irsdk_Dirt1Material,
        irsdk_Dirt2Material,
        irsdk_Dirt3Material,
        irsdk_Dirt4Material,
        irsdk_SandMaterial,
        irsdk_Gravel1Material,
        irsdk_Gravel2Material,
        irsdk_GrasscreteMaterial,
        irsdk_AstroturfMaterial,
    };

    public enum irsdk_SessionState
    {
        irsdk_StateInvalid = 0,
        irsdk_StateGetInCar,
        irsdk_StateWarmup,
        irsdk_StateParadeLaps,
        irsdk_StateRacing,
        irsdk_StateCheckered,
        irsdk_StateCoolDown
    };

    public enum irsdk_CarLeftRight
    {
        irsdk_LROff = 0,
        irsdk_LRClear,          // no cars around us.
        irsdk_LRCarLeft,        // there is a car to our left.
        irsdk_LRCarRight,       // there is a car to our right.
        irsdk_LRCarLeftRight,   // there are cars on each side.
        irsdk_LR2CarsLeft,      // there are two cars to our left.
        irsdk_LR2CarsRight      // there are two cars to our right.
    };

    public enum irsdk_PitSvStatus
    {
        // status
        irsdk_PitSvNone = 0,
        irsdk_PitSvInProgress,
        irsdk_PitSvComplete,

        // errors
        irsdk_PitSvTooFarLeft = 100,
        irsdk_PitSvTooFarRight,
        irsdk_PitSvTooFarForward,
        irsdk_PitSvTooFarBack,
        irsdk_PitSvBadAngle,
        irsdk_PitSvCantFixThat,
    };

    public enum irsdk_PaceMode
    {
        irsdk_PaceModeSingleFileStart = 0,
        irsdk_PaceModeDoubleFileStart,
        irsdk_PaceModeSingleFileRestart,
        irsdk_PaceModeDoubleFileRestart,
        irsdk_PaceModeNotPacing,
    };

    public enum irsdk_TrackWetness
    {
        irsdk_TrackWetness_UNKNOWN = 0,
        irsdk_TrackWetness_Dry,
        irsdk_TrackWetness_MostlyDry,
        irsdk_TrackWetness_VeryLightlyWet,
        irsdk_TrackWetness_LightlyWet,
        irsdk_TrackWetness_ModeratelyWet,
        irsdk_TrackWetness_VeryWet,
        irsdk_TrackWetness_ExtremelyWet
    };
}
