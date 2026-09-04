
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

using System.Text.Json;

namespace CNC.Core.Services;

/// <summary>Observable per-widget offsets configuration (tool-table visibility).</summary>
public sealed class OffsetsConfig : CNC.Core.ViewModelBase
{
    private bool _showToolTable = true;

    /// <summary>Whether the tool table is shown in the Offsets editor. Hidden on
    /// controllers that can't persist one (classic grbl 1.1 has no G10 L1).
    /// The visible-tool count is profile-scoped (<see cref="MachineProfile.VisibleTools"/>
    /// in App Settings), not here.</summary>
    public bool ShowToolTable
    {
        get => _showToolTable;
        set => SetField(ref _showToolTable, value);
    }

    /// <summary>Editor-local tool names keyed by controller identity
    /// (<see cref="CNC.Core.Abstractions.IGrblService.ControllerKey"/> — the $I
    /// firmware fingerprint, no endpoint, so a machine keeps its names across
    /// ports and byte-identical boards share them), then by tool slot. grblHAL
    /// has no name field in its tool table (confirmed with the maintainer,
    /// 2024), so names are app-only metadata persisted here, scoped per
    /// controller so a different machine never inherits the previous one's
    /// annotations, and never sent to the controller.</summary>
    public Dictionary<string, Dictionary<int, string>> ToolNames { get; } = new();

    /// <summary>Gets (creating on first use) the name map for one controller key.</summary>
    public Dictionary<int, string> ToolNamesFor(string controllerKey)
    {
        if (!ToolNames.TryGetValue(controllerKey, out var slots))
            ToolNames[controllerKey] = slots = new Dictionary<int, string>();
        return slots;
    }
}

/// <summary>
/// Reads/writes the "Offsets" section of a <see cref="MachineProfile"/>'s
/// ModuleSettings: <c>{ "ShowToolTable": bool, "ToolNames": { controllerKey:
/// { slot: name } } }</c>. Per profile since the settings split (a toolset
/// belongs to the machine where it was set up), with the controllerKey
/// fingerprint still deciding which names apply to the connected controller.
/// Handles both the in-memory Dictionary and the freshly deserialized
/// JsonElement shapes.
/// </summary>
public static class OffsetsConfigStore
{
    private const string SectionKey = "Offsets";

    /// <summary>Static convenience for controllers that just need the name map
    /// (e.g. selector labels): loads the Offsets section from the profile graph
    /// and returns the names for one controller key (empty when unavailable).</summary>
    public static Dictionary<int, string> ToolNamesFor(MachineProfile profile, string? controllerKey) =>
        Load(profile).ToolNamesFor(controllerKey ?? "unknown");

    public static OffsetsConfig Load(MachineProfile profile)
    {
        var config = new OffsetsConfig();
        if (profile.ModuleSettings.TryGetValue(SectionKey, out var raw) &&
            AsObject(raw) is { } section)
        {
            if (ReadBool(section, "ShowToolTable") is { } show)
                config.ShowToolTable = show;
            if (ReadToolNames(section, "ToolNames") is { } names)
                foreach (var (controllerId, slots) in names)
                    foreach (var (id, name) in slots)
                        config.ToolNamesFor(controllerId)[id] = name;
        }
        return config;
    }

    public static void Save(MachineProfile profile, OffsetsConfig config)
    {
        Dictionary<string, object?> section;
        if (profile.ModuleSettings.TryGetValue(SectionKey, out var raw) && AsObject(raw) is { } existing)
            section = existing;
        else
            section = [];
        section["ShowToolTable"] = config.ShowToolTable;
        section["ToolNames"] = config.ToolNames;
        profile.ModuleSettings[SectionKey] = section;
    }

    private static Dictionary<string, object?>? AsObject(object? value) => value switch
    {
        Dictionary<string, object> typed => typed.ToDictionary(kv => kv.Key, kv => (object?)kv.Value),
        JsonElement { ValueKind: JsonValueKind.Object } el => ReadObject(el),
        _ => null,
    };

    private static Dictionary<string, object?> ReadObject(JsonElement el)
    {
        var result = new Dictionary<string, object?>();
        foreach (var prop in el.EnumerateObject())
            result[prop.Name] = prop.Value;
        return result;
    }

    private static bool? ReadBool(IReadOnlyDictionary<string, object?> entry, string key) =>
        entry.TryGetValue(key, out var v) && v is bool b ? b : null;

    /// <summary>Reads the <c>{ controllerId: { slot: name } }</c> tool-name map
    /// from either the in-memory Dictionary or the deserialized JsonElement
    /// shape.</summary>
    private static Dictionary<string, Dictionary<int, string>>? ReadToolNames(
        IReadOnlyDictionary<string, object?> entry, string key)
    {
        if (!entry.TryGetValue(key, out var raw))
            return null;
        var result = new Dictionary<string, Dictionary<int, string>>();
        switch (raw)
        {
            case Dictionary<string, Dictionary<int, string>> typed:
                foreach (var (controllerId, slots) in typed)
                    if (ReadSlotMap(slots) is { } map)
                        foreach (var (id, name) in map)
                            SlotsFor(result, controllerId)[id] = name;
                return result;
            case Dictionary<string, object> typed:
                foreach (var (controllerId, slots) in typed)
                    if (ReadSlotMap(slots) is { } map)
                        foreach (var (id, name) in map)
                            SlotsFor(result, controllerId)[id] = name;
                return result;
            case JsonElement { ValueKind: JsonValueKind.Object } el:
                foreach (var controller in el.EnumerateObject())
                    if (controller.Value.ValueKind == JsonValueKind.Object)
                        foreach (var slot in controller.Value.EnumerateObject())
                            if (int.TryParse(slot.Name, out var id) &&
                                slot.Value.ValueKind == JsonValueKind.String &&
                                !string.IsNullOrEmpty(slot.Value.GetString()))
                                SlotsFor(result, controller.Name)[id] = slot.Value.GetString()!;
                return result;
            default:
                return null;
        }
    }

    /// <summary>Reads a single <c>{ slot: name }</c> map from any in-memory or
    /// deserialized shape.</summary>
    private static Dictionary<int, string>? ReadSlotMap(object? raw)
    {
        var result = new Dictionary<int, string>();
        switch (raw)
        {
            case Dictionary<int, string> typed:
                foreach (var (id, name) in typed)
                    if (!string.IsNullOrEmpty(name))
                        result[id] = name;
                return result;
            case Dictionary<string, object> typed:
                foreach (var (k, v) in typed)
                    if (int.TryParse(k, out var id) && v is string s && !string.IsNullOrEmpty(s))
                        result[id] = s;
                return result;
            case JsonElement { ValueKind: JsonValueKind.Object } el:
                foreach (var prop in el.EnumerateObject())
                    if (int.TryParse(prop.Name, out var id) &&
                        prop.Value.ValueKind == JsonValueKind.String &&
                        !string.IsNullOrEmpty(prop.Value.GetString()))
                        result[id] = prop.Value.GetString()!;
                return result;
            default:
                return null;
        }
    }

    private static Dictionary<int, string> SlotsFor(Dictionary<string, Dictionary<int, string>> map, string controllerId)
    {
        if (!map.TryGetValue(controllerId, out var slots))
            map[controllerId] = slots = new Dictionary<int, string>();
        return slots;
    }
}