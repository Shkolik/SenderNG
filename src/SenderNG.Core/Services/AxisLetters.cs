/*
 * AxisLetters.cs - resolve a program's axis letter to a grbl axis index
 */

/*

Copyright (c) 2018-2022 Io Engineering (Terje Io)

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

using System.Collections.Generic;

namespace CNC.Core.Services;

/// <summary>
/// Resolves an axis letter written in a program to the grbl axis index it means.
/// <para>
/// The board's own reported letters (<c>[AXS:4:XYUV]</c> or the conventional
/// fallback) are the truth: the index is the letter's position in that string, so
/// a remapped board slices the axis the controller actually drives. A post
/// processor may still spell an axis with a letter the board does not use, which
/// is what the profile's CAM aliases cover. A letter neither knows is not a guess
/// to make: <see cref="TryResolve"/> returns false and the caller decides — the
/// parser keeps the word at its conventional index and records the mismatch.
/// </para>
/// <para>
/// The alias map is a parameter, never read from a profile here, so this class
/// stays independent of the machine model and testable on its own.
/// </para>
/// <para>
/// The conventional <c>XYZABCUVW</c> order has one source,
/// <see cref="MachineType.ConventionalLetters"/>; this class reads it instead of
/// keeping a second copy that could drift from it.
/// </para>
/// </summary>
public static class AxisLetters
{
    /// <summary>Number of grbl axes: valid indices are <c>0..AxisCount - 1</c>.</summary>
    public const int AxisCount = 9;

    /// <summary>
    /// The letter at <paramref name="index"/> in a letter set, or <c>'?'</c> when the
    /// set does not reach that index — an unknown letter, never a throw. The single
    /// spelling of "the controller's letter for this axis index" that
    /// <c>MachineKinematics.LetterFor</c> and the g-code token renderers share, so a
    /// set shorter than the index (a four-letter <c>[AXS:4:XYZA]</c> board against a
    /// nine-axis <c>AxisFlags.All</c> walk) cannot slice past its end.
    /// </summary>
    public static char LetterAt(string? letters, int index) =>
        letters is not null && index >= 0 && index < letters.Length ? letters[index] : '?';

    /// <summary>
    /// Resolves a program's axis letter to a grbl axis index: the board's reported
    /// letters first (index = position in the string), then the profile's CAM
    /// aliases. Case-insensitive. False when neither knows the letter.
    /// </summary>
    /// <param name="boardLetters">The controller's reported letters, in axis order
    /// (e.g. "XYZA", "XYUV"); null or empty when the board reported none.</param>
    /// <param name="aliases">Per-axis CAM aliases: program letter to grbl axis
    /// index. An alias that the board's letters already contain can never be
    /// reached — the board wins, and the kinematics model flags such a profile
    /// (invariant 8) without dropping the alias. A duplicate between two axes is
    /// one dictionary key in this map, so the last axis written wins; a
    /// case-variant collision resolves to the exact key. Deterministic either
    /// way.</param>
    /// <param name="letter">The letter as written in the program.</param>
    /// <param name="index">The resolved grbl axis index; -1 when unresolved.</param>
    public static bool TryResolve(
        string? boardLetters,
        IReadOnlyDictionary<char, int>? aliases,
        char letter,
        out int index)
    {
        index = -1;
        letter = char.ToUpperInvariant(letter);

        // The board's letters first: the position in the reported string IS the
        // grbl axis index, so a remapped board needs no heuristic.
        int at = IndexIn(boardLetters, letter);
        if (at >= 0 && at < AxisCount)
        {
            index = at;
            return true;
        }

        if (aliases is null)
            return false;

        if (aliases.TryGetValue(letter, out int alias) && alias >= 0 && alias < AxisCount)
        {
            index = alias;
            return true;
        }

        // Case-insensitive fallback for a map keyed in lower case. Only reached
        // when there is no exact key, so exactness settles any case variant.
        foreach (var pair in aliases)
        {
            if (char.ToUpperInvariant(pair.Key) == letter && pair.Value >= 0 && pair.Value < AxisCount)
            {
                index = pair.Value;
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The conventional <c>XYZABCUVW</c> index of a letter, ignoring case — the
    /// slot a program letter means when neither the board nor an alias knows it.
    /// -1 when the character is not one of the nine axis letters.
    /// </summary>
    public static int ConventionalIndex(char letter) =>
        IndexIn(MachineType.ConventionalLetters, char.ToUpperInvariant(letter));

    /// <summary>Ordinal, case-insensitive position of a letter in a letter string.</summary>
    private static int IndexIn(string? letters, char upper)
    {
        if (string.IsNullOrEmpty(letters))
            return -1;

        for (int i = 0; i < letters.Length; i++)
        {
            if (char.ToUpperInvariant(letters[i]) == upper)
                return i;
        }

        return -1;
    }
}
