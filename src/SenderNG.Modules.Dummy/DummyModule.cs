using CNC.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using SenderNG.Core.Localization;
using SenderNG.Core.Modules;

namespace SenderNG.Modules.Dummy;

/// <summary>
/// Proof-of-concept external plugin module discovered via ModuleCatalog assembly scan.
/// Claims the Flyout panel and shows a "Dummy plugin works!" view.
/// <para>
/// Localization: this module serves its OWN locale catalog (the <c>Locale/*.json</c>
/// files copied to <c>OutputDir/Modules/dummy/Locale</c> and registered with the
/// <see cref="LocalizationService"/> by the host) instead of the app's global en/ru
/// files — the contract for 3rd-party plugins. Keys are namespaced <c>module.dummy.*</c>.
/// </para>
/// </summary>
public class DummyModule : IModule, ILocalizedModule
{
    public string Id => "dummy";
    public string Name => "Dummy";
    public string Description => "Example third-party plugin used to prove assembly-scanned module discovery.";
    public int Order => 100;
    public PanelHint Panel => PanelHint.Flyout;
    public bool IsVisibleByDefault => true;
    public IReadOnlyList<HotKey> HotKeys { get; } = [];

    public IReadOnlyDictionary<string, string> LocalizationKeys { get; } =
        new Dictionary<string, string>
        {
            ["module.dummy.name"] = "Dummy",
            ["module.dummy.description"] = "Example third-party plugin used to prove assembly-scanned module discovery.",
            ["module.dummy.tagline"] = "Dummy plugin works!",
        };

    public void RegisterServices(IServiceCollection services) { }

    public void RegisterSettings(AppSettings settings)
    {
        if (!settings.Modules.ContainsKey("Dummy"))
            settings.Modules["Dummy"] = new List<object>();
    }

    public object CreateViewModel(IServiceProvider serviceProvider) =>
        new DummyViewModel(serviceProvider.GetRequiredService<LocalizationService>());

    public object CreateView(object viewModel) =>
        new DummyView { DataContext = viewModel };
}
