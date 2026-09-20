
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
using CNC.Core.ViewModels;

namespace SenderNG.Core.Tests;

public class ConnectViewModelTests
{
    private sealed class StubComPorts(string[] ports, string[]? preferred = null) : IComPortService
    {
        private readonly string[] _preferred = preferred ?? [];
        public string[] GetPorts() => ports;
        // Mimic the real service: plain names in GetPorts, device descriptions in entries.
        public IReadOnlyList<SerialPortEntry> GetPortEntries() =>
            ports.Select(p => new SerialPortEntry(p, $"{p} - USB Serial Device", _preferred.Contains(p))).ToArray();
    }

    private static MachineProfile SettingsWith(string? port = null, int baud = 115200)
    {
        var profile = new MachineProfile();
        if (port is not null)
            profile.Connection.SerialPort = port;
        profile.Connection.SerialBaudRate = baud;
        return profile;
    }

    // The Connect dialog reads app-level flags (network availability) from the
    // global tier; tests that don't touch those just pass a fresh one.
    private static GlobalSettings Global() => new();

    private static MachineProfile ProfileWithId(string id, string lastPort, int baud = 115200,
        string endpoint = "192.168.1.100:23", ConnectResetMode resetMode = ConnectResetMode.None)
    {
        var profile = new MachineProfile { Id = id, Name = id };
        profile.Connection.LastSerialPort = lastPort;
        profile.Connection.LastSerialBaudRate = baud;
        profile.Connection.SerialBaudRate = baud;
        profile.Connection.LastNetworkEndpoint = endpoint;
        profile.Connection.SerialResetMode = resetMode;
        return profile;
    }

    [Fact]
    public void Ports_are_populated_from_com_port_service()
    {
        var vm = new ConnectViewModel(SettingsWith(), Global(), new StubComPorts(["COM3", "COM7"]));
        Assert.Equal(["COM3", "COM7"], vm.Ports.Select(p => p.Name));
        // FullName is populated from the entry so the dropdown shows device info.
        Assert.Equal("COM3 - USB Serial Device", vm.Ports.First().FullName);
        // No persisted port -> first enumerated is pre-selected.
        Assert.Equal("COM3", vm.SelectedPort);
    }

    [Fact]
    public void Persisted_port_is_pre_selected_when_available()
    {
        var vm = new ConnectViewModel(SettingsWith("COM7"), Global(), new StubComPorts(["COM3", "COM7"]));
        Assert.Equal("COM7", vm.SelectedPort);
    }

    [Fact]
    public void Persisted_port_absent_falls_back_to_first_port()
    {
        var vm = new ConnectViewModel(SettingsWith("COM9"), Global(), new StubComPorts(["COM3"]));
        Assert.Equal("COM3", vm.SelectedPort);
    }

    [Fact]
    public void First_run_preselection_prefers_real_usb_ports_over_phantom()
    {
        // Bluetooth/phantom ports sort before the real one (COM4 < COM7); with
        // nothing persisted the dialog must still pick the real USB device.
        var vm = new ConnectViewModel(SettingsWith(), Global(),
            new StubComPorts(["COM4", "COM5", "COM7"], preferred: ["COM7"]));

        Assert.Equal("COM7", vm.SelectedPort);
    }

    [Fact]
    public void First_run_preselection_falls_back_to_any_port_when_none_preferred()
    {
        var vm = new ConnectViewModel(SettingsWith(), Global(),
            new StubComPorts(["COM4", "COM5"], preferred: []));

        Assert.Equal("COM4", vm.SelectedPort);
    }

    [Fact]
    public void No_ports_greys_serial_tab()
    {
        var settings = SettingsWith();
        var global = new GlobalSettings { NetworkAvailable = false };
        var vm = new ConnectViewModel(settings, global, new StubComPorts([]));
        Assert.Empty(vm.SelectedPort);
        Assert.False(vm.SerialEnabled);

        // Even when the (grayed) Serial tab is selected it cannot connect without a port.
        vm.SelectedIndex = (int)ConnectMode.Serial;
        Assert.False(vm.ConnectCommand.CanExecute(null));
    }

    [Fact]
    public void No_ports_defaults_to_demo_tab_when_network_off()
    {
        var settings = SettingsWith();
        var global = new GlobalSettings { NetworkAvailable = false };
        var vm = new ConnectViewModel(settings, global, new StubComPorts([]));
        Assert.Equal(ConnectMode.Demo, vm.SelectedTab);
    }

