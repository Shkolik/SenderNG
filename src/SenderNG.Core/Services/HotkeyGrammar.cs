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

namespace SenderNG.Core.Services;

/// <summary>Keyboard modifier bits, mirroring Avalonia's KeyModifiers without taking a UI dependency.</summary>
[Flags]
public enum HotkeyModifiers
{
    None = 0,
    Ctrl = 1,
    Alt = 2,
    Shift = 4,
    Win = 8,
}

/// <summary>
/// Canonical gesture strings: modifiers in Ctrl/Alt/Shift/Win order, joined by '+',
/// then an Avalonia <c>Key</c> enum name (<c>Space</c>, <c>Left</c>, <c>NumPad1</c>,
/// <c>OemPlus</c>, <c>D0</c>, <c>F1</c>). A bare key (no modifiers) is valid. Core
/// keeps its own known-key name set so it never references Avalonia.
/// </summary>
public static class HotkeyGrammar
{
    public const char Separator = '+';
    public const string None = "(none)";

    private static readonly HashSet<string> ModifierNames = new(StringComparer.Ordinal)
    {
        "Ctrl", "Alt", "Shift", "Win",
    };

    private static readonly Dictionary<string, string> KnownKeys = BuildKnownKeys();

    /// <summary>Canonicalize a raw gesture; null when it is empty or malformed.</summary>
    public static string? Normalize(string? gesture)
        => TryNormalize(gesture, out var normalized) ? normalized : null;

    public static bool TryNormalize(string? gesture, out string? normalized)
    {
        normalized = null;
        if (string.IsNullOrWhiteSpace(gesture))
            return false;

        var parts = gesture.Split(Separator, StringSplitOptions.None);
        var mods = HotkeyModifiers.None;
        for (var i = 0; i < parts.Length - 1; i++)
        {
            if (!TryParseModifier(parts[i].Trim(), out var bit) || mods.HasFlag(bit))
                return false;
            mods |= bit;
        }

        var key = parts[^1].Trim();
        if (!TryCanonicalKey(key, out var canonicalKey))
            return false;

        normalized = Build(canonicalKey, mods);
        return true;
    }

    public static string Build(string keyName, HotkeyModifiers modifiers)
    {
        var parts = new List<string>(5);
        if (modifiers.HasFlag(HotkeyModifiers.Ctrl)) parts.Add("Ctrl");
        if (modifiers.HasFlag(HotkeyModifiers.Alt)) parts.Add("Alt");
        if (modifiers.HasFlag(HotkeyModifiers.Shift)) parts.Add("Shift");
        if (modifiers.HasFlag(HotkeyModifiers.Win)) parts.Add("Win");
        parts.Add(keyName);
        return string.Join(Separator, parts);
    }

    public static bool TryBuild(string? keyName, HotkeyModifiers modifiers, out string? gesture)
    {
        gesture = null;
        if (!TryCanonicalKey(keyName, out var canonicalKey))
            return false;
        gesture = Build(canonicalKey, modifiers);
        return true;
    }

    public static bool HasModifiers(string gesture) => gesture.Contains(Separator);

    public static bool IsModifierKey(string keyName) => ModifierNames.Contains(keyName);

    private static bool TryParseModifier(string name, out HotkeyModifiers bit)
    {
        switch (name.ToUpperInvariant())
        {
            case "CTRL": bit = HotkeyModifiers.Ctrl; return true;
            case "ALT": bit = HotkeyModifiers.Alt; return true;
            case "SHIFT": bit = HotkeyModifiers.Shift; return true;
            case "WIN": bit = HotkeyModifiers.Win; return true;
            default: bit = HotkeyModifiers.None; return false;
        }
    }

    private static bool TryCanonicalKey(string? name, out string canonical)
    {
        canonical = null!;
        if (name is null || !KnownKeys.TryGetValue(name, out var c))
            return false;
        canonical = c;
        return true;
    }

    private static Dictionary<string, string> BuildKnownKeys()
    {
        var keys = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in "ABCDEFGHIJKLMNOPQRSTUVWXYZ")
            keys[c.ToString()] = c.ToString();
        for (var i = 0; i <= 9; i++)
        {
            keys[$"D{i}"] = $"D{i}";
            keys[$"NumPad{i}"] = $"NumPad{i}";
        }
        for (var i = 1; i <= 24; i++)
            keys[$"F{i}"] = $"F{i}";

        foreach (var key in new[]
                 {
                     "Space", "Left", "Right", "Up", "Down", "Home", "End", "PageUp", "PageDown",
                     "Insert", "Delete", "Back", "Tab", "Return", "Enter", "Escape", "Backspace",
                     "CapsLock", "PrintScreen", "Pause", "Scroll", "NumLock",
                     "OemPlus", "OemMinus", "OemComma", "OemPeriod", "OemSemicolon", "OemQuotes",
                     "OemQuestion", "OemTilde", "OemOpenBrackets", "OemCloseBrackets", "OemPipe",
                     "OemBackslash", "Oem8", "Multiply", "Add", "Separator", "Subtract", "Decimal", "Divide",
                 })
            keys[key] = key;
        return keys;
    }
}