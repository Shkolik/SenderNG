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
/// wrong-typed sections fall back to the defaults (all toggles on).
/// <para>A stale section written before the world mapping was deleted still
/// carries a <c>Planes</c> array; the deserializer ignores members the model no
/// longer declares, so such a section reads back as its toggles plus defaults
/// (verified by <c>PlotterConfigTests</c>, not assumed).</para>
/// <para>The section is machine-type independent now: the plane layout comes
/// from <c>IMachineKinematicsProvider</c>, so the store no longer needs — and
/// no longer takes — the resolved <see cref="MachineType"/>.</para></summary>
public static class PlotterConfig
{
    /// <summary>The profile's section key (per module id).</summary>
    public const string SectionKey = "plotter";

    /// <summary>Loads the section, or returns the defaults when it is absent,
    /// unreadable or carries a value of the wrong type.</summary>
    public static PlotterSettings Load(MachineProfile profile)
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
                return parsed;
        }
        return Defaults();
    }

    /// <summary>Serializes the section into the profile's bag.</summary>
    public static void Save(MachineProfile profile, PlotterSettings settings) =>
        profile.ModuleSettings[SectionKey] = JsonSerializer.Serialize(settings ?? Defaults());

    /// <summary>All toggles on — which is what the renderer already assumes, so
    /// this is a no-op for existing profiles.</summary>
    public static PlotterSettings Defaults() => new();
}
