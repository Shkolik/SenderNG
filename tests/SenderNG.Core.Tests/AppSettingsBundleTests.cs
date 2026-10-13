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

using System.IO.Compression;
using System.Text;
using System.Text.Json;
using CNC.Core.Services;

namespace SenderNG.Core.Tests;

public class AppSettingsBundleTests
{
    private static readonly DateTimeOffset Stamp = new(2026, 10, 13, 12, 0, 0, TimeSpan.Zero);

    // Deliberate odd whitespace: entries are stored verbatim, not re-serialized.
    private const string GlobalJsonText =
        "{\n  \"ActiveProfileId\": \"mill\",\n\n  \"Profiles\": { \"mill\": \"Mill\", \"foam\": \"Foam\" },\n  \"Language\": \"\"\n}\n";

    private static string TempDir() => Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

    private static string ProfileJson(string id) =>
        "{\n   \"Id\": \"" + id + "\",\n  \n      \"Name\": \"" + id + "\",\n  \"MachineTypeId\": \"Mill\"\n}\n";

    /// <summary><c>profiles/&lt;id&gt;.probing.json</c> in the shape the probing module writes it:
    /// <c>ProbingProfileStore.Save</c> serializes <c>List&lt;ProbingProfile&gt;</c> (PascalCase,
    /// indented), so a companion is a JSON <b>array</b>, never a <c>MachineProfile</c>.</summary>
    private const string ProbingCompanionJson =
        "[\n  {\n    \"Name\": \"<Default>\",\n    \"ProbeDiameter\": 2,\n    \"TouchPlateHeight\": 1,\n" +
        "    \"ProbeFeedRate\": 100\n  }\n]";

    /// <summary>The Task 2 fixture: global.json, three profile files (one a companion),
    /// a bystander non-JSON file and quarantine leftovers.</summary>
    private static string MakeLiveDir()
    {
        var dir = TempDir();
        Directory.CreateDirectory(Path.Combine(dir, "profiles"));
        File.WriteAllText(Path.Combine(dir, "global.json"), GlobalJsonText);
        File.WriteAllText(Path.Combine(dir, "profiles", "mill.json"), ProfileJson("mill"));
        File.WriteAllText(Path.Combine(dir, "profiles", "mill.probing.json"), "{\n  \"Enabled\": true\n}\n");
        File.WriteAllText(Path.Combine(dir, "profiles", "foam.json"), ProfileJson("foam"));
        File.WriteAllText(Path.Combine(dir, "profiles", "mill.probing.backup.txt"), "not json");
        File.WriteAllText(Path.Combine(dir, "global.json.corrupt"), "{ broken");
        File.WriteAllText(Path.Combine(dir, "profiles", "mill.json.corrupt"), "{ broken");
        return dir;
    }

    private static string CaptureToArchive(string settingsDir, out AppSettingsBundle bundle)
    {
        bundle = AppSettingsBundle.Capture(settingsDir, "1.2.3", Stamp);
        var path = Path.Combine(TempDir(), "settings" + AppSettingsBundle.Extension);
        bundle.WriteTo(path, Stamp);
        return path;
    }

    private static string ManifestJson(string kind = AppSettingsBundle.Kind, int formatVersion = 1, int fileCount = 1) =>
        "{\"kind\":\"" + kind + "\",\"formatVersion\":" + formatVersion +
        ",\"createdUtc\":\"2026-10-13T12:00:00Z\",\"appVersion\":\"1.2.3\",\"fileCount\":" + fileCount + "}";

