using CNC.Core.Abstractions;
using CNC.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using SenderNG.Core.Modules;

namespace SenderNG.Avalonia.Modules.Fixed;

public class GrblSettingsModule : IModule
{
    public string Id => "grbl-settings";
    public string Name => "GRBL Settings";
    public int Order => 40;
    // First module claiming the menu: opened as a modal dialog from Settings.
    public PanelHint Panel => PanelHint.Menu;
    public bool IsVisibleByDefault => true;
    public IReadOnlyList<HotKey> HotKeys { get; } = [];

    public void RegisterServices(IServiceCollection services) =>
        services.AddSingleton(sp => new GrblSettingsViewModel(
            async () => await sp.GetRequiredService<IGrblSettingsProvider>().GetSettingLinesAsync(),
            line => sp.GetRequiredService<IGrblService>().SendAsync(line)));

    public void RegisterSettings(AppSettings settings) { }

    public object CreateViewModel(IServiceProvider serviceProvider) =>
        serviceProvider.GetRequiredService<GrblSettingsViewModel>();

    public object CreateView(object viewModel) =>
        new GrblSettingsView { DataContext = viewModel };
}
