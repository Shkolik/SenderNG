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

/// <summary>Paths and helpers for the two-tier settings layout
/// (<c>global.json</c> + <c>profiles/&lt;id&gt;.json</c> + per-profile
/// module companion files such as <c>.probing.json</c>). See
/// <c>docs/settings-migration.md</c>.</summary>
public static class ProfileSettingsPaths
{
    public const string GlobalFileName = "global.json";
    public const string ProfilesDirName = "profiles";

    public static string GlobalPath(string dir) => Path.Combine(dir, GlobalFileName);
    public static string ProfilesDir(string dir) => Path.Combine(dir, ProfilesDirName);
    public static string ProfilePath(string dir, string id) => Path.Combine(ProfilesDir(dir), Sanitize(id) + ".json");

    /// <summary>Per-profile companion file path: <c>profiles/&lt;id&gt;&lt;suffix&gt;</c>,
    /// e.g. the probing module's <c>.probing.json</c>.</summary>
    public static string CompanionPath(string dir, string id, string suffix)
        => Path.Combine(ProfilesDir(dir), Sanitize(id) + suffix);

    /// <summary>Id → safe file stem (ids are slugs by construction; defense in depth).</summary>
    public static string Sanitize(string id) => string.IsNullOrWhiteSpace(id) ? MachineProfile.DefaultId : id.Trim();
}

/// <summary>Reads/writes <c>global.json</c> (app-level settings + profile registry).</summary>
public static class GlobalSettingsStore
{
    public static bool Exists(string dir) => File.Exists(ProfileSettingsPaths.GlobalPath(dir));

    public static GlobalSettings Load(string dir)
    {
        var path = ProfileSettingsPaths.GlobalPath(dir);
        if (!File.Exists(path))
            return new GlobalSettings();
        return JsonSerializer.Deserialize<GlobalSettings>(File.ReadAllText(path)) ?? new GlobalSettings();
    }

    public static void Save(string dir, GlobalSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Directory.CreateDirectory(dir);
        File.WriteAllText(ProfileSettingsPaths.GlobalPath(dir),
            JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
    }
}

/// <summary>File I/O for one machine per profile file. Shared-graph semantics
/// (one in-memory instance, single Save) belong to the future rewire of
/// <c>SettingsService</c>; this store is the dumb file layer.</summary>
public static class ProfilesStore
{
    public static bool Exists(string dir, string id) =>
        File.Exists(ProfileSettingsPaths.ProfilePath(dir, id));

    public static MachineProfile? TryLoad(string dir, string id)
    {
        var path = ProfileSettingsPaths.ProfilePath(dir, id);
        if (!File.Exists(path))
            return null;
        return JsonSerializer.Deserialize<MachineProfile>(File.ReadAllText(path));
    }

    public static void Save(string dir, MachineProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        Directory.CreateDirectory(ProfileSettingsPaths.ProfilesDir(dir));
        File.WriteAllText(ProfileSettingsPaths.ProfilePath(dir, profile.Id),
            JsonSerializer.Serialize(profile, new JsonSerializerOptions { WriteIndented = true }));
    }

    public static void Delete(string dir, string id)
    {
        var path = ProfileSettingsPaths.ProfilePath(dir, id);
        if (File.Exists(path))
            File.Delete(path);
    }

    /// <summary>Profile ids on disk — every *.json in the profiles dir except
    /// per-module companion files (ids are dot-free slugs; companions are
    /// always <c>&lt;id&gt;.&lt;suffix&gt;.json</c>, so a dot in the stem means
    /// "companion, not a profile").</summary>
    public static IEnumerable<string> Ids(string dir)
    {
        var profiles = ProfileSettingsPaths.ProfilesDir(dir);
        if (!Directory.Exists(profiles))
            return [];
        return Directory.EnumerateFiles(profiles, "*.json")
            // Profile files are <id>.json and ids are dot-free slugs; per-module
            // companions are always <id>.<suffix>.json, so a dot in the stem
            // (file name without ".json") means "companion, not a profile".
            .Where(p => !Path.GetFileNameWithoutExtension(p).Contains('.'))
            .Select(p => Path.GetFileNameWithoutExtension(p))
            .ToArray();
    }
}
