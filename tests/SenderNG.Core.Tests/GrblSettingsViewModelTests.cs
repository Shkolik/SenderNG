
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

using System.Reactive.Subjects;
using CNC.Core.Abstractions;
using CNC.Core.Services;
using SenderNG.Modules.Fixed;
using SenderNG.Core.Localization;
using SenderNG.Core.Settings;

namespace SenderNG.Core.Tests;

public class GrblSettingsViewModelTests
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

    private sealed class FakeGrbl : IGrblService
    {
        public List<string> Sent { get; } = [];
        public ControllerCapabilities Capabilities { get; init; } = new() { NumAxes = 3 };
        public bool IsConnected { get; private set; }
        public IObservable<MachineState> State { get; } = new Subject<MachineState>();
        public Task ConnectAsync(string port) { IsConnected = true; return Task.CompletedTask; }
        public void Disconnect() => IsConnected = false;
        public void SendRealtime(byte code) { }
        public Task SendAsync(string line) { Sent.Add(line); return Task.CompletedTask; }
    }

    private static readonly LocalizationService Loc = TestLocalization.Locale;

    private static GrblSettingsViewModel CreateVm(IGrblSettingsProvider provider, IGrblService? grbl = null) =>
        new(provider, grbl ?? new FakeGrbl(), Loc);

    private static IEnumerable<GrblSettingItem> AllSettings(IEnumerable<object> nodes)
    {
        foreach (var node in nodes)
        {
            switch (node)
            {
                case GrblSettingItem setting:
                    yield return setting;
                    break;
                case GrblSettingGroupItem group:
                    foreach (var s in AllSettings(group.Children))
                        yield return s;
                    break;
            }
        }
    }

    private static GrblSettingItem Find(IEnumerable<object> nodes, int id) =>
        AllSettings(nodes).Single(s => s.Id == id);

    /// <summary>A standalone setting for exercising Validate() without a loaded tree.</summary>
    private static GrblSettingItem MakeItem(GrblSettingType type, string value,
        double? min = null, double? max = null, string format = "") =>
        new(new GrblSetting
        {
            Id = 900, Name = "Synthetic", DataType = type, Value = value,
            Min = min, Max = max, Format = format,
        }, numAxes: 3);

    [Fact]
    public async Task Load_builds_the_tree_from_the_catalog_fallback()
    {
        var vm = CreateVm(new StubProvider { ValueLines = ["$0=10", "$10=1", "$100=250.000"] });
        await vm.LoadAsync();

        var groups = vm.Groups.OfType<GrblSettingGroupItem>().ToList();
        Assert.NotEmpty(groups);

        // $0 under Stepper, $10 under General, $100 under Axis > X-axis.
        var stepper = groups.Single(g => g.Name == "Stepper");
        Assert.Equal(0, AllSettings(stepper.Children).Single().Id);

        var axis = groups.Single(g => g.Name == "Axis");
        var xAxis = axis.Children.OfType<GrblSettingGroupItem>().Single(g => g.Name == "X-axis");
        Assert.Equal(100, AllSettings(xAxis.Children).Single().Id);

        Assert.Contains("Loaded 3 settings", vm.Status);
    }

    [Fact]
    public async Task Editing_a_value_marks_it_dirty_and_save_writes_only_dirty_settings()
    {
        var grbl = new FakeGrbl();
        var vm = CreateVm(new StubProvider { ValueLines = ["$0=10", "$1=25"] }, grbl);
        await vm.LoadAsync();

        Assert.False(Find(vm.Groups, 0).IsDirty);

        Find(vm.Groups, 0).Value = "11";
        Assert.True(Find(vm.Groups, 0).IsDirty);
        Assert.False(Find(vm.Groups, 1).IsDirty);

        await vm.SaveAsync();

        Assert.Equal(["$0=11"], grbl.Sent);
        Assert.False(Find(vm.Groups, 0).IsDirty);

        await vm.SaveAsync();
        Assert.Equal(["$0=11"], grbl.Sent); // nothing new written
        Assert.Equal("No changes", vm.Status);
    }

    [Fact]
    public async Task Search_filters_the_tree_and_drops_empty_groups()
    {
        var vm = CreateVm(new StubProvider
        {
            ValueLines = ["$0=10", "$10=1", "$30=12000", "$110=800.000"],
        });
        await vm.LoadAsync();

        vm.Search = "spindle";

        var visible = AllSettings(vm.Groups).ToList();
        Assert.NotEmpty(visible);
        Assert.All(visible, s =>
            Assert.True(s.Name.Contains("spindle", StringComparison.OrdinalIgnoreCase)));
        // Groups without matches (Stepper, Axis) are gone from the filtered tree.
        Assert.DoesNotContain(vm.Groups.OfType<GrblSettingGroupItem>(), g => g.Name == "Stepper");

        vm.Search = string.Empty;
        Assert.Equal(4, AllSettings(vm.Groups).Count()); // full tree restored
    }

    [Fact]
    public async Task Typed_editors_are_selected_by_the_setting_data_type()
    {
        var vm = CreateVm(new StubProvider { ValueLines = ["$0=10", "$2=0", "$10=0", "$32=0"] });
        await vm.LoadAsync();

        var numeric = Find(vm.Groups, 0);
        Assert.True(numeric.ShowTextBox);
        Assert.False(numeric.IsBool);
        Assert.False(numeric.IsBits);

        var axisMask = Find(vm.Groups, 2);
        Assert.True(axisMask.IsBits);
        Assert.Equal(["X", "Y", "Z"], axisMask.Options); // axis letters from NumAxes=3
        Assert.Equal(3, axisMask.Bits.Count);

        var bitfield = Find(vm.Groups, 10);
        Assert.True(bitfield.IsBits);
        Assert.Equal("Position in machine coordinate", bitfield.Bits[0].Label);

        var radio = Find(vm.Groups, 32);
        Assert.True(radio.IsRadio);
        Assert.Equal(["Normal", "Laser mode", "Lathe mode"], radio.Options);
    }

    [Fact]
    public async Task Bit_and_bool_and_radio_editors_round_trip_through_the_raw_value()
    {
        var vm = CreateVm(new StubProvider { ValueLines = ["$2=0", "$20=0", "$32=0"] });
        await vm.LoadAsync();

        // Axis mask: toggling bit 0 (X) recomputes the raw value.
        var mask = Find(vm.Groups, 2);
        mask.Bits[0].IsChecked = true;
        Assert.Equal("1", mask.Value);
        Assert.True(mask.IsDirty);

        // Bool: checkbox flips "0"/"1".
        var boolean = Find(vm.Groups, 20);
        Assert.True(boolean.IsBool);
        boolean.IsChecked = true;
        Assert.Equal("1", boolean.Value);

        // Radio: selecting an option writes its index.
        var radio = Find(vm.Groups, 32);
        radio.SelectedIndex = 2;
        Assert.Equal("2", radio.Value);
    }

    [Fact]
    public async Task Selected_setting_uses_the_catalog_description_when_present()
    {
        var vm = CreateVm(new StubProvider { ValueLines = ["$0=10"] });
        await vm.LoadAsync();

        var setting = Find(vm.Groups, 0);
        Assert.False(string.IsNullOrWhiteSpace(setting.Description)); // from the catalog

        vm.SelectedNode = setting;
        Assert.Same(setting, vm.SelectedSetting);
    }

    [Fact]
    public async Task Load_does_not_mark_settings_dirty_or_mutate_values()
    {
        // Regression: TwoWay editor bindings pushed control defaults into the setting
        // value when the detail pane first realized (radio combo wrote -1, bit check
        // boxes flipped on), marking settings dirty right after load/selection.
        var vm = CreateVm(new StubProvider { ValueLines = ["$0=10", "$10=1", "$32=0", "$650=0"] });
        await vm.LoadAsync();

        var expected = new Dictionary<int, string> { [0] = "10", [10] = "1", [32] = "0", [650] = "0" };
        foreach (var item in AllSettings(vm.Groups))
        {
            Assert.False(item.IsDirty, $"{item.Key} ({item.Name}) marked dirty right after load");
            Assert.Equal(expected[item.Id], item.Value); // unchanged from the board dump
        }
    }

    [Fact]
    public async Task Na_placeholder_bits_are_not_shown_and_toggles_are_incremental()
    {
        // $14 "Invert control inputs" format: "N/A,Feed hold,Cycle start,Safety door,N/A,N/A,EStop"
        // — unused bits are marked N/A on the wire and must not render as checkboxes.
        var vm = CreateVm(new StubProvider { ValueLines = ["$14=72"] }); // bits 3 (Safety door) + 6 (EStop)
        await vm.LoadAsync();

        var s14 = Find(vm.Groups, 14);

        Assert.All(s14.Bits, static b => Assert.False(b.Label.Contains("N/A", StringComparison.OrdinalIgnoreCase)));
        Assert.Equal(4, s14.Bits.Count); // only the 4 real options stay
        Assert.Equal(8, s14.Bits.Single(b => b.Label == "Safety door").Mask);  // keeps original bit 3
        Assert.Equal(64, s14.Bits.Single(b => b.Label == "EStop").Mask);       // keeps original bit 6
        Assert.False(s14.IsDirty);

        // Incremental toggle flips only that bit: 72 | Feed hold(2) = 74.
        s14.Bits.Single(b => b.Label == "Feed hold").IsChecked = true;
        Assert.Equal("74", s14.Value);
        Assert.True(s14.IsDirty);
    }

    [Fact]
    public async Task Apply_methods_are_the_editor_write_path_and_mark_dirty()
    {
        var vm = CreateVm(new StubProvider { ValueLines = ["$0=10", "$20=0", "$32=0", "$2=0"] });
        await vm.LoadAsync();

        var text = Find(vm.Groups, 0);
        text.ApplyText("11.5");
        Assert.Equal("11.5", text.Value);
        Assert.True(text.IsDirty);

        var boolean = Find(vm.Groups, 20);
        boolean.ApplyBool(true);
        Assert.Equal("1", boolean.Value);

        var radio = Find(vm.Groups, 32);
        radio.ApplyRadio(2);
        Assert.Equal("2", radio.Value);

        var mask = Find(vm.Groups, 2);
        mask.Bits[0].IsChecked = true;
        Assert.Equal("1", mask.Value);
    }

    [Fact]
    public async Task Changing_selection_raises_property_changed_so_the_detail_pane_rerenders()
    {
        // Regression: SelectedSetting was a plain auto-property, so swapping it never
        // raised PropertyChanged and every SelectedSetting.* binding in the detail
        // pane kept showing the first selection.
        var vm = CreateVm(new StubProvider { ValueLines = ["$0=10", "$100=250.000"] });
        await vm.LoadAsync();

        var changes = new List<string?>();
        vm.PropertyChanged += (_, e) => changes.Add(e.PropertyName);

        vm.SelectedNode = Find(vm.Groups, 0);
        vm.SelectedNode = Find(vm.Groups, 100);

        Assert.Same(Find(vm.Groups, 100), vm.SelectedSetting);
        Assert.Contains(nameof(GrblSettingsViewModel.SelectedSetting), changes);
        Assert.Contains(nameof(GrblSettingsViewModel.HasSelection), changes);
    }

    [Fact]
    public async Task Real_board_axis_masks_are_clean_right_after_selection()
    {
        // Regression: $4/$44/$45 showed "* changed" the moment they were selected.
        // Construction is proven clean here — the stale write must come only from a
        // real user gesture (see note in GrblSettingsView.axaml.cs about IsCheckedChanged).
        var vm = CreateVm(new StubProvider { ValueLines = ["$4=15", "$44=4", "$45=3"] });
        await vm.LoadAsync();

        Assert.All(AllSettings(vm.Groups), static s => Assert.False(s.IsDirty));

        // Values must be preserved exactly as the board sent them.
        Assert.Equal("15", Find(vm.Groups, 4).Value);
        Assert.Equal("4", Find(vm.Groups, 44).Value);
        Assert.Equal("3", Find(vm.Groups, 45).Value);
    }

    [Fact]
    public async Task Redundant_bit_write_does_not_mark_an_axis_mask_dirty()
    {
        // A stale realization callback must never dirty a clean setting. Setting a bit
        // to its current masked state is a no-op (same value) and keeps the mask clean.
        var vm = CreateVm(new StubProvider { ValueLines = ["$44=4"] }); // Z (mask 4) set
        await vm.LoadAsync();

        var mask = Find(vm.Groups, 44);
        var zBit = mask.Bits.Single(b => b.Mask == 4);
        Assert.True(zBit.IsChecked);
        Assert.False(mask.IsDirty);

        // Same-state write (what a stale callback does) must not flip the value or dirty it.
        zBit.IsChecked = true;
        Assert.Equal("4", mask.Value);
        Assert.False(mask.IsDirty);

        // Genuine removal of the Z axis is the only permitted dirty path.
        zBit.IsChecked = false;
        Assert.Equal("0", mask.Value);
        Assert.True(mask.IsDirty);
        mask.Accept(); // save clears the baseline against the just-wrote value ("0")
        Assert.False(mask.IsDirty);

        // A later genuine re-add reports as a new edit relative to the saved baseline.
        zBit.IsChecked = true;
        Assert.Equal("4", mask.Value);
        Assert.True(mask.IsDirty);
    }

    [Fact]
    public async Task Reload_preserves_the_selected_setting_and_expands_its_group()
    {
        var vm = CreateVm(new StubProvider { ValueLines = ["$100=250.000", "$0=10"] });
        await vm.LoadAsync();

        vm.SelectedNode = Find(vm.Groups, 100);

        await vm.LoadAsync(); // rebuilds items/roots into fresh instances

        Assert.NotNull(vm.SelectedSetting);
        Assert.Equal(100, vm.SelectedSetting.Id);
        Assert.False(vm.SelectedSetting.IsDirty);

        // The freshly built group chain (Axis > X-axis) must be expanded so the tree
        // doesn't collapse and the selection stays on screen.
        var root = vm.Groups.OfType<GrblSettingGroupItem>().Single(g => g.Name == "Axis");
        Assert.True(root.IsExpanded);
        var xAxis = root.Children.OfType<GrblSettingGroupItem>().Single(g => g.Name == "X-axis");
        Assert.True(xAxis.IsExpanded);
        Assert.Same(vm.SelectedSetting, Find(vm.Groups, 100));
    }

    [Fact]
    public async Task Reload_drops_selection_when_the_setting_is_no_longer_present()
    {
        var vm = CreateVm(new StubProvider { ValueLines = ["$100=250.000"] });
        await vm.LoadAsync();
        vm.SelectedNode = Find(vm.Groups, 100);
        Assert.NotNull(vm.SelectedSetting);

        // Reload with a board that no longer reports $100.
        var reloaded = CreateVm(new StubProvider { ValueLines = ["$0=10"] });
        await reloaded.LoadAsync();
        Assert.Null(reloaded.SelectedSetting); // cleared, not left stale
    }

    [Theory]
    [InlineData("-")]
    [InlineData("1e")]
    [InlineData("abc")]
    [InlineData("")]
    [InlineData("1.5")]
    public void Integer_rejects_partial_keystrokes_and_non_numbers(string text) =>
        Assert.Equal(GrblSettingValidationError.NotNumeric,
            MakeItem(GrblSettingType.Integer, text).Validate());

    [Theory]
    [InlineData("42")]
    [InlineData(" 42 ")]
    [InlineData("-7")]
    public void Integer_accepts_whitespace_padded_and_signed_integers(string text) =>
        Assert.Equal(GrblSettingValidationError.None,
            MakeItem(GrblSettingType.Integer, text).Validate());

    [Theory]
    [InlineData("0", false)]
    [InlineData("101", false)]
    [InlineData("1", true)]
    [InlineData("100", true)]
    public void Numeric_range_rejects_out_of_bounds_and_accepts_the_boundaries(string text, bool valid)
    {
        var error = MakeItem(GrblSettingType.Integer, text, min: 1, max: 100).Validate();
        Assert.Equal(valid ? GrblSettingValidationError.None : GrblSettingValidationError.OutOfRange, error);
    }

    [Theory]
    [InlineData("-")]
    [InlineData("1e")]
    [InlineData("abc")]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("-Infinity")]
    public void Float_rejects_partial_keystrokes_and_non_finite_values(string text) =>
        Assert.Equal(GrblSettingValidationError.NotNumeric,
            MakeItem(GrblSettingType.Float, text).Validate());

    [Theory]
    [InlineData("0.5")]
    [InlineData("250.000")]
    [InlineData("1e3")]
    public void Float_accepts_decimals_and_exponent_forms(string text) =>
        Assert.Equal(GrblSettingValidationError.None,
            MakeItem(GrblSettingType.Float, text, min: 0.001, max: 10000).Validate());

    [Theory]
    [InlineData("192.168.1.1", true)]
    [InlineData("0.0.0.0", true)]
    [InlineData("255.255.255.255", true)]
    [InlineData(" 10.0.0.1 ", true)]
    [InlineData("192.168.1.256", false)]
    [InlineData("1.2.3.4.5", false)]
    [InlineData("192.", false)]
    [InlineData("", false)]
    public void Ip4_requires_four_octets_in_range(string text, bool valid)
    {
        var error = MakeItem(GrblSettingType.Ip4, text).Validate();
        Assert.Equal(valid ? GrblSettingValidationError.None : GrblSettingValidationError.MalformedIp, error);
    }

    [Theory]
    [InlineData(GrblSettingType.Text, "abc")]
    [InlineData(GrblSettingType.Text, "")]
    [InlineData(GrblSettingType.Text, "-")]
    [InlineData(GrblSettingType.Text, "1e")]
    [InlineData(GrblSettingType.Password, "")]
    [InlineData(GrblSettingType.Password, "p@ss w0rd")]
    [InlineData(GrblSettingType.Bool, "1")]
    [InlineData(GrblSettingType.RadioButtons, "0")]
    public void Free_form_and_non_text_types_are_never_rejected(GrblSettingType type, string text) =>
        Assert.Equal(GrblSettingValidationError.None, MakeItem(type, text, format: "a,b,c").Validate());

    [Fact]
    public void A_bitfield_with_no_options_is_validated_as_an_integer_mask()
    {
        // Empty Format => SplitOptions returns [] => Bits empty => IsBits false, so the
        // view renders it as a text box holding a raw integer mask.
        Assert.Equal(GrblSettingValidationError.NotNumeric,
            MakeItem(GrblSettingType.Bitfield, "abc", format: "").Validate());
        Assert.Equal(GrblSettingValidationError.None,
            MakeItem(GrblSettingType.Bitfield, "7", format: "").Validate());

        // With options present it uses the checkbox editor and is never validated.
        Assert.Equal(GrblSettingValidationError.None,
            MakeItem(GrblSettingType.Bitfield, "abc", format: "0,1,2").Validate());
    }

    // $ES wire format:
    // [SETTING:<id>|<groupId>|<name>|<unit>|<datatype>|<format>|<min>|<max>|<reboot>|<allowNull>]
    private const string IntBounded = "[SETTING:0|35|Rapid rate|mm/min|5|#0|1|100|0|0]";
    private const string FloatBounded = "[SETTING:1|35|Feed rate|mm/min|6|#0.0|0.001|10000|0|0]";
    private const string MinOnly = "[SETTING:0|35|Step pulse time|us|5|#0|1||0|0]";
    private const string MaxOnly = "[SETTING:1|35|Feed rate|mm/min|6|#0.0||10000|0|0]";

    private static GrblSettingsViewModel CreateTypedVm(FakeGrbl grbl, params string[] detailLines) =>
        CreateVm(new StubProvider
        {
            ValueLines = ["$0=10", "$1=25"],
            DetailLines = detailLines,
        }, grbl);

    [Fact]
    public async Task An_invalid_value_blocks_the_whole_save_and_sends_nothing()
    {
        var grbl = new FakeGrbl();
        var vm = CreateTypedVm(grbl, IntBounded, FloatBounded);
        await vm.LoadAsync();

        // $0 is VALID and sorts first; $1 is invalid. A single-pass implementation would
        // already have written $0 by the time it reached $1 - this is the test that
        // distinguishes the two designs.
        Find(vm.Groups, 0).Value = "50";
        Find(vm.Groups, 1).Value = "1e";

        await vm.SaveAsync();

        Assert.Empty(grbl.Sent);
    }

    [Fact]
    public async Task A_blocked_save_leaves_every_setting_dirty_and_accepts_nothing()
    {
        var grbl = new FakeGrbl();
        var vm = CreateTypedVm(grbl, IntBounded, FloatBounded);
        await vm.LoadAsync();

        Find(vm.Groups, 0).Value = "50";
        Find(vm.Groups, 1).Value = "1e";

        await vm.SaveAsync();

        Assert.True(Find(vm.Groups, 0).IsDirty);
        Assert.True(Find(vm.Groups, 1).IsDirty);
    }

    [Fact]
    public async Task The_status_names_each_rejected_setting_and_its_reason()
    {
        var grbl = new FakeGrbl();
        var vm = CreateTypedVm(grbl, IntBounded, FloatBounded);
        await vm.LoadAsync();

        Find(vm.Groups, 1).Value = "1e";
        await vm.SaveAsync();

        Assert.Contains("$1", vm.Status);
        Assert.Contains("must be a number", vm.Status);
    }

    [Theory]
    [InlineData(0, "0", "must be at least 1")]         // $0 has Min only
    [InlineData(1, "20000", "must be at most 10000")]  // $1 has Max only
    public async Task A_half_bounded_setting_reports_only_the_bound_it_has(int id, string text, string expected)
    {
        var grbl = new FakeGrbl();
        var vm = CreateTypedVm(grbl, MinOnly, MaxOnly);
        await vm.LoadAsync();

        Find(vm.Groups, id).Value = text;
        await vm.SaveAsync();

        Assert.Empty(grbl.Sent);
        Assert.Contains(expected, vm.Status);
    }

    [Fact]
    public async Task A_valid_save_still_writes_only_dirty_settings()
    {
        var grbl = new FakeGrbl();
        var vm = CreateTypedVm(grbl, IntBounded);
        await vm.LoadAsync();

        Find(vm.Groups, 0).Value = "50";
        await vm.SaveAsync();

        Assert.Equal(["$0=50"], grbl.Sent);
        Assert.False(Find(vm.Groups, 0).IsDirty);
    }

    [Fact]
    public async Task A_non_finite_float_is_blocked_with_the_numeric_reason()
    {
        var grbl = new FakeGrbl();
        var vm = CreateTypedVm(grbl, FloatBounded);
        await vm.LoadAsync();

        Find(vm.Groups, 1).Value = "NaN";
        await vm.SaveAsync();

        Assert.Empty(grbl.Sent);
        Assert.Contains("must be a number", vm.Status);
    }
}
