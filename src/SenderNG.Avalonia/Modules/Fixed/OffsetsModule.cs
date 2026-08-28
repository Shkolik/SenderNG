using CNC.Core.Abstractions;
using CNC.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using SenderNG.Core.Modules;

namespace SenderNG.Avalonia.Modules.Fixed;

public sealed class OffsetsModule : IModule, IModuleSettingsProvider
{
    private SettingsService? _settingsService;
    private IServiceProvider? _services;

    public string Id => "offsets-table";
    public string Name => "Offsets";
    public int Order => 30;
    public PanelHint Panel => PanelHint.Flyout;
    public bool IsVisibleByDefault => true;
    // Core functionality: always visible, no hide checkbox in App Settings.
    public bool CanHide => false;
    public IReadOnlyList<HotKey> HotKeys { get; } = [];

    public void RegisterServices(IServiceCollection services) =>
        services.AddSingleton<OffsetsViewModel>();

    public void RegisterSettings(AppSettings settings) { }

    public object CreateViewModel(IServiceProvider serviceProvider) =>
        serviceProvider.GetRequiredService<OffsetsViewModel>();

    // Fields must work before the Offsets view is ever opened (App Settings dialog).
    public void AttachServices(IServiceProvider services)
    {
        _services = services;
        _settingsService ??= services.GetRequiredService<SettingsService>();
    }

    // Boolean checkbox in the App Settings card: show/hide the tool table in the
    // Offsets editor (hidden by default on controllers with no G10 L1 tool table).
    public IEnumerable<ModuleSettingBool> GetBoolSettingFields()
    {
        if (_settingsService is null || _services is null)
            return [];
        var vm = _services.GetRequiredService<OffsetsViewModel>();
        return [new ModuleSettingBool("Show tool table", () => vm.ShowToolTable, v => vm.ShowToolTable = v)];
    }

    public object CreateView(object viewModel) =>
        new OffsetsView { DataContext = viewModel };
}