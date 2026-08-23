using System;
using CNC.Core.Services;
using CNC.Core.ViewModels;
using Xunit;

namespace SenderNG.Core.Tests;

public class DemoMachineSettingsViewModelTests
{
    private static AppSettings SettingsWithAxes(int demoAxes, string mode = "Mill")
    {
        var s = new AppSettings { Mode = mode };
        s.Current.DemoAxes = demoAxes;
        return s;
    }

    private static AppSettings SettingsWithMode(string mode) => new() { Mode = mode };

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-3, 1)]
    [InlineData(15, 9)]
    public void Ok_clamps_NumAxes_to_valid_range(int input, int expected)
    {
        var settings = new AppSettings();
        var demo = new DemoGrblService(settings);
        var vm = new DemoMachineSettingsViewModel(settings, demo) { NumAxes = input };

        vm.OkCommand.Execute(null);

        Assert.Equal(expected, settings.Current.DemoAxes);
        Assert.Equal(expected, vm.NumAxes);
    }

    [Fact]
    public void Ok_saves_settings_and_applies_to_demo_service()
    {
        var settings = SettingsWithAxes(3);
        var demo = new DemoGrblService(settings);
        MachineState? received = null;
        using var sub = demo.State.Subscribe(s => received = s);

        var vm = new DemoMachineSettingsViewModel(settings, demo) { NumAxes = 5, IsFoam = true };

        Assert.True(vm.IsFoam);
        Assert.False(vm.IsMill);

        vm.OkCommand.Execute(null);

        Assert.Equal(5, settings.Current.DemoAxes);
        Assert.Equal("Foam", settings.Mode);
        Assert.NotNull(received);
        Assert.Equal(5, received.NumAxes);
    }

    [Fact]
    public void Ok_invokes_saved_callback()
    {
        var settings = new AppSettings();
        var demo = new DemoGrblService(settings);
        var calls = 0;
        var vm = new DemoMachineSettingsViewModel(settings, demo, () => calls++);

        vm.OkCommand.Execute(null);

        Assert.Equal(1, calls);
    }

    [Fact]
    public void Cancel_does_not_save_or_apply()
    {
        var settings = SettingsWithAxes(3, "Mill");
        var demo = new DemoGrblService(settings);
        var pushes = 0;
        using var sub = demo.State.Subscribe(_ => pushes++);
        bool? closeResult = null;
        var vm = new DemoMachineSettingsViewModel(settings, demo, () => throw new InvalidOperationException("callback must not run on cancel"));
        vm.CloseRequested += (_, r) => closeResult = r;

        vm.NumAxes = 7;
        vm.IsFoam = true;
        vm.CancelCommand.Execute(null);

        Assert.Equal(3, settings.Current.DemoAxes);
        Assert.Equal("Mill", settings.Mode);
        Assert.False(settings.Mode == "Foam");
        Assert.Equal(1, pushes);
        Assert.False(closeResult ?? true);
    }

    [Fact]
    public void Mode_radios_are_mutually_exclusive()
    {
        var settings = SettingsWithMode("Mill");
        var demo = new DemoGrblService(settings);
        var vm = new DemoMachineSettingsViewModel(settings, demo);

        Assert.True(vm.IsMill);
        Assert.False(vm.IsFoam);

        vm.IsFoam = true;

        Assert.True(vm.IsFoam);
        Assert.False(vm.IsMill);

        vm.IsMill = true;

        Assert.True(vm.IsMill);
        Assert.False(vm.IsFoam);
    }
}
