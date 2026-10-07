/*
 * ModuleSettingsJson.cs - generic per-module JSON settings store (SDK)
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
using CNC.Core.Services;

namespace SenderNG.Modules.Common;

/// <summary>
/// Generic per-module JSON settings store (docs/plugin-development.md §8 — the
/// SDK's settings helper). Reads/writes a typed settings object under the
/// module's id in <see cref="MachineProfile.ModuleSettings"/> (profile scope:
/// parameters belong to a machine, never to the app). Missing or corrupt
/// entries return null (callers fall back to defaults), so a fresh profile
/// works with zero configuration. The built-in converters build their
/// <c>ConverterSettingsStore</c> on this; third-party plugins may use it too.
/// </summary>
public static class ModuleSettingsJson
{
    /// <summary>Deserializes the module's settings section, or null when absent/corrupt.</summary>
    public static T? Load<T>(MachineProfile profile, string moduleId) where T : class
    {
        if (profile.ModuleSettings.TryGetValue(moduleId, out var raw) && raw is string json)
            return JsonDefaults.TryDeserialize<T>(json);

        return null;
    }

    /// <summary>Serializes the module's settings into its profile section.</summary>
    public static void Save<T>(MachineProfile profile, string moduleId, T settings)
    {
        profile.ModuleSettings[moduleId] = JsonSerializer.Serialize(settings);
    }
}