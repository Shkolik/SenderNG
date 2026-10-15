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

using System.Text.Json;
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
    private static (SettingsService Settings, ServiceProvider Services, string FoamId, string MillId,
        RecordingDialogService Dialogs)
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

        var dialogs = new RecordingDialogService();
        var sc = new ServiceCollection();
        sc.AddSingleton(settings);
        sc.AddSingleton(Localizable(dir));
        sc.AddSingleton<ILoadedProgramSource>(new StubProgramSource());
        sc.AddSingleton<IGrblService>(new StubGrbl());
        // Registered here so the DI wiring the module depends on is the same
        // container every test in this class builds — including the machine seam
        // the module resolves its view model through.
        sc.AddSingleton<IFileDialogService>(dialogs);
        sc.AddSingleton<IMachineKinematicsProvider>(
            sp => new MachineKinematicsProvider(
                sp.GetRequiredService<SettingsService>(),
                sp.GetRequiredService<IGrblService>()));
        return (settings, sc.BuildServiceProvider(), foam.Id, mill.Id, dialogs);
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
    public void The_plot_view_reads_its_planes_from_the_machine_not_from_the_section()
    {
        // The plane layout is the machine model's. Opening the Plot tab must show
        // the ACTIVE machine's planes and letters; the section carries only the
        // view toggles now (the per-plotter mapping is deleted rather than moved).
        var (_, services, _, _, _) = ModuleHost(new PlotterSettings { ShowGrid = false });
        var module = new PlotterModule();
        var vm = (PlotterViewModel)module.CreateViewModel(services);

        // The active profile is foam: two planes, machine axes X,Y then Z,A.
        Assert.Equal(2, vm.PlaneCount);
        Assert.Equal([0, 1], vm.PlaneAxisIndices(0));
        Assert.Equal([2, 3], vm.PlaneAxisIndices(1));
        Assert.Equal("XY | ZA", string.Join(" | ", vm.PlaneLetters));
    }

    [AvaloniaFact]
    public void A_profile_switch_moves_the_plot_view_to_the_new_machines_planes_and_toggles()
    {
        // The active profile is foam with the grid OFF; the mill profile is
        // untouched. After a switch the plot view must carry the MILL machine's
        // single three-axis plane and the new toggles — not the old profile's,
        // and not an empty set.
        //
        // Asserted on the plane count and slices, which is what the profile
        // switch actually changes now: the per-plotter mapping was deleted, so
        // there is nothing for the module to push onto the view.
        var (settings, services, _, millId, _) = ModuleHost(new PlotterSettings
        {
            ShowGrid = false,
        });
        var module = new PlotterModule();
        module.AttachServices(services);
        var plot = (PlotterViewModel)module.CreateViewModel(services);
        Assert.Equal(2, plot.PlaneCount);
        Assert.Equal([2, 3], plot.PlaneAxisIndices(1));
        Assert.False(LeafOf((IModuleSettingsProvider)module).ShowGrid);

        settings.Activate(millId);

        Assert.Equal(1, plot.PlaneCount);
        Assert.Equal([0, 1, 2], plot.PlaneAxisIndices(0));
        Assert.Equal("XYZ", string.Join(" | ", plot.PlaneLetters));
        Assert.True(LeafOf((IModuleSettingsProvider)module).ShowGrid);
    }

    /// <summary>After a profile switch, the overlay's checkboxes must edit the
    /// NEW profile, and the old profile's file must be left alone.
    /// <para>Asserted on FILE CONTENTS, deliberately. The obvious weaker test —
    /// "the overlay's model is not the old instance" — passes while the data is
    /// still being written to the wrong place, and it passed here: the module
    /// disposed the outgoing model and built a replacement, but never re-pointed
    /// the view, so the overlay kept binding the DISPOSED one. Every checkbox
    /// wrote through it and <c>Persist()</c> saved the PREVIOUS profile's
    /// section, so switching profiles and toggling anything silently rewrote the
    /// old profile and discarded the edit.</para>
    /// <para>So the assertion is end-to-end through the disk: toggle one flag on
    /// the mill profile via the overlay, then read BOTH
    /// <c>profiles/&lt;id&gt;.json</c> files back off disk. The mill file must
    /// carry the flag and the foam file must not — which is the only statement
    /// that distinguishes "wrote to the right profile" from "wrote somewhere".</para></summary>
    [AvaloniaFact]
    public void A_profile_switch_rebinds_the_overlay_so_an_edit_lands_on_the_new_profile()
    {
        // Foam stores ShowGrid = TRUE. The mill profile has no plotter section at
        // all, so its overlay model is SYNTHESISED DEFAULTS — all toggles on,
        // ShowGrid = true as well. The edit therefore has to move the flag OFF:
        // setting it to true is a no-op (the setter ignores an unchanged value),
        // which would leave both files untouched and the test would pass for the
        // wrong reason.
        //
        // That makes the two files distinguishable, which is the whole point:
        // foam = true before, mill = true before, mill = FALSE after. A correct
        // run leaves foam at true; the broken one writes false into FOAM.
        var (settings, services, foamId, millId, _) = ModuleHost(new PlotterSettings
        {
            ShowGrid = true,
        });
        var module = new PlotterModule();
        module.AttachServices(services);
        _ = module.CreateViewModel(services);
        // The VIEW has to exist: the binding under test is the one the overlay
        // holds, and only CreateView gives the module a view to re-point.
        var view = (PlotterView)module.CreateView(new object());
        Assert.NotNull(view.OverlaySettings);

        settings.Activate(millId);

        // Edit through the overlay — the surface the user actually touches.
        var overlay = view.OverlaySettings!;
        overlay.ShowGrid = false;

        // Foam stores the grid ON; the mill profile has no plotter section at
        // all, so its overlay model is synthesized defaults — the grid is on
        // there too. The edit therefore has to move the flag OFF, and setting
        // it back to true would be a no-op the setter ignores, leaving both
        // files untouched and the test green for the wrong reason.
        Assert.False(GridOnDisk(settings, millId),
            "the edit must reach the ACTIVE profile's file");
        Assert.True(GridOnDisk(settings, foamId),
            "the previous profile's file must be untouched");
    }

    /// <summary>The view the MODULE builds must arrive fully wired: the app's
    /// dialog service for the PNG button, and a live overlay model for the
    /// chrome checkboxes.
    /// <para>Both were broken in the same two lines, in ways that looked fine.
    /// The view was constructed with the parameterless ctor, so <c>_dialogs</c>
    /// was null in the only production path there is — the field's own doc
    /// claimed null happened "wherever the module builds the view without the
    /// app's DI", which was everywhere. And the overlay read the
    /// <c>_sharedSettingsVm</c> FIELD rather than the accessor, so it bound
    /// whatever happened to be cached: null if <c>CreateView</c> ran before
    /// <c>CreateViewModel</c>, and null again if the accessor had swallowed a
    /// service-lookup failure. A null overlay model makes every checkbox dead
    /// with no diagnostic anywhere.</para></summary>
    [AvaloniaFact]
    public void The_view_the_module_builds_carries_the_apps_dialogs_and_a_live_overlay()
    {
        var (_, services, _, _, dialogs) = ModuleHost();

        var module = new PlotterModule();
        module.AttachServices(services);
        // View BEFORE view model, on purpose. The order the host happens to use
        // is not a contract, and it is the order that exposes the difference
        // between reading the `_sharedSettingsVm` FIELD and going through the
        // accessor: at this point the field is still null, so the field version
        // binds the overlay to nothing and every checkbox in the view is dead.
        var view = (PlotterView)module.CreateView(new object());
        _ = module.CreateViewModel(services);

        Assert.Same(dialogs, view.DialogsForTest);
        Assert.NotNull(view.OverlaySettings);
        // Same instance the App Settings leaf edits — the two surfaces drift
        // apart the moment they are not.
        Assert.Same(view.OverlaySettings, LeafOf((IModuleSettingsProvider)module));
    }

    /// <summary>Only its identity matters here; nothing calls it yet because
    /// PNG export is still a stub.</summary>
    private sealed class RecordingDialogService : IFileDialogService
    {
        public Task<string?> OpenFileAsync(string title, params string[] filters) =>
            Task.FromResult<string?>(null);

        public Task<string?> SaveFileAsync(string title, string suggestedFileName, params string[] filters) =>
            Task.FromResult<string?>(null);

        public Task ShowErrorAsync(string title, string message) => Task.CompletedTask;
    }

    /// <summary>Reads a profile's persisted <c>ShowGrid</c> back OFF DISK, so
    /// the assertion is about what a restart would see rather than about
    /// anything still in memory.</summary>
    /// <remarks>Two levels of indirection, both easy to get wrong: the section
    /// is stored as a JSON <i>string</i> under
    /// <c>ModuleSettings</c>&nbsp;→&nbsp;<c>plotter</c>, and inside it every
    /// quote comes back as <c>\u0022</c>. Substring-matching the raw text fails
    /// on both counts — a <c>"ShowGrid":false</c> needle never matches, and
    /// reading <c>plotter</c> off the root throws or reports "absent", which
    /// looks exactly like "the write went nowhere".</remarks>
    /// <remarks>A MISSING section deliberately throws instead of reading as
    /// "grid off". Defaulting it to false would make the broken case pass:
    /// dropping the edit leaves the mill profile with no section at all, which
    /// under that fallback would answer the assertion it is supposed to
    /// fail.</remarks>
    private static bool GridOnDisk(SettingsService settings, string profileId)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(
            ProfileSettingsPaths.ProfilePath(settings.SettingsDir, profileId)));
        var section = doc.RootElement
            .GetProperty("ModuleSettings")
            .GetProperty(PlotterConfig.SectionKey)
            .GetString()!;
        using var inner = JsonDocument.Parse(section);
        return inner.RootElement.GetProperty(nameof(PlotterSettings.ShowGrid)).GetBoolean();
    }
}
