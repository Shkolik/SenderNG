using CNC.Core.Abstractions;
using CNC.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using SenderNG.Avalonia.Modules.Fixed;
using SenderNG.Avalonia.ViewModels;

namespace SenderNG.Core.Tests;

public class MainViewModelStateDisplayTests
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
    public async Task State_indicator_shows_uppercase_state()
    {
        var settings = MakeSettings();
        var svc = new DemoGrblService(settings.Load());
        var vm = new MainViewModel(svc, settings, MakeProvider(settings));
        await svc.ConnectAsync("demo");

        Assert.Equal("Idle", vm.MachineState);
        Assert.Equal("IDLE", vm.MachineStateDisplay);

        svc.SendRealtime(GrblRealtime.FeedHold);
        Assert.Equal("Hold", vm.MachineState);
        Assert.Equal("HOLD", vm.MachineStateDisplay);

        svc.SendRealtime(GrblRealtime.CycleStart);
        Assert.Equal("RUN", vm.MachineStateDisplay);
    }
}

public class MainViewModelRecentFilesTests
{
    private static ServiceProvider MakeProvider(SettingsService settings)
    {
        var services = new ServiceCollection();
        services.AddSingleton(settings);
        services.AddSingleton(_ => new DemoGrblService(settings.Load()));
        services.AddSingleton<RealGrblService>();
        // LoadGCodeFileAsync resolves ProgramViewModel via the provider.
        services.AddSingleton<ProgramViewModel>();
        return services.BuildServiceProvider();
    }

    private static async Task WaitForAsync(Func<bool> condition, string what)
    {
        var deadline = DateTime.UtcNow.AddSeconds(2);
        while (!condition() && DateTime.UtcNow < deadline)
            await Task.Delay(10);
        Assert.True(condition(), $"timed out waiting for {what}");
    }

    [Fact]
    public async Task Recent_files_persist_across_app_restart()
    {
        var dir = Path.Combine(Path.GetTempPath(), "senderng-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, "part1.nc");
        await File.WriteAllTextAsync(file, "G0 X0\nG0 Y0");
        try
        {
            var settings = new SettingsService(dir);
            var vm1 = new MainViewModel(new DemoGrblService(settings.Load()), settings, MakeProvider(settings));
            vm1.SelectRecentCommand.Execute(file);
            await WaitForAsync(() => vm1.Status.StartsWith("Loaded"), "load finished");
            // A fresh app instance on the same settings dir must see the list.
            var settings2 = new SettingsService(dir);
            var vm2 = new MainViewModel(new DemoGrblService(settings2.Load()), settings2, MakeProvider(settings2));
            Assert.Contains(file, vm2.RecentFiles);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Recent_files_capped_at_ten_most_recent_first()
    {
        var dir = Path.Combine(Path.GetTempPath(), "senderng-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var settings = new SettingsService(dir);
            var vm = new MainViewModel(new DemoGrblService(settings.Load()), settings, MakeProvider(settings));

            for (var i = 1; i <= 12; i++)
            {
                var f = Path.Combine(dir, $"part{i}.nc");
                await File.WriteAllTextAsync(f, "G0 X0");
                vm.SelectRecentCommand.Execute(f);
                // Wait until this load fully finished (Status is set last) before
                // starting the next, so the completion order is deterministic.
                await WaitForAsync(() => vm.Status.Contains(Path.GetFileName(f)), $"load {i} landed");
            }

            Assert.Equal(10, vm.RecentFiles.Count);
            Assert.Equal(Path.Combine(dir, "part12.nc"), vm.RecentFiles[0]);
            Assert.Equal(Path.Combine(dir, "part3.nc"), vm.RecentFiles[^1]);
            Assert.DoesNotContain(Path.Combine(dir, "part1.nc"), vm.RecentFiles);
            Assert.DoesNotContain(Path.Combine(dir, "part2.nc"), vm.RecentFiles);

            // And the persisted copy matches.
            var reloaded = new SettingsService(dir).Load();
            Assert.Equal(vm.RecentFiles, reloaded.RecentFiles);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }
}
