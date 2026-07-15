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
/// In-sim chat window commands.
/// </summary>
public interface IChatCommands
{
    /// <summary>
    /// Sends one of the predefined chat macros.
    /// </summary>
    /// <param name="macroNumber">the chat macro to launch (1-15, per the official SDK)</param>
    /// <exception cref="ArgumentOutOfRangeException">the macro number is outside 1-15</exception>
    void SendMacro(int macroNumber);

    /// <summary>
    /// Opens a new chat window.
    /// </summary>
    void Open();

    /// <summary>
    /// Opens a reply to the last private chat.
    /// </summary>
    void ReplyToPrivateChat();

    /// <summary>
    /// Closes the chat window.
    /// </summary>
    void Close();
}

/// <inheritdoc cref="IChatCommands" />
public sealed class ChatCommands : IChatCommands
{
    readonly IBroadcastMessageSender _sender;

    internal ChatCommands(IBroadcastMessageSender sender) => _sender = sender;

    /// <inheritdoc />
    public void SendMacro(int macroNumber)
    {
        if (macroNumber is < 1 or > 15)
            throw new ArgumentOutOfRangeException(nameof(macroNumber), macroNumber, "Chat macro number must be between 1 and 15.");
        Send(ChatCommandMode.Macro, macroNumber);
    }

    /// <inheritdoc />
    public void Open()
        => Send(ChatCommandMode.BeginChat);

    /// <inheritdoc />
    public void ReplyToPrivateChat()
        => Send(ChatCommandMode.Reply);

    /// <inheritdoc />
    public void Close()
        => Send(ChatCommandMode.Cancel);

    void Send(ChatCommandMode mode, int subCommand = 0)
        => _sender.Send(BroadcastMessageType.ChatCommand, (int)mode, subCommand);
}
