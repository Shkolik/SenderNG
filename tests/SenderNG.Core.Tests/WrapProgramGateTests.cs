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

using System.Reactive.Linq;
using CNC.Core.Abstractions;
using CNC.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using SenderNG.Modules.Fixed;
using SenderNG.ViewModels;
using Xunit;

namespace SenderNG.Core.Tests;

/// <summary>
/// The wrap/unwrap rotary gate in <see cref="MainViewModel"/>: it asks the resolved
/// kinematics whether any axis is rotary (<c>RotaryAxes.Count == 0</c>), never whether
/// the controller reports more than three axes. The live bug this pins down: a foam
/// profile runs FOUR axes and every one of them is a linear tower axis, so the old
/// <c>NumAxes &lt;= 3</c> test offered "Wrap to A-axis" where <c>A</c> is a linear tower.
/// </summary>
public class WrapProgramGateTests
{
    /// <summary>Hands out one dialog view model and leaves the dialog "open" — these
    /// tests assert what is OFFERED, never what it returns.</summary>
    private sealed class StubDialogs : IDialogService
    {
        public object? LastVm { get; private set; }

        public Task<bool?> ShowDialogAsync(string title, object viewModel, (double Width, double Height)? preferredSize = null)
        {
            LastVm = viewModel;
            return new TaskCompletionSource<bool?>().Task;
        }
    }

    private sealed class RecordingGrbl : IGrblService
    {
        public bool IsConnected => true;
        public IObservable<MachineState> State => Observable.Empty<MachineState>();
        public Task ConnectAsync(string port) => Task.CompletedTask;
        public void Disconnect() { }
        public Task SendAsync(string line) => Task.CompletedTask;
        public void SendRealtime(byte code) { }
    }

    /// <summary>A fixed kinematics snapshot, so the gate's source (the shared provider,
    /// not the ambient info) is provable.</summary>
    private sealed class FakeKinematics : IMachineKinematicsProvider
    {
        public FakeKinematics(MachineKinematics current) => Current = current;

        public MachineKinematics Current { get; }

        public event EventHandler? Changed { add { } remove { } }

        public void Refresh() { }
    }

    private static SettingsService MakeSettings(string machineTypeId, bool withRotaryAxis)
    {
        var dir = Path.Combine(Path.GetTempPath(), "senderng-wrapgate-" + Guid.NewGuid().ToString("N"));
        var settings = new SettingsService(dir);
        var preset = MachineTypeCatalog.Resolve(machineTypeId);
        var profile = settings.Load();
        profile.MachineTypeId = machineTypeId;
        profile.Planes = preset.CreateDefaultPlanes();
        profile.OptionalAxes = [];
        profile.DemoAxes = preset.DefaultDemoAxes;
        if (withRotaryAxis)
        {
            profile.OptionalAxes.Add(new MachineAxis
            {
                Index = 3,
                Kind = AxisKind.Rotary,
                Direction = AxisDirection.Length,
                RotationOrigin = [0d, 0d],
            });
        }
        settings.Save(profile);
        return settings;
    }

    private static ServiceProvider MakeProvider(SettingsService settings,
        IMachineKinematicsProvider? kinematics = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(settings);
        services.AddSingleton<ProgramViewModel>();
        if (kinematics is not null)
            services.AddSingleton(kinematics);
        return services.BuildServiceProvider();
    }

    private static MainViewModel MakeViewModel(SettingsService settings, ServiceProvider provider,
        StubDialogs dialogs, int reportedAxes)
    {
        var vm = new MainViewModel(new RecordingGrbl(), settings, provider, dialogs: dialogs,
            info: new CNC.Core.GrblInfoState { NumAxes = reportedAxes });
        provider.GetRequiredService<ProgramViewModel>().LoadGCode("G0 X0 Y0 Z0 A0\nG1 X10 Y10 Z-1 A10");
        return vm;
    }

    [Fact]
    public void Foam_profile_offers_no_wrap_and_no_unwrap()
    {
        // Foam: four axes (DefaultDemoAxes = 4), all linear tower axes.
        var settings = MakeSettings(MachineTypes.Foam, withRotaryAxis: false);
        var dialogs = new StubDialogs();
        using var provider = MakeProvider(settings);
        var vm = MakeViewModel(settings, provider, dialogs, reportedAxes: 4);
        Assert.True(vm.WrapProgramCommand.CanExecute(null));

        vm.WrapProgramCommand.Execute(null);

        Assert.Null(dialogs.LastVm);                       // no wrap target was offered
        Assert.False(string.IsNullOrWhiteSpace(vm.Status)); // the user was told why

        vm.UnwrapProgramCommand.Execute(null);

        Assert.Null(dialogs.LastVm);                       // and no unwrap source either
        Assert.False(string.IsNullOrWhiteSpace(vm.Status));
    }

    [Fact]
    public void Mill_with_a_declared_rotary_axis_offers_exactly_that_axis()
    {
        var settings = MakeSettings(MachineTypes.Mill, withRotaryAxis: true);
        var dialogs = new StubDialogs();
        using var provider = MakeProvider(settings);
        var vm = MakeViewModel(settings, provider, dialogs, reportedAxes: 4);

        vm.WrapProgramCommand.Execute(null);

        var dialog = Assert.IsType<CNC.Core.ViewModels.WrapProgramViewModel>(dialogs.LastVm);
        Assert.Equal([3], dialog.TargetAxes.Select(a => a.Index));
        Assert.Equal(3, dialog.TargetAxis!.Index);         // default selection unchanged
        Assert.Equal([0, 1, 2], dialog.SourceAxes.Select(a => a.Index));
    }

    [Fact]
    public void Gate_reads_the_shared_kinematics_provider()
    {
        // The profile on disk declares NO rotary axis, while the provider (the seam
        // every consumer reads) reports one: the provider wins.
        var settings = MakeSettings(MachineTypes.Mill, withRotaryAxis: false);
        var preset = MachineTypeCatalog.Resolve(MachineTypes.Mill);
        var rotaryProfile = new MachineProfile
        {
            Id = "gate",
            MachineTypeId = MachineTypes.Mill,
            Planes = preset.CreateDefaultPlanes(),
            OptionalAxes =
            [
                new MachineAxis
                {
                    Index = 3,
                    Kind = AxisKind.Rotary,
                    Direction = AxisDirection.Length,
                    RotationOrigin = [0d, 0d],
                },
            ],
        };
        var kinematics = MachineKinematicsResolver.Resolve(rotaryProfile, preset, null, reportedAxes: 4);
        var dialogs = new StubDialogs();
        using var provider = MakeProvider(settings, new FakeKinematics(kinematics));
        var vm = MakeViewModel(settings, provider, dialogs, reportedAxes: 4);

        vm.WrapProgramCommand.Execute(null);

        var dialog = Assert.IsType<CNC.Core.ViewModels.WrapProgramViewModel>(dialogs.LastVm);
        Assert.Equal([3], dialog.TargetAxes.Select(a => a.Index));
    }
}
