using CNC.Core.Abstractions;
using CNC.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using SenderNG.Core.Modules;

namespace SenderNG.Avalonia.Modules.Plugins;

public class JogModule : IModule
{
    public string Id => "jog";
    public string Name => "Jog";
    public int Order => 15;
    public IReadOnlyList<HotKey> HotKeys { get; } = [];

    public void RegisterServices(IServiceCollection services) =>
        services.AddSingleton<JogViewModel>();

    public void RegisterSettings(AppSettings settings)
    {
        if (!settings.Modules.ContainsKey("Jog"))
            settings.Modules["Jog"] = new Dictionary<string, object>
            {
                ["XySpeed"] = 1000.0,
                ["ZSpeed"] = 500.0,
                ["Steps"] = new List<double> { 0.1, 1, 10 },
                ["SelectedStepIndex"] = 1,
            };
    }

    public object CreateViewModel(IServiceProvider serviceProvider) =>
        serviceProvider.GetRequiredService<JogViewModel>();

    public object CreateView(object viewModel) =>
        new JogView { DataContext = viewModel };
}
