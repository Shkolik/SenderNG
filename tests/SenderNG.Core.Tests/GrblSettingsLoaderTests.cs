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

    private static readonly LocalizationService Loc = LocalizationService.CreateEmbedded();

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
