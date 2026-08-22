using CNC.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using SenderNG.Core.Modules;

namespace SenderNG.Avalonia.Modules.Fixed;

public class ProgramModule : IModule
{
    public string Id => "program";
    public string Name => "Program";
    public int Order => 20;
    public PanelHint Panel => PanelHint.Center;
    public bool IsVisibleByDefault => true;
    public IReadOnlyList<HotKey> HotKeys { get; } = [];

    public void RegisterServices(IServiceCollection services) =>
        services.AddSingleton<ProgramViewModel>();

    public void RegisterSettings(AppSettings settings) { }

    public object CreateViewModel(IServiceProvider serviceProvider) =>
        serviceProvider.GetRequiredService<ProgramViewModel>();

    public object CreateView(object viewModel) =>
        new ProgramView { DataContext = viewModel };
}
