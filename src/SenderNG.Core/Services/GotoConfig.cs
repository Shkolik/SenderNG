
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

/// <summary>Observable per-widget goto configuration (axis count preference).</summary>
public sealed class GotoConfig : CNC.Core.ViewModelBase
{
    private int _axisCount = 3;

    /// <summary>How many axes the user wants in the Goto widget; the controller's
    /// actual axis count caps the displayed rows (Min of the two).</summary>
    public int AxisCount
    {
        get => _axisCount;
        set => SetField(ref _axisCount, value);
    }
}

/// <summary>
/// Reads/writes the "Goto" section of a <see cref="MachineProfile"/>'s
/// ModuleSettings: <c>{ "AxisCount": 4 }</c>. Per profile since the settings
/// split — the goto axis preference is how the user drives that machine, and
/// the connected controller's reported axis count is the real cap. The section
/// may be a Dictionary (in-memory) or a JsonElement (freshly deserialized
/// profile file), so both shapes are handled defensively.
/// </summary>
public static class GotoConfigStore
{
    private const string SectionKey = "Goto";

    public static GotoConfig Load(MachineProfile profile)
    {
        var config = new GotoConfig();
        if (profile.ModuleSettings.TryGetValue(SectionKey, out var raw) &&
            AsObject(raw) is { } section &&
            ReadDouble(section, "AxisCount") is { } axisCount)
        {
            config.AxisCount = (int)axisCount;
        }
        return Normalize(config);
    }

    public static void Save(MachineProfile profile, GotoConfig config)
    {
        Dictionary<string, object?> section;
        if (profile.ModuleSettings.TryGetValue(SectionKey, out var raw) && AsObject(raw) is { } existing)
            section = existing;
        else
            section = [];
        var normalized = Normalize(config);
        section["AxisCount"] = normalized.AxisCount;
        profile.ModuleSettings[SectionKey] = section;
    }

    private static GotoConfig Normalize(GotoConfig config)
    {
        config.AxisCount = Math.Clamp(config.AxisCount, 1, CNC.Core.GrblInfo.AxisLetters.Length);
        return config;
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

    private static double? ReadDouble(IReadOnlyDictionary<string, object?> entry, string key) =>
        entry.TryGetValue(key, out var v) ? v switch
        {
            double d => d,
            int i => i,
            long l => l,
            JsonElement { ValueKind: JsonValueKind.Number } e when e.TryGetDouble(out var d) => d,
            _ => null,
        } : null;
}
