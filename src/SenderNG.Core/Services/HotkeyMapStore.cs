/*

Copyright (c) 2026 Andrew Shkolik

All rights reserved.

Redistribution and use in source and binary forms, with or without modification,
are permitted provided that the following conditions are met:

· Redistributions of source code must retain the above copyright notice, this
list of conditions and the following disclaimer.

· Redistributions in binary form must reproduce the above copyright notice, this
list of conditions and the following disclaimer in the documentation and/or
other materials provided with the distribution.

· Neither the name of the copyright holder nor the names of its contributors may
be used to endorse or promote products derived from this software without
specific prior written permission.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND
ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT OWNER OR CONTRIBUTORS BE LIABLE FOR
ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
(INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON
ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
(INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.

*/

using CNC.Core.Services;

namespace SenderNG.Core.Services;

/// <summary>
/// Reads/writes <see cref="GlobalSettings.Hotkeys"/>. Pure: callers persist via
/// <c>SettingsService.SaveGlobal</c>. Shape mirrors the JogConfigStore pattern.
/// </summary>
public static class HotkeyMapStore
{
    public const string Unbound = "(none)";

    /// <summary>Effective gesture for an action: stored value, or the default when absent.
    /// Corrupt entries are removed (set <paramref name="mutated"/>) and fall back to default.</summary>
    public static string? Read(GlobalSettings global, string actionId, string? defaultGesture, out bool mutated)
    {
        mutated = false;
        if (!global.Hotkeys.TryGetValue(actionId, out var raw))
            return defaultGesture;
        if (string.IsNullOrWhiteSpace(raw) || raw == Unbound)
            return null;
        if (HotkeyGrammar.TryNormalize(raw, out var normalized))
            return normalized;
        global.Hotkeys.Remove(actionId);
        mutated = true;
        return defaultGesture;
    }

    /// <summary>Store a gesture; null/empty writes the unbound sentinel.</summary>
    public static void Write(GlobalSettings global, string actionId, string? gesture)
        => global.Hotkeys[actionId] = string.IsNullOrWhiteSpace(gesture) ? Unbound : gesture;

    /// <summary>Remove the entry so the action falls back to its default.</summary>
    public static void Remove(GlobalSettings global, string actionId) => global.Hotkeys.Remove(actionId);

    /// <summary>Drop the whole user keymap (all defaults restored).</summary>
    public static void Clear(GlobalSettings global) => global.Hotkeys.Clear();
}