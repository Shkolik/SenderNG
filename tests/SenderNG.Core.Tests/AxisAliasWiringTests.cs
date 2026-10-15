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
using CNC.Core.Abstractions;
using CNC.Core.Services;
using CNC.GCode;
using Xunit;

namespace SenderNG.Core.Tests;

/// <summary>
/// The profile's CAM letter alias (<see cref="MachineAxis.Alias"/>), which exists
/// because a post-processor may name an axis the board names differently. T1
/// stored it and T2 resolved it with an empty map, so before this wiring the
/// feature was dead code: a stored alias that nothing read.
/// <para>Serialized (<c>LegacyStatics</c>): the parser round trip swaps the
/// process-global <see cref="GrblInfo.Current"/> to stand in for the host's ambient
/// push, which every parser construction in the suite reads.</para>
/// </summary>
[Collection("LegacyStatics")]
public class AxisAliasWiringTests : IDisposable
{
    private readonly List<string> _dirs = new();

    public void Dispose()
    {
        foreach (var dir in _dirs)
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    private SettingsService SettingsWithAlias(char? alias)
    {
        var dir = Path.Combine(Path.GetTempPath(), "senderng-alias-wiring-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        _dirs.Add(dir);

        var settings = new SettingsService(dir);
        var profile = settings.Load();
        var type = MachineTypeCatalog.Resolve(MachineTypes.Foam);
        profile.MachineTypeId = MachineTypes.Foam;
        profile.Planes = type.CreateDefaultPlanes();
        // Plane 2's first slot is index 2 by default; alias it.
        profile.Planes[1].Axes[0].Alias = alias;
        settings.Save(profile);
        return settings;
    }

    private static MachineProfile FoamWithAlias(char? alias)
    {
        var type = MachineTypeCatalog.Resolve(MachineTypes.Foam);
        var profile = new MachineProfile { Id = "p", Name = "P", MachineTypeId = MachineTypes.Foam };
        profile.Planes = type.CreateDefaultPlanes();
        // Plane 2's first slot is index 2 by default; alias it.
        profile.Planes[1].Axes[0].Alias = alias;
        return profile;
    }

    /// <summary>Connection double: capabilities land first, then a connect/disconnect
    /// raise — the ordering the real handshake approximates and the provider assumes.</summary>
    private sealed class FakeGrbl : IGrblService
    {
        public bool IsConnected { get; private set; }
        public ControllerCapabilities Capabilities { get; set; } = ControllerCapabilities.Unknown;
        public event EventHandler? ConnectionChanged;

        public IObservable<MachineState> State => System.Reactive.Linq.Observable.Empty<MachineState>();

        public void Connect()
        {
            IsConnected = true;
            ConnectionChanged?.Invoke(this, EventArgs.Empty);
        }

        public void Disconnect()
        {
            IsConnected = false;
            ConnectionChanged?.Invoke(this, EventArgs.Empty);
        }

        public Task ConnectAsync(string port) { Connect(); return Task.CompletedTask; }
        public void SendRealtime(byte code) { }
        public Task SendAsync(string line) => Task.CompletedTask;
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
    public void Duplicate_aliases_stay_deterministic_and_last_wins()
    {
        // A duplicate is flagged as an error (invariant 8) but the profile still
        // resolves, and the map must not depend on dictionary luck: axes are
        // written in plane-then-optional order, so the last writer wins.
        var profile = FoamWithAlias('U');       // plane 2's first axis, index 2
        profile.Planes[0].Axes[0].Alias = 'U';  // plane 1's first axis, written first
        var k = MachineKinematicsResolver.Resolve(profile, "XYZA", 4);

        Assert.Contains(k.Problems, p => p.Code == KinematicsProblemCodes.AliasDuplicate);
        Assert.Equal(2, k.AliasMap['U']);
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

    /* -----------------------------------------------------------------------
     * Lifetime: the map is PROFILE state, not connection state
     * --------------------------------------------------------------------- */

    [Fact]
    public void The_profiles_aliases_reach_the_ambient_state_before_any_connect()
    {
        // The startup push: HostSetup writes the first resolution's AliasMap into the
        // ambient state, and a program loaded offline cannot re-derive it — the load gate
        // only constructs a GCodeParser, which reads that ambient state. Without a
        // controller the reported count is 0, so the map can only come from the stored
        // profile.
        var settings = SettingsWithAlias('U');
        var grbl = new FakeGrbl();
        using var provider = new MachineKinematicsProvider(settings, grbl);

        Assert.Equal(0, provider.Current.ReportedAxes);
        Assert.Equal(2, provider.Current.AliasMap['U']);

        var previous = GrblInfo.Current;
        try
        {
            // Exactly what HostSetup's startup PushAliases() leaves behind.
            GrblInfo.Current = new GrblInfoState { AxisAliases = provider.Current.AliasMap };

            var parser = new GCodeParser();          // the ctor calls Reset()

            Assert.Equal(2, parser.AxisAliases['U']);
        }
        finally
        {
            GrblInfo.Current = previous;
        }
    }

    [Fact]
    public void The_map_follows_the_active_profile_so_nothing_stale_can_linger()
    {
        // The anti-leakage property the brief's "empty after a disconnect" criterion was
        // really after: the map is re-derived and re-pushed on every publish, so a
        // profile switch replaces it outright — an alias from another profile cannot
        // survive in the ambient state the parser reads.
        var settings = SettingsWithAlias('U');
        var grbl = new FakeGrbl();
        using var provider = new MachineKinematicsProvider(settings, grbl);
        var aliasedProfileId = provider.Current.ProfileId;
        Assert.Equal(2, provider.Current.AliasMap['U']);

        var changes = 0;
        provider.Changed += (_, _) => changes++;

        var plain = settings.CreateProfile("no-aliases", MachineTypes.Mill);
        settings.Activate(plain.Id);
        Assert.Empty(provider.Current.AliasMap);

        settings.Activate(aliasedProfileId);
        Assert.Equal(2, provider.Current.AliasMap['U']);

        Assert.True(changes >= 2, $"expected a publish per profile switch, got {changes}");
    }

    [Fact]
    public void A_disconnect_keeps_the_profiles_aliases_so_an_offline_load_still_resolves_them()
    {
        // Deliberate, and pinned: the CAM alias belongs to the PROFILE (the machine
        // setup), not to the connection. It exists *because* the controller's letters
        // differ from what the CAM writes, so making its lifetime depend on the
        // connection inverts the dependency. A program is parsed when it loads — the
        // load gate's new GCodeParser(), and the transformers — which can be while
        // disconnected; emptying the map there would re-seat the alias letter on its
        // conventional index, the wrong physical axis on a token list parsed now and run
        // later. Nothing board-derived can linger either way: the map is built from the
        // stored profile on every publish.
        var settings = SettingsWithAlias('U');
        var grbl = new FakeGrbl();
        using var provider = new MachineKinematicsProvider(settings, grbl);

        grbl.Capabilities = ControllerCapabilities.Full with { NumAxes = 4, AxisLetters = "XYZA" };
        grbl.Connect();
        Assert.Equal(4, provider.Current.ReportedAxes);
        Assert.Equal(2, provider.Current.AliasMap['U']);

        grbl.Disconnect();
        Assert.Equal(0, provider.Current.ReportedAxes);     // no controller
        Assert.Equal(2, provider.Current.AliasMap['U']);    // the profile's aliases, still resolvable

        var previous = GrblInfo.Current;
        try
        {
            // The ambient state as the app has it right after a disconnect: the letters
            // are the ones the connect wrote (RealGrblService.ApplyCapabilities does not
            // clear them) and the aliases are the push above.
            GrblInfo.Current = new GrblInfoState
            {
                AxisLetters = "XYZA",
                AxisAliases = provider.Current.AliasMap,
            };

            var parser = new GCodeParser();                 // the load gate's parser
            var motion = Motion(parser, "G1 U5 F100");

            Assert.Equal(5d, motion.Values[2]);             // the profile's index 2, not U's conventional 6
            Assert.Empty(parser.UnknownAxisLetters);
        }
        finally
        {
            GrblInfo.Current = previous;
        }
    }
}
