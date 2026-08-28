using System.Reactive.Subjects;
using CNC.Core.Abstractions;
using CNC.Core.Services;
using SenderNG.Avalonia.Modules.Fixed;
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

    private static readonly LocalizationService Loc = LocalizationService.CreateEmbedded();

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
}
