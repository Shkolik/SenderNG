/*
 *
 * Copyright (c) 2026 Andrew Shkolik
 *
 * All rights reserved.
 *
 * Redistribution and use in source and binary forms, with or without modification,
 * are permitted provided that the following conditions are met:
 *
 * · Redistributions of source code must retain the above copyright notice, this
 * list of conditions and the following disclaimer.
 *
 * · Redistributions in binary form must reproduce the above copyright notice,
 * this list of conditions and the following disclaimer in the documentation and/or
 * other materials provided with the distribution.
 *
 * · Neither the name of the copyright holder nor the names of its contributors may
 * be used to endorse or promote products derived from this software without
 * specific prior written permission.
 *
 * THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND
 * ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
 * WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
 * DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE FOR
 * ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
 * (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
 * LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON
 * ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
 * (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
 * SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
 *
 */

using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using CNC.Core.Abstractions;
using CNC.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using SenderNG.Core.Localization;
using SenderNG.Core.Modules;
using SenderNG.Modules.Plotter;
using SenderNG.Modules.Plotter.Settings;
using Xunit;

using static SenderNG.Tests.PlotterTestStubs;

/// <summary>The provider tests that need a REAL <see cref="PlotterViewModel"/>
/// live here rather than in SenderNG.Core.Tests, and that relocation is load
/// bearing. <c>PlotterModule.CreateViewModel</c> calls
/// <c>PlotterUi.Initialize()</c>, which captures <c>Dispatcher.UIThread</c>
/// into a process-wide static. Run under xUnit's plain worker pool the first
/// plotter test to touch it binds the dispatcher to ITS thread; every later test
/// on a different thread then fails <c>CheckAccess()</c>, so the view model's
/// marshaled work is queued to a dispatcher nobody pumps and never runs. The
/// result was 20+ unrelated pre-existing <c>PlotterModuleTests</c> failing
/// together, each of which passed alone. <see cref="AvaloniaFactAttribute"/>
/// runs this body ON the headless UI thread, so the capture is correct and the
/// damage is contained to this project.</summary>
public class PlotterModuleSettingsHeadlessTests
{
    private sealed class StubGrbl : IGrblService
    {
        public bool IsConnected => false;
        public ControllerCapabilities Capabilities => ControllerCapabilities.Full with { AxisLetters = "XYZA" };
        public IObservable<MachineState> State => new NeverObservable();
        public Task ConnectAsync(string port) => Task.CompletedTask;
        public void Disconnect() { }
        public Task SendAsync(string line) => Task.CompletedTask;
        public void SendRealtime(byte code) { }

        private sealed class NeverObservable : IObservable<MachineState>
        {
            public IDisposable Subscribe(IObserver<MachineState> observer) => new Noop();
            private sealed class Noop : IDisposable
            {
                public void Dispose() { }
            }
        }
    }

