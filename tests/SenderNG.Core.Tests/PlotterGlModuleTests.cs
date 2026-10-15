/*
 * PlotterGlModuleTests.cs
 *
 * The GL plotter ships as a true external plugin (own DLL, Modules/ scan).
 * These tests pin the module contract (id, panel hint, locale keys, plugin
 * metadata) and the ScanDirectory discovery of the staged DLL, mirroring the
 * Skia plotter's module tests.
 */

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
using SenderNG.Core.Localization;
using SenderNG.Core.Modules;
using SenderNG.Modules.Fixed;
using SenderNG.Modules.Plotter.GL;
using SenderNG.Modules.Plotter.GL.Scene;
using SenderNG.Modules.Plotter.GL.Settings;

namespace SenderNG.Core.Tests;

public class PlotterGlModuleTests
{
    [Fact]
    public void Module_id_is_distinct_from_skia_plotter()
    {
        var m = new PlotterGlModule();

        Assert.Equal("plotter-gl", m.Id);
        Assert.NotEqual("plotter", m.Id);
    }

    [Fact]
    public void Module_exposes_contract_identity()
    {
        var module = new PlotterGlModule();

        Assert.IsAssignableFrom<IModule>(module);
        Assert.IsAssignableFrom<ILocalizedModule>(module);
        Assert.IsAssignableFrom<IPluginInfo>(module);
        Assert.Equal("plotter-gl", module.Id);
        Assert.Equal(PanelHint.Center, module.Panel);
        Assert.False(module.IsVisibleByDefault);
        Assert.True(module.CanHide);
        Assert.Equal(23, module.Order);
        Assert.Empty(module.HotKeys);
    }

    [Fact]
    public void Module_declares_locale_keys_with_english_fallbacks()
    {
        var module = new PlotterGlModule();

        // Keys follow the module.<id>. scheme (Id = "plotter-gl") so the tab /
        // App Settings / About lookups (module.plotter-gl.name) resolve through
        // the catalog instead of always falling back to the English literals.
        Assert.Equal("Plot 3D", module.LocalizationKeys[$"module.{module.Id}.name"]);
        Assert.Equal("Plot 3D", module.Name);
        Assert.Equal("G-code 3D path plotter: program toolpath with live run overlay.", module.Description);
        Assert.False(string.IsNullOrWhiteSpace(module.LocalizationKeys[$"module.{module.Id}.description"]));
    }

    /// <summary>Every declared key must live under the module's own prefix:
    /// a key outside it can never be reached through the module scheme and
    /// would silently fall back to its English literal forever.</summary>
    [Fact]
    public void Every_localization_key_starts_with_the_module_prefix()
    {
        var module = new PlotterGlModule();

        Assert.NotEmpty(module.LocalizationKeys);
        Assert.All(module.LocalizationKeys.Keys,
            key => Assert.StartsWith("module.plotter-gl.", key));
    }

    [Fact]
    public void Module_carries_plugin_metadata()
    {
        var module = new PlotterGlModule();

        Assert.False(string.IsNullOrWhiteSpace(module.Version));
        Assert.False(string.IsNullOrWhiteSpace(module.Author));
        Assert.False(string.IsNullOrWhiteSpace(module.License));
    }

    [Fact]
    public void ScanDirectory_discovers_the_staged_plotter_gl_dll()
    {
        // The plugin DLL is staged into this project's OutputDir/Modules (same
        // Link trick the app csproj uses) — the real drop-in path.
        var dir = Path.Combine(AppContext.BaseDirectory, "Modules");
        Assert.True(Directory.Exists(dir), "plugin staging missing — rebuild the test project: " + dir);

        var sc = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        ModuleCatalog.ScanDirectory(sc, dir);

        // LoadFrom context: match by full name, not compile-time type identity.
        Assert.Contains(sc, d => d.ServiceType.FullName == "SenderNG.Modules.Plotter.GL.PlotterGlModule");

        using var sp = sc.BuildServiceProvider();
        var modules = sp.GetServices<IModule>().ToList();
        Assert.Contains(modules, m => m.Id == "plotter-gl");
    }

    [Fact]
    public void Module_implements_settings_provider_with_scope_profile()
    {
        var module = new PlotterGlModule();

        Assert.IsAssignableFrom<IModuleSettingsProvider>(module);
        Assert.Equal(SettingsScope.Profile, ((IModuleSettingsProvider)module).Scope);
    }

