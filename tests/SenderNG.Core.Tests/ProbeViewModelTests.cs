using CNC.Core.Services;
using SenderNG.Avalonia.Modules.Plugins;
using SenderNG.Core.Modules.Probing;
using Xunit;

namespace SenderNG.Core.Tests;

public class ProbeViewModelTests : IDisposable
{
    private readonly string _dir;
    private readonly SettingsService _settings;

    public ProbeViewModelTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "senderng-tests-" + Guid.NewGuid().ToString("N"));
        _settings = new SettingsService(_dir);
    }

    private async Task<DemoGrblService> ConnectDemoAsync()
    {
        var demo = new DemoGrblService(_settings.Load());
        await demo.ConnectAsync("demo");
        return demo;
    }

    [Fact]
    public async Task ToolLength_start_probes_Z_and_reports_offset()
    {
        var demo = await ConnectDemoAsync();
        var vm = new ProbeViewModel(demo, _settings);

        await vm.StartAsync();

        Assert.True(vm.LastResult.Contains("G43.1Z-11.5"), $"Unexpected result: {vm.LastResult}");
    }

    [Fact]
    public async Task ToolLength_with_add_offset_writes_G10()
    {
        var demo = await ConnectDemoAsync();
        var vm = new ProbeViewModel(demo, _settings);
        vm.AddAction = true;
        vm.WorkpieceHeight = 20;
        vm.TouchPlateHeight = 1;

        await vm.StartAsync();

        Assert.True(vm.LastResult.Contains("G10L2P1Z21"), $"Unexpected result: {vm.LastResult}");
    }

    [Fact]
    public async Task Profile_add_persists_and_loads_in_new_instance()
    {
        var demo = await ConnectDemoAsync();
        var vm = new ProbeViewModel(demo, _settings);
        vm.ProbeFeedRate = 250;
        vm.NewProfileName = "fast";

        vm.ProfileAddCommand.Execute(null);

        var second = new ProbeViewModel(demo, _settings);
        Assert.Equal(2, second.Profiles.Count);
        var fast = second.Profiles.Single(p => p.Name == "fast");
        Assert.Equal(250, fast.ProbeFeedRate);
    }

    [Fact]
    public async Task Selected_profile_loads_its_values_into_working_fields()
    {
        var demo = await ConnectDemoAsync();
        var vm = new ProbeViewModel(demo, _settings);
        vm.ProbeDiameter = 6;
        vm.ProbeDistance = 25;
        vm.NewProfileName = "big";
        vm.ProfileAddCommand.Execute(null);

        var second = new ProbeViewModel(demo, _settings);
        second.SelectedProfile = second.Profiles.Single(p => p.Name == "big");

        Assert.Equal(6, second.ProbeDiameter);
        Assert.Equal(25, second.ProbeDistance);
    }

    [Fact]
    public void SelectedProfile_ignores_null_keeps_previous_while_typing_new_name()
    {
        var demo = ConnectDemoAsync().Result;
        var vm = new ProbeViewModel(demo, _settings);
        var original = vm.SelectedProfile!;

        // The editable combo nulls the selection while a typed name does not
        // match a profile; the VM must keep the previous profile so Update/Add
        // still know what is being edited.
        vm.SelectedProfile = null;
        vm.NewProfileName = "renamed";

        Assert.Same(original, vm.SelectedProfile);
        Assert.Equal("renamed", vm.NewProfileName);
    }

    [Fact]
    public async Task Profiles_file_path_comes_from_module_settings()
    {
        var customPath = Path.Combine(_dir, "custom-probing.json");
        var settings = _settings.Load();
        settings.Modules["Probing"] = new Dictionary<string, object>
        {
            ["ProfilesFile"] = customPath,
            ["EnabledTools"] = new Dictionary<string, object>(),
        };
        _settings.Save(settings);

        var demo = new DemoGrblService(settings);
        var vm = new ProbeViewModel(demo, _settings);
        vm.ProbeFeedRate = 175;
        vm.NewProfileName = "custom";
        vm.ProfileAddCommand.Execute(null);

        Assert.True(File.Exists(customPath));
        Assert.Equal(2, new ProbeViewModel(demo, _settings).Profiles.Count);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }
}
