
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
using Microsoft.Extensions.DependencyInjection;
using SenderNG.Modules.Fixed;
using SenderNG.ViewModels;

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

    // The load itself is ~40ms, but it runs via Task.Run and the parallel suite can
    // starve the thread pool, so the wait must be condition-based with a generous
    // budget rather than a fixed "sleep and hope" deadline. This orders the loads;
    // it is not asserting speed. A genuine hang still surfaces with the status text.
    private static async Task WaitForAsync(Func<bool> condition, Func<string> detail, string what)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (!condition() && sw.Elapsed < TimeSpan.FromSeconds(30))
            await Task.Delay(10);
        Assert.True(condition(), $"timed out waiting for {what}; status: '{detail()}'");
    }

    // The load sets "Loading {0}…" (which also contains the filename) before parsing
    // and "Loaded {0} (N lines)" only at the very end. Matching on the filename alone would
    // release the wait while the previous load is still running, so the loads overlap
    // and the recent-files order becomes nondeterministic. The finished status is the
    // only filename-bearing status without the ellipsis (locale-independent).
    private static bool LoadFinished(MainViewModel vm, string file) =>
        vm.Status.Contains(Path.GetFileName(file)) && !vm.Status.Contains("…");

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
            await WaitForAsync(() => LoadFinished(vm1, file), () => vm1.Status, "load finished");
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
                await WaitForAsync(() => LoadFinished(vm, f), () => vm.Status, $"load {i} landed");
            }

            Assert.Equal(10, vm.RecentFiles.Count);
            Assert.Equal(Path.Combine(dir, "part12.nc"), vm.RecentFiles[0]);
            Assert.Equal(Path.Combine(dir, "part3.nc"), vm.RecentFiles[^1]);
            Assert.DoesNotContain(Path.Combine(dir, "part1.nc"), vm.RecentFiles);
            Assert.DoesNotContain(Path.Combine(dir, "part2.nc"), vm.RecentFiles);

            // And the persisted copy matches (recent files are app-global).
            Assert.Equal(vm.RecentFiles, new SettingsService(dir).Global.RecentFiles);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }
}