    /// <summary>A hostile/foreign archive built straight from entries.</summary>
    private static string WriteZip(string path, params (string Name, string Text)[] entries)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var (name, text) in entries)
        {
            var entry = archive.CreateEntry(name);
            using var stream = entry.Open();
            var bytes = Encoding.UTF8.GetBytes(text);
            stream.Write(bytes, 0, bytes.Length);
        }
        return path;
    }

    private static string ZipPath(string stem)
    {
        var dir = TempDir();
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, stem + AppSettingsBundle.Extension);
    }

    private static readonly byte[] Utf8Bom = [0xEF, 0xBB, 0xBF];

    /// <summary>Writes a UTF-8 file with a byte-order mark, as some editors do.</summary>
    private static void WriteWithBom(string path, string text) =>
        File.WriteAllBytes(path, [.. Utf8Bom, .. Encoding.UTF8.GetBytes(text)]);

    private static string ReadEntryText(string zipPath, string name)
    {
        using var archive = ZipFile.OpenRead(zipPath);
        var entry = archive.GetEntry(name);
        Assert.NotNull(entry);
        using var reader = new StreamReader(entry!.Open(), Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private static AppSettingsBundle BundleOf(params (string Name, string Text)[] files) => new()
    {
        Manifest = new AppSettingsBundleManifest(
            AppSettingsBundle.Kind, AppSettingsBundle.CurrentFormatVersion, "2026-10-13T12:00:00Z", "1.2.3", files.Length),
        Files = files.ToDictionary(f => f.Name, f => f.Text, StringComparer.Ordinal),
    };

    [Fact]
    public void SuggestedFileName_is_dated_and_uses_the_archive_extension()
    {
        Assert.Equal("senderng-settings-2026-10-13" + AppSettingsBundle.Extension,
            AppSettingsBundle.SuggestedFileName(Stamp));
    }

    [Fact]
    public void Capture_includes_global_json_and_every_profile_and_companion()
    {
        var bundle = AppSettingsBundle.Capture(MakeLiveDir(), "1.2.3", Stamp);

        Assert.Equal(
            new[] { "global.json", "profiles/foam.json", "profiles/mill.json", "profiles/mill.probing.json" },
            bundle.Files.Keys.ToArray());
        Assert.Equal(AppSettingsBundle.Kind, bundle.Manifest.Kind);
        Assert.Equal(AppSettingsBundle.CurrentFormatVersion, bundle.Manifest.FormatVersion);
        Assert.Equal("1.2.3", bundle.Manifest.AppVersion);
        Assert.Equal("2026-10-13T12:00:00Z", bundle.Manifest.CreatedUtc);
        Assert.Equal(bundle.Files.Count, bundle.Manifest.FileCount);
    }

    [Fact]
    public void Capture_excludes_corrupt_quarantine_files()
    {
        var bundle = AppSettingsBundle.Capture(MakeLiveDir(), "1.2.3", Stamp);

        Assert.DoesNotContain(bundle.Files.Keys, key => key.Contains(".corrupt", StringComparison.Ordinal));
        Assert.DoesNotContain(bundle.Files.Keys, key => key.Contains(".tmp", StringComparison.Ordinal));
    }

    [Fact]
    public void Capture_stores_file_bytes_verbatim()
    {
        var dir = MakeLiveDir();
        var bundle = AppSettingsBundle.Capture(dir, "1.2.3", Stamp);

        foreach (var (key, text) in bundle.Files)
            Assert.Equal(File.ReadAllText(Path.Combine(dir, key.Replace('/', Path.DirectorySeparatorChar))), text);

        // The odd whitespace in the fixture survived: this is a byte copy, not a re-serialize.
        Assert.Equal(ProfileJson("mill"), bundle.Files["profiles/mill.json"]);
        Assert.Contains("\n  \n", bundle.Files["profiles/mill.json"], StringComparison.Ordinal);
    }

    [Fact]
    public void Capture_and_restore_keep_a_BOMd_non_ASCII_file_as_the_same_text()
    {
        // What round-trips is the file's text as UTF-8, not its raw bytes: the BOM is
        // normalised away (Capture's ReadAllText detects and strips it; the write side
        // emits UTF-8 with no BOM). This pins both halves of that claim.
        const string globalText =
            "{\"ActiveProfileId\":\"default\",\"Profiles\":{\"default\":\"Мельница\"},\"Language\":\"ru\"}";
        const string profileText = "{\"Id\":\"default\",\"Name\":\"Станок\"}";
        var source = TempDir();
        Directory.CreateDirectory(Path.Combine(source, "profiles"));
        WriteWithBom(Path.Combine(source, "global.json"), globalText);
        WriteWithBom(Path.Combine(source, "profiles", "default.json"), profileText);

        var bundle = AppSettingsBundle.Capture(source, "1.2.3", Stamp);
        Assert.Equal(globalText, bundle.Files["global.json"]);
        Assert.Equal(profileText, bundle.Files["profiles/default.json"]);

        var archive = ZipPath("bom");
        bundle.WriteTo(archive, Stamp);
        var read = AppSettingsBundle.ReadFrom(archive);
        Assert.True(read.Ok, read.Error);
        Assert.Equal(globalText, read.Bundle!.Files["global.json"]);

        var target = TempDir();
        var result = AppSettingsRestore.Apply(target, read.Bundle);
        Assert.True(result.Ok, string.Join("; ", result.Failed));
        Assert.Equal(globalText, File.ReadAllText(Path.Combine(target, "global.json")));
        Assert.Equal(profileText, File.ReadAllText(Path.Combine(target, "profiles", "default.json")));
        Assert.False(File.ReadAllBytes(Path.Combine(target, "global.json")).AsSpan().StartsWith(Utf8Bom));
    }

    [Fact]
    public void WriteTo_produces_a_deterministic_archive()
    {
        var bundle = AppSettingsBundle.Capture(MakeLiveDir(), "1.2.3", Stamp);
        var a = ZipPath("a");
        var b = ZipPath("b");

        bundle.WriteTo(a, Stamp);
        bundle.WriteTo(b, Stamp);

        Assert.Equal(File.ReadAllBytes(a), File.ReadAllBytes(b));
    }

    [Fact]
    public void WriteTo_clamps_entry_timestamps_into_the_zip_range()
    {
        var bundle = AppSettingsBundle.Capture(MakeLiveDir(), "1.2.3", Stamp);

        foreach (var stamp in new[]
                 {
                     new DateTimeOffset(1900, 1, 1, 0, 0, 0, TimeSpan.Zero),
                     new DateTimeOffset(2200, 1, 1, 0, 0, 0, TimeSpan.Zero),
                 })
        {
            var path = ZipPath("clamp");
            bundle.WriteTo(path, stamp);

            using var archive = ZipFile.OpenRead(path);
            Assert.NotEmpty(archive.Entries);
            foreach (var entry in archive.Entries)
            {
                var offset = entry.LastWriteTime.Offset;
                Assert.InRange(entry.LastWriteTime,
                    new DateTimeOffset(1980, 1, 1, 0, 0, 0, offset),
                    new DateTimeOffset(2107, 12, 31, 23, 59, 59, offset));
            }
        }
    }

    [Fact]
    public void WriteTo_puts_global_json_and_profiles_at_the_archive_root()
    {
        var path = CaptureToArchive(MakeLiveDir(), out _);

        using var archive = ZipFile.OpenRead(path);
        var names = archive.Entries.Select(e => e.FullName).ToArray();
        Assert.Contains("global.json", names);
        Assert.Contains("profiles/mill.json", names);
        Assert.Contains("profiles/mill.probing.json", names);
        Assert.Contains("profiles/foam.json", names);
        Assert.DoesNotContain(names, n => n.Contains('\\', StringComparison.Ordinal));
        Assert.DoesNotContain(names, n => n.Contains(".corrupt", StringComparison.Ordinal));
        // Nothing is nested deeper than profiles/.
        Assert.DoesNotContain(names, n => n.StartsWith("profiles/", StringComparison.Ordinal) &&
                                          n["profiles/".Length..].Contains('/'));
    }

    [Fact]
    public void WriteTo_writes_a_manifest_with_kind_and_format_version()
    {
        var path = CaptureToArchive(MakeLiveDir(), out var bundle);

        using var archive = ZipFile.OpenRead(path);
        Assert.Equal(bundle.Files.Count + 1, archive.Entries.Count);

        using var document = JsonDocument.Parse(ReadEntryText(path, ".senderng-backup.json"));
        var root = document.RootElement;
        Assert.Equal(AppSettingsBundle.Kind, root.GetProperty("kind").GetString());
        Assert.Equal(AppSettingsBundle.CurrentFormatVersion, root.GetProperty("formatVersion").GetInt32());
        Assert.Equal("2026-10-13T12:00:00Z", root.GetProperty("createdUtc").GetString());
        Assert.Equal("1.2.3", root.GetProperty("appVersion").GetString());
        // fileCount excludes the manifest itself.
        Assert.Equal(bundle.Files.Count, root.GetProperty("fileCount").GetInt32());
        Assert.Equal(4, root.GetProperty("fileCount").GetInt32());
    }

    [Fact]
    public void ReadFrom_round_trips_a_written_archive()
    {
        var bundle = AppSettingsBundle.Capture(MakeLiveDir(), "1.2.3", Stamp);
        var path = ZipPath("roundtrip");
        bundle.WriteTo(path, Stamp);

        var result = AppSettingsBundle.ReadFrom(path);

        Assert.True(result.Ok, result.Error);
        Assert.Equal(bundle.Manifest, result.Bundle!.Manifest);
        Assert.Equal(bundle.Files.Count, result.Bundle.Files.Count);
        foreach (var (key, text) in bundle.Files)
            Assert.Equal(text, result.Bundle.Files[key]);
    }

    [Fact]
    public void ReadFrom_rejects_a_missing_file()
    {
        var result = AppSettingsBundle.ReadFrom(Path.Combine(TempDir(), "nope" + AppSettingsBundle.Extension));

        Assert.False(result.Ok);
        Assert.Null(result.Bundle);
        Assert.False(string.IsNullOrWhiteSpace(result.Error));
    }

    [Fact]
    public void ReadFrom_rejects_a_bad_zip()
    {
        var path = ZipPath("notazip");
        File.WriteAllText(path, "this is definitely not a zip archive");

        var result = AppSettingsBundle.ReadFrom(path);

        Assert.False(result.Ok);
        Assert.False(string.IsNullOrWhiteSpace(result.Error));
    }

    [Fact]
    public void ReadFrom_rejects_a_missing_manifest()
    {
        var path = WriteZip(ZipPath("nomanifest"),
            ("global.json", GlobalJsonText),
            ("profiles/mill.json", ProfileJson("mill")));

        var result = AppSettingsBundle.ReadFrom(path);

        Assert.False(result.Ok);
        Assert.Contains("manifest", result.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReadFrom_rejects_a_foreign_kind()
    {
        var path = WriteZip(ZipPath("foreignkind"),
            (".senderng-backup.json", ManifestJson(kind: "something-else")),
            ("global.json", GlobalJsonText));

        var result = AppSettingsBundle.ReadFrom(path);

        Assert.False(result.Ok);
        Assert.Contains("something-else", result.Error!, StringComparison.Ordinal);
    }

    [Fact]
    public void ReadFrom_rejects_a_newer_format_version()
    {
        var path = WriteZip(ZipPath("newerformat"),
            (".senderng-backup.json", ManifestJson(formatVersion: AppSettingsBundle.CurrentFormatVersion + 1)),
            ("global.json", GlobalJsonText));

        var result = AppSettingsBundle.ReadFrom(path);

        Assert.False(result.Ok);
        Assert.Contains("format version", result.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReadFrom_rejects_a_malformed_manifest()
    {
        var path = WriteZip(ZipPath("badmanifest"),
            (".senderng-backup.json", "{ not json"),
            ("global.json", GlobalJsonText));

        var result = AppSettingsBundle.ReadFrom(path);

        Assert.False(result.Ok);
    }

    [Theory]
    [InlineData("../evil.json")]
    [InlineData("/abs.json")]
    [InlineData("profiles/../global.json")]
    public void ReadFrom_rejects_a_traversal_entry_name(string evil)
    {
        var path = WriteZip(ZipPath("traversal"),
            (".senderng-backup.json", ManifestJson()),
            ("global.json", GlobalJsonText),
            (evil, "{}"));

        var result = AppSettingsBundle.ReadFrom(path);

        Assert.False(result.Ok);
        Assert.Contains(evil, result.Error!, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("locale/en.json")]
    [InlineData("profiles/nested/deep.json")]
    public void ReadFrom_rejects_an_entry_outside_the_known_layout(string outside)
    {
        var path = WriteZip(ZipPath("outside"),
            (".senderng-backup.json", ManifestJson()),
            ("global.json", GlobalJsonText),
            (outside, "{}"));

        var result = AppSettingsBundle.ReadFrom(path);

        Assert.False(result.Ok);
        Assert.Contains(outside, result.Error!, StringComparison.Ordinal);
    }

    [Fact]
    public void ReadFrom_rejects_a_too_large_entry()
    {
        var path = WriteZip(ZipPath("toobig"),
            (".senderng-backup.json", ManifestJson()),
            ("global.json", GlobalJsonText),
            ("profiles/big.json", new string('a', 4 * 1024 * 1024 + 1)));

        var result = AppSettingsBundle.ReadFrom(path);

        Assert.False(result.Ok);
        Assert.False(string.IsNullOrWhiteSpace(result.Error));
    }

    [Fact]
    public void ReadFrom_rejects_entry_names_that_collide_case_insensitively()
    {
        // profile ids are lowercase slugs, so no legitimate archive holds both; on
        // Windows the second file would silently overwrite the first while the
        // restore still reported two files written.
        var path = WriteZip(ZipPath("casecollision"),
            (".senderng-backup.json", ManifestJson()),
            ("global.json", GlobalJsonText),
            ("profiles/Mill.json", ProfileJson("Mill")),
            ("profiles/mill.json", ProfileJson("mill")));

        var result = AppSettingsBundle.ReadFrom(path);

        Assert.False(result.Ok);
        Assert.Contains("profiles/mill.json", result.Error!, StringComparison.Ordinal);
        Assert.Contains("more than once", result.Error!, StringComparison.Ordinal);
    }

    [Fact]
    public void TryReadBounded_refuses_a_stream_that_exceeds_the_limit()
    {
        // The cap enforced while copying, independent of any zip entry's declared
        // length: the stream holds one byte more than the limit.
        const long limit = 64;

        using var source = new MemoryStream(Encoding.UTF8.GetBytes(new string('a', (int)limit + 1)));

        Assert.False(AppSettingsBundle.TryReadBounded(source, limit, out var text, out var bytes, out var reason));
        Assert.Equal(string.Empty, text);
        Assert.Equal(0, bytes);
        Assert.Contains("larger than", reason!, StringComparison.Ordinal);
    }

    [Fact]
    public void TryReadBounded_reads_a_non_ASCII_stream_up_to_the_limit()
    {
        var expected = new string('ы', 40); // two bytes per character in UTF-8
        var limit = Encoding.UTF8.GetByteCount(expected);

        using var source = new MemoryStream(Encoding.UTF8.GetBytes(expected));

        Assert.True(AppSettingsBundle.TryReadBounded(source, limit, out var text, out var bytes, out var reason));
        Assert.Null(reason);
        Assert.Equal(expected, text);
        Assert.Equal(limit, bytes);
    }

    [Fact]
    public void ReadFrom_rejects_too_many_entries()
    {
        var entries = new List<(string, string)> { (".senderng-backup.json", ManifestJson()), ("global.json", GlobalJsonText) };
        for (var i = 0; i < 300; i++)
            entries.Add(($"profiles/p{i}.json", ProfileJson("p" + i)));
        var path = WriteZip(ZipPath("toomany"), entries.ToArray());

        var result = AppSettingsBundle.ReadFrom(path);

        Assert.False(result.Ok);
        Assert.False(string.IsNullOrWhiteSpace(result.Error));
    }

    [Fact]
    public void Validate_accepts_a_well_formed_bundle()
    {
        var validation = AppSettingsBundleValidator.Validate(BundleOf(
            ("global.json", GlobalJsonText),
            ("profiles/mill.json", ProfileJson("mill"))));

        Assert.True(validation.Ok);
        Assert.Empty(validation.Errors);
        Assert.Empty(validation.Warnings);
    }

    [Fact]
    public void Validate_treats_a_module_companion_as_opaque_and_not_as_a_profile()
    {
        var validation = AppSettingsBundleValidator.Validate(BundleOf(
            ("global.json", GlobalJsonText),
            ("profiles/mill.json", ProfileJson("mill")),
            ("profiles/mill.probing.json", ProbingCompanionJson)));

        Assert.True(validation.Ok, string.Join("; ", validation.Errors));
    }

    [Fact]
    public void Validate_reports_a_missing_global_json()
    {
        var validation = AppSettingsBundleValidator.Validate(BundleOf(
            ("profiles/mill.json", ProfileJson("mill"))));

        Assert.False(validation.Ok);
        Assert.Contains(validation.Errors, e => e.Contains("global.json", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_reports_a_broken_global_json()
    {
        var validation = AppSettingsBundleValidator.Validate(BundleOf(
            ("global.json", "{ broken"),
            ("profiles/mill.json", ProfileJson("mill"))));

        Assert.False(validation.Ok);
        Assert.Contains(validation.Errors, e => e.Contains("global.json", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_reports_a_broken_profile()
    {
        var validation = AppSettingsBundleValidator.Validate(BundleOf(
            ("global.json", GlobalJsonText),
            ("profiles/mill.json", "{ broken")));

        Assert.False(validation.Ok);
        Assert.Contains(validation.Errors, e => e.Contains("profiles/mill.json", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_rejects_a_global_json_that_only_binds_case_insensitively()
    {
        // The loaders (GlobalSettingsStore.Load) bind case-sensitively, so this file
        // loads as defaults after a restore: the registry and the active-profile
        // pointer silently vanish. Validation must refuse it up front.
        var camelCase =
            "{\n  \"activeProfileId\": \"mill\",\n  \"profiles\": { \"mill\": \"Mill\" },\n  \"language\": \"ru\"\n}\n";

        var validation = AppSettingsBundleValidator.Validate(BundleOf(
            ("global.json", camelCase),
            ("profiles/mill.json", ProfileJson("mill"))));

        Assert.False(validation.Ok);
        Assert.Contains(validation.Errors, e => e.Contains("global.json", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_rejects_a_profile_whose_payload_id_is_missing()
    {
        // No Id: the loaded profile keeps Id "default" while the file is mill.json,
        // so the next Save writes profiles/default.json and orphans the restored file.
        var validation = AppSettingsBundleValidator.Validate(BundleOf(
            ("global.json", GlobalJsonText),
            ("profiles/mill.json", "{\n  \"Name\": \"Mill\"\n}\n")));

        Assert.False(validation.Ok);
        Assert.Contains(validation.Errors, e => e.Contains("profiles/mill.json", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_rejects_a_profile_whose_payload_id_disagrees_with_its_file_name()
    {
        var validation = AppSettingsBundleValidator.Validate(BundleOf(
            ("global.json", GlobalJsonText),
            ("profiles/mill.json", ProfileJson("other"))));

        Assert.False(validation.Ok);
        Assert.Contains(validation.Errors, e => e.Contains("profiles/mill.json", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_warns_when_the_active_profile_is_absent()
    {
        var global = GlobalJsonText.Replace("\"mill\"", "\"ghost\"", StringComparison.Ordinal);
        var validation = AppSettingsBundleValidator.Validate(BundleOf(
            ("global.json", global),
            ("profiles/mill.json", ProfileJson("mill")),
            ("profiles/foam.json", ProfileJson("foam"))));

        Assert.True(validation.Ok);
        Assert.Empty(validation.Errors);
        Assert.Contains(validation.Warnings, w => w.Contains("ghost", StringComparison.Ordinal));
    }
}
