using CNC.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using SenderNG.Core.Modules;
using SenderNG.Core.Modules.Probing;

namespace SenderNG.Avalonia.Modules.Plugins;

public class ProbingModule : IModule, IModuleSettingsProvider
{
    public string Id => "probing";
    public string Name => "Probing";
    public int Order => 50;
    public PanelHint Panel => PanelHint.Center;
    public bool IsVisibleByDefault => true;
    public IReadOnlyList<HotKey> HotKeys { get; } = [];

    /// <summary>Module settings keys (the only things the module persists in settings.json).</summary>
    public const string ProfilesFileKey = "ProfilesFile";
    public const string EnabledToolsKey = "EnabledTools";

    public static string[] ToolKeys { get; } =
    [
        "tool-length",
        "edge-external",
        "edge-internal",
        "rotation",
        "center",
        "heightmap",
    ];

    private SettingsService? _settingsService;

    public void RegisterServices(IServiceCollection services)
    {
        services.AddSingleton<ProbeRunner>();
        services.AddSingleton<ProbeViewModel>();
    }

    public void RegisterSettings(AppSettings settings)
    {
        var section = EnsureSection(settings);
        section.TryAdd(ProfilesFileKey, Path.Combine(SettingsService.DefaultDir(), "probing.json"));
        if (!section.ContainsKey(EnabledToolsKey))
            section[EnabledToolsKey] = ToolKeys.ToDictionary(k => k, _ => true);
    }

    // Enabled-tool checkboxes appear in the App Settings dialog; the profiles
    // file path is persisted alongside but is not user-editable there.
    public IEnumerable<ModuleSettingBool> GetBoolSettingFields()
    {
        var tools = ReadTools(_settingsService);

        string Label(string key) => key switch
        {
            "tool-length" => "Tool length offset",
            "edge-external" => "Edge finder, external",
            "edge-internal" => "Edge finder, internal",
            "rotation" => "Rotation",
            "center" => "Center finder",
            "heightmap" => "Height map",
            _ => key,
        };

        foreach (var key in ToolKeys)
        {
            yield return new ModuleSettingBool(Label(key),
                () => tools.TryGetValue(key, out var v) && v,
                value =>
                {
                    tools[key] = value;
                    if (_settingsService is { } svc)
                    {
                        var settings = svc.Load();
                        EnsureSection(settings)[EnabledToolsKey] = tools;
                        svc.Save(settings);
                    }
                });
        }
    }

    public void AttachServices(IServiceProvider services)
    {
        _settingsService = services.GetService<SettingsService>();
    }

    private static Dictionary<string, object> EnsureSection(AppSettings settings)
    {
        if (settings.Modules.TryGetValue("Probing", out var raw) && raw is Dictionary<string, object> dict)
            return dict;
        dict = new Dictionary<string, object>();
        settings.Modules["Probing"] = dict;
        return dict;
    }

    private static Dictionary<string, bool> ReadTools(SettingsService? settingsService)
    {
        var result = ToolKeys.ToDictionary(k => k, _ => true);
        var settings = settingsService?.Load();
        if (settings is null || !settings.Modules.TryGetValue("Probing", out var raw) || raw is not Dictionary<string, object> section)
            return result;
        if (!section.TryGetValue(EnabledToolsKey, out var rawTools) || rawTools is not Dictionary<string, object> dict)
            return result;
        foreach (var key in ToolKeys)
            if (dict.TryGetValue(key, out var v) && v is bool b)
                result[key] = b;
        return result;
    }

    public object CreateViewModel(IServiceProvider serviceProvider) =>
        serviceProvider.GetRequiredService<ProbeViewModel>();

    public object CreateView(object viewModel) =>
        new ProbeView { DataContext = viewModel };
}
