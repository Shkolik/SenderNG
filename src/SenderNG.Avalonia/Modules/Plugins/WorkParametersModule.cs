using CNC.Core.Abstractions;
using CNC.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using SenderNG.Core.Modules;

namespace SenderNG.Avalonia.Modules.Plugins;

public sealed class WorkParametersModule : IModule
{
    public string Id => "work-parameters";
    public string Name => "Work Parameters";
    public int Order => 25;
    public PanelHint Panel => PanelHint.Flyout;
    public bool IsVisibleByDefault => true;
    public bool CanHide => true;
    public IReadOnlyList<HotKey> HotKeys { get; } = [];

    public void RegisterServices(IServiceCollection services) =>
        services.AddSingleton<WorkParametersViewModel>();

    public void RegisterSettings(AppSettings settings) { }

    public object CreateViewModel(IServiceProvider serviceProvider) =>
        serviceProvider.GetRequiredService<WorkParametersViewModel>();

    public object CreateView(object viewModel) =>
        new WorkParametersView { DataContext = viewModel };
}
