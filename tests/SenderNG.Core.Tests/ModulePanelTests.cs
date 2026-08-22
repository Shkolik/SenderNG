using CNC.Core.Services;
using SenderNG.Core.Modules;
using Xunit;

public class ModulePanelTests
{
    [Fact]
    public void New_PanelHint_property_exists()
    {
        var m = new DummyModule();
        Assert.Equal(PanelHint.Center, m.Panel);
        Assert.True(m.IsVisibleByDefault);
    }

    private class DummyModule : IModule
    {
        public string Id => "dummy";
        public string Name => "Dummy";
        public int Order => 0;
        public PanelHint Panel => PanelHint.Center;
        public bool IsVisibleByDefault => true;
        public IReadOnlyList<HotKey> HotKeys => [];
        public void RegisterServices(Microsoft.Extensions.DependencyInjection.IServiceCollection s) { }
        public void RegisterSettings(AppSettings s) { }
        public object CreateViewModel(System.IServiceProvider sp) => new object();
        public object CreateView(object vm) => new object();
    }
}
