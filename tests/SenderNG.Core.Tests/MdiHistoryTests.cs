using CNC.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using SenderNG.ViewModels;

namespace SenderNG.Core.Tests;

public class MdiHistoryTests
{
    private static SettingsService MakeSettings()
    {
        var dir = Path.Combine(Path.GetTempPath(), "senderng-tests-" + Guid.NewGuid().ToString("N"));
        return new SettingsService(dir);
    }

    private static ServiceProvider MakeProvider(SettingsService settings)
    {
        var services = new ServiceCollection();
        services.AddSingleton(settings);
        services.AddSingleton(_ => new DemoGrblService(settings.Load()));
        services.AddSingleton<RealGrblService>();
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task UP_cycles_history_and_Send_pushes_tail()
    {
        var settings = MakeSettings();
        var provider = MakeProvider(settings);
        var vm = new MainViewModel(new DemoGrblService(new AppSettings()), settings, provider);

        Assert.Empty(vm.MdiHistory);
        Assert.Equal(string.Empty, vm.MdiInput);

        vm.MdiInput = "G0 X1";
        vm.SendMdiCommand.Execute(null);
        await Task.Yield();
        await Task.Delay(20);
        Assert.Single(vm.MdiHistory);
        Assert.Equal("G0 X1", vm.MdiHistory[0]);
        Assert.Equal("G0 X1", vm.MdiHistory.Last());

        vm.MdiInput = "G1 Y2";
        vm.SendMdiCommand.Execute(null);
        await Task.Yield();
        await Task.Delay(20);
        Assert.Equal(2, vm.MdiHistory.Count);
        Assert.Equal("G1 Y2", vm.MdiHistory.Last());

        // dedup tail: sending same as last should not duplicate
        vm.MdiInput = "G1 Y2";
        vm.SendMdiCommand.Execute(null);
        await Task.Yield();
        await Task.Delay(20);
        Assert.Equal(2, vm.MdiHistory.Count);

        // UP cycles history (most recent first)
        vm.MdiHistoryPrevCommand.Execute(null);
        Assert.Equal("G1 Y2", vm.MdiInput);
        vm.MdiHistoryPrevCommand.Execute(null);
        Assert.Equal("G0 X1", vm.MdiInput);
        // stay at oldest when pressing UP at top
        vm.MdiHistoryPrevCommand.Execute(null);
        Assert.Equal("G0 X1", vm.MdiInput);

        // DOWN cycles forward
        vm.MdiHistoryNextCommand.Execute(null);
        Assert.Equal("G1 Y2", vm.MdiInput);
        vm.MdiHistoryNextCommand.Execute(null);
        Assert.Equal(string.Empty, vm.MdiInput);

        // Send pushes tail again after navigation
        vm.MdiInput = "G2 Z3";
        vm.SendMdiCommand.Execute(null);
        await Task.Yield();
        await Task.Delay(20);
        Assert.Equal(3, vm.MdiHistory.Count);
        Assert.Equal("G2 Z3", vm.MdiHistory.Last());
    }

    [Fact]
    public void OpenFileCommand_exists_as_placeholder()
    {
        var settings = MakeSettings();
        var provider = MakeProvider(settings);
        var vm = new MainViewModel(new DemoGrblService(new AppSettings()), settings, provider);
        Assert.NotNull(vm.OpenFileCommand);
        Assert.True(vm.OpenFileCommand.CanExecute(null));
    }

    [Fact]
    public void Cycle_commands_exist()
    {
        var settings = MakeSettings();
        var provider = MakeProvider(settings);
        var vm = new MainViewModel(new DemoGrblService(new AppSettings()), settings, provider);
        Assert.NotNull(vm.CycleStartCommand);
        Assert.NotNull(vm.FeedHoldCommand);
        Assert.NotNull(vm.StopCommand);
        Assert.NotNull(vm.RewindCommand);
    }
}
