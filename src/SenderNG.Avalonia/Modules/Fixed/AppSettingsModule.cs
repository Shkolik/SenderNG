using CNC.Core.Services;
using CNC.Core.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using SenderNG.Core.Modules;

namespace SenderNG.Avalonia.Modules.Fixed;

public class AppSettingsModule : IModule
{
    public string Id => "app-settings";
    public string Name => "App Settings";
    public int Order => 41;
    public PanelHint Panel => PanelHint.Menu;
    public bool IsVisibleByDefault => true;
    public IReadOnlyList<HotKey> HotKeys { get; } = [];

    public void RegisterServices(IServiceCollection services) =>
        services.AddSingleton(sp => new AppSettingsViewModel(
            sp.GetRequiredService<SettingsService>(),
            sp.GetServices<IModule>()));

    public void RegisterSettings(AppSettings settings) { }

    public object CreateViewModel(IServiceProvider serviceProvider) =>
        serviceProvider.GetRequiredService<AppSettingsViewModel>();

    public object CreateView(object viewModel) =>
        new AppSettingsView { DataContext = viewModel };

    // Content-heavy wrap grid: fixed resizable window instead of size-to-content.
    public (double Width, double Height)? DialogPreferredSize => (660.0, 520.0);
}
