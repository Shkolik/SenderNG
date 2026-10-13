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

using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace CNC.Core.Services;

/// <summary>Manifest (<c>.senderng-backup.json</c>) of an app-settings archive.</summary>
public sealed record AppSettingsBundleManifest(string Kind, int FormatVersion, string CreatedUtc, string AppVersion, int FileCount);

/// <summary>
/// An app-settings snapshot: settings-dir-relative path → verbatim file text.
/// The archive layout <b>is</b> the live layout (<c>global.json</c> +
/// <c>profiles/&lt;id&gt;.json</c>), so unzipping it into the settings directory
/// reproduces the live directory by hand; the only extra entry is the
/// dot-prefixed manifest, which the loaders ignore.
/// </summary>
public sealed class AppSettingsBundle
{
    /// <summary>Manifest <c>kind</c> identifying a SenderNG app-settings archive.</summary>
    public const string Kind = "senderng-app-settings";

    /// <summary>Archive format this build writes and can read.</summary>
    public const int CurrentFormatVersion = 1;

    /// <summary>File extension of the app-settings archive.</summary>
    public const string Extension = ".ngsettings.zip";

    /// <summary>Suggested backup file name: <c>senderng-settings-yyyy-MM-dd.ngsettings.zip</c>.</summary>
    public static string SuggestedFileName(DateTimeOffset now) =>
        "senderng-settings-" + now.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + Extension;

    /// <summary>Archive metadata. <see cref="AppSettingsBundleManifest.FileCount"/> counts the
    /// settings files only — the manifest itself is not a settings file.</summary>
    public AppSettingsBundleManifest Manifest { get; init; } =
        new(Kind, CurrentFormatVersion, string.Empty, string.Empty, 0);

    /// <summary>Entry name → verbatim file text. Entry names are settings-dir-relative
    /// with forward slashes: <c>global.json</c>, <c>profiles/mill.json</c>, …</summary>
    public IReadOnlyDictionary<string, string> Files { get; init; } = new Dictionary<string, string>();

    /// <summary>Reads the live settings dir into an archive model. Throws on IO failure.</summary>
    public static AppSettingsBundle Capture(string settingsDir, string appVersion, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(settingsDir);

        var globalPath = ProfileSettingsPaths.GlobalPath(settingsDir);
        if (!File.Exists(globalPath))
            throw new FileNotFoundException("The settings directory has no global.json.", globalPath);

        var files = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            [ProfileSettingsPaths.GlobalFileName] = File.ReadAllText(globalPath),
        };

        var profilesDir = ProfileSettingsPaths.ProfilesDir(settingsDir);
        if (Directory.Exists(profilesDir))
        {
            foreach (var path in Directory.EnumerateFiles(profilesDir, "*.json"))
            {
                var name = Path.GetFileName(path);
                // Quarantine leftovers and half-written temp files are never archived.
                if (name.Contains(".corrupt", StringComparison.OrdinalIgnoreCase) ||
                    name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (Path.GetFileNameWithoutExtension(name).Length == 0)
                    continue;
                files[ProfileSettingsPaths.ProfilesDirName + "/" + name] = File.ReadAllText(path);
            }
        }

        var manifest = new AppSettingsBundleManifest(
            Kind, CurrentFormatVersion, FormatUtc(now), appVersion, files.Count);
        return new AppSettingsBundle { Manifest = manifest, Files = files };
    }

    /// <summary>
    /// Writes a deterministic archive at <paramref name="path"/>: entries in a fixed
    /// order (manifest, <c>global.json</c>, profiles sorted ordinal),
    /// <see cref="CompressionLevel.Optimal"/> and every entry stamped with
    /// <paramref name="now"/> (clamped into the zip range 1980-01-01…2107-12-31), so
    /// identical inputs produce byte-identical archives.
    /// </summary>
    public void WriteTo(string path, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var stamp = ClampToZipRange(now);
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        using var file = File.Create(path);
        using var archive = new ZipArchive(file, ZipArchiveMode.Create);
        WriteEntry(archive, AppSettingsBundleLayout.ManifestEntryName,
            JsonSerializer.Serialize(Manifest, JsonOptions), stamp);
        foreach (var (name, text) in OrderedEntries())
            WriteEntry(archive, name, text, stamp);
    }

