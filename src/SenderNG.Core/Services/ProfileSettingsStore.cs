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

/// <summary>Why a settings file could not be loaded.</summary>
public enum SettingsFallbackReason
{
    /// <summary>The bytes are bad: malformed, truncated or hand-edited JSON.</summary>
    Corrupt,

    /// <summary>The bytes may be fine, but the file could not be read - locked,
    /// missing permission, or an I/O fault.</summary>
    Unreadable,
}

/// <summary>
/// A settings file that could not be loaded, so the caller fell back to defaults.
/// Structured rather than a formatted string on purpose: the message is composed
/// by the host, which owns the <c>app.msg.*</c> resources — Core emits no user-facing
/// text (audit §1.6).
/// </summary>
/// <param name="Path">Full path of the file that could not be loaded.</param>
/// <param name="Reason">Whether the content was bad or merely unreachable.</param>
/// <param name="Error">The underlying exception, for logging or detail.</param>
public sealed record SettingsFallback(string Path, SettingsFallbackReason Reason, Exception? Error)
{
    /// <summary>True when the original was moved aside to <c>&lt;path&gt;.corrupt</c>.
    /// False when quarantine was attempted and failed, which means the file is
    /// still sitting in place.</summary>
    public bool Quarantined { get; init; }
}

/// <summary>File-level helpers shared by the settings stores.</summary>
public static class ProfileSettingsStore
{
    /// <summary>
    /// Returns the first free quarantine path for <paramref name="path"/>:
    /// <c>&lt;path&gt;.corrupt</c>, then <c>&lt;path&gt;.corrupt.1</c>, <c>.2</c>, …
    /// Never returns a name that already exists, so an earlier bad file the user
    /// may still want is not overwritten. Both files and directories count as
    /// taken, since <c>File.Move</c> would fail on a directory destination.
    /// This creates, moves and deletes nothing — it only reads directory
    /// entries to find a free name.
    /// </summary>
    public static string QuarantinePath(string path)
    {
        var candidate = path + ".corrupt";
        for (var n = 1; File.Exists(candidate) || Directory.Exists(candidate); n++)
            candidate = $"{path}.corrupt.{n}";
        return candidate;
    }
}

/// <summary>Reads/writes <c>global.json</c> (app-level settings + profile registry).</summary>
public static class GlobalSettingsStore
{
    public static bool Exists(string dir) => File.Exists(ProfileSettingsPaths.GlobalPath(dir));

    /// <summary>Reads <c>global.json</c>, falling back to defaults when the
    /// file is corrupt, truncated or unreadable. Never throws for bad file
    /// content — this is on the startup path (audit §1.6).
    ///
    /// A file that exists but cannot be loaded is moved aside to
    /// <c>&lt;name&gt;.corrupt</c> and reported through
    /// <paramref name="onFallback"/>, because the next Save overwrites
    /// <c>global.json</c> in place and would otherwise destroy the only copy.
    /// </summary>
    /// <param name="dir">Directory holding <c>global.json</c>.</param>
    /// <param name="onFallback">Receives the structured report. Optional, so
    /// existing callers keep compiling. A callback that throws is contained
    /// here — reporting must never turn a recoverable fallback back into the
    /// startup crash this finding is about (see <see cref="Report"/>).</param>
    public static GlobalSettings Load(string dir, Action<SettingsFallback>? onFallback = null)
    {
        var path = ProfileSettingsPaths.GlobalPath(dir);
        // A directory at the settings path is NOT a fresh install: File.Exists is
        // false for one, so testing only the file would silently serve defaults
        // forever and then throw on the next Save. Read it, let it fail as
        // Unreadable, and let the caller hear about it.
        if (!File.Exists(path) && !Directory.Exists(path))
            return new GlobalSettings();
        try
        {
            return JsonSerializer.Deserialize<GlobalSettings>(File.ReadAllText(path)) ?? new GlobalSettings();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // Corrupt/truncated JSON, or a locked/unreadable file. The other
            // settings loaders fall back the same way; throwing here would take
            // down startup (HostSetup reads Language via SettingsService.Global).
            // UnauthorizedAccessException is not an IOException, so without it in
            // this filter a permission problem still crashed startup.
            var reason = ex is JsonException
                ? SettingsFallbackReason.Corrupt
                : SettingsFallbackReason.Unreadable;
            Report(onFallback, Quarantine(path, reason, ex));
        }
        return new GlobalSettings();
    }

