
/*

Copyright (c) 2026 Andrew Shkolik

All rights reserved.

Redistribution and use in source and binary forms, with or without modification,
are permitted provided that the following conditions are met:

· Redistributions of source code must retain the above copyright notice, this
list of conditions and the following disclaimer.

· Redistributions in binary form must reproduce the above copyright notice, this
list of conditions and the following disclaimer in the documentation and/or
other materials provided with the distribution.

· Neither the name of the copyright holder nor the names of its contributors may
be used to endorse or promote products derived from this software without
specific prior written permission.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND
ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT OWNER OR CONTRIBUTORS BE LIABLE FOR
ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
(INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON
ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
(INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.

*/

using CNC.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using SenderNG.Core.Modules;
using SenderNG.Core.Modules.Probing;

namespace SenderNG.Modules.Plugins;

public class ProbingModule : IModule, IModuleSettingsProvider, ILocalizedModule
{
    public string Id => "probing";
    public string Name => "Probing";
    public string Description => "Tool-length, edge, center, rotation and height-map probing cycles.";
    public int Order => 50;
    public PanelHint Panel => PanelHint.Flyout;
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

    public IReadOnlyDictionary<string, string> LocalizationKeys { get; } =
        new Dictionary<string, string>
        {
            ["module.probing.tool-length"] = "Tool length offset",
            ["module.probing.edge-external"] = "Edge finder, external",
            ["module.probing.edge-internal"] = "Edge finder, internal",
            ["module.probing.rotation"] = "Rotation",
            ["module.probing.center"] = "Center finder",
            ["module.probing.heightmap"] = "Height map",
            ["module.probing.tipTool"] = "Tool length",
            ["module.probing.edgeExternal"] = "Edge external",
            ["module.probing.edgeInternal"] = "Edge internal",
        };

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

        string Label(string key) => LocalizationKeys.TryGetValue("module.probing." + key, out var en)
            ? en
            : key;

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
                },
                key: "module.probing." + key);
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
