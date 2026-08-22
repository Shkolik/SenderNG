using CNC.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using SenderNG.Core.Modules;

namespace SenderNG.Avalonia.Modules.Fixed;

public class OffsetsModule : IModule
{
    public string Id => "offsets";
    public string Name => "Offsets";
    public int Order => 30;
    public PanelHint Panel => PanelHint.Left;
    public bool IsVisibleByDefault => true;
    public IReadOnlyList<HotKey> HotKeys { get; } = [];

    public void RegisterServices(IServiceCollection services) =>
        services.AddSingleton<OffsetsViewModel>();

    public void RegisterSettings(AppSettings settings) { }

    public object CreateViewModel(IServiceProvider serviceProvider) =>
        serviceProvider.GetRequiredService<OffsetsViewModel>();

    public object CreateView(object viewModel) =>
        new OffsetsView { DataContext = viewModel };
}
