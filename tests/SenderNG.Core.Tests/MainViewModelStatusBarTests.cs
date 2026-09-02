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

public class MainViewModelStatusBarTests
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
        // ProgramViewModel resolves IGrblService from DI (used by run paths);
        // without the alias its grbl is null and RunAsync silently no-ops.
        services.AddSingleton<IGrblService>(sp => sp.GetRequiredService<DemoGrblService>());
        services.AddSingleton<RealGrblService>();
        services.AddSingleton<ProgramViewModel>();
        return services.BuildServiceProvider();
    }

    [Fact]
    public void Jog_step_shows_the_active_distance_from_settings()
    {
        var settings = MakeSettings();
        var svc = new DemoGrblService(settings.Load());
        var vm = new MainViewModel(svc, settings, MakeProvider(settings));

        // Default JogConfig: Distances [0.1, 1.0, 10.0], DistanceIndex 1 -> 1.0.
        Assert.Contains("1", vm.StatusBarJogStep);
    }

    [Fact]
    public void Jog_step_tracks_a_settings_change()
    {
        var settings = MakeSettings();
        var svc = new DemoGrblService(settings.Load());
        var vm = new MainViewModel(svc, settings, MakeProvider(settings));

        var app = settings.Load();
        var cfg = JogConfigStore.Load(app);
        cfg.DistanceIndex = 2; // -> the active distance becomes 10.0
        JogConfigStore.Save(app, cfg);
        settings.Save(app);

        Assert.Contains("10", vm.StatusBarJogStep);
    }

    [Fact]
    public void Blocks_reflect_the_loaded_program()
    {
        var settings = MakeSettings();
        var provider = MakeProvider(settings);
        var vm = new MainViewModel(new DemoGrblService(settings.Load()), settings, provider);

        // No program loaded -> placeholder.
        Assert.Contains("--/--", vm.StatusBarBlocks);

        var program = provider.GetRequiredService<ProgramViewModel>();
        program.LoadGCode("G0 X0\nG1 X10 F100\nM5");

        Assert.Equal("Block: 0/3", vm.StatusBarBlocks);
    }

    [Fact]
    public async Task Block_counter_counts_sendable_lines_only_and_reads_n_over_n_when_done()
    {
        var settings = MakeSettings();
        var provider = MakeProvider(settings);
        var svc = provider.GetRequiredService<DemoGrblService>();
        await svc.ConnectAsync("demo");
        var vm = new MainViewModel(svc, settings, provider);

        var program = provider.GetRequiredService<ProgramViewModel>();
        program.LoadGCode("G21\n; comment\nG0 X0\nG1 X10 F100\n; another comment\nM5");

        Assert.Equal("Block: 0/4", vm.StatusBarBlocks);

        await program.RunAsync();

        Assert.Equal("Run finished (4 blocks)", program.RunMessage);
        Assert.Equal("Block: 4/4", vm.StatusBarBlocks);
    }

    [Fact]
    public async Task Run_time_advances_while_running_and_freezes_on_hold()
    {
        var settings = MakeSettings();
        var svc = new DemoGrblService(settings.Load());
        await svc.ConnectAsync("demo");
        var vm = new MainViewModel(svc, settings, MakeProvider(settings));

        svc.SendRealtime(GrblRealtime.CycleStart); // Idle -> Run starts the clock
        var t0 = vm.RunTime;

        await Task.Delay(1300);
        var t1 = vm.RunTime;

        // The clock advanced and the value is a time (m:ss after the prefix).
        Assert.NotEqual(t0, t1);
        Assert.Matches(@"\d+:\d{2}", t1.Split(": ", 2)[^1]);

        svc.SendRealtime(GrblRealtime.FeedHold); // Run -> Hold freezes the clock
        var frozen = vm.RunTime;
        await Task.Delay(200);
        Assert.Equal(frozen, vm.RunTime);

        // Resume (Hold -> Run) CONTINUES the clock from the frozen value — a
        // resume is not a new cycle, so the counter must not restart at zero.
        svc.SendRealtime(GrblRealtime.CycleStart);
        Assert.Equal(frozen, vm.RunTime); // still showing the frozen elapsed
        await Task.Delay(1100);
        Assert.NotEqual(frozen, vm.RunTime); // and it advances again
    }
}