    /// <summary>Two profiles (foam active, mill idle) plus the service provider
    /// the module resolves them through. The section must reach the DISK before
    /// <c>Activate</c>: Activate reloads the graph from the file and
    /// <c>SaveProfile</c> does not refresh it, so a section written only in
    /// memory is invisible to the view model's <c>Load()</c>.</summary>
    private static (SettingsService Settings, ServiceProvider Services, string FoamId, string MillId)
        ModuleHost(PlotterSettings? foamSection = null)
    {
        var dir = Path.Combine(Path.GetTempPath(), "plotter-module-hl-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var settings = new SettingsService(dir);
        var foam = settings.CreateProfile("foam", MachineTypes.Foam);
        var mill = settings.CreateProfile("mill", MachineTypes.Mill);
        if (foamSection is not null)
        {
            PlotterConfig.Save(foam, foamSection);
            settings.SaveProfile(foam);
        }
        settings.Activate(foam.Id);

        var sc = new ServiceCollection();
        sc.AddSingleton(settings);
        sc.AddSingleton(Localizable(dir));
        sc.AddSingleton<ILoadedProgramSource>(new StubProgramSource());
        sc.AddSingleton<IGrblService>(new StubGrbl());
        return (settings, sc.BuildServiceProvider(), foam.Id, mill.Id);
    }

    /// <summary>Directory-backed so <c>SetLocale</c> actually RAISES
    /// <c>Changed</c>: the stream ctor leaves <c>_localeDirectory</c> null and
    /// returns before the raise (LocalizationService.cs:160-161).</summary>
    private static LocalizationService Localizable(string root)
    {
        var dir = Path.Combine(root, "loc");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "en.json"),
            """{"strings":{"module.plotter.settings.view":"VIEW"}}""");
        File.WriteAllText(Path.Combine(dir, "ru.json"),
            """{"strings":{"module.plotter.settings.view":"VIEW-RU"}}""");
        return LocalizationService.LoadFromDirectory(dir, "en");
    }

    /// <summary>The one shared settings model behind the App Settings leaf. Read
    /// through the leaf, which is how the host reaches it — never through a
    /// private field, so a leaf that stopped being bound fails here.</summary>
    private static PlotterSettingsViewModel LeafOf(IModuleSettingsProvider provider) =>
        (PlotterSettingsViewModel)((Control)provider.CreateSettingsView()!).DataContext!;

    [AvaloniaFact]
    public void The_plot_view_starts_on_the_stored_plane_mapping()
    {
        // The plan's "build on the stored mappings rather than identity": opening
        // the Plot tab must show the profile's saved mapping, not a hardcoded
        // identity that ignores the section.
        var (_, services, _, _) = ModuleHost(new PlotterSettings
        {
            Planes = [PlotterPlaneMapper.Identity(2), new PlotterPlaneGeometry { WorldAxes = [1, 0] }],
        });
        var module = new PlotterModule();
        var vm = (PlotterViewModel)module.CreateViewModel(services);

        // Foam plane 1 is mirrored; identity would read "XY | XY".
        Assert.Equal("XY | YX", string.Join(" | ", vm.PlaneLetters));
    }

    [AvaloniaFact]
    public void A_profile_switch_pushes_the_new_profiles_mapping_and_toggles()
    {
        // The active profile is foam with the grid OFF and a mirrored plane 1;
        // the mill profile is untouched. After a switch the plot view must carry
        // the MILL profile's single identity plane and the new toggles — not the
        // old profile's, and not an empty set (a `SetPlaneGeometries([])` first
        // would leave the view reporting no planes at all).
        //
        // Asserted on the pushed MAPPING, not on PlaneLetters: letters render
        // through `GeometryFor(plane)`, which clamps a stored geometry to
        // `PlaneLetters`' OWN machine type — and PlotterViewModel only re-resolves
        // that on a PROGRAM change (PlotterViewModel.OnProgramChanged), not on a
        // profile switch. With no program loaded it still reads "Foam", so
        // letters would report this task's behaviour through an unrelated layer's
        // stale state. The mill's three-axis identity is the unambiguous signal:
        // foam's stored first plane is two-axis, and no push at all leaves zero.
        var (settings, services, _, millId) = ModuleHost(new PlotterSettings
        {
            ShowGrid = false,
            Planes = [PlotterPlaneMapper.Identity(2), new PlotterPlaneGeometry { WorldAxes = [1, 0] }],
        });
        var module = new PlotterModule();
        module.AttachServices(services);
        var plot = (PlotterViewModel)module.CreateViewModel(services);
        Assert.Equal(2, plot.PlaneGeometries.Count);
        Assert.Equal([1, 0], plot.PlaneGeometries[1].WorldAxes);   // foam plane 1 mirrored
        Assert.False(LeafOf((IModuleSettingsProvider)module).ShowGrid);

        settings.Activate(millId);

        var mill = Assert.Single(plot.PlaneGeometries);
        Assert.Equal([0, 1, 2], mill.WorldAxes);                   // mill identity, 3 axes
        Assert.True(LeafOf((IModuleSettingsProvider)module).ShowGrid);
    }
}