    [Fact]
    public void Refresh_re_enumerates_and_keeps_current_selection()
    {
        var ports = new StubComPorts(["COM3"]);
        var vm = new ConnectViewModel(SettingsWith(), Global(), ports);
        Assert.Equal("COM3", vm.SelectedPort);
    }

    [Fact]
    public void Connect_reports_serial_result_and_requests_close_true()
    {
        var settings = SettingsWith("COM3", 115200);
        var vm = new ConnectViewModel(settings, Global(), new StubComPorts(["COM3", "COM7"]));

        vm.SelectedPort = "COM7";
        vm.BaudRate = 250000;
        bool? result = null;
        vm.CloseRequested += (_, r) => result = r;

        vm.ConnectCommand.Execute(null);

        Assert.True(result);
        Assert.Equal(ConnectMode.Serial, vm.Result!.Mode);
        Assert.Equal("COM7", vm.ResultPort);
        Assert.Equal(250000, vm.ResultBaudRate);
        // Persistence of the last successful connection is the caller's job
        // (MainViewModel, only after the connect succeeds) — the dialog must
        // not write settings on click alone.
        Assert.Equal("COM3", settings.Connection.SerialPort);
    }

    [Fact]
    public void Prefills_from_last_successful_serial_connection()
    {
        var settings = SettingsWith("COM3", 115200);
        settings.Connection.LastSerialPort = "COM7";
        settings.Connection.LastSerialBaudRate = 250000;
        var vm = new ConnectViewModel(settings, Global(), new StubComPorts(["COM3", "COM7"]));

        Assert.Equal("COM7", vm.SelectedPort);
        Assert.Equal(250000, vm.BaudRate);
    }

    [Fact]
    public void Prefills_from_last_successful_network_endpoint()
    {
        var settings = SettingsWith();
        settings.Connection.LastNetworkEndpoint = "10.0.0.9:23";
        var vm = new ConnectViewModel(settings, Global(), new StubComPorts([]));

        Assert.Equal("10.0.0.9:23", vm.NetworkEndpoint);
    }

    [Fact]
    public void Cancel_requests_close_false()
    {
        var vm = new ConnectViewModel(SettingsWith(), Global(), new StubComPorts(["COM3"]));
        bool? result = null;
        vm.CloseRequested += (_, r) => result = r;

        vm.CancelCommand.Execute(null);

        Assert.False(result);
    }

    [Fact]
    public void Network_tab_follows_settings_availability()
    {
        var settings = SettingsWith();
        var global = new GlobalSettings { NetworkAvailable = false };
        var noNet = new ConnectViewModel(settings, global, new StubComPorts(["COM3"]));
        Assert.False(noNet.NetworkEnabled);

        global.NetworkAvailable = true;
        var yesNet = new ConnectViewModel(settings, global, new StubComPorts(["COM3"]));
        Assert.True(yesNet.NetworkEnabled);
    }

    [Fact]
    public void No_ports_defaults_to_network_tab_when_enabled()
    {
        var settings = SettingsWith();
        var global = new GlobalSettings { NetworkAvailable = true };
        var vm = new ConnectViewModel(settings, global, new StubComPorts([]));
        Assert.Equal(ConnectMode.Network, vm.SelectedTab);
    }

    [Fact]
    public void Network_connect_requires_a_valid_endpoint()
    {
        var settings = SettingsWith();
        var global = new GlobalSettings { NetworkAvailable = true };
        var vm = new ConnectViewModel(settings, global, new StubComPorts([]));
        vm.SelectedIndex = (int)ConnectMode.Network;

        vm.NetworkEndpoint = "not-an-endpoint:";
        Assert.False(vm.ConnectCommand.CanExecute(null));

        vm.NetworkEndpoint = "192.168.1.100:23";
        Assert.True(vm.ConnectCommand.CanExecute(null));
    }

    [Fact]
    public void Network_connect_reports_endpoint_result()
    {
        var settings = SettingsWith();
        var global = new GlobalSettings { NetworkAvailable = true };
        var vm = new ConnectViewModel(settings, global, new StubComPorts([]));
        vm.SelectedIndex = (int)ConnectMode.Network;
        vm.NetworkEndpoint = "10.0.0.5:8080";

        bool? result = null;
        vm.CloseRequested += (_, r) => result = r;
        vm.ConnectCommand.Execute(null);

        Assert.True(result);
        Assert.Equal(ConnectMode.Network, vm.Result!.Mode);
        Assert.Equal("10.0.0.5:8080", vm.ResultEndpoint);
    }

