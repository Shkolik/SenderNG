
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

using System.Reactive.Subjects;
using System.Text.Json;
using System.Text.Json.Nodes;
using CNC.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using SenderNG.Core.Modules;
using SenderNG.Core.Modules.Probing;
using SenderNG.Core.Services;
using SenderNG.Modules.Probing;

namespace SenderNG.Modules.Plugins;

public class ProbingModule : IModule, IModuleSettingsProvider, ILocalizedModule, IHotkeyActionProvider,
    IHeightMapSource, IProfileCompanionSuffixes
{
    public string Id => "probing";
    public string Name => "Probing";
    public string Description => "Tool-length, edge, center, rotation and height-map probing cycles.";
    public int Order => 50;
    public PanelHint Panel => PanelHint.Flyout;
    public bool IsVisibleByDefault => true;
    public IReadOnlyList<HotKey> HotKeys { get; } =
    [
        new("Alt+R", "probe.start"),
        new("Alt+S", "probe.stop"),
        new("Alt+C", "probe.connected"),
    ];

    /// <summary>Module settings keys (the only things the module persists in settings.json).</summary>
    public const string ProfilesFileKey = "ProfilesFile";
    public const string EnabledToolsKey = "EnabledTools";

    public static string[] ToolKeys { get; } =
    [
        "tool-length",
        "edge-finder",
        "rotation",
        "center",
        "heightmap",
    ];

    public IReadOnlyDictionary<string, string> LocalizationKeys { get; } =
        new Dictionary<string, string>
        {
            ["module.probing.tool-length"] = "Tool length offset",
            ["module.probing.edge-finder"] = "Edge finder",
            ["module.probing.rotation"] = "Rotation",
            ["module.probing.center"] = "Center finder",
            ["module.probing.heightmap"] = "Height map",
            ["module.probing.tipTool"] = "Tool length",
            ["module.probing.edgeExternal"] = "Edge external",
            ["module.probing.edgeInternal"] = "Edge internal",
            ["module.probing.preview3d"] = "Preview in 3D",
            ["module.probing.previewStarted"] = "3D preview started (renderer plugin)",
        };

    private SettingsService? _settingsService;

    /// <summary>Per-profile companion file (<c>profiles/&lt;id&gt;.probing.json</c>).</summary>
    public IReadOnlyList<string> Suffixes => [ProbingProfileStore.CompanionSuffix];

    private readonly Subject<HeightMapLayer?> _heightMap = new();

    /// <inheritdoc />
    public IObservable<HeightMapLayer?> Snapshot => _heightMap;

    /// <summary>Converts a probed grid into the renderer-facing contract layer.
    /// Unprobed cells become <see cref="double.NaN"/>.</summary>
    public static HeightMapLayer? ToHeightMapLayer(HeightMap map)
    {
        if (map is null)
            return null;
        var z = new double[map.SizeY * map.SizeX];
        for (var row = 0; row < map.SizeY; row++)
            for (var col = 0; col < map.SizeX; col++)
                z[row * map.SizeX + col] = map.Points[col, row] ?? double.NaN;
        return new HeightMapLayer(
            Id: "heightmap",
            OriginX: map.Min.X, OriginY: map.Min.Y,
            CellX: map.GridX, CellY: map.GridY,
            Columns: map.SizeX, Rows: map.SizeY,
            Z: z);
    }

    /// <summary>Push the latest map (null clears it). Called from the VM when a
    /// height-map probe completes, a map loads, or a new session begins.</summary>
    public void PublishHeightMap(HeightMapLayer? layer) => _heightMap.OnNext(layer);

    public void RegisterServices(IServiceCollection services)
    {
        services.AddSingleton<ProbeRunner>();
        services.AddSingleton<ProbeViewModel>();
        services.AddSingleton<IProfileCompanionSuffixes>(this);
    }

    // Enabled-tool checkboxes appear in the App Settings dialog; the profiles
    // file path is persisted alongside but is not user-editable there.
    public IEnumerable<ModuleSettingBool> GetBoolSettingFields()
    {
        // Boxed: the setter stores this dictionary into the shared profile's
        // ModuleSettings, and the in-memory re-read (dialog reopen in the same
        // session) requires a Dictionary<string, object> value — a
        // Dictionary<string, bool> defeats the generic `is` checks (generic
        // invariance) and silently re-enables every tool.
        var tools = ReadEnabledTools(_settingsService?.Load())
            .ToDictionary(kv => kv.Key, kv => (object)kv.Value);

        string Label(string key) => LocalizationKeys.TryGetValue("module.probing." + key, out var en)
            ? en
            : key;

        foreach (var key in ToolKeys)
        {
            yield return new ModuleSettingBool(Label(key),
                () => tools.TryGetValue(key, out var v) && v is true,
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
        // ProbeViewModel is constructed here, not in CreateViewModel, and its
        // constructor subscribes SettingsService.Changed -> UiThread.Post, so the
        // dispatcher must be captured before the view model is resolved.
        UiThread.Initialize();
        _settingsService = services.GetService<SettingsService>();
        var vm = services.GetService<ProbeViewModel>();
        if (vm is not null)
            vm.HeightMapChanged += PublishHeightMap;
    }

    private static Dictionary<string, object> EnsureSection(MachineProfile settings)
    {
        if (settings.ModuleSettings.TryGetValue("Probing", out var raw))
        {
            if (raw is Dictionary<string, object> dict)
                return dict;
            // ModuleSettings values deserialize as JsonElement (no custom
            // STJ converters): materialize so sibling keys (e.g. ProfilesFile)
            // survive the next save instead of being replaced by a fresh dict.
            if (raw is JsonElement { ValueKind: JsonValueKind.Object } section)
            {
                dict = new Dictionary<string, object>();
                foreach (var prop in section.EnumerateObject())
                    dict[prop.Name] = Materialize(prop.Value)!;
                settings.ModuleSettings["Probing"] = dict;
                return dict;
            }
        }
        var fresh = new Dictionary<string, object>();
        settings.ModuleSettings["Probing"] = fresh;
        return fresh;
    }

    /// <summary>Converts a deserialized JsonElement back into plain CLR values
    /// (nested objects become dictionaries, numbers doubles, booleans bools).
    /// Used when materializing a module section loaded from disk so writes
    /// preserve sibling keys in a serializable shape.</summary>
    private static object Materialize(object value) => value switch
    {
        JsonElement el => el.ValueKind switch
        {
            JsonValueKind.Object => el.EnumerateObject().ToDictionary(p => p.Name, p => Materialize(p.Value)),
            JsonValueKind.Array => el.EnumerateArray().Select(e => Materialize(e)).ToList(),
            JsonValueKind.String => el.GetString() ?? string.Empty,
            JsonValueKind.Number => el.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => string.Empty,
        },
        _ => value,
    };

    /// <summary>Enabled-tool state with legacy-key migration. Settings saved
    /// before the Edge finder merge (P2) stored per-mode keys
    /// (<c>edge-external</c> / <c>edge-internal</c>); the single
    /// <c>edge-finder</c> flag replaces them, defaulting to enabled when
    /// either legacy mode was enabled (an absent legacy key means that mode's
    /// default, i.e. enabled). Reads tolerate both an in-memory
    /// <c>Dictionary&lt;string, object&gt;</c> section and the JsonElement shape
    /// produced by a save/load round-trip (plain STJ deserialization of
    /// <c>MachineProfile.ModuleSettings</c>).</summary>
    internal static Dictionary<string, bool> ReadEnabledTools(MachineProfile? profile)
    {
        var result = ToolKeys.ToDictionary(k => k, _ => true);
        if (profile is null ||
            !profile.ModuleSettings.TryGetValue("Probing", out var raw) ||
            raw is not (Dictionary<string, object> or JsonElement { ValueKind: JsonValueKind.Object }) ||
            !TryGetTools(raw, out var rawTools))
        {
            return result;
        }

        bool? Flag(string key) => rawTools switch
        {
            Dictionary<string, object> dict when dict.TryGetValue(key, out var v) && v is bool b => b,
            JsonElement { ValueKind: JsonValueKind.Object } el when
                el.TryGetProperty(key, out var p) && p.ValueKind is JsonValueKind.True or JsonValueKind.False => p.GetBoolean(),
            _ => null,
        };

        foreach (var key in ToolKeys)
            if (Flag(key) is bool enabled)
                result[key] = enabled;

        if (Flag("edge-finder") is null &&
            (Flag("edge-external") is not null || Flag("edge-internal") is not null))
        {
            bool ModeEnabled(string key) => Flag(key) is null || Flag(key) == true;
            result["edge-finder"] = ModeEnabled("edge-external") || ModeEnabled("edge-internal");
        }
        return result;
    }

    private static bool TryGetTools(object section, out object rawTools)
    {
        switch (section)
        {
            case Dictionary<string, object> dict when dict.TryGetValue(EnabledToolsKey, out var tools):
                rawTools = tools;
                return rawTools is Dictionary<string, object> or JsonElement { ValueKind: JsonValueKind.Object };
            case JsonElement { ValueKind: JsonValueKind.Object } el when el.TryGetProperty(EnabledToolsKey, out var tools):
                rawTools = tools;
                return rawTools is Dictionary<string, object> or JsonElement { ValueKind: JsonValueKind.Object };
            default:
                rawTools = null!;
                return false;
        }
    }

    public IReadOnlyList<IHotkeyAction> CreateHotkeyActions(IServiceProvider services)
        => ProbingHotkeyProvider.Create(services);

    public object CreateViewModel(IServiceProvider serviceProvider) =>
        serviceProvider.GetRequiredService<ProbeViewModel>();

    public object CreateView(object viewModel) =>
        new ProbeView { DataContext = viewModel };
}
