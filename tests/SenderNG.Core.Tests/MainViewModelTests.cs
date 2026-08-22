using CNC.Core.Abstractions;
using CNC.Core.Services;
using SenderNG.Avalonia.ViewModels;

namespace SenderNG.Core.Tests;

public class MainViewModelTests
{
    private static SettingsService MakeSettings()
    {
        var dir = Path.Combine(Path.GetTempPath(), "senderng-tests-" + Guid.NewGuid().ToString("N"));
        return new SettingsService(dir);
    }

    [Fact]
    public void MainViewModel_IsDemo_true_initially_when_demo_service_registered()
    {
        var vm = new MainViewModel(new DemoGrblService(new AppSettings()), MakeSettings());

        Assert.True(vm.IsDemo);
        Assert.IsType<DemoGrblService>(vm.Grbl);
    }

    [Fact]
    public void MainViewModel_toggles_demo_and_swaps_service()
    {
        var vm = new MainViewModel(new DemoGrblService(new AppSettings()), MakeSettings());

        vm.DemoToggleCommand.Execute(null);

        Assert.False(vm.IsDemo);
        Assert.IsType<RealGrblService>(vm.Grbl);

        vm.DemoToggleCommand.Execute(null);

        Assert.True(vm.IsDemo);
        Assert.IsType<DemoGrblService>(vm.Grbl);
    }
}
