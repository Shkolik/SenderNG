using CNC.Core.Abstractions;
using CNC.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using SenderNG.Core.Modules;

namespace SenderNG.Avalonia.Modules.Plugins;

public class CoolantModule : IModule
{
    public string Id => "coolant";
    public string Name => "Coolant";
    public string Description => "Toggle flood and mist coolant as well as coolant overrides.";
    public int Order => 35;
    public PanelHint Panel => PanelHint.Right;
    public bool IsVisibleByDefault => true;
    public IReadOnlyList<HotKey> HotKeys { get; } = [];

    public void RegisterServices(IServiceCollection services) =>
        services.AddSingleton<CoolantViewModel>();

    public void RegisterSettings(AppSettings settings) { }

    public object CreateViewModel(IServiceProvider serviceProvider) =>
        serviceProvider.GetRequiredService<CoolantViewModel>();

    public object CreateView(object viewModel) =>
        new CoolantView { DataContext = viewModel };
}
