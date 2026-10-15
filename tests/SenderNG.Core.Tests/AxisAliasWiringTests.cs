/*
 * AxisAliasWiringTests.cs - the CAM letter alias, end to end
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
/// The profile's CAM letter alias (<see cref="MachineAxis.Alias"/>), which exists
/// because a post-processor may name an axis the board names differently. T1
/// stored it and T2 resolved it with an empty map, so before this wiring the
/// feature was dead code: a stored alias that nothing read.
/// </summary>
public class AxisAliasWiringTests
{
    private static MachineProfile FoamWithAlias(char? alias)
    {
        var type = MachineTypeCatalog.Resolve(MachineTypes.Foam);
        var profile = new MachineProfile { Id = "p", Name = "P", MachineTypeId = MachineTypes.Foam };
        profile.Planes = type.CreateDefaultPlanes();
        // Plane 2's first slot is index 2 by default; alias it.
        profile.Planes[1].Axes[0].Alias = alias;
        return profile;
    }

    private static GCLinearMotion Motion(GCodeParser parser, string block)
    {
        int before = parser.Tokens.Count;
        var line = block;
        parser.ParseBlock(ref line, false);
        return parser.Tokens.Skip(before).OfType<GCLinearMotion>().Single();
    }

    private static GCodeParser Parser(string boardLetters, IReadOnlyDictionary<char, int>? aliases) =>
        new() { BoardLetters = boardLetters, AxisAliases = aliases ?? new Dictionary<char, int>() };

    [Fact]
    public void AliasMap_is_built_from_the_declared_axes()
    {
        var k = MachineKinematicsResolver.Resolve(FoamWithAlias('U'), "XYZA", 4);
        Assert.Equal(2, k.AliasMap['U']);
    }

    [Fact]
    public void AliasMap_normalises_a_lowercase_alias_and_is_empty_without_one()
    {
        var lower = MachineKinematicsResolver.Resolve(FoamWithAlias('u'), "XYZA", 4);
        Assert.Equal(2, lower.AliasMap['u']);
        Assert.Equal(2, lower.AliasMap['U']);

        var none = MachineKinematicsResolver.Resolve(FoamWithAlias(null), "XYZA", 4);
        Assert.Empty(none.AliasMap);
    }

    [Fact]
    public void AliasMap_includes_optional_axes()
    {
        var profile = FoamWithAlias(null);
        profile.OptionalAxes.Add(new MachineAxis
        {
            Index = 4,
            Kind = AxisKind.Rotary,
            Direction = AxisDirection.Length,
            RotationOrigin = [0, 0],
            Alias = 'B',
        });

        var k = MachineKinematicsResolver.Resolve(profile, "XYZA", 4);
        Assert.Equal(4, k.AliasMap['B']);
    }

    [Fact]
    public void A_program_letter_resolves_through_the_alias()
    {
        // The board has no U; the CAM writes one for plane 2's first axis. Without
        // the alias this lands on U's conventional slot (index 6) instead.
        var parser = Parser("XYZA", new Dictionary<char, int> { ['U'] = 2 });
        var motion = Motion(parser, "G1 X0 U5 F100");

        Assert.True(motion.AxisFlags.HasFlag((AxisFlags)(1 << 2)));
        Assert.Equal(5d, motion.Values[2]);
        Assert.Empty(parser.UnknownAxisLetters);
    }

    [Fact]
    public void The_boards_own_letters_still_win_over_an_alias()
    {
        // A profile that aliases a letter the board already reports can never move
        // that axis: index 2 is Z, so the alias to 3 is unreachable.
        var parser = Parser("XYZA", new Dictionary<char, int> { ['Z'] = 3 });
        var motion = Motion(parser, "G1 X0 Z5 F100");

        Assert.True(motion.AxisFlags.HasFlag((AxisFlags)(1 << 2)));
        Assert.Equal(5d, motion.Values[2]);
    }

    [Fact]
    public void A_letter_nobody_knows_keeps_its_conventional_slot_and_is_recorded()
    {
        var parser = Parser("XYZA", null);
        var motion = Motion(parser, "G1 X0 V5 F100");

        Assert.True(motion.AxisFlags.HasFlag((AxisFlags)(1 << 7)));
        Assert.Equal(5d, motion.Values[7]);
        Assert.Contains('V', parser.UnknownAxisLetters);
    }

    [Fact]
    public void Reset_takes_the_aliases_from_the_ambient_connect_state()
    {
        // The wiring: a parser built after the kinematics provider pushed the
        // profile's aliases resolves against them, because Reset() reads the same
        // ambient instance the board's letters come from.
        var previous = GrblInfo.Current;
        try
        {
            GrblInfo.Current = new GrblInfoState
            {
                AxisLetters = "XYZA",
                NumAxes = 4,
                AxisAliases = new Dictionary<char, int> { ['U'] = 2 },
            };

            var parser = new GCodeParser();          // the ctor calls Reset()
            Assert.Equal("XYZA", parser.BoardLetters);
            Assert.Equal(2, parser.AxisAliases['U']);

            var motion = Motion(parser, "G1 X0 U5 F100");
            Assert.Equal(5d, motion.Values[2]);
        }
        finally
        {
            GrblInfo.Current = previous;
        }
    }
}
