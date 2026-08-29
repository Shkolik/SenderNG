using CNC.Core.Abstractions;
using CNC.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using SenderNG.Core.Modules;

namespace SenderNG.Avalonia.Modules.Plugins;

public class JogModule : IModule, IModuleSettingsProvider, ILocalizedModule
{
    private SettingsService? _settingsService;
    private IServiceProvider? _services;

    public string Id => "jog";
    public string Name => "Jog";
    public string Description => "Manual jog with configurable step distances and speeds.";
    public int Order => 15;
    // Flyout: the widget is wide in foam/multiaxis layouts — drawer overlay suits it better.
    public PanelHint Panel => PanelHint.Flyout;
    public bool IsVisibleByDefault => true;
    public bool CanHide => false;
    public IReadOnlyList<HotKey> HotKeys { get; } = [];

    public IReadOnlyDictionary<string, string> LocalizationKeys { get; } =
        new Dictionary<string, string>
        {
            ["module.jog.distances"] = "Distances (mm)",
            ["module.jog.speeds"] = "Speeds (mm/min)",
            ["module.jog.step1"] = "Step 1",
            ["module.jog.step2"] = "Step 2",
            ["module.jog.step3"] = "Step 3",
            ["module.jog.speed1"] = "Speed 1",
            ["module.jog.speed2"] = "Speed 2",
            ["module.jog.speed3"] = "Speed 3",
            ["module.jog.diagonal"] = "Diagonal jogging",
        };

    public void RegisterServices(IServiceCollection services) =>
        services.AddSingleton<JogViewModel>();

    // No seeding needed: JogConfigStore supplies defaults for both modes.
    public void RegisterSettings(AppSettings settings) { }

    public object CreateViewModel(IServiceProvider serviceProvider)
    {
        _settingsService = serviceProvider.GetRequiredService<SettingsService>();
        return serviceProvider.GetRequiredService<JogViewModel>();
    }

    // Fields must work before the Jog view is ever opened (App Settings dialog).
    public void AttachServices(IServiceProvider services)
    {
        _services = services;
        _settingsService ??= services.GetRequiredService<SettingsService>();
    }

    // Module-provided settings UI: the same selector controls as the widget,
    // bound to the shared JogViewModel (singleton), so changes apply live.
    public object? CreateSettingsView() =>
        _services is null
            ? null
            : new JogSettingsView { DataContext = _services.GetRequiredService<JogViewModel>() };

    public object CreateView(object viewModel) =>
        new JogView { DataContext = viewModel };
}
