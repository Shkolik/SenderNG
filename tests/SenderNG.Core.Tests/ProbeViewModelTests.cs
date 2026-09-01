
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
    public async Task Tool_table_availability_reflects_controller_capabilities()
    {
        var demo = await ConnectDemoAsync();
        var vm = new ProbeViewModel(demo, _settings);

        // Demo is grblHAL Full with 4 tool slots -> tool table present.
        Assert.True(vm.HasToolTable);
        Assert.Equal([0, 1, 2, 3, 4], vm.ToolChoices.Select(t => t.Id));
        Assert.False(vm.ProbeToolTable); // 0 selected -> dynamic offset

        vm.SelectedToolNumber = 2;
        Assert.True(vm.ProbeToolTable);
    }

    [Fact]
    public async Task Tool_selector_lists_none_then_slots_with_names_and_defaults_to_none()
    {
        var demo = await ConnectDemoAsync();
        var vm = new ProbeViewModel(demo, _settings);

        // Items: None first, then T1..T4 (no names yet -> bare Tx labels).
        Assert.Equal(
            ["None", "T1", "T2", "T3", "T4"],
            vm.ToolChoices.Select(t => t.DisplayName));
        Assert.Equal(0, vm.SelectedToolNumber); // None selected by default
        Assert.Equal("None", vm.SelectedToolChoice?.DisplayName);

        // Save a name for T2 under the demo controller key (as the Offsets
        // editor would on "Set all") and the label picks it up.
        var app = _settings.Load();
        var config = OffsetsConfigStore.Load(app);
        config.ToolNamesFor("demo")[2] = "Ø3 spot drill";
        OffsetsConfigStore.Save(app, config);
        _settings.Save(app);

        Assert.Equal(
            ["None", "T1", "T2 (Ø3 spot drill)", "T3", "T4"],
            vm.ToolChoices.Select(t => t.DisplayName));
    }

    [Fact]
    public async Task Tool_selector_follows_the_visible_tools_setting()
    {
        var demo = await ConnectDemoAsync();
        var vm = new ProbeViewModel(demo, _settings);

        // Demo has 4 tool slots; default VisibleTools (8) clamps to 4.
        Assert.Equal([0, 1, 2, 3, 4], vm.ToolChoices.Select(t => t.Id));

        // App Settings -> Offsets: "Visible tools" 8 -> 2 propagates here (the
        // picker must NOT keep listing all firmware slots).
        var app = _settings.Load();
        app.Current.VisibleTools = 2;
        _settings.Save(app);

        Assert.Equal([0, 1, 2], vm.ToolChoices.Select(t => t.Id));
        Assert.Equal(0, vm.SelectedToolNumber); // None still selected

        // A selection beyond the reduced count clamps back into range.
        vm.SelectedToolNumber = 2;
        Assert.Equal(2, vm.SelectedToolNumber);
        vm.SelectedToolNumber = 5;
        Assert.Equal(2, vm.SelectedToolNumber);
        Assert.True(vm.ProbeToolTable);

        // 0 hides every slot — only "None" remains selectable.
        app = _settings.Load();
        app.Current.VisibleTools = 0;
        _settings.Save(app);
        Assert.Equal([0], vm.ToolChoices.Select(t => t.Id));
        vm.SelectedToolNumber = 1;
        Assert.Equal(0, vm.SelectedToolNumber); // clamped to None-only
        Assert.False(vm.ProbeToolTable);
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
    public async Task SelectedProfile_ignores_null_keeps_previous_while_typing_new_name()
    {
        var demo = await ConnectDemoAsync();
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
