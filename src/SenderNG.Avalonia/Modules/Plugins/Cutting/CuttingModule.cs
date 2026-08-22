using CNC.Core.Abstractions;
using CNC.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using SenderNG.Core.Modules;

namespace SenderNG.Avalonia.Modules.Plugins;

public class CuttingModule : IModule
{
    public string Id => "cutting";
    public string Name => "Cutting";
    public int Order => 25;
    public PanelHint Panel => PanelHint.Right;
    public bool IsVisibleByDefault => true;
    public IReadOnlyList<HotKey> HotKeys { get; } = [];

    public void RegisterServices(IServiceCollection services) =>
        services.AddSingleton<CuttingViewModel>();

    public void RegisterSettings(AppSettings settings) { }

    public object CreateViewModel(IServiceProvider serviceProvider) =>
        serviceProvider.GetRequiredService<CuttingViewModel>();

    public object CreateView(object viewModel) =>
        new CuttingView { DataContext = viewModel };
}