    [Fact]
    public void Demo_tab_always_available_and_connectable()
    {
        var settings = SettingsWith();
        var global = new GlobalSettings { NetworkAvailable = false };
        var vm = new ConnectViewModel(settings, global, new StubComPorts([]));
        Assert.Equal(ConnectMode.Demo, vm.SelectedTab);
        Assert.True(vm.ConnectCommand.CanExecute(null));
        Assert.Equal(3, vm.DemoAxes);

        bool? result = null;
        vm.CloseRequested += (_, r) => result = r;
        vm.ConnectCommand.Execute(null);

        Assert.True(result);
        Assert.Equal(ConnectMode.Demo, vm.Result!.Mode);
        Assert.Equal(3, vm.Result.DemoAxes);
        Assert.Equal("Start demo", vm.ConnectButtonLabel);
    }

    [Fact]
    public void Demo_axis_options_are_3_4_5()
    {
        var vm = new ConnectViewModel(SettingsWith(), Global(), new StubComPorts([]));
        try
        {
            Assert.Equal([3, 4, 5], vm.DemoAxisOptions);
        }
        finally
        {
            vm.Dispose();
        }
    }

    [Fact]
    public void Demo_axes_preselect_from_profile()
    {
        var settings = SettingsWith();
        settings.DemoAxes = 4;
        var vm = new ConnectViewModel(settings, Global(), new StubComPorts([]));
        try
        {
            Assert.Equal(4, vm.DemoAxes);
        }
        finally
        {
            vm.Dispose();
        }
    }

    [Fact]
    public void Demo_axes_preselect_clamps_out_of_range()
    {
        foreach (var (profileAxes, expected) in new[] { (1, 3), (6, 5), (7, 5) })
        {
            var settings = SettingsWith();
            settings.DemoAxes = profileAxes;
            var vm = new ConnectViewModel(settings, Global(), new StubComPorts([]));
            Assert.Equal(expected, vm.DemoAxes);
            vm.Dispose();
        }
    }

    [Fact]
    public void Demo_axes_result_carries_selection()
    {
        var global = new GlobalSettings { NetworkAvailable = false };
        var vm = new ConnectViewModel(SettingsWith(), global, new StubComPorts([]));
        vm.DemoAxes = 5;

        bool? result = null;
        vm.CloseRequested += (_, r) => result = r;
        vm.ConnectCommand.Execute(null);

        Assert.True(result);
        Assert.Equal(ConnectMode.Demo, vm.Result!.Mode);
        Assert.Equal(5, vm.Result.DemoAxes);
    }

    [Fact]
    public void Switching_profile_represelects_demo_axes()
    {
        var active = SettingsWith();
        var foam = new MachineProfile { Id = "foam", Name = "Foam", MachineTypeId = MachineTypes.Foam, DemoAxes = 4 };
        var vm = new ConnectViewModel(active, Global(), new StubComPorts([]), profiles: [active, foam]);
        try
        {
            Assert.Equal(3, vm.DemoAxes);

            vm.SelectedProfile = vm.Profiles.First(p => p.Id == "foam");

            Assert.Equal(4, vm.DemoAxes);
        }
        finally
        {
            vm.Dispose();
        }
    }

    [Fact]
    public void Demo_connect_does_not_write_settings()
    {
        var settings = SettingsWith();
        var global = new GlobalSettings { NetworkAvailable = false };
        var vm = new ConnectViewModel(settings, global, new StubComPorts([]));
        vm.DemoAxes = 5;

        bool? result = null;
        vm.CloseRequested += (_, r) => result = r;
        vm.ConnectCommand.Execute(null);

        Assert.True(result);
        // Persistence is the caller's job (MainViewModel, on demo activation) —
        // the dialog must not write to the profile on click alone.
        Assert.Equal(3, settings.DemoAxes);
    }

