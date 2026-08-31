
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

using CNC.Core.Abstractions;
using SenderNG.Core.Localization;
using SenderNG.Core.Settings;

namespace SenderNG.Core.Tests;

public class GrblSettingsLoaderTests
{
    private sealed class StubProvider : IGrblSettingsProvider
    {
        public IReadOnlyList<string> ValueLines { get; init; } = [];
        public IReadOnlyList<string> DetailLines { get; init; } = [];
        public IReadOnlyList<string> GroupLines { get; init; } = [];

        public Task<IReadOnlyList<string>> GetSettingLinesAsync() => Task.FromResult(ValueLines);
        public Task<IReadOnlyList<string>> GetSettingDetailLinesAsync() => Task.FromResult(DetailLines);
        public Task<IReadOnlyList<string>> GetSettingGroupLinesAsync() => Task.FromResult(GroupLines);
        public Task<string?> GetSettingDescriptionAsync(int id) => Task.FromResult<string?>(null);
    }

    private static readonly LocalizationService Loc = TestLocalization.Locale;

    [Fact]
    public async Task Classic_grbl_gets_names_types_and_groups_from_the_english_catalog()
    {
        // Classic grbl answers only $$ — no $ES/$EG. Names, types, min/max and the
        // group tree must come from the embedded en.json so the widget still shows
        // the full self-documented tree.
        var provider = new StubProvider
        {
            ValueLines = ["$0=10", "$10=1", "$100=250.000", "$130=200.000", "$999=42"],
        };

        var snapshot = await new GrblSettingsLoader(provider, Loc).LoadAsync();

        Assert.False(snapshot.MetadataFromController);
        Assert.Equal(5, snapshot.Settings.Count);

        var s0 = snapshot.Settings.Single(s => s.Id == 0);
        Assert.Equal("Step pulse time", s0.Name);
        Assert.Equal(GrblSettingType.Float, s0.DataType);
        Assert.Equal("10", s0.Value);

        var s100 = snapshot.Settings.Single(s => s.Id == 100);
        Assert.Equal("X-axis travel resolution", s100.Name);

        // A setting unknown to both the wire and the catalog is still shown (key as name).
        var unknown = snapshot.Settings.Single(s => s.Id == 999);
        Assert.Equal("$999", unknown.Name);

        // Tree: Stepper holds $0, General holds $10, Axis > X-axis holds $100/$130,
        // unknown $999 lands in the synthetic Ungrouped node. Empty groups (e.g.
        // Coolant, Spindle, Homing) are dropped.
        var ids = snapshot.TopGroups.Select(static g => g.Id).ToList();
        Assert.Equal([-1, 1, 35, 42], ids);

        var stepper = snapshot.TopGroups.Single(g => g.Id == 35);
        Assert.Equal([0], stepper.Settings.Select(static s => s.Id));

        var general = snapshot.TopGroups.Single(g => g.Id == 1);
        Assert.Equal([10], general.Settings.Select(static s => s.Id));

        var axis = snapshot.TopGroups.Single(g => g.Id == 42);
        var xAxis = Assert.Single(axis.Children);
        Assert.Equal(43, xAxis.Id);
        Assert.Equal([100, 130], xAxis.Settings.Select(static s => s.Id));

        var ungrouped = snapshot.TopGroups.Single(g => g.Id == -1);
        Assert.Equal([999], ungrouped.Settings.Select(static s => s.Id));
    }

    [Fact]
    public async Task Garbage_group_lines_fall_back_to_the_catalog_so_classic_settings_stay_grouped()
    {
        // Classic grbl doesn't understand $EG/$ES and can reply "error:NN". That
        // garbage must not be treated as a (empty but real) group tree — every setting
        // would collapse into "Ungrouped". The loader should drop it and use the catalog
        // hierarchy instead.
        var provider = new StubProvider
        {
            ValueLines = ["$0=10", "$100=250.000"],
            GroupLines = ["error:22"],     // classic's reply to the unsupported $EG
            DetailLines = ["error:22"],    // ...and to $ES
        };

        var snapshot = await new GrblSettingsLoader(provider, Loc).LoadAsync();

        Assert.False(snapshot.MetadataFromController);
        Assert.DoesNotContain(snapshot.TopGroups, static g => g.Name == "Ungrouped");

        var stepper = snapshot.TopGroups.Single(g => g.Id == 35);
        Assert.Contains(snapshot.Settings.Single(s => s.Id == 0), stepper.Settings);
        var xAxis = snapshot.TopGroups.Single(g => g.Id == 42).Children.Single(g => g.Id == 43);
        Assert.Contains(snapshot.Settings.Single(s => s.Id == 100), xAxis.Settings);
    }

