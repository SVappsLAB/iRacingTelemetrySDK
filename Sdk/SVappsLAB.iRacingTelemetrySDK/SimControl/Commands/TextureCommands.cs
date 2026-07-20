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

namespace SVappsLAB.iRacingTelemetrySDK.SimControl;

/// <summary>
/// Car texture reload commands (useful after editing custom paint files).
/// </summary>
public interface ITextureCommands
{
    /// <summary>
    /// Reloads the textures for all cars.
    /// </summary>
    void ReloadAll();

    /// <summary>
    /// Reloads the textures for a single car.
    /// </summary>
    /// <param name="carIdx">the CarIdx of the car to reload</param>
    /// <exception cref="ArgumentOutOfRangeException">the car index is negative</exception>
    void ReloadForCar(int carIdx);
}

/// <inheritdoc cref="ITextureCommands" />
public sealed class TextureCommands : ITextureCommands
{
    readonly IBroadcastMessageSender _sender;

    internal TextureCommands(IBroadcastMessageSender sender) => _sender = sender;

    /// <inheritdoc />
    public void ReloadAll()
        => _sender.Send(BroadcastMessageType.ReloadTextures, (int)ReloadTexturesMode.All, 0);

    /// <inheritdoc />
    public void ReloadForCar(int carIdx)
    {
        BroadcastValidation.NonNegative(carIdx, nameof(carIdx));
        _sender.Send(BroadcastMessageType.ReloadTextures, (int)ReloadTexturesMode.CarIdx, carIdx);
    }
}
