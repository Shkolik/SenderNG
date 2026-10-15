
/*
 * AxisLetterRemapTests.cs - board-driven program letter -> grbl axis index
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

using System.Collections.Generic;
using System.Linq;
using CNC.Core;
using CNC.Core.Services;
using CNC.GCode;
using Xunit;

namespace SenderNG.Core.Tests;

/// <summary>
/// The resolution rule on its own: the board's reported letters first (index =
/// position in the string), then the profile's CAM aliases, and False when
/// neither knows the letter — no guessing.
/// </summary>
public class AxisLettersTests
{
    private static readonly IReadOnlyDictionary<char, int> NoAliases = new Dictionary<char, int>();

    [Theory]
    [InlineData("XYZA", 'X', 0)]
    [InlineData("XYZA", 'Y', 1)]
    [InlineData("XYZA", 'Z', 2)]
    [InlineData("XYZA", 'A', 3)]
    public void Conventional_board_resolves_letters_to_their_own_positions(string board, char letter, int expected)
    {
        Assert.True(AxisLetters.TryResolve(board, NoAliases, letter, out int index));
        Assert.Equal(expected, index);
    }

    [Theory]
    // [AXS:4:XYUV] is the remap the old Contains('A') heuristic got wrong: the
    // program's U is the board's third axis (index 2), not the fourth.
    [InlineData('X', 0)]
    [InlineData('Y', 1)]
    [InlineData('U', 2)]
    [InlineData('V', 3)]
    public void Remapped_board_resolves_by_position_not_by_convention(char letter, int expected)
    {
        Assert.True(AxisLetters.TryResolve("XYUV", NoAliases, letter, out int index));
        Assert.Equal(expected, index);
    }

    [Theory]
    [InlineData("XYZU", 'U', 3)]        // renamed fourth axis
    [InlineData("XYZABCUVW", 'U', 6)]   // nine conventional axes
    [InlineData("XYZABCUVW", 'V', 7)]
    [InlineData("XYZABCUVW", 'W', 8)]
    public void Board_letters_cover_renamed_and_full_axis_sets(string board, char letter, int expected)
    {
        Assert.True(AxisLetters.TryResolve(board, NoAliases, letter, out int index));
        Assert.Equal(expected, index);
    }

    [Fact]
    public void Alias_resolves_a_letter_the_board_does_not_use()
    {
        // The CAM writes U for the axis this board calls A — the case the deleted
        // remapU2A heuristic was groping for.
        var aliases = new Dictionary<char, int> { ['U'] = 3 };

        Assert.True(AxisLetters.TryResolve("XYZA", aliases, 'U', out int index));
        Assert.Equal(3, index);
    }

    [Fact]
    public void Board_letters_win_over_an_alias_for_the_same_letter()
    {
        // An alias that the board's letters already contain can never be reached;
        // the board is the truth about which motor moves.
        var aliases = new Dictionary<char, int> { ['A'] = 6 };

        Assert.True(AxisLetters.TryResolve("XYZA", aliases, 'A', out int index));
        Assert.Equal(3, index);
    }

    [Fact]
    public void Alias_lookup_is_case_insensitive_with_the_exact_key_winning()
    {
        // Two axes cannot claim one alias letter through a dictionary key (the
        // kinematics model rejects that, invariant 8). A case variant can exist,
        // and the letter is upper-cased before lookup, so the exact key decides.
        var aliases = new Dictionary<char, int> { ['u'] = 6, ['U'] = 3 };

        Assert.True(AxisLetters.TryResolve("XYZA", aliases, 'U', out int exact));
        Assert.Equal(3, exact);

        Assert.True(AxisLetters.TryResolve("XYZA", aliases, 'u', out int lower));
        Assert.Equal(3, lower);

        // A lower-case-only key still resolves through the case-insensitive pass.
        Assert.True(AxisLetters.TryResolve("XYZA", new Dictionary<char, int> { ['w'] = 8 }, 'W', out int only));
        Assert.Equal(8, only);
    }

    [Theory]
    [InlineData("xyza", 'a', 3)]
    [InlineData("XYZA", 'a', 3)]
    [InlineData("XYUV", 'u', 2)]
    public void Letter_and_board_case_are_ignored(string board, char letter, int expected)
    {
        Assert.True(AxisLetters.TryResolve(board, NoAliases, letter, out int index));
        Assert.Equal(expected, index);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Missing_board_letters_resolve_nothing(string? board)
    {
        Assert.False(AxisLetters.TryResolve(board, null, 'X', out int index));
        Assert.Equal(-1, index);

        // ...but an alias can still know the letter when the board reported none.
        Assert.True(AxisLetters.TryResolve(board, new Dictionary<char, int> { ['X'] = 0 }, 'X', out int aliased));
        Assert.Equal(0, aliased);
    }

    [Theory]
    [InlineData("XYZA", 'U')]           // board has no U and no alias knows it
    [InlineData("XYZA", 'W')]
    [InlineData("XYUV", 'A')]
    [InlineData("XYUV", 'Q')]           // not an axis letter at all
    public void Unknown_letter_is_not_resolved(string board, char letter)
    {
        Assert.False(AxisLetters.TryResolve(board, NoAliases, letter, out int index));
        Assert.Equal(-1, index);
    }

    [Fact]
    public void An_index_outside_the_nine_axes_is_never_resolved()
    {
        // A board string longer than the nine grbl axes cannot name axis 9, and an
        // alias pointing off the end is unusable rather than a crash to index with.
        Assert.False(AxisLetters.TryResolve("XYZABCUVWQ", NoAliases, 'Q', out _));
        Assert.False(AxisLetters.TryResolve("XYZA", new Dictionary<char, int> { ['U'] = 9 }, 'U', out _));
        Assert.False(AxisLetters.TryResolve("XYZA", new Dictionary<char, int> { ['U'] = -1 }, 'U', out _));
    }

    [Theory]
    [InlineData('X', 0)]
    [InlineData('v', 7)]
    [InlineData('W', 8)]
    [InlineData('Q', -1)]
    [InlineData('0', -1)]
    public void Conventional_index_is_the_XYZABCUVW_position(char letter, int expected)
    {
        Assert.Equal(expected, AxisLetters.ConventionalIndex(letter));
    }
}

/// <summary>
/// The parser end of the seam: an axis word's value and axis bit land on the
/// index the board actually uses, and a letter nobody knows is preserved at its
/// conventional slot and recorded rather than dropped.
/// </summary>
public class AxisLetterRemapTests
{
    private static readonly IReadOnlyDictionary<char, int> NoAliases = new Dictionary<char, int>();

    private static GCodeParser Parser(string boardLetters, IReadOnlyDictionary<char, int>? aliases = null) =>
        new GCodeParser { BoardLetters = boardLetters, AxisAliases = aliases ?? NoAliases };

    private static GCLinearMotion Motion(GCodeParser parser, string block)
    {
        // Tokens accumulate across blocks on a reused parser; take only this one's.
        int before = parser.Tokens.Count;
        var line = block;
        parser.ParseBlock(ref line, false);

        return parser.Tokens.Skip(before).OfType<GCLinearMotion>().Single();
    }

    private static AxisFlags Bit(int index) => (AxisFlags)(1 << index);

    [Fact]
    public void Board_letters_come_from_the_ambient_connect_state()
    {
        // Production wiring: Reset() takes the connect's [AXS:] letters. Tests set
        // the property instead, so no test has to mutate the process-global state.
        Assert.Equal(GrblInfo.AxisLetters, new GCodeParser().BoardLetters);
    }

    [Fact]
    public void Remapped_board_lands_U_on_the_third_axis_and_keeps_V_off_it()
    {
        var parser = Parser("XYUV");
        var motion = Motion(parser, "G1 X0 U5 V6 F100");

        Assert.Equal(5d, motion.Values[2]);                     // board's U is index 2
        Assert.Equal(6d, motion.Values[3]);                     // board's V is index 3
        Assert.True(motion.AxisFlags.HasFlag(Bit(2)));
        Assert.True(motion.AxisFlags.HasFlag(Bit(3)));
        Assert.Equal(0d, motion.Values[4]);                     // nothing past a 4-axis board
        Assert.Empty(parser.UnknownAxisLetters);
    }

    [Fact]
    public void Renamed_fourth_axis_resolves_U_to_index_three()
    {
        var parser = Parser("XYZU");
        var motion = Motion(parser, "G1 X0 U5 F100");

        Assert.Equal(5d, motion.Values[3]);
        Assert.True(motion.AxisFlags.HasFlag(Bit(3)));
        Assert.Empty(parser.UnknownAxisLetters);
    }

    [Fact]
    public void Conventional_board_keeps_UVW_on_their_conventional_slots_and_reports_them()
    {
        // [AXS:4:XYZA] has no U/V/W: the values are preserved at the conventional
        // indices 6/7/8 (never dropped) and the letters are recorded so a caller can
        // say "this program is for a different machine". Surfacing that in the UI is
        // a later task. The deleted heuristic sent V to index 4 and W to index 5
        // here — only U happened to land right.
        var parser = Parser("XYZA");
        var motion = Motion(parser, "G1 X0 U5.5 V6.5 W7.5 F100");

        Assert.Equal(5.5d, motion.Values[6]);
        Assert.Equal(6.5d, motion.Values[7]);
        Assert.Equal(7.5d, motion.Values[8]);
        Assert.True(motion.AxisFlags.HasFlag(Bit(6)));
        Assert.True(motion.AxisFlags.HasFlag(Bit(7)));
        Assert.True(motion.AxisFlags.HasFlag(Bit(8)));
        Assert.Equal(['U', 'V', 'W'], parser.UnknownAxisLetters);
    }

    [Fact]
    public void Unknown_letters_are_recorded_once_each_and_cleared_by_reset()
    {
        var parser = Parser("XYZU");
        Motion(parser, "G1 X0 V5 W6 F100");
        var second = Motion(parser, "G1 Y1 V7 F100");

        Assert.Equal(['V', 'W'], parser.UnknownAxisLetters);
        Assert.Equal(7d, second.Values[7]);                     // V kept its conventional slot

        parser.Reset();
        Assert.Empty(parser.UnknownAxisLetters);
    }

    [Fact]
    public void Alias_resolves_a_post_processor_letter_the_board_names_differently()
    {
        // Board [AXS:4:XYZA]; the CAM writes U for the axis the board calls A.
        var parser = Parser("XYZA", new Dictionary<char, int> { ['U'] = 3 });
        var motion = Motion(parser, "G1 X0 U5 F100");

        Assert.Equal(5d, motion.Values[3]);
        Assert.True(motion.AxisFlags.HasFlag(Bit(3)));
        Assert.Empty(parser.UnknownAxisLetters);
    }

    [Fact]
    public void A_V_word_survives_on_a_board_whose_letters_contain_B()
    {
        // Regression: the non-remap V branch wrote gcValues.W (slot 8) while
        // setting AxisFlags.V (bit 7), so every V word was silently zeroed on
        // XYZAB / XYZABCUVW boards.
        var program = GCodeProgram.Parse("G1 X0 V5 F100\n");
        var motion = program.Tokens.OfType<GCLinearMotion>().Single();

        Assert.True(motion.AxisFlags.HasFlag(AxisFlags.V));
        Assert.Equal(5d, motion.Values[7]);
        Assert.Equal(0d, motion.Values[8]);
        Assert.False(program.HasErrors);
    }

    [Fact]
    public void A_V_word_survives_on_an_explicit_XYZAB_board()
    {
        var motion = Motion(Parser("XYZAB"), "G1 X0 V5 F100");

        Assert.True(motion.AxisFlags.HasFlag(AxisFlags.V));
        Assert.Equal(5d, motion.Values[7]);
        Assert.Equal(0d, motion.Values[8]);
    }

    [Fact]
    public void A_nine_axis_board_keeps_the_conventional_indices()
    {
        var parser = Parser("XYZABCUVW");
        var motion = Motion(parser, "G1 A1 B2 C3 U4 V5 W6 F100");

        Assert.Equal(1d, motion.Values[3]);
        Assert.Equal(2d, motion.Values[4]);
        Assert.Equal(3d, motion.Values[5]);
        Assert.Equal(4d, motion.Values[6]);
        Assert.Equal(5d, motion.Values[7]);
        Assert.Equal(6d, motion.Values[8]);
        Assert.Empty(parser.UnknownAxisLetters);
    }
}
