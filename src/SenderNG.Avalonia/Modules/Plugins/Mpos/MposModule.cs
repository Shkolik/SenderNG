using CNC.Core.Abstractions;
using CNC.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using SenderNG.Core.Modules;

namespace SenderNG.Avalonia.Modules.Plugins;

public class MposModule : IModule
{
    public string Id => "mpos";
    public string Name => "MPos";
    public string Description => "Show machine-position and work-position read-outs.";
    public int Order => 20;
    public PanelHint Panel => PanelHint.Flyout;
    public bool IsVisibleByDefault => true;
    public IReadOnlyList<HotKey> HotKeys { get; } = [];

    public void RegisterServices(IServiceCollection services) =>
        services.AddSingleton<MposViewModel>();

    public void RegisterSettings(AppSettings settings) { }

    public object CreateViewModel(IServiceProvider serviceProvider) =>
        serviceProvider.GetRequiredService<MposViewModel>();

    public object CreateView(object viewModel) =>
        new MposView { DataContext = viewModel };
}
