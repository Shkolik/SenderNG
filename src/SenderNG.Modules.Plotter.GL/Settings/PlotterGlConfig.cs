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
using SenderNG.Modules.Common.Render;

namespace SenderNG.Modules.Plotter.GL.Settings;

/// <summary>
/// GL-local per-profile settings store on the <c>ModuleSettingsJson</c> pattern:
/// the section is a JSON string under <see cref="SectionKey"/> in
/// <see cref="MachineProfile.ModuleSettings"/>. Missing/corrupt/plane-count-mismatched
/// sections fall back to defaults synthesized from the resolved <see cref="MachineType"/>.
/// </summary>
public static class PlotterGlConfig
{
    /// <summary>The profile's section key (per module id).</summary>
    public const string SectionKey = "plotter-gl";

    /// <summary>Loads the section, or synthesizes defaults when absent/corrupt/mismatched.</summary>
    public static PlotterGlSettings Load(MachineProfile profile, MachineType type)
    {
        if (profile.ModuleSettings.TryGetValue(SectionKey, out var raw))
        {
            var json = raw switch
            {
                string s => s,
                JsonElement { ValueKind: JsonValueKind.String } e => e.GetString(),
                _ => null,
            };
            var parsed = JsonDefaults.TryDeserialize<PlotterGlSettings>(json);
            if (parsed is not null)
                return Normalize(parsed, type);
        }
        return Defaults(type);
    }

    /// <summary>Normalizes and serializes the section into the profile's module bag.</summary>
    public static void Save(MachineProfile profile, PlotterGlSettings settings)
    {
        var type = MachineTypeCatalog.Resolve(profile.MachineTypeId);
        profile.ModuleSettings[SectionKey] = JsonSerializer.Serialize(Normalize(settings, type));
    }

    /// <summary>Synthesized defaults: mill identity, foam symmetric on the wire axis.</summary>
    public static PlotterGlSettings Defaults(MachineType type)
    {
        var multi = type.IsMultiPlane;
        var settings = new PlotterGlSettings();
        for (var p = 0; p < type.Planes.Count; p++)
        {
            var roles = type.Planes[p].Roles.Count;
            var worldAxes = multi
                ? Enumerable.Range(1, roles).ToArray()                 // foam: [1,2] — world X is the wire axis
                : Enumerable.Range(0, roles).ToArray();                // mill: [0,1,2] — identity
            var origin = multi
                ? new double[] { settings.WireLength * (p - (type.Planes.Count - 1) / 2.0), 0, 0 }
                : new double[] { 0, 0, 0 };
            settings.Planes.Add(new GlPlaneGeometry { WorldAxes = worldAxes, Origin = origin });
        }
        return settings;
    }

    /// <summary>Recomputes each plane's origin along the wire axis (a single world axis
    /// none of the planes maps) so the towers stay symmetric about the world origin. The
    /// wire axis falls back to world X when every axis is mapped somewhere.</summary>
    public static PlotterGlSettings RecenterOnWire(PlotterGlSettings s)
    {
        var n = s.Planes.Count;
        var wireAxis = new[] { 0, 1, 2 }
            .FirstOrDefault(a => s.Planes.All(p => p.WorldAxes is null || !p.WorldAxes.Contains(a)));
        for (var p = 0; p < n; p++)
        {
            var plane = s.Planes[p];
            var origin = plane.Origin is { Length: 3 } ? plane.Origin : new double[] { 0, 0, 0 };
            origin[wireAxis] = s.WireLength * (p - (n - 1) / 2.0);
            plane.Origin = origin;
        }
        return s;
    }

    private static PlotterGlSettings Normalize(PlotterGlSettings s, MachineType type)
    {
        var defaults = Defaults(type);
        if (s is null || s.Planes is null || s.Planes.Count != type.Planes.Count)
            return defaults;
        for (var p = 0; p < s.Planes.Count; p++)
        {
            var g = s.Planes[p];
            if (g is null)
            {
                s.Planes[p] = defaults.Planes[p];
                continue;
            }
            var roles = type.Planes[p].Roles.Count;
            var worldAxes = g.WorldAxes;
            if (worldAxes is null || worldAxes.Length != roles
                || worldAxes.Distinct().Count() != roles
                || worldAxes.Any(w => w < 0 || w > 2))
            {
                s.Planes[p] = defaults.Planes[p];
            }
            else if (g.Origin is null || g.Origin.Length != 3)
            {
                s.Planes[p] = new GlPlaneGeometry { WorldAxes = worldAxes, Origin = [0, 0, 0] };
            }
        }
        if (s.WireLength <= 0)
            s.WireLength = 100;
        // System.Text.Json reads ANY integer into an enum, so a hand-edited or
        // forward-dated profile section can carry a mode the renderer has no
        // branch for — clamp it to the plain path instead of passing it on.
        if (!Enum.IsDefined(typeof(MsaaMode), s.MsaaMode))
            s.MsaaMode = MsaaMode.None;
        return s;
    }
}