    [Fact]
    public void Serial_connect_reports_serial_result()
    {
        var settings = SettingsWith("COM3", 115200);
        var vm = new ConnectViewModel(settings, Global(), new StubComPorts(["COM3", "COM7"]));
        vm.SelectedPort = "COM7";

        bool? result = null;
        vm.CloseRequested += (_, r) => result = r;
        vm.ConnectCommand.Execute(null);

        Assert.True(result);
        Assert.Equal(ConnectMode.Serial, vm.Result!.Mode);
        Assert.Equal("COM7", vm.ResultPort);
    }

    [Fact]
    public void Baud_defaults_from_persisted_or_standard()
    {
        var vm = new ConnectViewModel(SettingsWith("COM3", 250000), Global(), new StubComPorts(["COM3"]));
        Assert.Equal(250000, vm.BaudRate);

        var legacy = new ConnectViewModel(SettingsWith("COM3", 12345), Global(), new StubComPorts(["COM3"]));
        Assert.Equal(115200, legacy.BaudRate); // unsupported saved baud falls back
    }

    [Fact]
    public void On_connect_reset_dropdown_lists_ioSender_options_and_defaults_to_none()
    {
        var vm = new ConnectViewModel(SettingsWith(), Global(), new StubComPorts(["COM3"]));
        Assert.Equal(["No action", "Toggle DTR", "Toggle RTS"], vm.ResetModeOptions.Select(o => o.Name));
        Assert.Equal(ConnectResetMode.None, vm.SelectedResetMode);
        Assert.Equal("No action", vm.SelectedResetModeOption.Name);
    }

    [Fact]
    public void On_connect_reset_mode_prefills_from_settings()
    {
        var settings = SettingsWith("COM3", 115200);
        settings.Connection.SerialResetMode = ConnectResetMode.ToggleDtr;
        var vm = new ConnectViewModel(settings, Global(), new StubComPorts(["COM3"]));
        Assert.Equal(ConnectResetMode.ToggleDtr, vm.SelectedResetMode);
        Assert.Equal("Toggle DTR", vm.SelectedResetModeOption.Name);
    }

    [Fact]
    public void Serial_connect_reports_reset_mode_in_result()
    {
        var settings = SettingsWith("COM3", 115200);
        var vm = new ConnectViewModel(settings, Global(), new StubComPorts(["COM3", "COM7"]));
        vm.SelectedPort = "COM7";
        vm.SelectedResetMode = ConnectResetMode.ToggleDtr;

        bool? result = null;
        vm.CloseRequested += (_, r) => result = r;
        vm.ConnectCommand.Execute(null);

        Assert.True(result);
        Assert.Equal(ConnectResetMode.ToggleDtr, vm.Result!.SerialResetMode);
    }

    [Fact]
    public async Task Auto_discovers_new_com_port_while_dialog_is_open()
    {
        var ports = new MutableComPorts(["COM3"]);
        var vm = new ConnectViewModel(SettingsWith(), Global(), ports);
        try
        {
            Assert.Equal(["COM3"], vm.Ports.Select(p => p.Name));
            Assert.True(vm.SerialEnabled);

            // A controller is plugged in while the dialog stays open: the serial
            // tab was already usable here, but the point is the new port appears
            // without a manual refresh.
            ports.Set(["COM3", "COM5"]);

            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (!vm.Ports.Any(p => p.Name == "COM5") && DateTime.UtcNow < deadline)
                await Task.Delay(100);

            Assert.Contains(vm.Ports, p => p.Name == "COM5");
        }
        finally
        {
            vm.Dispose();
        }
    }

    [Fact]
    public async Task Auto_discovers_port_greysed_serial_tab_when_controller_plugged_in()
    {
        // Start with no ports -> Serial tab is grayed out and unusable.
        var ports = new MutableComPorts([]);
        var settings = SettingsWith();
        var global = new GlobalSettings { NetworkAvailable = false };
        var vm = new ConnectViewModel(settings, global, ports);
        try
        {
            Assert.False(vm.SerialEnabled);
            vm.SelectedIndex = (int)ConnectMode.Serial;
            Assert.False(vm.ConnectCommand.CanExecute(null));

            // Plugging in a controller revives the Serial tab automatically.
            ports.Set(["COM9"]);

            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (!vm.SerialEnabled && DateTime.UtcNow < deadline)
                await Task.Delay(100);

            Assert.True(vm.SerialEnabled);
            Assert.Contains(vm.Ports, p => p.Name == "COM9");
            // A port was auto-selected, so the Serial tab can now connect.
            Assert.Equal("COM9", vm.SelectedPort);
            Assert.True(vm.ConnectCommand.CanExecute(null));
        }
        finally
        {
            vm.Dispose();
        }
    }

