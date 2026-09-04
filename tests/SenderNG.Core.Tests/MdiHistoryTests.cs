
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
        var vm = new MainViewModel(new DemoGrblService(new MachineProfile()), settings, provider);

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
        var vm = new MainViewModel(new DemoGrblService(new MachineProfile()), settings, provider);
        Assert.NotNull(vm.OpenFileCommand);
        Assert.True(vm.OpenFileCommand.CanExecute(null));
    }

    [Fact]
    public void Cycle_commands_exist()
    {
        var settings = MakeSettings();
        var provider = MakeProvider(settings);
        var vm = new MainViewModel(new DemoGrblService(new MachineProfile()), settings, provider);
        Assert.NotNull(vm.CycleStartCommand);
        Assert.NotNull(vm.FeedHoldCommand);
        Assert.NotNull(vm.StopCommand);
        Assert.NotNull(vm.RewindCommand);
    }
}
