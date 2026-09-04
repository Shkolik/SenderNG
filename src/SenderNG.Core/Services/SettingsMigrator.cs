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
using System.Text.Json.Nodes;

namespace CNC.Core.Services;

/// <summary>
/// One-time migration of a legacy flat or mode-scoped <c>settings.json</c>
/// into the two-tier layout (<c>global.json</c> + <c>profiles/&lt;id&gt;.json</c>),
/// following the mapping in <c>docs/settings-migration.md</c> §4-§5:
/// <list type="bullet">
/// <item>every existing mode block (Mill, Foam) becomes one profile, active = old <c>Mode</c>;</item>
/// <item>per-mode fields split by profile; the mode-nested Jog section unwraps;</item>
/// <item>Console moves to global; other module sections copy into both profiles
/// (Probing drops its <c>ProfilesFile</c> override — the companion path is derived);</item>
/// <item>last-connection state seeds the profile matching the old <c>Mode</c>;</item>
/// <item>the old <c>probing.json</c> companion copies to each profile's <c>.probing.json</c>.</item>
/// </list>
/// Legacy files are never deleted — once <c>global.json</c> exists the migrator
/// is inert. Sections are copied as raw JSON nodes so opaque module payloads
/// (macros, tool names, enabled tools) survive verbatim.
/// </summary>
public static class SettingsMigrator
{
    public const string LegacyFileName = "settings.json";