    [Fact]
    public async Task GrblHAL_metadata_wins_and_unknown_ids_fall_back_to_the_catalog()
    {
        // grblHAL ENUMS board: $ES metadata + $EG groups on the wire. A value whose
        // id has no $ES line ($100) still resolves through the catalog.
        var provider = new StubProvider
        {
            ValueLines = ["$0=5.0", "$1=25", "$100=250.000"],
            DetailLines =
            [
                "[SETTING:0|35|Step pulse time|microseconds|6|#0.0|1.0||0|0]",
                "[SETTING:1|35|Step idle delay|milliseconds|5|####0||65535|0|0]",
            ],
            GroupLines =
            [
                "[SETTINGGROUP:35|0|Stepper]",
                "[SETTINGGROUP:42|0|Axis]",
                "[SETTINGGROUP:43|42|X-axis]",
            ],
        };

        var snapshot = await new GrblSettingsLoader(provider, Loc).LoadAsync();

        Assert.True(snapshot.MetadataFromController);

        var s0 = snapshot.Settings.Single(s => s.Id == 0);
        Assert.Equal("Step pulse time", s0.Name); // from the wire, not the catalog
        Assert.Equal(1.0, s0.Min);

        var s100 = snapshot.Settings.Single(s => s.Id == 100);
        Assert.Equal("X-axis travel resolution", s100.Name); // catalog fallback

        var stepper = snapshot.TopGroups.Single(g => g.Id == 35);
        Assert.Equal([0, 1], stepper.Settings.Select(static s => s.Id));
    }

    [Fact]
    public async Task Controller_metadata_settings_get_descriptions_from_the_language_file()
    {
        // grblHAL $ES metadata has no description field, so the English catalog is the
        // source of truth for docs; the lazy $SED fetch is only a fallback for ids the
        // catalog doesn't know. The board is connected now, so on load every HAL
        // setting carries its language-file description.
        var provider = new StubProvider
        {
            ValueLines = ["$0=5.0", "$1=25", "$9999=1"],
            DetailLines =
            [
                "[SETTING:0|35|Step pulse time|microseconds|6|#0.0|1.0||0|0]",
                "[SETTING:1|35|Step idle delay|milliseconds|5|####0||65535|0|0]",
                "[SETTING:9999|1|My custom setting||0|||0|0]",
            ],
        };

        var snapshot = await new GrblSettingsLoader(provider, Loc).LoadAsync();

        var s0 = snapshot.Settings.Single(s => s.Id == 0);
        Assert.False(string.IsNullOrWhiteSpace(s0.Description)); // from the catalog

        // An id the catalog doesn't know keeps whatever the controller sent (or nothing).
        Assert.Null(snapshot.Settings.Single(s => s.Id == 9999).Description);
    }

    [Fact]
    public async Task Empty_groups_are_dropped_and_settings_are_ordered_by_id()
    {
        var provider = new StubProvider
        {
            ValueLines = ["$130=200.000", "$100=250.000", "$0=10"],
        };

        var snapshot = await new GrblSettingsLoader(provider, Loc).LoadAsync();

        // Only Stepper and Axis (with X-axis) survive; ids are sorted within groups.
        var xAxis = snapshot.TopGroups.Single(g => g.Id == 42).Children.Single();
        Assert.Equal([100, 130], xAxis.Settings.Select(static s => s.Id));
        Assert.DoesNotContain(snapshot.TopGroups, static g => g.Id is 5 or 6 or 11); // Coolant/Spindle/Homing
    }
}