    /// <summary>Reads + structurally validates an archive. Never throws for bad input.</summary>
    public static AppSettingsBundleReadResult ReadFrom(string path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return new AppSettingsBundleReadResult(null, $"The backup file '{path}' was not found.");

            using var file = File.OpenRead(path);
            using var archive = new ZipArchive(file, ZipArchiveMode.Read);
            var entries = archive.Entries;

            // Structure before payload: count, then names, then declared sizes.
            if (entries.Count > AppSettingsBundleLayout.MaxEntries)
                return new AppSettingsBundleReadResult(
                    null, $"The archive has {entries.Count} entries; at most {AppSettingsBundleLayout.MaxEntries} are allowed.");

            // Case-insensitive: profile ids are lowercase slugs by construction, so two
            // entries differing only in case are a duplicate, not two files. With an
            // ordinal set the second one would silently overwrite the first on Windows.
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in entries)
            {
                if (!AppSettingsBundleLayout.IsAllowedEntryName(entry.FullName))
                    return new AppSettingsBundleReadResult(null, $"The archive contains an unexpected entry '{entry.FullName}'.");
                if (!seen.Add(entry.FullName))
                    return new AppSettingsBundleReadResult(null, $"The archive contains '{entry.FullName}' more than once.");
                if (entry.Length > AppSettingsBundleLayout.MaxEntryBytes)
                    return new AppSettingsBundleReadResult(
                        null, $"'{entry.FullName}' is larger than {AppSettingsBundleLayout.MaxEntryBytes} bytes.");
            }

            // Payloads, with the caps enforced again while copying: a zip entry's
            // declared length is not trustworthy.
            var files = new SortedDictionary<string, string>(StringComparer.Ordinal);
            string? manifestJson = null;
            long total = 0;
            foreach (var entry in entries)
            {
                if (!TryReadEntry(entry, AppSettingsBundleLayout.MaxEntryBytes, out var text, out var bytes, out var error))
                    return new AppSettingsBundleReadResult(null, error);
                total += bytes;
                if (total > AppSettingsBundleLayout.MaxTotalBytes)
                    return new AppSettingsBundleReadResult(
                        null, $"The archive holds more than {AppSettingsBundleLayout.MaxTotalBytes} bytes.");
                if (AppSettingsBundleLayout.IsManifest(entry.FullName))
                    manifestJson = text;
                else
                    files[entry.FullName] = text;
            }

            if (manifestJson is null)
                return new AppSettingsBundleReadResult(
                    null, $"The archive has no {AppSettingsBundleLayout.ManifestEntryName} manifest.");

            AppSettingsBundleManifest? manifest;
            try
            {
                manifest = JsonSerializer.Deserialize<AppSettingsBundleManifest>(manifestJson, JsonOptions);
            }
            catch (JsonException ex)
            {
                return new AppSettingsBundleReadResult(null, "The archive manifest is not valid JSON: " + ex.Message);
            }

            if (manifest is null)
                return new AppSettingsBundleReadResult(null, "The archive manifest is empty.");
            if (!string.Equals(manifest.Kind, Kind, StringComparison.Ordinal))
                return new AppSettingsBundleReadResult(
                    null, $"Not a SenderNG app-settings archive (kind '{manifest.Kind}').");
            if (manifest.FormatVersion < 1 || manifest.FormatVersion > CurrentFormatVersion)
                return new AppSettingsBundleReadResult(
                    null, $"Archive format version {manifest.FormatVersion} is not supported by this build ({CurrentFormatVersion}).");

