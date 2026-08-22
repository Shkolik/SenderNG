using CNC.Core.Abstractions;
using CNC.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using SenderNG.Core.Modules;

namespace SenderNG.Avalonia.Modules.Plugins;

public class ProbingModule : IModule
{
    public string Id => "probing";
    public string Name => "Probing";
    public int Order => 50;
    public PanelHint Panel => PanelHint.Right;
    public bool IsVisibleByDefault => true;
    public IReadOnlyList<HotKey> HotKeys { get; } = [];

    public void RegisterServices(IServiceCollection services) =>
        services.AddSingleton<ProbeViewModel>();

    public void RegisterSettings(AppSettings settings)
    {
        if (!settings.Modules.ContainsKey("Probing"))
            settings.Modules["Probing"] = new Dictionary<string, object>
            {
                ["ProbeDepth"] = 10.0,
                ["ProbeFeed"] = 100.0,
            };
    }

    public object CreateViewModel(IServiceProvider serviceProvider) =>
        new ProbeViewModel(
            serviceProvider.GetRequiredService<IGrblService>(),
            serviceProvider.GetRequiredService<SettingsService>());

    public object CreateView(object viewModel) =>
        new ProbeView { DataContext = viewModel };
}
