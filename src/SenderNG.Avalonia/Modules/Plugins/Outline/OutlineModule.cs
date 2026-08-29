using CNC.Core.Abstractions;
using CNC.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using SenderNG.Core.Modules;

namespace SenderNG.Avalonia.Modules.Plugins;

public class OutlineModule : IModule
{
    public string Id => "outline";
    public string Name => "Outline";
    public string Description => "Outline the loaded program's travel limits and move between them.";
    public int Order => 32;
    public PanelHint Panel => PanelHint.Flyout;
    public bool IsVisibleByDefault => true;
    public IReadOnlyList<HotKey> HotKeys { get; } = [];

    // Module settings key for the persisted outline feed rate.
    public const string SettingsSection = "Outline";
    public const string FeedRateKey = "FeedRate";

    public void RegisterServices(IServiceCollection services) =>
        services.AddSingleton<OutlineViewModel>();

    public void RegisterSettings(AppSettings settings)
    {
        var section = EnsureSection(settings);
        section.TryAdd(FeedRateKey, (double)OutlineViewModel.DefaultFeedRate);
    }

    public object CreateViewModel(IServiceProvider serviceProvider) =>
        serviceProvider.GetRequiredService<OutlineViewModel>();

    public object CreateView(object viewModel) =>
        new OutlineView { DataContext = viewModel };

    private static Dictionary<string, object> EnsureSection(AppSettings settings)
    {
        if (settings.Modules.TryGetValue(SettingsSection, out var raw) && raw is Dictionary<string, object> dict)
            return dict;
        dict = new Dictionary<string, object>();
        settings.Modules[SettingsSection] = dict;
        return dict;
    }
}