    /// <summary>
    /// Invokes <paramref name="onFallback"/>, containing any failure it raises.
    /// The host callback resolves a service, formats a message and writes to the
    /// status bar, so it is arbitrary code running inside what is otherwise a
    /// never-throw loader on the <c>HostSetup</c> startup path. An exception
    /// escaping here would be the same crash audit §1.6 exists to close, arriving
    /// through the notification instead of the file.
    ///
    /// The catch is deliberately broad — this is the outermost boundary of a
    /// best-effort notification, and the alternative is an exception escaping
    /// during startup. It is not silent: <see cref="System.Diagnostics.Debug"/>
    /// output and the swallowed-exception comment keep the failure visible to a
    /// developer, because there is no logging framework in Core to route it to
    /// and no return channel left to report it on.
    /// </summary>
    private static void Report(Action<SettingsFallback>? onFallback, SettingsFallback report)
    {
        try
        {
            onFallback?.Invoke(report);
        }
        catch (Exception)
        {
            // Swallowed on purpose: a broken callback must not take down startup.
            System.Diagnostics.Debug.WriteLine("settings-fallback reporting failed: " + report);
        }
    }

    /// <summary>
    /// Moves <paramref name="path"/> aside so the next Save cannot destroy it, and
    /// returns the report. Best-effort: if the move fails (read-only directory,
    /// name collision that cannot be resolved) the file is left in place and
    /// <see cref="SettingsFallback.Quarantined"/> is false, which lets the caller
    /// tell the user the original still exists. Never throws — a failure to
    /// preserve a file must not become the startup crash this finding is about.
    /// </summary>
    private static SettingsFallback Quarantine(string path, SettingsFallbackReason reason, Exception error)
    {
        try
        {
            File.Move(path, ProfileSettingsStore.QuarantinePath(path));
            return new SettingsFallback(path, reason, error) { Quarantined = true };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new SettingsFallback(path, reason, error) { Quarantined = false };
        }
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

    /// <summary>Loads one profile, or null when it is absent, corrupt,
    /// truncated or unreadable. Being a <c>Try</c> method it must not throw
    /// for bad file content (audit §1.6). A file that exists but cannot be loaded
    /// is moved aside to <c>&lt;name&gt;.corrupt</c> and reported through
    /// <paramref name="onFallback"/>.</summary>
    /// <param name="dir">Directory holding the <c>profiles</c> subdirectory.</param>
    /// <param name="id">Profile id; sanitized into the file stem.</param>
    /// <param name="onFallback">Receives the structured report. Optional, so
    /// existing callers keep compiling. A callback that throws is contained
    /// here — reporting must never turn a recoverable fallback back into the
    /// startup crash this finding is about (see <see cref="Report"/>).</param>
    public static MachineProfile? TryLoad(string dir, string id, Action<SettingsFallback>? onFallback = null)
    {
        var path = ProfileSettingsPaths.ProfilePath(dir, id);
        // Same rule as GlobalSettingsStore.Load: a directory squatting on the
        // profile path is unreadable, not absent.
        if (!File.Exists(path) && !Directory.Exists(path))
            return null;
        try
        {
            return JsonSerializer.Deserialize<MachineProfile>(File.ReadAllText(path));
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // Null is this method's "not loadable" answer; callers fall back to
            // a default profile (SettingsService.Load does `?? new MachineProfile()`).
            var reason = ex is JsonException
                ? SettingsFallbackReason.Corrupt
                : SettingsFallbackReason.Unreadable;
            Report(onFallback, Quarantine(path, reason, ex));
        }
        return null;
    }

    /// <summary>
    /// Invokes <paramref name="onFallback"/>, containing any failure it raises.
    /// The host callback resolves a service, formats a message and writes to the
    /// status bar, so it is arbitrary code running inside what is otherwise a
    /// never-throw loader on the <c>HostSetup</c> startup path. An exception
    /// escaping here would be the same crash audit §1.6 exists to close, arriving
    /// through the notification instead of the file.
    ///
    /// The catch is deliberately broad — this is the outermost boundary of a
    /// best-effort notification, and the alternative is an exception escaping
    /// during startup. It is not silent: <see cref="System.Diagnostics.Debug"/>
    /// output and the swallowed-exception comment keep the failure visible to a
    /// developer, because there is no logging framework in Core to route it to
    /// and no return channel left to report it on.
    /// </summary>
    private static void Report(Action<SettingsFallback>? onFallback, SettingsFallback report)
    {
        try
        {
            onFallback?.Invoke(report);
        }
        catch (Exception)
        {
            // Swallowed on purpose: a broken callback must not take down startup.
            System.Diagnostics.Debug.WriteLine("settings-fallback reporting failed: " + report);
        }
    }

    /// <summary>
    /// Moves <paramref name="path"/> aside so the next Save cannot destroy it, and
    /// returns the report. Best-effort: if the move fails (read-only directory,
    /// name collision that cannot be resolved) the file is left in place and
    /// <see cref="SettingsFallback.Quarantined"/> is false, which lets the caller
    /// tell the user the original still exists. Never throws — a failure to
    /// preserve a file must not become the startup crash this finding is about.
    /// </summary>
    private static SettingsFallback Quarantine(string path, SettingsFallbackReason reason, Exception error)
    {
        try
        {
            File.Move(path, ProfileSettingsStore.QuarantinePath(path));
            return new SettingsFallback(path, reason, error) { Quarantined = true };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new SettingsFallback(path, reason, error) { Quarantined = false };
        }
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
