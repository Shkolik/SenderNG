/*
 * ProgramConfig.cs - per-profile program module settings (Compress options)
 */

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
using CNC.GCode;

namespace CNC.Core.Services;

/// <summary>Per-profile G-code program settings (section "Program" in ModuleSettings).</summary>
public sealed class ProgramConfig
{
    /// <summary>How the Compress transform treats comment tokens (default: keep
    /// all — CAM comments carry operation/tool notes).</summary>
    public GCodeCommentMode CompressCommentMode { get; set; } = GCodeCommentMode.Keep;
}

/// <summary>
/// Reads/writes the "Program" section of a MachineProfile's ModuleSettings:
/// { CompressCommentMode }. Per profile since the settings split — one
/// machine's program preferences are not another's. The section may be a
/// Dictionary (in-memory) or a JsonElement (freshly deserialized profile
/// file), so both shapes are handled defensively, like JogConfigStore.
/// </summary>
public static class ProgramConfigStore
{
    private const string SectionKey = "Program";

    public static ProgramConfig Load(MachineProfile profile)
    {
        var config = new ProgramConfig();

        if (profile.ModuleSettings.TryGetValue(SectionKey, out var raw) &&
            AsObject(raw) is { } entry &&
            entry.TryGetValue("CompressCommentMode", out var mode) &&
            mode is string s &&
            Enum.TryParse<GCodeCommentMode>(s, out var parsed))
        {
            config.CompressCommentMode = parsed;
        }

        return config;
    }

    public static void Save(MachineProfile profile, ProgramConfig config)
    {
        Dictionary<string, object?> section;
        if (profile.ModuleSettings.TryGetValue(SectionKey, out var raw) && AsObject(raw) is { } existing)
            section = existing;
        else
            section = [];

        section["CompressCommentMode"] = config.CompressCommentMode.ToString();
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
}