using CNC.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using SenderNG.Core.Modules;

namespace SenderNG.Avalonia.Modules.Fixed;

public class GCodeConsoleModule : IModule
{
    public string Id => "gcode-console";
    public string Name => "G-Code / Console";
    public int Order => 20;
    public IReadOnlyList<HotKey> HotKeys { get; } = [];

    public void RegisterServices(IServiceCollection services) =>
        services.AddSingleton<GCodeConsoleViewModel>();

    public void RegisterSettings(AppSettings settings) { }

    public object CreateViewModel(IServiceProvider serviceProvider) =>
        serviceProvider.GetRequiredService<GCodeConsoleViewModel>();

    public object CreateView(object viewModel) =>
        new GCodeConsoleView { DataContext = viewModel };
}
