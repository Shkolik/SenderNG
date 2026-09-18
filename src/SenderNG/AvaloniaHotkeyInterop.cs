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

/// <summary>Bridges Avalonia key events to Core's gesture grammar without leaking
/// Avalonia types into Core.</summary>
internal static class AvaloniaHotkeyInterop
{
    public static readonly Key[] ModifierKeys =
    [
        Key.LeftCtrl, Key.RightCtrl, Key.LeftAlt, Key.RightAlt,
        Key.LeftShift, Key.RightShift, Key.LWin, Key.RWin,
    ];

    public static bool IsModifierKey(Key key) => ModifierKeys.Contains(key);

    public static HotkeyModifiers Map(KeyModifiers modifiers)
    {
        var result = HotkeyModifiers.None;
        if (modifiers.HasFlag(KeyModifiers.Control)) result |= HotkeyModifiers.Ctrl;
        if (modifiers.HasFlag(KeyModifiers.Alt)) result |= HotkeyModifiers.Alt;
        if (modifiers.HasFlag(KeyModifiers.Shift)) result |= HotkeyModifiers.Shift;
        if (modifiers.HasFlag(KeyModifiers.Meta)) result |= HotkeyModifiers.Win;
        return result;
    }

    public static bool TryGesture(Key key, KeyModifiers modifiers, out string? gesture)
    {
        gesture = null;
        return !IsModifierKey(key) && HotkeyGrammar.TryBuild(key.ToString(), Map(modifiers), out gesture);
    }
}