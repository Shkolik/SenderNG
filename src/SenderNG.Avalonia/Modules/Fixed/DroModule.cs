using CNC.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using SenderNG.Core.Modules;

namespace SenderNG.Avalonia.Modules.Fixed;

public class DroModule : IModule
{
    public string Id => "dro";
    public string Name => "DRO";
    public int Order => 10;
    public PanelHint Panel => PanelHint.Left;
    public bool IsVisibleByDefault => true;
    public IReadOnlyList<HotKey> HotKeys { get; } = [];

    public void RegisterServices(IServiceCollection services) =>
        services.AddSingleton<DroViewModel>();

    public void RegisterSettings(AppSettings settings) { }

    public object CreateViewModel(IServiceProvider serviceProvider) =>
        serviceProvider.GetRequiredService<DroViewModel>();

    public object CreateView(object viewModel) =>
        new DroView { DataContext = viewModel };
}
