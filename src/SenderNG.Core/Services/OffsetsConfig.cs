
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
    /// controllers that can't persist one (classic grbl 1.1 has no G10 L1).</summary>
    public bool ShowToolTable
    {
        get => _showToolTable;
        set => SetField(ref _showToolTable, value);
    }
}

/// <summary>
/// Reads/writes the "Offsets" section of <c>AppSettings.Modules</c>:
/// <c>{ "ShowToolTable": bool }</c>. Global (not mode-aware) like the Goto config —
/// the tool table is a property of the controller. Handles both the in-memory
/// Dictionary and the freshly deserialized JsonElement shapes.
/// </summary>
public static class OffsetsConfigStore
{
    private const string SectionKey = "Offsets";

    public static OffsetsConfig Load(AppSettings settings)
    {
        var config = new OffsetsConfig();
        if (settings.Modules.TryGetValue(SectionKey, out var raw) &&
            AsObject(raw) is { } section &&
            ReadBool(section, "ShowToolTable") is { } show)
        {
            config.ShowToolTable = show;
        }
        return config;
    }

    public static void Save(AppSettings settings, OffsetsConfig config)
    {
        Dictionary<string, object?> section;
        if (settings.Modules.TryGetValue(SectionKey, out var raw) && AsObject(raw) is { } existing)
            section = existing;
        else
            section = [];
        section["ShowToolTable"] = config.ShowToolTable;
        settings.Modules[SectionKey] = section;
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
}