            return new AppSettingsBundleReadResult(new AppSettingsBundle { Manifest = manifest, Files = files }, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException
                                      or JsonException or ArgumentException or NotSupportedException or FormatException)
        {
            return new AppSettingsBundleReadResult(null, "The backup is not a readable settings archive: " + ex.Message);
        }
    }

    /// <summary>Manifest, then <c>global.json</c>, then profiles sorted ordinal.</summary>
    private IEnumerable<KeyValuePair<string, string>> OrderedEntries()
    {
        if (Files.TryGetValue(ProfileSettingsPaths.GlobalFileName, out var global))
            yield return new KeyValuePair<string, string>(ProfileSettingsPaths.GlobalFileName, global);
        foreach (var pair in Files
                     .Where(p => p.Key != ProfileSettingsPaths.GlobalFileName)
                     .OrderBy(p => p.Key, StringComparer.Ordinal))
            yield return pair;
    }

    private static void WriteEntry(ZipArchive archive, string name, string text, DateTimeOffset stamp)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        entry.LastWriteTime = stamp;
        using var stream = entry.Open();
        var bytes = Encoding.UTF8.GetBytes(text);
        stream.Write(bytes, 0, bytes.Length);
    }

    /// <summary>Copies an entry into memory, refusing to read past <paramref name="cap"/> bytes.</summary>
    private static bool TryReadEntry(ZipArchiveEntry entry, long cap, out string text, out long bytes, out string? error)
    {
        text = string.Empty;
        bytes = 0;
        error = null;
        try
        {
            using var source = entry.Open();
            using var buffer = new MemoryStream();
            var chunk = new byte[81920];
            long total = 0;
            int read;
            while ((read = source.Read(chunk, 0, chunk.Length)) > 0)
            {
                total += read;
                if (total > cap)
                {
                    error = $"'{entry.FullName}' is larger than {cap} bytes.";
                    return false;
                }
                buffer.Write(chunk, 0, read);
            }
            bytes = total;
            text = Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
            return true;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or NotSupportedException)
        {
            error = $"'{entry.FullName}' could not be read: {ex.Message}";
            return false;
        }
    }

    private static readonly DateTime ZipMin = new(1980, 1, 1, 0, 0, 0, DateTimeKind.Unspecified);
    private static readonly DateTime ZipMax = new(2107, 12, 31, 23, 59, 58, DateTimeKind.Unspecified);

    private static DateTimeOffset ClampToZipRange(DateTimeOffset now)
    {
        // Zip timestamps are local wall-clock with no zone; clamp in that frame.
        var local = now.ToLocalTime();
        var value = local.DateTime;
        if (value < ZipMin)
            value = ZipMin;
        else if (value > ZipMax)
            value = ZipMax;
        return new DateTimeOffset(value, local.Offset);
    }

    private static string FormatUtc(DateTimeOffset now) =>
        now.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };
}

/// <summary>Outcome of reading an archive: the bundle, or the reason it was refused.</summary>
public sealed record AppSettingsBundleReadResult(AppSettingsBundle? Bundle, string? Error)
{
    /// <summary>True when the archive was read and structurally accepted.</summary>
    public bool Ok => Bundle is not null;
}

/// <summary>Payload validation (errors block a restore, warnings do not).</summary>
public sealed record AppSettingsBundleValidation(IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings)
{
    /// <summary>True when nothing blocks the restore.</summary>
    public bool Ok => Errors.Count == 0;
}

/// <summary>
/// Payload validation: every settings file in the bundle must deserialize and the
/// profile registry must agree, checked before any restore writes anything.
/// </summary>
public static class AppSettingsBundleValidator
{
    public static AppSettingsBundleValidation Validate(AppSettingsBundle bundle)
    {
        ArgumentNullException.ThrowIfNull(bundle);

        var errors = new List<string>();
        var warnings = new List<string>();
        GlobalSettings? global = null;

        if (!bundle.Files.TryGetValue(ProfileSettingsPaths.GlobalFileName, out var globalJson))
        {
            errors.Add("The archive has no global.json.");
        }
        else if (TryDeserialize<GlobalSettings>(globalJson, out var parsed, out var error))
        {
            global = parsed;
        }
        else
        {
            errors.Add($"global.json is not valid settings: {error}");
        }

        var profileIds = new List<string>();
        foreach (var name in bundle.Files.Keys)
        {
            if (!AppSettingsBundleLayout.IsProfileEntry(name))
                continue;
            // Companions (<id>.<suffix>.json) are opaque and restored verbatim: no loader
            // ever parses one — ProfilesStore.Ids skips a dotted stem, and the only writer
            // is ProfileSettingsPaths.CompanionPath — so their shape is module-owned. The
            // probing companion is a JSON *array* (ProbingProfileStore.Save serializes
            // List<ProbingProfile>), which is not a MachineProfile; validating it here
            // would refuse a real user's own backup. Only profiles/<id>.json is a profile.
            if (AppSettingsBundleLayout.ProfileIdOf(name) is not { } id)
                continue;
            profileIds.Add(id);
            if (!TryDeserialize<MachineProfile>(bundle.Files[name], out var profile, out var error))
                errors.Add($"{name} is not valid settings: {error}");
            else if (!string.Equals(profile!.Id, id, StringComparison.Ordinal))
                errors.Add(
                    $"{name} holds profile id '{profile.Id}', not '{id}': the loaders read the id " +
                    "from the payload, so the next save would write a different file.");
        }

        if (global is not null && !profileIds.Contains(global.ActiveProfileId, StringComparer.Ordinal))
            warnings.Add(
                $"The archive's active profile '{global.ActiveProfileId}' is not among its {profileIds.Count} profiles; " +
                "the first profile on disk will be used.");

        return new AppSettingsBundleValidation(errors, warnings);
    }

