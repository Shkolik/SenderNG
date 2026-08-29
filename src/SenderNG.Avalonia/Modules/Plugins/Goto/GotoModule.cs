using CNC.Core.Abstractions;
using CNC.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using SenderNG.Core.Modules;

namespace SenderNG.Avalonia.Modules.Plugins;

public class GotoModule : IModule, IModuleSettingsProvider, ILocalizedModule
{
    private IServiceProvider? _services;

    public string Id => "goto";
    public string Name => "Goto";
    public string Description => "Move axes to G28/G30 reference positions or to typed coordinates.";
    public int Order => 18;
    public PanelHint Panel => PanelHint.Flyout;
    public bool IsVisibleByDefault => true;
    public IReadOnlyList<HotKey> HotKeys { get; } = [];

    public IReadOnlyDictionary<string, string> LocalizationKeys { get; } =
        new Dictionary<string, string>
        {
            ["module.goto.axisCount"] = "Goto axis count",
            ["module.goto.axisCountHint"] = "Capped by the connected controller's axis count.",
        };

    public void RegisterServices(IServiceCollection services) =>
        services.AddSingleton<GotoViewModel>();

    // No seeding needed: GotoConfigStore supplies the default axis count.
    public void RegisterSettings(AppSettings settings) { }

    public object CreateViewModel(IServiceProvider serviceProvider) =>
        serviceProvider.GetRequiredService<GotoViewModel>();

    // Fields must work before the Goto view is ever opened (App Settings dialog).
    public void AttachServices(IServiceProvider services) =>
        _services = services;

    // Module-provided settings UI: the same axis selector as the widget, bound to
    // the shared GotoViewModel (singleton), so changes apply live.
    public object? CreateSettingsView() =>
        _services is null
            ? null
            : new GotoSettingsView { DataContext = _services.GetRequiredService<GotoViewModel>() };

    public object CreateView(object viewModel) =>
        new GotoView { DataContext = viewModel };
}
