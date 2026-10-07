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
using CNC.Core.Services;

namespace SenderNG.Modules.Plotter.Settings;

/// <summary>Per-profile settings store for the <c>plotter</c> section, on the
/// <c>ModuleSettingsJson</c> pattern: a JSON string under <see cref="SectionKey"/>
/// in <see cref="MachineProfile.ModuleSettings"/>. Missing, corrupt and
/// plane-count-mismatched sections fall back to identity defaults synthesized
/// from the resolved <see cref="MachineType"/>.</summary>
public static class PlotterConfig
{
    /// <summary>The profile's section key (per module id).</summary>
    public const string SectionKey = "plotter";

    /// <summary>Loads the section, or synthesizes defaults when absent, corrupt
    /// or mismatched.</summary>
    public static PlotterSettings Load(MachineProfile profile, MachineType type)
    {
        if (profile.ModuleSettings.TryGetValue(SectionKey, out var raw))
        {
            var json = raw switch
            {
                string s => s,
                JsonElement { ValueKind: JsonValueKind.String } e => e.GetString(),
                _ => null,
            };
            var parsed = JsonDefaults.TryDeserialize<PlotterSettings>(json);
            if (parsed is not null)
                return Normalize(parsed, type);
        }
        return Defaults(type);
    }

    /// <summary>Normalizes and serializes the section into the profile's bag.</summary>
    public static void Save(MachineProfile profile, PlotterSettings settings)
    {
        var type = MachineTypeCatalog.Resolve(profile.MachineTypeId);
        profile.ModuleSettings[SectionKey] = JsonSerializer.Serialize(Normalize(settings, type));
    }

    /// <summary>All toggles on, every plane at identity — which is what the
    /// renderer already assumes, so this is a no-op for existing profiles.</summary>
    public static PlotterSettings Defaults(MachineType type)
    {
        var settings = new PlotterSettings();
        for (var p = 0; p < type.Planes.Count; p++)
            settings.Planes.Add(PlotterPlaneMapper.Identity(type.Planes[p].Roles.Count));
        return settings;
    }

    private static PlotterSettings Normalize(PlotterSettings s, MachineType type)
    {
        var defaults = Defaults(type);
        if (s is null || s.Planes is null || s.Planes.Count != type.Planes.Count)
            return defaults;
        for (var p = 0; p < s.Planes.Count; p++)
        {
            var roles = type.Planes[p].Roles.Count;
            if (!PlotterPlaneMapper.IsUsable(s.Planes[p], roles))
                s.Planes[p] = defaults.Planes[p];
            else if (s.Planes[p].Origin is not { Length: PlotterPlaneMapper.WorldAxisCount })
                s.Planes[p].Origin = [0, 0, 0];
        }
        return s;
    }
}
