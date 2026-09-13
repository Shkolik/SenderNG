/*
 * PlotterNexModuleTests.cs
 *
 * The Nex plotter ships as a true external plugin (own DLL, Modules/ scan), a
 * sibling of the Skia plotter. These tests pin the module contract (id, panel
 * hint, locale keys, plugin metadata) and the ScanDirectory discovery of the
 * staged DLL, mirroring PlotterModuleTests.
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

        Assert.Equal("Plot 3D", module.LocalizationKeys["module.plotterNex.name"]);
        Assert.Equal("Plot 3D", module.Name);
        Assert.Equal("G-code 3D path plotter: HelixToolkit.Nex orbit view (PoC).", module.Description);
        Assert.False(string.IsNullOrWhiteSpace(module.LocalizationKeys["module.plotterNex.description"]));
        Assert.False(string.IsNullOrWhiteSpace(module.LocalizationKeys["module.plotterNex.placeholder"]));
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
}
