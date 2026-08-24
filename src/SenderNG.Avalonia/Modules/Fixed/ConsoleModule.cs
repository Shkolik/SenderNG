using CNC.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using SenderNG.Core.Modules;

namespace SenderNG.Avalonia.Modules.Fixed;

public class ConsoleModule : IModule
{
    public string Id => "console";
    public string Name => "Console";
    public int Order => 21;
    public PanelHint Panel => PanelHint.Center;
    public bool IsVisibleByDefault => true;
    public bool CanHide => false;
    public IReadOnlyList<HotKey> HotKeys { get; } = [];

    public void RegisterServices(IServiceCollection services) =>
        services.AddSingleton<ConsoleViewModel>();

    public void RegisterSettings(AppSettings settings) { }

    public object CreateViewModel(IServiceProvider serviceProvider) =>
        serviceProvider.GetRequiredService<ConsoleViewModel>();

    public object CreateView(object viewModel) =>
        new ConsoleView { DataContext = viewModel };
}
