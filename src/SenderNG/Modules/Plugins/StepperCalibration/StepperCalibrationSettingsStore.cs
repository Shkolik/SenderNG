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

namespace SenderNG.Modules.Plugins;

/// <summary>Persisted wizard settings (profile scope): only the default move distance.
/// Steps/mm and backlash live on the controller; no local copy (spec §8).</summary>
public sealed class StepperCalibrationSettings
{
    public double Distance { get; set; } = 100d;
}

/// <summary>Reads/writes the <c>stepper-calibration</c> section of a profile's
/// <c>ModuleSettings</c>. Tolerates both the in-memory dictionary and the JsonElement
/// shape produced by a profile save/load round-trip (JogConfigStore precedent).</summary>
public static class StepperCalibrationSettingsStore
{
    private const string SectionKey = "stepper-calibration";
    private const string DistanceKey = "Distance";

    public static StepperCalibrationSettings Load(MachineProfile profile)
    {
        var settings = new StepperCalibrationSettings();
        if (profile.ModuleSettings.TryGetValue(SectionKey, out var raw) &&
            AsObject(raw) is { } section &&
            ReadDouble(section, DistanceKey) is { } distance && distance > 0)
        {
            settings.Distance = distance;
        }
        return settings;
    }

    public static void Save(MachineProfile profile, StepperCalibrationSettings settings)
    {
        var section = profile.ModuleSettings.TryGetValue(SectionKey, out var raw) &&
                      AsObject(raw) is { } existing
            ? existing
            : [];
        section[DistanceKey] = settings.Distance;
        profile.ModuleSettings[SectionKey] = section;
    }

    private static Dictionary<string, object?>? AsObject(object? value) => value switch
    {
        Dictionary<string, object> typed => typed.ToDictionary(kv => kv.Key, kv => (object?)kv.Value),
        JsonElement { ValueKind: JsonValueKind.Object } el => el.EnumerateObject()
            .ToDictionary(p => p.Name, p => (object?)p.Value),
        _ => null,
    };

    private static double? ReadDouble(IReadOnlyDictionary<string, object?> section, string key) =>
        section.TryGetValue(key, out var value)
            ? value switch
            {
                double d => d,
                int i => i,
                long l => l,
                JsonElement { ValueKind: JsonValueKind.Number } e when e.TryGetDouble(out var d) => d,
                _ => null,
            }
            : null;
}