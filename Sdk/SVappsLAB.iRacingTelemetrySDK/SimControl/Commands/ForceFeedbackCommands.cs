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
/// Force feedback commands. Can be called at any time.
/// </summary>
public interface IForceFeedbackCommands
{
    /// <summary>
    /// Sets the maximum force used when mapping steering torque to direct input units.
    /// </summary>
    /// <param name="maxForceNm">maximum force, in newton-meters</param>
    /// <exception cref="ArgumentOutOfRangeException">the value is not finite or cannot be represented in signed 16.16 fixed-point format</exception>
    void SetMaxForce(float maxForceNm);
}

/// <inheritdoc cref="IForceFeedbackCommands" />
public sealed class ForceFeedbackCommands : IForceFeedbackCommands
{
    readonly IBroadcastMessageSender _sender;

    internal ForceFeedbackCommands(IBroadcastMessageSender sender) => _sender = sender;

    /// <inheritdoc />
    public void SetMaxForce(float maxForceNm)
    {
        BroadcastValidation.FixedPoint16_16(maxForceNm, nameof(maxForceNm));
        _sender.Send(BroadcastMessageType.FFBCommand, (int)FFBCommandMode.MaxForce, maxForceNm);
    }
}
