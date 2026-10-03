/*
 * PlotterNexModuleTests.cs
 *
 * The Nex plotter ships as a true external plugin (own DLL, Modules/ scan), a
 * sibling of the Skia plotter. These tests pin the module contract (id, panel
 * hint, locale keys, plugin metadata) and the ScanDirectory discovery of the
 * staged DLL, mirroring PlotterModuleTests.
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


using CNC.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using SenderNG.Core.Modules;
using SenderNG.Modules.Plotter.Nex;
using SenderNG.Modules.Plotter.Nex.Settings;

namespace SenderNG.Core.Tests;

public class PlotterNexModuleTests
{
    [Fact]
    public void Module_id_is_distinct_from_skia_plotter()
    {
        var m = new PlotterNexModule();

        Assert.Equal("plotter-nex", m.Id);
        Assert.NotEqual("plotter", m.Id);
    }

    [Fact]
    public void Module_exposes_contract_identity()
    {
        var module = new PlotterNexModule();

        Assert.IsAssignableFrom<IModule>(module);
        Assert.IsAssignableFrom<ILocalizedModule>(module);
        Assert.IsAssignableFrom<IPluginInfo>(module);
        Assert.Equal("plotter-nex", module.Id);
        Assert.Equal(PanelHint.Center, module.Panel);
        Assert.False(module.IsVisibleByDefault);
        Assert.True(module.CanHide);
        Assert.Equal(23, module.Order);
        Assert.Empty(module.HotKeys);
    }

    [Fact]
    public void Module_declares_locale_keys_with_english_fallbacks()
    {
        var module = new PlotterNexModule();

        // Keys follow the module.<id>. scheme (Id = "plotter-nex") so the tab /
        // App Settings / About lookups (module.plotter-nex.name) resolve through
        // the catalog instead of always falling back to the English literals.
        Assert.Equal("Plot 3D", module.LocalizationKeys[$"module.{module.Id}.name"]);
        Assert.Equal("Plot 3D", module.Name);
        Assert.Equal("G-code 3D path plotter: program toolpath with live run overlay.", module.Description);
        Assert.False(string.IsNullOrWhiteSpace(module.LocalizationKeys[$"module.{module.Id}.description"]));
        Assert.False(string.IsNullOrWhiteSpace(module.LocalizationKeys[$"module.{module.Id}.placeholder"]));
    }

    [Fact]
    public void Module_carries_plugin_metadata()
    {
        var module = new PlotterNexModule();

        Assert.False(string.IsNullOrWhiteSpace(module.Version));
        Assert.False(string.IsNullOrWhiteSpace(module.Author));
        Assert.False(string.IsNullOrWhiteSpace(module.License));
    }

    [Fact]
    public void ScanDirectory_discovers_the_staged_plotter_nex_dll()
    {
        // The plugin DLL is staged into this project's OutputDir/Modules (same
        // Link trick the app csproj uses) — the real drop-in path.
        var dir = Path.Combine(AppContext.BaseDirectory, "Modules");
        Assert.True(Directory.Exists(dir), "plugin staging missing — rebuild the test project: " + dir);

        var sc = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        ModuleCatalog.ScanDirectory(sc, dir);

        // LoadFrom context: match by full name, not compile-time type identity.
        Assert.Contains(sc, d => d.ServiceType.FullName == "SenderNG.Modules.Plotter.Nex.PlotterNexModule");

        using var sp = sc.BuildServiceProvider();
        var modules = sp.GetServices<IModule>().ToList();
        Assert.Contains(modules, m => m.Id == "plotter-nex");
    }

    [Fact]
    public void Module_implements_settings_provider_with_scope_profile()
    {
        var module = new PlotterNexModule();

        Assert.IsAssignableFrom<IModuleSettingsProvider>(module);
        Assert.Equal(SettingsScope.Profile, ((IModuleSettingsProvider)module).Scope);
    }

    [Fact]
    public void Settings_view_unavailable_before_services_attached_and_available_after()
    {
        var module = new PlotterNexModule();
        Assert.Null(module.CreateSettingsView()); // not attached yet

        var dir = Path.Combine(Path.GetTempPath(), "plotter-nex-settings-tests-" + Guid.NewGuid().ToString("N"));
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
        var module = new PlotterNexModule();
        var dir = Path.Combine(Path.GetTempPath(), "plotter-nex-settings-tests-" + Guid.NewGuid().ToString("N"));
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

            var firstVm = (PlotterNexSettingsViewModel)((PlotterNexSettingsView)module.CreateSettingsView()!).DataContext!;
            Assert.False(firstVm.ShowWireLength); // Mill: single plane
            Assert.Single(firstVm.PlaneRows);

            // Switching the active profile (App Settings profile switch / connect flow)
            // must be reflected by a new CreateSettingsView call.
            var foam = settingsService.CreateProfile("Foam", MachineTypes.Foam);
            settingsService.Activate(foam.Id);

            var secondVm = (PlotterNexSettingsViewModel)((PlotterNexSettingsView)module.CreateSettingsView()!).DataContext!;
            Assert.True(secondVm.ShowWireLength); // Foam: wire length + two planes
            Assert.Equal(2, secondVm.PlaneRows.Count);
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
        var module = new PlotterNexModule();
        var dir = Path.Combine(Path.GetTempPath(), "plotter-nex-settings-tests-" + Guid.NewGuid().ToString("N"));
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

            var firstVm = (PlotterNexSettingsViewModel)((PlotterNexSettingsView)module.CreateSettingsView()!).DataContext!;
            // A second App Settings session on the SAME profile reuses the ONE
            // settings VM the overlay toggle bar binds (both surfaces, one model)
            // — no second service, no second clone.
            var secondVm = (PlotterNexSettingsViewModel)((PlotterNexSettingsView)module.CreateSettingsView()!).DataContext!;
            Assert.Same(firstVm, secondVm);

            // Round trip on the shared instance: a flip made through the leaf
            // surface is immediately visible through the overlay-bound surface
            // (SAME VM) — no re-attach, no disk re-read.
            var changed = new System.Collections.Generic.List<string?>();
            firstVm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
            firstVm.ShowGrid = false;
            Assert.False(secondVm.ShowGrid);
            Assert.Contains(nameof(PlotterNexSettingsViewModel.ShowGrid), changed);

            firstVm.ShowGrid = true;
            Assert.True(secondVm.ShowGrid);

            // And the flip persisted per-profile through the shared service.
            var reloaded = PlotterNexConfig.Load(settingsService.Load(), MachineTypeCatalog.Resolve(MachineTypes.Mill));
            Assert.Equal(firstVm.ShowGrid, reloaded.ShowGrid);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }
}
