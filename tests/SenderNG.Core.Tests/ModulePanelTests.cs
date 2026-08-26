using CNC.Core.Services;
using SenderNG.Avalonia.Modules.Fixed;
using SenderNG.Avalonia.Modules.Plugins;
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

    [Fact]
    public void DIM_fallback_gives_Center_and_visible_true_when_not_overridden()
    {
        IModule m = new BareModule();
        Assert.Equal(PanelHint.Center, m.Panel);
        Assert.True(m.IsVisibleByDefault);
    }

    [Fact]
    public void BuiltIn_modules_have_explicit_Panel_overrides_per_spec()
    {
        Assert.Equal(PanelHint.Left, new DroModule().Panel);
        Assert.Equal(10, new DroModule().Order);
        Assert.True(new DroModule().IsVisibleByDefault);

        Assert.Equal(PanelHint.Center, new ProgramModule().Panel);
        Assert.Equal(20, new ProgramModule().Order);

        Assert.Equal(PanelHint.Center, new ConsoleModule().Panel);
        Assert.Equal(21, new ConsoleModule().Order);

        Assert.Equal(PanelHint.Left, new OffsetsModule().Panel);
        Assert.Equal(30, new OffsetsModule().Order);

        // First menu-claiming module: opens as a modal dialog from Settings.
        Assert.Equal(PanelHint.Menu, new GrblSettingsModule().Panel);
        Assert.Equal(40, new GrblSettingsModule().Order);

        // Wide widget (foam/multiaxis) — claims the flyout drawer.
        Assert.Equal(PanelHint.Flyout, new JogModule().Panel);
        Assert.Equal(15, new JogModule().Order);

        Assert.Equal(PanelHint.Right, new CuttingModule().Panel);
        Assert.Equal(25, new CuttingModule().Order);

        Assert.Equal(PanelHint.Right, new CoolantModule().Panel);
        Assert.Equal(35, new CoolantModule().Order);

        Assert.Equal(PanelHint.Flyout, new MacroModule().Panel);
        Assert.Equal(45, new MacroModule().Order);

        Assert.Equal(PanelHint.Center, new ProbingModule().Panel);
        Assert.Equal(50, new ProbingModule().Order);
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

    private class BareModule : IModule
    {
        public string Id => "bare";
        public string Name => "Bare";
        public int Order => 99;
        public IReadOnlyList<HotKey> HotKeys => [];
        public void RegisterServices(Microsoft.Extensions.DependencyInjection.IServiceCollection s) { }
        public void RegisterSettings(AppSettings s) { }
        public object CreateViewModel(System.IServiceProvider sp) => new object();
        public object CreateView(object vm) => new object();
    }
}
