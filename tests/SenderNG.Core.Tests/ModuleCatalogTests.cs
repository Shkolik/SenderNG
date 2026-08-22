using CNC.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using SenderNG.Core.Modules;
using Xunit;

namespace SenderNG.Core.Tests;

public class ModuleCatalogTests
{
    private sealed class TestService { }

    public sealed class TestModule : IModule
    {
        public string Id => "Test";
        public string Name => "Test Module";
        public int Order => 0;
        public IReadOnlyList<HotKey> HotKeys => Array.Empty<HotKey>();

        public void RegisterServices(IServiceCollection services) =>
            services.AddSingleton<TestService>();

        public void RegisterSettings(AppSettings settings) { }

        public object CreateViewModel(IServiceProvider serviceProvider) => new object();
        public object CreateView(object viewModel) => new object();
    }

    [Fact]
    public void RegisterServices_registers_services()
    {
        var sc = new ServiceCollection();
        new TestModule().RegisterServices(sc);
        Assert.Single(sc);
    }

    [Fact]
    public void Scan_discovers_module_and_registers_it_as_singleton()
    {
        var sc = new ServiceCollection();
        ModuleCatalog.Scan(sc, typeof(TestModule).Assembly);

        var descriptor = Assert.Single(sc, sd => sd.ServiceType == typeof(TestModule));
        Assert.Equal(typeof(TestModule), descriptor.ImplementationType);
        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
    }

    [Fact]
    public void Scan_applies_module_service_registrations()
    {
        var sc = new ServiceCollection();
        ModuleCatalog.Scan(sc, typeof(TestModule).Assembly);

        using var sp = sc.BuildServiceProvider();
        Assert.NotNull(sp.GetService<TestService>());
    }

    [Fact]
    public void Scan_does_not_duplicate_already_registered_modules()
    {
        var sc = new ServiceCollection();
        ModuleCatalog.Scan(sc, typeof(TestModule).Assembly);
        ModuleCatalog.Scan(sc, typeof(TestModule).Assembly);

        Assert.Single(sc, sd => sd.ServiceType == typeof(TestModule));
    }

    [Fact]
    public void Scan_discovers_dummy_module_from_external_assembly()
    {
        // SenderNG.Modules.Dummy is a separate project (external plugin assembly);
        // the host passes it to Scan exactly like this.
        var sc = new ServiceCollection();
        ModuleCatalog.Scan(sc, typeof(SenderNG.Modules.Dummy.DummyModule).Assembly);

        using var sp = sc.BuildServiceProvider();
        var modules = sp.GetServices<IModule>().ToList();

        var dummy = Assert.Single(modules, m => m.Id == "dummy");
        Assert.IsType<SenderNG.Modules.Dummy.DummyModule>(dummy);
        Assert.Equal("Dummy", dummy.Name);
        Assert.Equal(100, dummy.Order);
        Assert.Equal(PanelHint.Flyout, dummy.Panel);
        Assert.True(dummy.IsVisibleByDefault);
    }

    [Fact]
    public void HotKey_record_holds_gesture_and_action()
    {
        var hk = new HotKey("Ctrl+O", "File.Open");
        Assert.Equal("Ctrl+O", hk.Gesture);
        Assert.Equal("File.Open", hk.ActionId);
    }
}