    /// <summary>
    /// Deserializes a payload the way its loader will — the loaders' own options
    /// (<see cref="JsonSerializer.Deserialize{TValue}(string, JsonSerializerOptions?)"/>
    /// with none, i.e. case-sensitive), not <see cref="JsonSerializerDefaults.Web"/>:
    /// validation has to predict what <c>GlobalSettingsStore.Load</c> /
    /// <c>ProfilesStore.TryLoad</c> actually read.
    /// </summary>
    private static bool TryDeserialize<T>(string json, out T? value, out string error)
    {
        try
        {
            value = JsonSerializer.Deserialize<T>(json);
            if (value is null)
            {
                error = "the payload is null";
                return false;
            }

            // Case-insensitive binding would silently fill in what the loader drops:
            // a camelCase global.json reads as defaults, losing the profile registry
            // and the active-profile pointer. Refuse a payload whose case-insensitive
            // read differs from the case-sensitive one the loader performs.
            var lenient = JsonSerializer.Deserialize<T>(json, AppSettingsBundle.JsonOptions);
            if (!string.Equals(
                    JsonSerializer.Serialize(value, AppSettingsBundle.JsonOptions),
                    JsonSerializer.Serialize(lenient, AppSettingsBundle.JsonOptions),
                    StringComparison.Ordinal))
            {
                value = default;
                error = "the payload binds only case-insensitively, so the loader would read defaults " +
                        "from it (property names must match the file format exactly)";
                return false;
            }

            error = string.Empty;
            return true;
        }
        catch (JsonException ex)
        {
            value = default;
            error = ex.Message;
            return false;
        }
    }
}

/// <summary>Archive layout rules — entry-name shape and the read-side caps.</summary>
internal static class AppSettingsBundleLayout
{
    /// <summary>Dot-prefixed so it can never collide with the live settings layout.</summary>
    internal const string ManifestEntryName = ".senderng-backup.json";

    internal const int MaxEntries = 256;
    internal const long MaxEntryBytes = 4L * 1024 * 1024;
    internal const long MaxTotalBytes = 16L * 1024 * 1024;

    internal static bool IsManifest(string name) => string.Equals(name, ManifestEntryName, StringComparison.Ordinal);

    /// <summary>
    /// Exactly <c>global.json</c>, <c>.senderng-backup.json</c> or one
    /// <c>profiles/&lt;name&gt;.json</c> segment: no absolute paths, no <c>..</c>,
    /// no backslashes, nothing nested deeper than <c>profiles/</c>.
    /// </summary>
    internal static bool IsAllowedEntryName(string name)
    {
        if (string.IsNullOrEmpty(name))
            return false;
        if (IsManifest(name) || string.Equals(name, ProfileSettingsPaths.GlobalFileName, StringComparison.Ordinal))
            return true;
        if (name.Contains('\\') || name.Contains("..", StringComparison.Ordinal) ||
            name.Contains(':') || name.StartsWith('/'))
            return false;
        var prefix = ProfileSettingsPaths.ProfilesDirName + "/";
        return name.StartsWith(prefix, StringComparison.Ordinal) && IsProfileFileName(name[prefix.Length..]);
    }

    internal static bool IsProfileEntry(string name) =>
        name.StartsWith(ProfileSettingsPaths.ProfilesDirName + "/", StringComparison.Ordinal) &&
        IsProfileFileName(name[(ProfileSettingsPaths.ProfilesDirName.Length + 1)..]);

    /// <summary>Profile id of a <c>profiles/&lt;id&gt;.json</c> entry, or null for a
    /// per-module companion — a dot in the stem means "companion, not a profile",
    /// the same rule <see cref="ProfilesStore.Ids"/> uses.</summary>
    internal static string? ProfileIdOf(string name)
    {
        if (!IsProfileEntry(name))
            return null;
        var stem = Path.GetFileNameWithoutExtension(name);
        return stem.Length == 0 || stem.Contains('.') ? null : stem;
    }

    private static bool IsProfileFileName(string file) =>
        file.Length > ".json".Length &&
        !file.Contains('/') &&
        file.EndsWith(".json", StringComparison.OrdinalIgnoreCase);
}
