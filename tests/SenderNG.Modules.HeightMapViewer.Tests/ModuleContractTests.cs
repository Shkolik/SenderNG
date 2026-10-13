using Microsoft.Extensions.DependencyInjection;
using SenderNG.Core.Modules;
using SenderNG.Modules.HeightMapViewer;

namespace SenderNG.Modules.HeightMapViewer.Tests;

public class ModuleContractTests
{
    [Fact]
    public void Module_exposes_identity_and_metadata()
    {
        var m = new HeightMapViewerModule();
        Assert.Equal("heightmap-viewer", m.Id);
        Assert.Equal(PanelHint.Dialog, m.Panel);
        Assert.False(m.IsVisibleByDefault);
        Assert.True(m.CanHide);
        Assert.False(string.IsNullOrWhiteSpace(m.Version));
        Assert.False(string.IsNullOrWhiteSpace(m.Author));
        Assert.False(string.IsNullOrWhiteSpace(m.License));
        Assert.NotEmpty(m.LocalizationKeys);
    }

    [Fact]
    public void Module_registers_itself_as_renderer()
    {
        var m = new HeightMapViewerModule();
        var sc = new ServiceCollection();
        m.RegisterServices(sc);
        var sp = sc.BuildServiceProvider();
        Assert.Same(m, sp.GetService<IHeightMap3DRenderer>());
    }
}
