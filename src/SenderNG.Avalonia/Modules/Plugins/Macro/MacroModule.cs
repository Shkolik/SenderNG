using CNC.Core.Abstractions;
using CNC.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using SenderNG.Core.Modules;

namespace SenderNG.Avalonia.Modules.Plugins;

public class MacroModule : IModule
{
    public string Id => "macro";
    public string Name => "Macros";
    public int Order => 45;
    public PanelHint Panel => PanelHint.Flyout;
    public bool IsVisibleByDefault => true;
    public IReadOnlyList<HotKey> HotKeys { get; } = [];

    public void RegisterServices(IServiceCollection services) =>
        services.AddSingleton<MacroViewModel>();

    public void RegisterSettings(AppSettings settings)
    {
        if (!settings.Modules.ContainsKey("Macros"))
            settings.Modules["Macros"] = new List<object>();
    }

    public object CreateViewModel(IServiceProvider serviceProvider) =>
        serviceProvider.GetRequiredService<MacroViewModel>();

    public object CreateView(object viewModel) =>
        new MacroView { DataContext = viewModel };
}
