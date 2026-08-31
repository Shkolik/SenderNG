using CNC.Core.Abstractions;
using CNC.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using SenderNG.Core.Modules;

namespace SenderNG.Modules.Plugins;

public class CuttingModule : IModule, ILocalizedModule
{
    public string Id => "cutting";
    public string Name => "Cutting";
    public string Description => "Feed/spindle overrides and cycle controls for manual cutting.";
    public int Order => 25;
    public PanelHint Panel => PanelHint.Right;
    public bool IsVisibleByDefault => true;
    public IReadOnlyList<HotKey> HotKeys { get; } = [];

    public IReadOnlyDictionary<string, string> LocalizationKeys { get; } =
        new Dictionary<string, string>
        {
            ["module.cutting.feed"] = "Feed",
            ["module.cutting.speed"] = "Speed",
            ["module.cutting.feedOverride"] = "Feed override",
            ["module.cutting.rapidsOverride"] = "Rapids override",
            ["module.cutting.speedOverride"] = "Speed override",
            ["module.cutting.reset100"] = "Reset to 100%",
            ["module.cutting.spindleRpmHint"] = "Spindle speed (RPM) used by CW/CCW",
            ["module.cutting.cw"] = "CW",
            ["module.cutting.ccw"] = "CCW",
            ["module.cutting.off"] = "Off",
            ["module.cutting.spindleCwHint"] = "Spindle on, clockwise (M3)",
            ["module.cutting.spindleCcwHint"] = "Spindle on, counter-clockwise (M4)",
            ["module.cutting.spindleOffHint"] = "Spindle off (M5)",
        };

    public void RegisterServices(IServiceCollection services) =>
        services.AddSingleton<CuttingViewModel>();

    public void RegisterSettings(AppSettings settings) { }

    public object CreateViewModel(IServiceProvider serviceProvider) =>
        serviceProvider.GetRequiredService<CuttingViewModel>();

    public object CreateView(object viewModel) =>
        new CuttingView { DataContext = viewModel };
}
