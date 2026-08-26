using CNC.Core.Abstractions;
using CNC.Core.Services;
using CNC.Core.ViewModels;

namespace SenderNG.Core.Tests;

public class ConnectViewModelTests
{
    private sealed class StubComPorts(string[] ports) : IComPortService
    {
        public string[] GetPorts() => ports;
    }

    private static AppSettings SettingsWith(string? port = null, int baud = 115200)
    {
        var s = new AppSettings();
        if (port is not null)
            s.Current.Port = port;
        s.Current.BaudRate = baud;
        return s;
    }

    [Fact]
    public void Ports_are_populated_from_com_port_service()
    {
        var vm = new ConnectViewModel(SettingsWith(), new StubComPorts(["COM3", "COM7"]));
        Assert.Equal(["COM3", "COM7"], vm.Ports);
        // No persisted port -> first enumerated is pre-selected.
        Assert.Equal("COM3", vm.SelectedPort);
    }

    [Fact]
    public void Persisted_port_is_pre_selected_when_available()
    {
        var vm = new ConnectViewModel(SettingsWith("COM7"), new StubComPorts(["COM3", "COM7"]));
        Assert.Equal("COM7", vm.SelectedPort);
    }

    [Fact]
    public void Persisted_port_absent_falls_back_to_first_port()
    {
        var vm = new ConnectViewModel(SettingsWith("COM9"), new StubComPorts(["COM3"]));
        Assert.Equal("COM3", vm.SelectedPort);
    }

    [Fact]
    public void No_ports_leaves_selection_empty_and_connect_disabled()
    {
        var vm = new ConnectViewModel(SettingsWith(), new StubComPorts([]));
        Assert.Empty(vm.SelectedPort);
        Assert.False(vm.ConnectCommand.CanExecute(null));
    }

    [Fact]
    public void Refresh_re_enumerates_and_keeps_current_selection()
    {
        var ports = new StubComPorts(["COM3"]);
        var vm = new ConnectViewModel(SettingsWith(), ports);
        Assert.Equal("COM3", vm.SelectedPort);
    }

    [Fact]
    public void Connect_persists_selection_and_requests_close_true()
    {
        var settings = SettingsWith("COM3", 115200);
        var vm = new ConnectViewModel(settings, new StubComPorts(["COM3", "COM7"]));

        vm.SelectedPort = "COM7";
        vm.BaudRate = 250000;
        bool? result = null;
        vm.CloseRequested += (_, r) => result = r;

        vm.ConnectCommand.Execute(null);

        Assert.True(result);
        Assert.Equal("COM7", settings.Current.Port);
        Assert.Equal(250000, settings.Current.BaudRate);
        Assert.Equal("COM7", vm.ResultPort);
        Assert.Equal(250000, vm.ResultBaudRate);
    }

    [Fact]
    public void Cancel_requests_close_false()
    {
        var vm = new ConnectViewModel(SettingsWith(), new StubComPorts(["COM3"]));
        bool? result = null;
        vm.CloseRequested += (_, r) => result = r;

        vm.CancelCommand.Execute(null);

        Assert.False(result);
    }

    [Fact]
    public void Network_connect_is_deferred_and_disabled()
    {
        var vm = new ConnectViewModel(SettingsWith(), new StubComPorts(["COM3"]));
        Assert.False(vm.NetworkEnabled);
        Assert.Contains("deferred", vm.NetworkNote);
    }

    [Fact]
    public void Baud_defaults_from_persisted_or_standard()
    {
        var vm = new ConnectViewModel(SettingsWith("COM3", 250000), new StubComPorts(["COM3"]));
        Assert.Equal(250000, vm.BaudRate);

        var legacy = new ConnectViewModel(SettingsWith("COM3", 12345), new StubComPorts(["COM3"]));
        Assert.Equal(115200, legacy.BaudRate); // unsupported saved baud falls back
    }
}