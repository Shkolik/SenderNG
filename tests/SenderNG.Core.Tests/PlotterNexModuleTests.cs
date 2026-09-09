/*
 * PlotterNexModuleTests.cs
 *
 * The Nex plotter ships as a true external plugin (own DLL, Modules/ scan), a
 * sibling of the Skia plotter. These tests pin the module contract (id, panel
 * hint, locale keys, plugin metadata) and the ScanDirectory discovery of the
 * staged DLL, mirroring PlotterModuleTests.
 */

using Microsoft.Extensions.DependencyInjection;
using SenderNG.Core.Modules;
using SenderNG.Modules.Plotter.Nex;

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
}
