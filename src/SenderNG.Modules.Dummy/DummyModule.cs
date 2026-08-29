using CNC.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using SenderNG.Core.Modules;

namespace SenderNG.Modules.Dummy;

/// <summary>
/// Proof-of-concept external plugin module discovered via ModuleCatalog assembly scan.
/// Claims the Flyout panel and shows a static "Dummy plugin works!" view.
/// </summary>
public class DummyModule : IModule
{
    public string Id => "dummy";
    public string Name => "Dummy";
    public string Description => "Example third-party plugin used to prove assembly-scanned module discovery.";
    public int Order => 100;
    public PanelHint Panel => PanelHint.Flyout;
    public bool IsVisibleByDefault => true;
    public IReadOnlyList<HotKey> HotKeys { get; } = [];

    public void RegisterServices(IServiceCollection services) { }

    public void RegisterSettings(AppSettings settings)
    {
        if (!settings.Modules.ContainsKey("Dummy"))
            settings.Modules["Dummy"] = new List<object>();
    }

    public object CreateViewModel(IServiceProvider serviceProvider) => new DummyViewModel();

    public object CreateView(object viewModel) =>
        new DummyView { DataContext = viewModel };
}
