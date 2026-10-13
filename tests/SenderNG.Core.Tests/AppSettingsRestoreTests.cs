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

using CNC.Core.Services;

namespace SenderNG.Core.Tests;

public class AppSettingsRestoreTests
{
    private const string GlobalJsonText = "{\n  \"ActiveProfileId\": \"mill\",\n  \"Profiles\": { \"mill\": \"Mill\" }\n}\n";

    private static string TempDir() => Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

    private static string ProfileJson(string id) => "{\n  \"Id\": \"" + id + "\",\n  \"Name\": \"" + id + "\"\n}\n";

    private static AppSettingsBundle BundleOf(params (string Name, string Text)[] files) => new()
    {
        Manifest = new AppSettingsBundleManifest(
            AppSettingsBundle.Kind, AppSettingsBundle.CurrentFormatVersion, "2026-10-13T12:00:00Z", "1.2.3", files.Length),
        Files = files.ToDictionary(f => f.Name, f => f.Text, StringComparer.Ordinal),
    };

    private static AppSettingsBundle MakeBundle() => BundleOf(
        ("global.json", GlobalJsonText),
        ("profiles/mill.json", ProfileJson("mill")),
        ("profiles/mill.probing.json", "{\n  \"Enabled\": true\n}\n"));

    private static string LocalPath(string dir, string key) =>
        Path.Combine(dir, key.Replace('/', Path.DirectorySeparatorChar));

    private static void AssertNoTempFiles(string dir) =>
        Assert.Empty(Directory.EnumerateFiles(dir, "*.tmp", SearchOption.AllDirectories));

    [Fact]
    public void Apply_overwrites_global_json_and_profiles()
    {
        var dir = TempDir();
        Directory.CreateDirectory(Path.Combine(dir, "profiles"));
        File.WriteAllText(LocalPath(dir, "global.json"), "stale");
        File.WriteAllText(LocalPath(dir, "profiles/mill.json"), "stale");
        var bundle = MakeBundle();

        var result = AppSettingsRestore.Apply(dir, bundle);

        Assert.True(result.Ok, string.Join("; ", result.Failed));
        foreach (var (key, text) in bundle.Files)
            Assert.Equal(text, File.ReadAllText(LocalPath(dir, key)));
    }

    [Fact]
    public void Apply_creates_the_profiles_directory_when_absent()
    {
        var dir = TempDir();
        Directory.CreateDirectory(dir);
        Assert.False(Directory.Exists(Path.Combine(dir, "profiles")));

        var result = AppSettingsRestore.Apply(dir, MakeBundle());

        Assert.True(result.Ok, string.Join("; ", result.Failed));
        Assert.True(Directory.Exists(Path.Combine(dir, "profiles")));
        Assert.Equal(ProfileJson("mill"), File.ReadAllText(Path.Combine(dir, "profiles", "mill.json")));
    }

    [Fact]
    public void Apply_leaves_bystander_files_alone()
    {
        var dir = TempDir();
        Directory.CreateDirectory(Path.Combine(dir, "profiles"));
        var bystander = Path.Combine(dir, "profiles", "extra.json");
        File.WriteAllText(bystander, "bystander");

        var result = AppSettingsRestore.Apply(dir, MakeBundle());

        Assert.True(result.Ok, string.Join("; ", result.Failed));
        Assert.True(File.Exists(bystander));
        Assert.Equal("bystander", File.ReadAllText(bystander));
    }

    [Fact]
    public void Apply_leaves_a_bystander_file_that_is_not_in_the_archive()
    {
        var dir = TempDir();
        Directory.CreateDirectory(Path.Combine(dir, "profiles"));
        var liveOnlyCompanion = Path.Combine(dir, "profiles", "mill.probing.json");
        File.WriteAllText(liveOnlyCompanion, "live-only");
        // The archive carries the profile but not its probing companion.
        var bundle = BundleOf(
            ("global.json", GlobalJsonText),
            ("profiles/mill.json", ProfileJson("mill")));

        var result = AppSettingsRestore.Apply(dir, bundle);

        Assert.True(result.Ok, string.Join("; ", result.Failed));
        Assert.True(File.Exists(liveOnlyCompanion));
        Assert.Equal("live-only", File.ReadAllText(liveOnlyCompanion));
    }

    [Fact]
    public void Apply_reports_written_count_and_no_failures_on_success()
    {
        var dir = TempDir();
        var bundle = MakeBundle();

        var result = AppSettingsRestore.Apply(dir, bundle);

        Assert.True(result.Ok);
        Assert.Empty(result.Failed);
        Assert.Equal(bundle.Files.Count, result.Written);
    }

    [Fact]
    public void Apply_leaves_no_tmp_files_behind()
    {
        var dir = TempDir();

        var result = AppSettingsRestore.Apply(dir, MakeBundle());

        Assert.True(result.Ok, string.Join("; ", result.Failed));
        AssertNoTempFiles(dir);
        Assert.DoesNotContain(Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories),
            path => path.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Apply_does_not_write_a_partial_file_when_the_source_is_unwritable()
    {
        var dir = TempDir();
        Directory.CreateDirectory(Path.Combine(dir, "profiles"));
        var target = LocalPath(dir, "global.json");
        File.WriteAllText(target, "original");
        var bundle = MakeBundle();

        AppSettingsRestoreResult result;
        using (new FileStream(target, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            result = AppSettingsRestore.Apply(dir, bundle);
        }

        Assert.False(result.Ok);
        Assert.Contains("global.json", result.Failed);
        Assert.Equal("original", File.ReadAllText(target));
        Assert.Equal(bundle.Files.Count - 1, result.Written);
        AssertNoTempFiles(dir);
    }

    [Fact]
    public void Apply_can_restore_over_a_directory_that_is_missing_entirely()
    {
        var dir = Path.Combine(TempDir(), "nested", "settings");
        Assert.False(Directory.Exists(dir));

        var result = AppSettingsRestore.Apply(dir, MakeBundle());

        Assert.True(result.Ok, string.Join("; ", result.Failed));
        Assert.True(File.Exists(LocalPath(dir, "global.json")));
        Assert.True(File.Exists(LocalPath(dir, "profiles/mill.json")));
        Assert.Equal(GlobalJsonText, File.ReadAllText(LocalPath(dir, "global.json")));
    }
}
