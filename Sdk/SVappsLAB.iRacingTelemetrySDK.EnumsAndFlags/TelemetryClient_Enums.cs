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
    public enum TrackLocation
    {
        NotInWorld = -1,
        OffTrack = 0,
        InPitStall,
        // This indicates the lead in to pit road, as well as the pit road itself (where speed limits are enforced)
        // if you just want to know that your on the pit road surface look at the live value 'OnPitRoad'
        AproachingPits,
        OnTrack
    };

    public enum TrackSurface
    {
        SurfaceNotInWorld = -1,
        UndefinedMaterial = 0,

        Asphalt1Material,
        Asphalt2Material,
        Asphalt3Material,
        Asphalt4Material,
        Concrete1Material,
        Concrete2Material,
        RacingDirt1Material,
        RacingDirt2Material,
        Paint1Material,
        Paint2Material,
        Rumble1Material,
        Rumble2Material,
        Rumble3Material,
        Rumble4Material,

        Grass1Material,
        Grass2Material,
        Grass3Material,
        Grass4Material,
        Dirt1Material,
        Dirt2Material,
        Dirt3Material,
        Dirt4Material,
        SandMaterial,
        Gravel1Material,
        Gravel2Material,
        GrasscreteMaterial,
        AstroturfMaterial,
    };

    public enum SessionState
    {
        Invalid = 0,
        GetInCar,
        Warmup,
        ParadeLaps,
        Racing,
        Checkered,
        CoolDown
    };

    public enum CarLeftRight
    {
        Off = 0,
        Clear,          // no cars around us.
        CarLeft,        // there is a car to our left.
        CarRight,       // there is a car to our right.
        CarLeftRight,   // there are cars on each side.
        TwoCarsLeft,    // there are two cars to our left.
        TwoCarsRight    // there are two cars to our right.
    };

    public enum PitServiceStatus
    {
        // status
        None = 0,
        InProgress,
        Complete,

        // errors
        TooFarLeft = 100,
        TooFarRight,
        TooFarForward,
        TooFarBack,
        BadAngle,
        CantFixThat,
    };

    public enum PaceMode
    {
        SingleFileStart = 0,
        DoubleFileStart,
        SingleFileRestart,
        DoubleFileRestart,
        NotPacing,
    };

    public enum TrackWetness
    {
        Unknown = 0,
        Dry,
        MostlyDry,
        VeryLightlyWet,
        LightlyWet,
        ModeratelyWet,
        VeryWet,
        ExtremelyWet
    };
}