    [Fact]
    public void ProfileSelectorVisible_false_when_single_profile()
    {
        var vm = new ConnectViewModel(SettingsWith(), Global(), new StubComPorts(["COM3"]));
        try
        {
            Assert.Single(vm.Profiles);
            Assert.False(vm.ProfileSelectorVisible);
        }
        finally
        {
            vm.Dispose();
        }
    }

    [Fact]
    public void ProfileSelectorVisible_true_when_multiple_profiles()
    {
        var active = SettingsWith();
        var second = ProfileWithId("other", "COM3");
        var vm = new ConnectViewModel(active, Global(), new StubComPorts(["COM3"]), profiles: [active, second]);
        try
        {
            Assert.True(vm.ProfileSelectorVisible);
            Assert.Equal(["default", "other"], vm.Profiles.Select(p => p.Id));
        }
        finally
        {
            vm.Dispose();
        }
    }

    [Fact]
    public void SelectedProfile_defaults_to_the_active_profile()
    {
        var active = SettingsWith("COM7", 250000);
        var second = ProfileWithId("other", "COM3");
        var vm = new ConnectViewModel(active, Global(), new StubComPorts(["COM3", "COM7"]),
            profiles: [active, second]);
        try
        {
            Assert.Equal(active.Id, vm.SelectedProfile.Id);
        }
        finally
        {
            vm.Dispose();
        }
    }

    [Fact]
    public void Switching_SelectedProfile_rebinds_prefills_and_keeps_tab()
    {
        var profileA = SettingsWith("COM7", 250000);
        profileA.Connection.LastSerialPort = "COM7";
        profileA.Connection.LastSerialBaudRate = 250000;
        profileA.Connection.LastNetworkEndpoint = "10.0.0.9:23";
        profileA.Connection.SerialResetMode = ConnectResetMode.ToggleDtr;
        var profileB = ProfileWithId("B", "COM3", 115200, "192.168.1.100:23", ConnectResetMode.None);

        var vm = new ConnectViewModel(profileA, Global(), new StubComPorts(["COM3", "COM7"]),
            profiles: [profileA, profileB]);
        try
        {
            Assert.Equal("COM7", vm.SelectedPort);
            Assert.Equal(0, vm.SelectedIndex);

            vm.SelectedProfile = vm.Profiles.First(p => p.Id == "B");

            Assert.Equal("COM3", vm.SelectedPort);
            Assert.Equal(115200, vm.BaudRate);
            Assert.Equal("192.168.1.100:23", vm.NetworkEndpoint);
            Assert.Equal(ConnectResetMode.None, vm.SelectedResetMode);
            Assert.Equal(0, vm.SelectedIndex);
        }
        finally
        {
            vm.Dispose();
        }
    }

    [Fact]
    public void Connect_result_carries_the_chosen_profile_id()
    {
        var profileA = SettingsWith("COM7", 250000);
        profileA.Connection.LastSerialPort = "COM7";
        profileA.Connection.LastSerialBaudRate = 250000;
        profileA.Connection.LastNetworkEndpoint = "10.0.0.9:23";
        profileA.Connection.SerialResetMode = ConnectResetMode.ToggleDtr;
        var profileB = ProfileWithId("B", "COM3", 115200);

        var vm = new ConnectViewModel(profileA, Global(), new StubComPorts(["COM3", "COM7"]),
            profiles: [profileA, profileB]);
        try
        {
            vm.SelectedProfile = vm.Profiles.First(p => p.Id == "B");
            vm.SelectedIndex = (int)ConnectMode.Serial;

            bool? result = null;
            vm.CloseRequested += (_, r) => result = r;
            vm.ConnectCommand.Execute(null);

            Assert.True(result);
            Assert.Equal("B", vm.Result!.ProfileId);
        }
        finally
        {
            vm.Dispose();
        }
    }

    private sealed class MutableComPorts(string[] ports) : IComPortService
    {
        private string[] _ports = ports;
        public string[] GetPorts() => _ports;
        public IReadOnlyList<SerialPortEntry> GetPortEntries() =>
            _ports.Select(p => new SerialPortEntry(p, p)).ToArray();
        public void Set(string[] ports) => _ports = ports;
    }
}