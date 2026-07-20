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
/// Pit service commands. These set/clear the pit service checkboxes and
/// only work while the driver is in the car.
/// </summary>
public interface IPitCommands
{
    /// <summary>
    /// Clears all pit service checkboxes.
    /// </summary>
    void ClearAll();

    /// <summary>
    /// Requests the windshield be cleaned, using one tear off.
    /// </summary>
    void CleanWindshield();

    /// <summary>
    /// Cancels a windshield-clean request.
    /// </summary>
    void CancelCleanWindshield();

    /// <summary>
    /// Requests fuel be added at the next pit stop.
    /// </summary>
    /// <param name="liters">amount of fuel to add in liters, or 0 to use the existing amount</param>
    /// <exception cref="ArgumentOutOfRangeException">the amount is negative</exception>
    void AddFuel(int liters = 0);

    /// <summary>
    /// Cancels a fuel request.
    /// </summary>
    void CancelFuel();

    /// <summary>
    /// Requests a tire change at the next pit stop.
    /// </summary>
    /// <param name="tire">which tire to change</param>
    /// <param name="pressureKPa">tire pressure in KPa, or 0 to use the existing pressure</param>
    /// <exception cref="ArgumentOutOfRangeException">the tire is undefined or the pressure is negative</exception>
    void ChangeTire(TireLocation tire, int pressureKPa = 0);

    /// <summary>
    /// Clears all tire change checkboxes.
    /// </summary>
    void CancelTireChanges();

    /// <summary>
    /// Requests a fast repair at the next pit stop.
    /// </summary>
    void RequestFastRepair();

    /// <summary>
    /// Cancels a fast repair request.
    /// </summary>
    void CancelFastRepair();

    /// <summary>
    /// Changes the tire compound.
    /// </summary>
    /// <param name="compoundIndex">index of the tire compound to switch to</param>
    /// <exception cref="ArgumentOutOfRangeException">the compound index is negative</exception>
    void ChangeTireCompound(int compoundIndex);
}

/// <inheritdoc cref="IPitCommands" />
public sealed class PitCommands : IPitCommands
{
    readonly IBroadcastMessageSender _sender;

    internal PitCommands(IBroadcastMessageSender sender) => _sender = sender;

    /// <inheritdoc />
    public void ClearAll()
        => Send(PitCommandMode.Clear);

    /// <inheritdoc />
    public void CleanWindshield()
        => Send(PitCommandMode.CleanWindshield);

    /// <inheritdoc />
    public void CancelCleanWindshield()
        => Send(PitCommandMode.ClearWindshield);

    /// <inheritdoc />
    public void AddFuel(int liters = 0)
    {
        BroadcastValidation.NonNegative(liters, nameof(liters));
        Send(PitCommandMode.Fuel, liters);
    }

    /// <inheritdoc />
    public void CancelFuel()
        => Send(PitCommandMode.ClearFuel);

    /// <inheritdoc />
    public void ChangeTire(TireLocation tire, int pressureKPa = 0)
    {
        BroadcastValidation.NonNegative(pressureKPa, nameof(pressureKPa));
        var mode = tire switch
        {
            TireLocation.LeftFront => PitCommandMode.LeftFrontTire,
            TireLocation.RightFront => PitCommandMode.RightFrontTire,
            TireLocation.LeftRear => PitCommandMode.LeftRearTire,
            TireLocation.RightRear => PitCommandMode.RightRearTire,
            _ => throw new ArgumentOutOfRangeException(nameof(tire), tire, "unknown tire location")
        };
        Send(mode, pressureKPa);
    }

    /// <inheritdoc />
    public void CancelTireChanges()
        => Send(PitCommandMode.ClearTires);

    /// <inheritdoc />
    public void RequestFastRepair()
        => Send(PitCommandMode.FastRepair);

    /// <inheritdoc />
    public void CancelFastRepair()
        => Send(PitCommandMode.ClearFastRepair);

    /// <inheritdoc />
    public void ChangeTireCompound(int compoundIndex)
    {
        BroadcastValidation.NonNegative(compoundIndex, nameof(compoundIndex));
        Send(PitCommandMode.TireCompound, compoundIndex);
    }

    void Send(PitCommandMode mode, int parameter = 0)
        => _sender.Send(BroadcastMessageType.PitCommand, (int)mode, parameter);
}
