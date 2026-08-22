using CNC.Core.Abstractions;
using CNC.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using SenderNG.Avalonia.ViewModels;
using SenderNG.Core.Modules;

namespace SenderNG.Core.Tests;

public class MainViewModelTests
{
    private sealed class StubModule : IModule
    {
        public string Id => "stub";
        public string Name => "Stub";
        public int Order => 0;
        public IReadOnlyList<HotKey> HotKeys { get; } = [];
        public void RegisterServices(IServiceCollection services) { }
        public void RegisterSettings(AppSettings settings) { }
        public object CreateViewModel(IServiceProvider serviceProvider) => new object();
        public object CreateView(object viewModel) => new object();
    }

    private static SettingsService MakeSettings()
    {
        var dir = Path.Combine(Path.GetTempPath(), "senderng-tests-" + Guid.NewGuid().ToString("N"));
        return new SettingsService(dir);
    }

    private static ServiceProvider MakeProvider(SettingsService settings)
    {
        var services = new ServiceCollection();
        services.AddSingleton(settings);
        services.AddSingleton(_ => new DemoGrblService(settings.Load()));
        services.AddSingleton<RealGrblService>();
        return services.BuildServiceProvider();
    }

    [Fact]
    public void MainViewModel_IsDemo_true_initially_when_demo_service_registered()
    {
        var settings = MakeSettings();
        var vm = new MainViewModel(new DemoGrblService(new AppSettings()), settings, MakeProvider(settings));

        Assert.True(vm.IsDemo);
        Assert.IsType<DemoGrblService>(vm.Grbl);
    }

    [Fact]
    public void MainViewModel_seeds_modules_from_injected_modules()
    {
        var settings = MakeSettings();
        IModule[] modules = [new StubModule(), new StubModule()];
        var vm = new MainViewModel(new DemoGrblService(new AppSettings()), settings, MakeProvider(settings), modules: modules);

        Assert.Equal(2, vm.Modules.Count);
        Assert.All(vm.Modules, m => Assert.IsType<StubModule>(m));
    }

    [Fact]
    public void MainViewModel_toggles_demo_and_swaps_service_reusing_di_singletons()
    {
        var settings = MakeSettings();
        var provider = MakeProvider(settings);
        var vm = new MainViewModel(provider.GetRequiredService<DemoGrblService>(), settings, provider);

        vm.DemoToggleCommand.Execute(null);

        Assert.False(vm.IsDemo);
        Assert.Same(provider.GetRequiredService<RealGrblService>(), vm.Grbl);

        vm.DemoToggleCommand.Execute(null);

        Assert.True(vm.IsDemo);
        Assert.Same(provider.GetRequiredService<DemoGrblService>(), vm.Grbl);
    }

    [Fact]
    public void MainViewModel_toggles_proxy_inner_and_keeps_proxy_instance()
    {
        var settings = MakeSettings();
        var provider = MakeProvider(settings);
        var proxy = new GrblServiceProxy(provider.GetRequiredService<DemoGrblService>());
        var vm = new MainViewModel(proxy, settings, provider);

        Assert.True(vm.IsDemo);
        Assert.Same(proxy, vm.Grbl);

        vm.DemoToggleCommand.Execute(null);

        Assert.False(vm.IsDemo);
        Assert.Same(provider.GetRequiredService<RealGrblService>(), proxy.Inner);

        vm.DemoToggleCommand.Execute(null);

        Assert.True(vm.IsDemo);
        Assert.Same(provider.GetRequiredService<DemoGrblService>(), proxy.Inner);
    }

    [Fact]
    public void SelectModuleCommand_builds_view_via_factory()
    {
        var settings = MakeSettings();
        IModule[] modules = [new StubModule()];
        Func<IModule, object> factory = m => new ViewStub();
        var vm = new MainViewModel(new DemoGrblService(new AppSettings()), settings, MakeProvider(settings), modules: modules, viewFactory: factory);

        Assert.Null(vm.SelectedModule);
        Assert.Null(vm.CurrentModuleView);

        vm.SelectModuleCommand.Execute(modules[0]);

        Assert.Same(modules[0], vm.SelectedModule);
        Assert.IsType<ViewStub>(vm.CurrentModuleView);
    }

    [Fact]
    public void SelectModuleCommand_ignores_null_parameter()
    {
        var settings = MakeSettings();
        var vm = new MainViewModel(new DemoGrblService(new AppSettings()), settings, MakeProvider(settings), viewFactory: _ => new ViewStub());

        vm.SelectModuleCommand.Execute(null);

        Assert.Null(vm.SelectedModule);
        Assert.Null(vm.CurrentModuleView);
    }

    private sealed class ViewStub;

    [Fact]
    public async Task ConnectCommand_sets_status_connected_on_success()
    {
        var settings = MakeSettings();
        var vm = new MainViewModel(new DemoGrblService(new AppSettings()), settings, MakeProvider(settings));

        vm.ConnectCommand.Execute(null);
        await Task.Yield();

        Assert.Contains("Connected", vm.Status);
    }

    private sealed class StubComPorts(string[] ports) : IComPortService
    {
        public string[] GetPorts() => ports;
    }

    [Fact]
    public async Task ConnectAsync_with_auto_port_picks_first_enumerated_port()
    {
        var settings = MakeSettings();
        var vm = new MainViewModel(
            new DemoGrblService(new AppSettings()), settings, MakeProvider(settings),
            comPorts: new StubComPorts(["COM3", "COM7"]));

        vm.ConnectCommand.Execute(null);
        await Task.Yield();

        Assert.Contains("COM3", vm.Status);
    }

    [Fact]
    public async Task ConnectAsync_with_auto_port_and_no_ports_reports_no_ports_found()
    {
        var settings = MakeSettings();
        var vm = new MainViewModel(
            new DemoGrblService(new AppSettings()), settings, MakeProvider(settings),
            comPorts: new StubComPorts([]));

        vm.ConnectCommand.Execute(null);
        await Task.Yield();

        Assert.Equal("No ports found", vm.Status);
    }

    [Fact]
    public async Task ConnectCommand_sets_status_failed_and_does_not_throw_on_bad_port()
    {
        var settings = MakeSettings();
        settings.Save(new AppSettings { Port = "nonexistent-port-xyz" });
        var vm = new MainViewModel(new RealGrblService(), settings, MakeProvider(settings));

        vm.ConnectCommand.Execute(null);
        await Task.Yield();

        Assert.StartsWith("Connection failed", vm.Status);
    }
}
