using System.Text;
using CNC.Core.Services;
using SenderNG.Core.Localization;
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
    public void Labels_fall_back_to_english_without_a_localization_service()
    {
        var vm = MakeVm(MachineTypes.Foam);

        Assert.Equal("Wire length (tower distance)", vm.WireLengthLabel);
        Assert.Equal("World axes", vm.PlaneRows[0].WorldAxesLabel);
        Assert.Equal("Origin", vm.PlaneRows[0].OriginLabel);
        Assert.Equal("Plane 1", vm.PlaneRows[0].Title);
    }

    [Fact]
    public void Labels_resolve_through_the_module_locale_catalog()
    {
        var settingsService = new SettingsService(_dir);
        var profile = settingsService.Load();
        profile.MachineTypeId = MachineTypes.Foam;
        settingsService.Save(profile);

        const string en = """
            {"strings":{
              "module.plotter-nex.settings.wireLength":"Wire length (tower distance)",
              "module.plotter-nex.settings.worldAxes":"World axes",
              "module.plotter-nex.settings.origin":"Origin",
              "module.plotter-nex.settings.plane":"Plane {0}"}}
            """;
        const string ru = """
            {"strings":{
              "module.plotter-nex.settings.wireLength":"Длина проволоки (расстояние между стойками)",
              "module.plotter-nex.settings.worldAxes":"Оси мира",
              "module.plotter-nex.settings.origin":"Начало координат",
              "module.plotter-nex.settings.plane":"Плоскость {0}"}}
            """;
        using var enStream = new MemoryStream(Encoding.UTF8.GetBytes(en));
        using var ruStream = new MemoryStream(Encoding.UTF8.GetBytes(ru));
        var loc = new LocalizationService(enStream, "ru", ruStream);
        var vm = new PlotterNexSettingsViewModel(settingsService, loc);

        Assert.Equal("Длина проволоки (расстояние между стойками)", vm.WireLengthLabel);
        Assert.Equal("Оси мира", vm.PlaneRows[0].WorldAxesLabel);
        Assert.Equal("Начало координат", vm.PlaneRows[0].OriginLabel);
        Assert.Equal("Плоскость 1", vm.PlaneRows[0].Title);
    }

    [Fact]
    public void Labels_raise_property_changed_and_reresolve_when_locale_switches_live()
    {
        var settingsService = new SettingsService(_dir);
        var profile = settingsService.Load();
        profile.MachineTypeId = MachineTypes.Foam;
        settingsService.Save(profile);

        // SetLocale() is a no-op for stream-built services, so the live-switch
        // behavior must be exercised against a directory-backed service.
        var locDir = Path.Combine(_dir, "loc");
        Directory.CreateDirectory(locDir);
        File.WriteAllText(Path.Combine(locDir, "en.json"), """
            {"strings":{
              "module.plotter-nex.settings.wireLength":"Wire length (tower distance)",
              "module.plotter-nex.settings.view":"View",
              "module.plotter-nex.settings.showOrigin":"Show origin",
              "module.plotter-nex.settings.showEnvelope":"Show envelope",
              "module.plotter-nex.settings.showRunState":"Show run state",
              "module.plotter-nex.settings.showGrid":"Show grid",
              "module.plotter-nex.settings.antiAliasing":"Rendering / Anti-aliasing",
              "module.plotter-nex.settings.aaMode":"Mode",
              "module.plotter-nex.settings.aaQuality":"Quality"}}
            """);
        File.WriteAllText(Path.Combine(locDir, "ru.json"), """
            {"strings":{
              "module.plotter-nex.settings.wireLength":"Длина проволоки (расстояние между стойками)",
              "module.plotter-nex.settings.view":"Вид",
              "module.plotter-nex.settings.showOrigin":"Показать начало координат",
              "module.plotter-nex.settings.showEnvelope":"Показать конверт",
              "module.plotter-nex.settings.showRunState":"Показать состояние выполнения",
              "module.plotter-nex.settings.showGrid":"Показать сетку",
              "module.plotter-nex.settings.antiAliasing":"Рендеринг / Сглаживание",
              "module.plotter-nex.settings.aaMode":"Режим",
              "module.plotter-nex.settings.aaQuality":"Качество"}}
            """);
        var loc = LocalizationService.LoadFromDirectory(locDir, "en");
        var vm = new PlotterNexSettingsViewModel(settingsService, loc);

        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        loc.SetLocale("ru");

        foreach (var name in new[]
        {
            nameof(PlotterNexSettingsViewModel.WireLengthLabel),
            nameof(PlotterNexSettingsViewModel.ViewLabel),
            nameof(PlotterNexSettingsViewModel.ShowOriginLabel),
            nameof(PlotterNexSettingsViewModel.ShowEnvelopeLabel),
            nameof(PlotterNexSettingsViewModel.ShowRunStateLabel),
            nameof(PlotterNexSettingsViewModel.ShowGridLabel),
            nameof(PlotterNexSettingsViewModel.AntiAliasingLabel),
            nameof(PlotterNexSettingsViewModel.AaModeLabel),
            nameof(PlotterNexSettingsViewModel.AaQualityLabel),
        })
            Assert.Contains(name, raised);

        Assert.Equal("Длина проволоки (расстояние между стойками)", vm.WireLengthLabel);
        Assert.Equal("Вид", vm.ViewLabel);
        Assert.Equal("Показать начало координат", vm.ShowOriginLabel);
        Assert.Equal("Показать конверт", vm.ShowEnvelopeLabel);
        Assert.Equal("Показать состояние выполнения", vm.ShowRunStateLabel);
        Assert.Equal("Показать сетку", vm.ShowGridLabel);
        Assert.Equal("Рендеринг / Сглаживание", vm.AntiAliasingLabel);
        Assert.Equal("Режим", vm.AaModeLabel);
        Assert.Equal("Качество", vm.AaQualityLabel);
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

    [Fact]
    public void Wire_length_after_axis_edit_centers_towers_on_one_world_axis()
    {
        var vm = MakeVm(MachineTypes.Foam);

        // Divergent remaps: plane0 leaves Y unmapped, plane1 leaves Z unmapped.
        vm.PlaneRows[0].Slots[0].Axis = 0; // plane0 -> [0,2]
        vm.PlaneRows[1].Slots[1].Axis = 0; // plane1 -> [1,0]

        vm.WireLength = 200;

        Assert.Equal(-100, vm.PlaneRows[0].OriginX);
        Assert.Equal(100, vm.PlaneRows[1].OriginX);
        Assert.Equal(0.0, vm.PlaneRows[0].OriginY);
        Assert.Equal(0.0, vm.PlaneRows[1].OriginY);
    }

    [Fact]
    public void View_and_AA_toggles_read_defaults_and_round_trip_through_save()
    {
        var vm = MakeVm(MachineTypes.Foam);

        // Defaults equal current behavior: SMAA + High, everything visible.
        Assert.True(vm.ShowOrigin);
        Assert.True(vm.ShowEnvelope);
        Assert.True(vm.ShowRunState);
        Assert.True(vm.ShowGrid);
        Assert.Equal(1, vm.AaMode);
        Assert.Equal(2, vm.AaQuality);

        vm.ShowOrigin = false;
        vm.ShowEnvelope = false;
        vm.ShowRunState = false;
        vm.ShowGrid = false;
        vm.AaMode = 2;    // FXAA
        vm.AaQuality = 3; // Ultra

        // VM getters reflect the flip immediately.
        Assert.False(vm.ShowOrigin);
        Assert.False(vm.ShowEnvelope);
        Assert.False(vm.ShowRunState);
        Assert.False(vm.ShowGrid);
        Assert.Equal(2, vm.AaMode);
        Assert.Equal(3, vm.AaQuality);

        // Persisted through the profile's plotter-nex section (VM setter -> Save -> reload).
        var settingsService = new SettingsService(_dir);
        var loaded = PlotterNexConfig.Load(settingsService.Load(),
            MachineTypeCatalog.Resolve(MachineTypes.Foam));
        Assert.False(loaded.ShowOrigin);
        Assert.False(loaded.ShowEnvelope);
        Assert.False(loaded.ShowRunState);
        Assert.False(loaded.ShowGrid);
        Assert.Equal(2, loaded.AaMode);
        Assert.Equal(3, loaded.AaQuality);
    }
}
