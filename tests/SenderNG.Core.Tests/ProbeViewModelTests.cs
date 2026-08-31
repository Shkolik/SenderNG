using CNC.Core.Abstractions;
using CNC.Core.Services;
using SenderNG.Modules.Plugins;
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
    public void Tool_table_availability_reflects_controller_capabilities()
    {
        var demo = ConnectDemoAsync().Result;
        var vm = new ProbeViewModel(demo, _settings);

        // Demo is grblHAL Full with 4 tool slots -> tool table present.
        Assert.True(vm.HasToolTable);
        Assert.Equal([0, 1, 2, 3, 4], vm.ToolChoices);
        Assert.False(vm.ProbeToolTable); // 0 selected -> dynamic offset

        vm.SelectedToolNumber = 2;
        Assert.True(vm.ProbeToolTable);
    }

    [Fact]
    public async Task ToolLength_with_tool_stores_to_tool_table_and_applies_G43()
    {
        var demo = await ConnectDemoAsync();
        var vm = new ProbeViewModel(demo, _settings);
        vm.SelectedToolNumber = 2;

        await vm.StartAsync();

        Assert.True(
            vm.LastResult.StartsWith("Tool length offset stored to T2:", StringComparison.Ordinal),
            $"Unexpected result: {vm.LastResult}");
        Assert.Contains("G10L1P2Z-11.5", vm.LastResult);
        // The demo stores the probed Z into the tool-table slot; verify the probe
        // actually persisted it (ioSender only scaffolded this path).
        var stored = Assert.Single(demo.WorkParameters.Tools, t => t.Id == 2);
        Assert.Equal(-11.5, stored.Offsets[2], 3);
    }

    [Fact]
    public async Task ToolLength_without_tool_keeps_dynamic_G43_and_never_writes_table()
    {
        var demo = await ConnectDemoAsync();
        var vm = new ProbeViewModel(demo, _settings);
        // Default SelectedToolNumber = 0 -> dynamic G43.1, no G10 L1.
        vm.SelectedToolNumber = 0;

        await vm.StartAsync();

        Assert.Contains("G43.1Z-11.5", vm.LastResult);
        Assert.DoesNotContain("G10L1", vm.LastResult);
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

    [Fact]
    public async Task ToolLength_with_tool_requests_work_parameter_refresh()
    {
        var demo = await ConnectDemoAsync();
        var recording = new RecordingGrblService(demo);
        var vm = new ProbeViewModel(recording, _settings);
        vm.SelectedToolNumber = 2;

        Assert.Equal(0, recording.RefreshCalls);
        await vm.StartAsync();

        // After G10 L1 Pn + G43 Hn the probe re-reads $# so the Offsets tool
        // table / Work Parameters widgets update immediately on a real board
        // (the demo is already live, so the publish happens via the G10 L1 path).
        Assert.Equal(1, recording.RefreshCalls);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    /// <summary>Wraps an IGrblService, counting RefreshWorkParametersAsync calls.</summary>
    private sealed class RecordingGrblService : CNC.Core.Abstractions.IGrblService
    {
        private readonly IGrblService _inner;
        public int RefreshCalls { get; private set; }
        public RecordingGrblService(IGrblService inner) => _inner = inner;
        public bool IsConnected => _inner.IsConnected;
        public event EventHandler? ConnectionChanged { add => _inner.ConnectionChanged += value; remove => _inner.ConnectionChanged -= value; }
        public ControllerCapabilities Capabilities => _inner.Capabilities;
        public WorkParameters WorkParameters => _inner.WorkParameters;
        public IObservable<MachineState> State => _inner.State;
        public IObservable<WorkParameters> WorkParametersChanges => _inner.WorkParametersChanges;
        public Task ConnectAsync(string port) => _inner.ConnectAsync(port);
        public void Disconnect() => _inner.Disconnect();
        public void SendRealtime(byte code) => _inner.SendRealtime(code);
        public Task SendAsync(string line) => _inner.SendAsync(line);
        public Task RefreshWorkParametersAsync() { RefreshCalls++; return _inner.RefreshWorkParametersAsync(); }
    }
}
