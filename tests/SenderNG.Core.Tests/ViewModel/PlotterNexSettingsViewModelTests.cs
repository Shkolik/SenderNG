using CNC.Core.Services;
using SenderNG.Modules.Plotter.Nex.Settings;

namespace SenderNG.Core.Tests;

public class PlotterNexSettingsViewModelTests : IDisposable
{
    private readonly string _dir;

    public PlotterNexSettingsViewModelTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "plotter-nex-settings-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); }
        catch { /* best-effort */ }
    }

    private PlotterNexSettingsViewModel MakeVm(string typeId)
    {
        var settingsService = new SettingsService(_dir);
        var profile = settingsService.Load();
        profile.MachineTypeId = typeId;
        settingsService.Save(profile);
        return new PlotterNexSettingsViewModel(settingsService);
    }

    [Fact]
    public void Foam_shows_wire_length_and_two_plane_rows_two_slots_each()
    {
        var vm = MakeVm(MachineTypes.Foam);

        Assert.True(vm.ShowWireLength);
        Assert.Equal(100, vm.WireLength);
        Assert.Equal(2, vm.PlaneRows.Count);
        Assert.Equal(2, vm.PlaneRows[0].Slots.Count);
        Assert.Equal(2, vm.PlaneRows[1].Slots.Count);
    }

    [Fact]
    public void Mill_hides_wire_length_and_shows_one_plane_three_slots()
    {
        var vm = MakeVm(MachineTypes.Mill);

        Assert.False(vm.ShowWireLength);
        var row = Assert.Single(vm.PlaneRows);
        Assert.Equal(3, row.Slots.Count);
    }

    [Fact]
    public void Wire_length_edit_recenters_both_foam_origins_and_persists()
    {
        var vm = MakeVm(MachineTypes.Foam);

        vm.WireLength = 200;

        Assert.Equal(-100, vm.PlaneRows[0].OriginX);
        Assert.Equal(100, vm.PlaneRows[1].OriginX);

        // Persisted through the profile's plotter-nex section.
        var settingsService = new SettingsService(_dir);
        var loaded = PlotterNexConfig.Load(settingsService.Load(),
            MachineTypeCatalog.Resolve(MachineTypes.Foam));
        Assert.Equal(200, loaded.WireLength);
        Assert.Equal(-100.0, loaded.Planes[0].Origin[0]);
        Assert.Equal(100.0, loaded.Planes[1].Origin[0]);
    }

    [Fact]
    public void Per_plane_world_axis_edit_persists_and_stays_distinct()
    {
        var vm = MakeVm(MachineTypes.Foam);

        vm.PlaneRows[0].Slots[0].Axis = 2; // first slot X -> world Z

        Assert.Equal(2, vm.PlaneRows[0].Slots[0].Axis);
        Assert.NotEqual(vm.PlaneRows[0].Slots[0].Axis, vm.PlaneRows[0].Slots[1].Axis);

        var settingsService = new SettingsService(_dir);
        var loaded = PlotterNexConfig.Load(settingsService.Load(),
            MachineTypeCatalog.Resolve(MachineTypes.Foam));
        Assert.Equal(2, loaded.Planes[0].WorldAxes[0]);
    }
}
