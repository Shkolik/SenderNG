using CNC.Core.Abstractions;
using CNC.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using SenderNG.Core.Modules;

namespace SenderNG.Avalonia.Modules.Fixed;

public sealed class WorkParametersModule : IModule
{
    public string Id => "work-parameters";
    public string Name => "Work Parameters";
    public int Order => 36;
    public PanelHint Panel => PanelHint.Right;
    public bool IsVisibleByDefault => true;
    // Core functionality: always visible, no hide checkbox in App Settings.
    public bool CanHide => false;
    public IReadOnlyList<HotKey> HotKeys { get; } = [];

    public void RegisterServices(IServiceCollection services) =>
        services.AddSingleton<WorkParametersViewModel>();

    public void RegisterSettings(AppSettings settings) { }

    public object CreateViewModel(IServiceProvider serviceProvider) =>
        serviceProvider.GetRequiredService<WorkParametersViewModel>();

    public object CreateView(object viewModel) =>
        new WorkParametersView { DataContext = viewModel };
}