    /// <summary>The host calls CreateViewModel on the UI thread with exactly
    /// these five services resolvable (ILoadedProgramSource, IGrblService,
    /// SettingsService, LocalizationService, IMachineKinematicsProvider — the
    /// shared stub set plus the machine seam). It must hand back the GL
    /// plotter's own view model, and it must not touch the Avalonia dispatcher
    /// beyond capturing it (headless construction stays inline).</summary>
    [Fact]
    public void CreateViewModel_returns_the_plot_gl_view_model()
    {
        var dir = Path.Combine(Path.GetTempPath(), "plotter-gl-vm-tests-" + Guid.NewGuid().ToString("N"));
        try
        {
            var settings = new SettingsService(dir);
            using var grbl = new DemoGrblService(settings.Load());
            var services = new ServiceCollection()
                .AddSingleton<ILoadedProgramSource>(new ProgramViewModel())
                .AddSingleton<IGrblService>(grbl)
                .AddSingleton(settings)
                .AddSingleton(new LocalizationService(
                    new MemoryStream("{ \"strings\": {} }"u8.ToArray())))
                .AddSingleton<IMachineKinematicsProvider>(
                    sp => new MachineKinematicsProvider(
                        sp.GetRequiredService<SettingsService>(),
                        sp.GetRequiredService<IGrblService>()))
                .BuildServiceProvider();

            var vm = new PlotterGlModule().CreateViewModel(services);

            Assert.IsType<PlotterGlViewModel>(vm);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Settings_view_unavailable_before_services_attached_and_available_after()
    {
        var module = new PlotterGlModule();
        Assert.Null(module.CreateSettingsView()); // not attached yet

        var dir = Path.Combine(Path.GetTempPath(), "plotter-gl-settings-tests-" + Guid.NewGuid().ToString("N"));
        try
        {
            var settingsService = new SettingsService(dir);
            var sc = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
            module.RegisterServices(sc);
            sc.AddSingleton(settingsService);
            sc.AddSingleton(settingsService.Load());
            using var sp = sc.BuildServiceProvider();
            module.AttachServices(sp);

            Assert.NotNull(module.CreateSettingsView());
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Settings_view_binds_a_fresh_model_for_each_create_when_profile_switches()
    {
        var module = new PlotterGlModule();
        var dir = Path.Combine(Path.GetTempPath(), "plotter-gl-settings-tests-" + Guid.NewGuid().ToString("N"));
        try
        {
            var settingsService = new SettingsService(dir);
            var mill = settingsService.Load();
            mill.MachineTypeId = MachineTypes.Mill;
            settingsService.Save(mill);

            var sc = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
            module.RegisterServices(sc);
            sc.AddSingleton(settingsService);
            sc.AddSingleton(settingsService.Load());
            using var sp = sc.BuildServiceProvider();
            module.AttachServices(sp);

            var firstVm = (PlotterGlSettingsViewModel)((PlotterGlSettingsView)module.CreateSettingsView()!).DataContext!;
            Assert.False(firstVm.HasLoft); // Mill: single plane, no sheet

            // Switching the active profile (App Settings profile switch / connect flow)
            // must be reflected by a new CreateSettingsView call.
            var foam = settingsService.CreateProfile("Foam", MachineTypes.Foam);
            settingsService.Activate(foam.Id);

            var secondVm = (PlotterGlSettingsViewModel)((PlotterGlSettingsView)module.CreateSettingsView()!).DataContext!;
            Assert.True(secondVm.HasLoft); // Foam: two planes, so the sheet toggle exists
            Assert.NotSame(firstVm, secondVm);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Settings_leaf_and_overlay_share_the_single_settings_vm()
    {
        var module = new PlotterGlModule();
        var dir = Path.Combine(Path.GetTempPath(), "plotter-gl-settings-tests-" + Guid.NewGuid().ToString("N"));
        try
        {
            var settingsService = new SettingsService(dir);
            var mill = settingsService.Load();
            mill.MachineTypeId = MachineTypes.Mill;
            settingsService.Save(mill);

            var sc = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
            module.RegisterServices(sc);
            sc.AddSingleton(settingsService);
            sc.AddSingleton(settingsService.Load());
            using var sp = sc.BuildServiceProvider();
            module.AttachServices(sp);

            var firstVm = (PlotterGlSettingsViewModel)((PlotterGlSettingsView)module.CreateSettingsView()!).DataContext!;
            // A second App Settings session on the SAME profile reuses the ONE
            // settings VM the overlay toggle bar binds (both surfaces, one model)
            // — no second service, no second clone.
            var secondVm = (PlotterGlSettingsViewModel)((PlotterGlSettingsView)module.CreateSettingsView()!).DataContext!;
            Assert.Same(firstVm, secondVm);

            // Round trip on the shared instance: a flip made through the leaf
            // surface is immediately visible through the overlay-bound surface
            // (SAME VM) — no re-attach, no disk re-read.
            var changed = new System.Collections.Generic.List<string?>();
            firstVm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
            firstVm.ShowGrid = false;
            Assert.False(secondVm.ShowGrid);
            Assert.Contains(nameof(PlotterGlSettingsViewModel.ShowGrid), changed);

            firstVm.ShowGrid = true;
            Assert.True(secondVm.ShowGrid);

            // And the flip persisted per-profile through the shared service.
            var reloaded = PlotterGlConfig.Load(settingsService.Load());
            Assert.Equal(firstVm.ShowGrid, reloaded.ShowGrid);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    /// <summary>
    /// A live controller can report FEWER axes than the machine's highest plane
    /// index — and on the shipped bug-report foam layout plane 2's indices are
    /// <c>[3, 2]</c>, so the plane's LAST entry is 2 while its true maximum is 3.
    /// The guard must test the maximum: with <c>indices[^1]</c> the check
    /// <c>3 &lt;= 2</c> passed and the world mapping then read
    /// <c>positions[3]</c> — an IndexOutOfRangeException on every status poll of
    /// exactly the layout this feature exists for. The cross is abandoned for the
    /// whole tick (the plane cannot be located), and the same report WITH the
    /// fourth axis does move the tool, so the test cannot pass vacuously.
    /// </summary>
    [Fact]
    public void A_short_controller_report_does_not_index_past_the_position_on_a_non_monotonic_plane()
    {
        var dir = Path.Combine(Path.GetTempPath(), "plotter-gl-short-report-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var settings = new SettingsService(dir);
            var profile = settings.Load();
            profile.MachineTypeId = MachineTypes.Foam;
            profile.Planes = MachineTypeCatalog.Resolve(MachineTypes.Foam).CreateDefaultPlanes();
            profile.Planes[1].Axes[0].Index = 3; // slot 0 = A
            profile.Planes[1].Axes[1].Index = 2; // slot 1 = Z
            settings.Save(profile);

            var grbl = new PushStateGrbl();
            var scene = new FakePlotScene();
            var factory = new KinematicSceneFactory(scene);
            using var vm = new PlotterGlViewModel(
                new ProgramViewModel(),
                grbl,
                settings,
                new LocalizationService(new MemoryStream("{ \"error\":\"\" }"u8.ToArray())),
                factory);

            var tool = Assert.IsType<FakePlotTool>(factory.Tool);
            Assert.Single(tool.Positions); // the BuildScene placeholder

            // Three axes against a plane that needs index 3: nothing is drawn for
            // the cross this tick, and nothing throws.
            grbl.Push(new MachineState { Positions = [1, 2, 3], Status = "Idle" });
            Assert.Single(tool.Positions);

            // Four axes: the cross resolves and both carriages move.
            grbl.Push(new MachineState { Positions = [1, 2, 3, 4], Status = "Idle" });
            Assert.Equal(2, tool.Positions.Count);
            var points = tool.Positions[1].Points;
            Assert.Equal(2, points.Length);
            // Plane 1 sits at the separation (0 by default) and spans the vertical
            // plane: A=4 on world Y (the tower's length), Z=3 on world Z (its
            // height) — slot order, not sorted order.
            Assert.Equal(new System.Numerics.Vector3(0, 4, 3), points[1]);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    /// <summary>Minimal controller double whose status stream the test pushes:
    /// the resolved kinematics reads <see cref="IGrblService.Capabilities"/> and
    /// the plotter's live pose reads the state stream.</summary>
    private sealed class PushStateGrbl : IGrblService
    {
        private readonly System.Reactive.Subjects.Subject<MachineState> _state = new();

        public bool IsConnected => true;

        public ControllerCapabilities Capabilities =>
            ControllerCapabilities.Full with { NumAxes = 4, AxisLetters = "XYZA" };

        public IObservable<MachineState> State => _state;

        public void Push(MachineState state) => _state.OnNext(state);

        public Task ConnectAsync(string port) => Task.CompletedTask;
        public void Disconnect() { }
        public Task SendAsync(string line) => Task.CompletedTask;
        public void SendRealtime(byte code) { }
    }
}