    /// <summary>
    /// Runs the migration once. Returns the created profile ids when a legacy
    /// <c>settings.json</c> was migrated; empty when there was nothing to do
    /// (no legacy file, or <c>global.json</c> already exists — fresh install).
    /// </summary>
    public static IReadOnlyList<string> MigrateIfNeeded(string dir)
    {
        var legacy = Path.Combine(dir, LegacyFileName);
        if (!File.Exists(legacy) || GlobalSettingsStore.Exists(dir))
            return [];

        JsonObject root;
        try
        {
            root = JsonNode.Parse(File.ReadAllText(legacy)) as JsonObject ?? new JsonObject();
        }
        catch (JsonException ex)
        {
            // Strict like the current SettingsService.Load: an unparseable
            // settings file surfaces the problem instead of silently resetting.
            throw new InvalidDataException($"Cannot migrate {legacy}: {ex.Message}", ex);
        }

        // Legacy flat file (no per-mode sections): seed Mill from the flat
        // fields and give Foam fresh defaults — mirrors the old Load seeding.
        var mode = StringOr(root, "Mode") ?? MachineTypes.Mill;
        var modes = root["Modes"] as JsonObject;
        if (modes is null || modes.Count == 0)
        {
            modes = new JsonObject
            {
                [MachineTypes.Mill] = new JsonObject
                {
                    ["DemoAxes"] = root["DemoAxes"]?.DeepClone() ?? 3,
                    ["Port"] = root["Port"]?.DeepClone() ?? "auto",
                    ["BaudRate"] = root["BaudRate"]?.DeepClone() ?? 115200,
                    ["ModulesVisibility"] = root["ModulesVisibility"]?.DeepClone() ?? new JsonObject(),
                    ["VisibleTools"] = root["VisibleTools"]?.DeepClone() ?? 8,
                },
                [MachineTypes.Foam] = new JsonObject(),
            };
        }

        // Global tier.
        var global = new JsonObject
        {
            ["Language"] = root["Language"]?.DeepClone() ?? string.Empty,
            ["ActiveProfileId"] = ProfileId(mode),
            ["RecentFiles"] = root["RecentFiles"]?.DeepClone() ?? new JsonArray(),
            ["MaxRecentFiles"] = 10,
            ["NetworkAvailable"] = root["NetworkAvailable"]?.DeepClone() ?? true,
            ["Profiles"] = new JsonObject
            {
                ["mill"] = MachineTypes.Mill,
                ["foam"] = MachineTypes.Foam,
            },
            ["ModuleSettings"] = new JsonObject(),
        };

        var sharedSections = root["Modules"] as JsonObject;
        if (sharedSections is not null)
        {
            // Console is observability UI preference → global.
            if (sharedSections["Console"] is { } console)
                GlobalSection(global)["Console"] = console.DeepClone();
        }

        var profileIds = new List<string>();
        foreach (var machineType in new[] { MachineTypes.Mill, MachineTypes.Foam })
        {
            var id = ProfileId(machineType);
            var block = modes[machineType] as JsonObject ?? new JsonObject();
            var isLastUsed = string.Equals(mode, machineType, StringComparison.Ordinal);

            var connection = new JsonObject
            {
                ["SerialPort"] = block["Port"]?.DeepClone() ?? "auto",
                ["SerialBaudRate"] = block["BaudRate"]?.DeepClone() ?? 115200,
                ["SerialResetMode"] = isLastUsed ? root["SerialResetMode"]?.DeepClone() ?? 0 : 0,
                ["LastSerialPort"] = isLastUsed ? root["LastSerialPort"]?.DeepClone() ?? string.Empty : string.Empty,
                ["LastSerialBaudRate"] = isLastUsed ? root["LastSerialBaudRate"]?.DeepClone() ?? 0 : 0,
                // A manually configured NetworkEndpoint is user data: keep it as the
                // initial last-endpoint for the machine that was active (the empty
                // string "no network use yet" must not mask a configured default).
                ["LastNetworkEndpoint"] = isLastUsed ? LastEndpoint(root) : string.Empty,
            };

            var profile = new JsonObject
            {
                ["Id"] = id,
                ["Name"] = machineType,
                ["MachineTypeId"] = machineType,
                ["Connection"] = connection,
                ["DemoAxes"] = block["DemoAxes"]?.DeepClone() ?? 3,
                ["VisibleTools"] = block["VisibleTools"]?.DeepClone() ?? 8,
                ["Modules"] = block["ModulesVisibility"]?.DeepClone() ?? new JsonObject(),
                ["ModuleSettings"] = new JsonObject(),
            };

            // Jog is mode-nested ({ "Mill": {...}, "Foam": {...} }) → unwrap per profile.
            var jog = sharedSections?["Jog"] as JsonObject;
            if (jog is not null && jog[machineType] is { } jogEntry)
                ProfileSections(profile)["Jog"] = jogEntry.DeepClone();

            // Everything else except Console copies verbatim into both profiles.
            if (sharedSections is not null)
            {
                foreach (var (key, value) in sharedSections)
                {
                    if (string.Equals(key, "Jog", StringComparison.Ordinal)
                        || string.Equals(key, "Console", StringComparison.Ordinal))
                        continue;
                    var copy = value!.DeepClone();
                    // Probing's file-path override dies — the companion path is
                    // derived from the profile id (profiles/<id>.probing.json).
                    if (string.Equals(key, "Probing", StringComparison.Ordinal) && copy is JsonObject probing)
                        probing.Remove("ProfilesFile");
                    ProfileSections(profile)[key] = copy;
                }
            }

            ProfilesStore.Save(dir, JsonSerializer.Deserialize<MachineProfile>(profile.ToJsonString())!);
            profileIds.Add(id);

            // Old probing.json companion → per-profile companion (verbatim copy).
            var legacyProbe = Path.Combine(dir, "probing.json");
            if (File.Exists(legacyProbe))
            {
                Directory.CreateDirectory(ProfileSettingsPaths.ProfilesDir(dir));
                File.Copy(legacyProbe, ProfileSettingsPaths.ProbeCompanionPath(dir, id), overwrite: true);
            }
        }

        GlobalSettingsStore.Save(dir, JsonSerializer.Deserialize<GlobalSettings>(global.ToJsonString())!);
        return profileIds;
    }

    private static string ProfileId(string machineType) =>
        string.Equals(machineType, MachineTypes.Foam, StringComparison.Ordinal) ? "foam" : "mill";

    private static string? StringOr(JsonObject obj, string key) =>
        obj[key] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    private static string LastEndpoint(JsonObject root)
    {
        var last = StringOr(root, "LastNetworkEndpoint");
        return !string.IsNullOrEmpty(last) ? last : StringOr(root, "NetworkEndpoint") ?? string.Empty;
    }

    private static JsonObject GlobalSection(JsonObject global) =>
        global["ModuleSettings"] as JsonObject ?? new JsonObject();

    private static JsonObject ProfileSections(JsonObject profile) =>
        profile["ModuleSettings"] as JsonObject ?? new JsonObject();
}
