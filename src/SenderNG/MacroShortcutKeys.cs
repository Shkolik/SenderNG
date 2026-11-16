/*
 * MacroShortcutKeys.cs - Avalonia keys for the macro module's F1-F12 slots
 */

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

using Avalonia.Input;
using SenderNG.Core.Services;

namespace SenderNG;

/// <summary>
/// Avalonia <see cref="Key"/> values for <see cref="HotkeyGrammar.MacroShortcutGestures"/>,
/// in slot order (F1 = slot 0). The grammar owns the reserved list; this table is
/// the only place that turns it into keys, so the dispatcher, the view model and
/// the hotkey editor cannot disagree about which keys run macros.
/// </summary>
internal static class MacroShortcutKeys
{
    private static readonly Key[] Keys = HotkeyGrammar.MacroShortcutGestures
        .Select(name => Enum.Parse<Key>(name))
        .ToArray();

    /// <summary>Number of macro shortcut slots (12).</summary>
    public static int Count => Keys.Length;

    /// <summary>Slot index of <paramref name="key"/> (0 = F1), or -1 when the key
    /// is not a macro shortcut.</summary>
    public static int IndexOf(Key key) => Array.IndexOf(Keys, key);
}
