
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

using System;
using CNC.Core.Services;
using CNC.Core.ViewModels;
using Xunit;

namespace SenderNG.Core.Tests;

public class DemoMachineSettingsViewModelTests
{
    private static MachineProfile SettingsWithAxes(int demoAxes) => new() { DemoAxes = demoAxes };

    private static MachineProfile SettingsWithType(string machineType) => new() { MachineTypeId = machineType };

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-3, 1)]
    [InlineData(15, 9)]
    public void Ok_clamps_NumAxes_to_valid_range(int input, int expected)
    {
        var profile = new MachineProfile();
        var demo = new DemoGrblService(profile);
        var vm = new DemoMachineSettingsViewModel(profile, demo) { NumAxes = input };

        vm.OkCommand.Execute(null);

        Assert.Equal(expected, profile.DemoAxes);
        Assert.Equal(expected, vm.NumAxes);
    }

    [Fact]
    public void Ok_saves_settings_and_applies_to_demo_service()
    {
        var profile = SettingsWithAxes(3);
        var demo = new DemoGrblService(profile);
        MachineState? received = null;
        using var sub = demo.State.Subscribe(s => received = s);

        var vm = new DemoMachineSettingsViewModel(profile, demo) { NumAxes = 5, IsFoam = true };

        Assert.True(vm.IsFoam);
        Assert.False(vm.IsMill);

        vm.OkCommand.Execute(null);

        Assert.Equal(5, profile.DemoAxes);
        Assert.Equal("Foam", profile.MachineTypeId);
        Assert.NotNull(received);
        Assert.Equal(5, received.NumAxes);
    }

    [Fact]
    public void Ok_invokes_saved_callback()
    {
        var profile = new MachineProfile();
        var demo = new DemoGrblService(profile);
        var calls = 0;
        var vm = new DemoMachineSettingsViewModel(profile, demo, () => calls++);

        vm.OkCommand.Execute(null);

        Assert.Equal(1, calls);
    }

    [Fact]
    public void Cancel_does_not_save_or_apply()
    {
        var profile = SettingsWithAxes(3);
        var demo = new DemoGrblService(profile);
        var pushes = 0;
        using var sub = demo.State.Subscribe(_ => pushes++);
        bool? closeResult = null;
        var vm = new DemoMachineSettingsViewModel(profile, demo, () => throw new InvalidOperationException("callback must not run on cancel"));
        vm.CloseRequested += (_, r) => closeResult = r;

        vm.NumAxes = 7;
        vm.IsFoam = true;
        vm.CancelCommand.Execute(null);

        Assert.Equal(3, profile.DemoAxes);
        Assert.Equal("Mill", profile.MachineTypeId);
        Assert.False(profile.MachineTypeId == "Foam");
        Assert.Equal(1, pushes);
        Assert.False(closeResult ?? true);
    }

    [Fact]
    public void Mode_radios_are_mutually_exclusive()
    {
        var profile = SettingsWithType("Mill");
        var demo = new DemoGrblService(profile);
        var vm = new DemoMachineSettingsViewModel(profile, demo);

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
