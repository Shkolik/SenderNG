using CNC.Core.Abstractions;
using CNC.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using SenderNG.Core.Modules;

namespace SenderNG.Avalonia.Modules.Plugins;

public class JogModule : IModule, IModuleSettingsProvider
{
    private SettingsService? _settingsService;

    public string Id => "jog";
    public string Name => "Jog";
    public int Order => 15;
    // Flyout: the widget is wide in foam/multiaxis layouts — drawer overlay suits it better.
    public PanelHint Panel => PanelHint.Flyout;
    public bool IsVisibleByDefault => true;
    public bool CanHide => false;
    public IReadOnlyList<HotKey> HotKeys { get; } = [];

    public void RegisterServices(IServiceCollection services) =>
        services.AddSingleton<JogViewModel>();

    // No seeding needed: JogConfigStore supplies defaults for both modes.
    public void RegisterSettings(AppSettings settings) { }

    public object CreateViewModel(IServiceProvider serviceProvider)
    {
        _settingsService = serviceProvider.GetRequiredService<SettingsService>();
        return serviceProvider.GetRequiredService<JogViewModel>();
    }

    public object CreateView(object viewModel) =>
        new JogView { DataContext = viewModel };

    public IEnumerable<ModuleSettingField> GetSettingFields()
    {
        if (_settingsService is null)
            yield break;

        for (var i = 1; i <= JogConfigStore.StepCount; i++)
        {
            var index = i - 1;
            yield return new ModuleSettingField(
                $"Distance {i}",
                () => LoadConfig().Distances[index],
                v => UpdateConfig(c => c.Distances[index] = v));
        }

        for (var i = 1; i <= JogConfigStore.StepCount; i++)
        {
            var index = i - 1;
            yield return new ModuleSettingField(
                $"Speed {i}",
                () => LoadConfig().Speeds[index],
                v => UpdateConfig(c => c.Speeds[index] = v));
        }
    }

    public IEnumerable<ModuleSettingBool> GetBoolSettingFields()
    {
        if (_settingsService is null)
            yield break;

        yield return new ModuleSettingBool(
            "Diagonal buttons",
            () => LoadConfig().Diagonals,
            v => UpdateConfig(c => c.Diagonals = v));
    }

    private JogConfig LoadConfig() =>
        JogConfigStore.Load(_settingsService!.Load());

    private void UpdateConfig(Action<JogConfig> update)
    {
        var settings = _settingsService!.Load();
        var config = JogConfigStore.Load(settings);
        update(config);
        JogConfigStore.Save(settings, config);
        _settingsService.Save(settings);
    }
}
