/*
 * ReleaseEngineTests.cs - end-to-end release pipeline tests
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

namespace ReleaseManager.Tests;

public class ReleaseEngineTests
{
    [Fact]
    public async Task Core_only_release_stages_bundle_and_checksums()
    {
        var (repo, output, cli) = Setup();
        var platform = PlatformInfo.Current;
        var folder = PlatformInfo.FolderName(platform);
        var appPublish = TempDir.New();
        Directory.CreateDirectory(Path.Combine(appPublish, "Modules"));
        Directory.CreateDirectory(Path.Combine(appPublish, "Locale"));
        File.WriteAllText(Path.Combine(appPublish, PlatformInfo.RunnerFileName(platform)), "exe");
        File.WriteAllText(Path.Combine(appPublish, "SenderNG.dll"), "dll");
        File.WriteAllText(Path.Combine(appPublish, "SenderNG.deps.json"), "{}");
        File.WriteAllText(Path.Combine(appPublish, "SenderNG.runtimeconfig.json"), "{}");
        File.WriteAllText(Path.Combine(appPublish, "SenderNG.Core.dll"), "core");
        File.WriteAllText(Path.Combine(appPublish, "SenderNG.pdb"), "pdb");
        File.WriteAllText(Path.Combine(appPublish, "Modules", "SenderNG.Modules.Common.dll"), "sdk");
        File.WriteAllText(Path.Combine(appPublish, "Modules", "SenderNG.Modules.Common.Render.dll"), "render");
        File.WriteAllText(Path.Combine(appPublish, "Modules", "Silk.NET.Core.dll"), "silk");
        File.WriteAllText(Path.Combine(appPublish, "Modules", "Silk.NET.Maths.dll"), "silk");
        File.WriteAllText(Path.Combine(appPublish, "Modules", "Silk.NET.OpenGL.dll"), "silk");
        File.WriteAllText(Path.Combine(appPublish, "Modules", "SenderNG.Modules.Excellon.dll"), "mod");
        Directory.CreateDirectory(Path.Combine(appPublish, "Modules", "excellon", "Locale"));
        File.WriteAllText(Path.Combine(appPublish, "Modules", "excellon", "Locale", "en.json"), "{}");
        File.WriteAllText(Path.Combine(appPublish, NativeLib(platform)), "n");
        cli.AppPublishDir = appPublish;

        var engine = new ReleaseEngine(repo, cli, new SilentLog());
        var report = await engine.ReleaseAsync(new ReleaseOptions { OutputDir = output, Stable = true });

        var bundle = Path.Combine(output, folder, "SenderNG");
        Assert.True(File.Exists(Path.Combine(bundle, PlatformInfo.RunnerFileName(platform))));
        Assert.False(File.Exists(Path.Combine(bundle, "SenderNG.pdb")));
        Assert.False(File.Exists(Path.Combine(bundle, "Modules", "SenderNG.Modules.Excellon.dll")));
        Assert.True(File.Exists(Path.Combine(bundle, "LICENSE")));
        Assert.False(Directory.Exists(Path.Combine(bundle, "Modules", "excellon")));
        Assert.True(File.Exists(Path.Combine(output, folder, $"SenderNG-0.1.0-{PlatformInfo.ZipSuffix(platform)}.zip")));
        Assert.True(File.Exists(Path.Combine(output, folder, "SHA256SUMS.txt")));
        Assert.Single(report.Artifacts);
    }

    [Fact]
    public async Task Full_release_stages_one_party_plugins()
    {
        var (repo, output, cli) = Setup();
        var appPublish = CompleteAppPublish();
        cli.AppPublishDir = appPublish;

        var engine = new ReleaseEngine(repo, cli, new SilentLog());
        var options = new ReleaseOptions { OutputDir = output, Stable = true, Selection = PluginSelection.All };
        var report = await engine.ReleaseAsync(options);

        var platform = PlatformInfo.Current;
        var pluginsRoot = Path.Combine(output, PlatformInfo.FolderName(platform), "plugins");
        Assert.True(File.Exists(Path.Combine(pluginsRoot, "excellon", "SenderNG.Modules.Excellon.dll")));
        Assert.True(File.Exists(Path.Combine(pluginsRoot, "excellon", "excellon", "Locale", "en.json")));
        Assert.True(File.Exists(Path.Combine(pluginsRoot, "excellon", "README.txt")));
        Assert.True(File.Exists(Path.Combine(output, PlatformInfo.FolderName(platform), $"SenderNG-plugin-excellon-0.1.0-{PlatformInfo.ZipSuffix(platform)}.zip")));
        Assert.Contains("excellon", report.PluginIds);
    }

    [Theory]
    [InlineData(false, "SenderNG-0.1.0-20261012-win64.zip", "SenderNG-plugin-excellon-0.1.0-20261012-win64.zip")]
    [InlineData(true, "SenderNG-0.1.0-win64.zip", "SenderNG-plugin-excellon-0.1.0-win64.zip")]
    public async Task Zip_names_apply_date_only_for_weekly(bool stable, string coreZip, string pluginZip)
    {
        var (repo, output, cli) = Setup();
        cli.AppPublishDir = CompleteAppPublish(ReleasePlatform.Win64);

        var engine = new ReleaseEngine(repo, cli, new SilentLog());
        var options = new ReleaseOptions
        {
            OutputDir = output,
            Platform = ReleasePlatform.Win64,
            VersionOverride = "0.1.0",
            Stable = stable,
            DateOverride = "20261012",
            Selection = PluginSelection.All,
        };
        await engine.ReleaseAsync(options);

        var platformRoot = Path.Combine(output, PlatformInfo.FolderName(ReleasePlatform.Win64));
        Assert.True(File.Exists(Path.Combine(platformRoot, coreZip)));
        Assert.True(File.Exists(Path.Combine(platformRoot, pluginZip)));
    }

    [Fact]
    public async Task Skips_tests_when_asked()
    {
        var (repo, output, cli) = Setup();
        cli.AppPublishDir = CompleteAppPublish();

        var engine = new ReleaseEngine(repo, cli, new SilentLog());
        await engine.ReleaseAsync(new ReleaseOptions { OutputDir = output, SkipTests = true });
        Assert.Equal(0, cli.TestCalls);
    }

    [Fact]
    public async Task Wipes_previous_platform_folder_before_staging()
    {
        var (repo, output, cli) = Setup();
        cli.AppPublishDir = CompleteAppPublish();
        var stale = Path.Combine(output, PlatformInfo.FolderName(PlatformInfo.Current), "plugins", "stale");
        Directory.CreateDirectory(stale);
        File.WriteAllText(Path.Combine(stale, "leftover.txt"), "x");

        var engine = new ReleaseEngine(repo, cli, new SilentLog());
        await engine.ReleaseAsync(new ReleaseOptions { OutputDir = output, Selection = PluginSelection.All });

        Assert.False(Directory.Exists(stale));
    }

    [Fact]
    public async Task Release_forwards_the_caller_token_to_ICli()
    {
        var (repo, output, cli) = Setup();
        cli.AppPublishDir = CompleteAppPublish();

        using var cts = new CancellationTokenSource();
        var engine = new ReleaseEngine(repo, cli, new SilentLog());
        await engine.ReleaseAsync(new ReleaseOptions { OutputDir = output }, cts.Token);

        Assert.Equal(cts.Token, cli.ReceivedToken);
    }

    [Fact]
    public async Task Cancelled_token_aborts_the_release_before_any_work()
    {
        var (repo, output, cli) = Setup();
        cli.AppPublishDir = CompleteAppPublish();
        var platformRoot = Path.Combine(output, PlatformInfo.FolderName(PlatformInfo.Current));

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var engine = new ReleaseEngine(repo, cli, new SilentLog());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            engine.ReleaseAsync(new ReleaseOptions { OutputDir = output }, cts.Token));

        Assert.False(Directory.Exists(platformRoot));
    }

    [Fact]
    public async Task Platform_override_stages_into_the_overridden_folder()
    {
        var (repo, output, cli) = Setup();
        cli.AppPublishDir = CompleteAppPublish(ReleasePlatform.LinuxArm64);

        var engine = new ReleaseEngine(repo, cli, new SilentLog());
        var report = await engine.ReleaseAsync(new ReleaseOptions
        {
            OutputDir = output,
            Stable = true,
            Platform = ReleasePlatform.LinuxArm64,
        });

        // Asserted against the mapping, not a literal, so this still holds if a
        // future platform is added. The host folder must not exist: proving the
        // override moved the target is the whole point of the test.
        var armFolder = PlatformInfo.FolderName(ReleasePlatform.LinuxArm64);
        var hostFolder = PlatformInfo.FolderName(PlatformInfo.Current);
        Assert.NotEqual(hostFolder, armFolder);
        Assert.False(Directory.Exists(Path.Combine(output, hostFolder)));
        Assert.True(Directory.Exists(Path.Combine(output, armFolder, "SenderNG")));
        Assert.True(File.Exists(Path.Combine(
            output, armFolder, "SenderNG", "SenderNG")));
        // linux-arm64 is a separate enum member, so it needs its own proof that
        // the desktop entry is staged - the linux fact alone would not catch a
        // `platform == ReleasePlatform.Linux` regression.
        Assert.True(File.Exists(Path.Combine(
            output, armFolder, "SenderNG", "install-desktop.sh")));
        Assert.True(File.Exists(Path.Combine(
            output, armFolder, "SenderNG", "appIcon.png")));
        Assert.True(File.Exists(Path.Combine(
            output, armFolder, $"SenderNG-0.1.0-{PlatformInfo.ZipSuffix(ReleasePlatform.LinuxArm64)}.zip")));
        Assert.Equal(armFolder, report.Platform);
    }

    [Fact]
    public async Task Linux_bundle_ships_the_desktop_entry_installer()
    {
        var (repo, output, cli) = Setup();
        cli.AppPublishDir = CompleteAppPublish(ReleasePlatform.Linux);

        var engine = new ReleaseEngine(repo, cli, new SilentLog());
        await engine.ReleaseAsync(new ReleaseOptions
        {
            OutputDir = output,
            Platform = ReleasePlatform.Linux,
        });

        var bundle = Path.Combine(output, PlatformInfo.FolderName(ReleasePlatform.Linux), "SenderNG");
        Assert.True(File.Exists(Path.Combine(bundle, "install-desktop.sh")));
        Assert.True(File.Exists(Path.Combine(bundle, "appIcon.png")));
    }

    [Fact]
    public async Task Win64_bundle_does_not_ship_the_desktop_entry_installer()
    {
        var (repo, output, cli) = Setup();
        cli.AppPublishDir = CompleteAppPublish(ReleasePlatform.Win64);

        var engine = new ReleaseEngine(repo, cli, new SilentLog());
        await engine.ReleaseAsync(new ReleaseOptions
        {
            OutputDir = output,
            Platform = ReleasePlatform.Win64,
        });

        var bundle = Path.Combine(output, PlatformInfo.FolderName(ReleasePlatform.Win64), "SenderNG");
        Assert.False(File.Exists(Path.Combine(bundle, "install-desktop.sh")));
        Assert.False(File.Exists(Path.Combine(bundle, "appIcon.png")));
    }

    [Fact]
    public async Task Linux_bundle_without_the_desktop_entry_installer_fails_loudly()
    {
        var (repo, output, cli) = Setup();
        cli.AppPublishDir = CompleteAppPublish(ReleasePlatform.Linux);
        // Setup() seeds the desktop-entry sources so the scratch repo resembles
        // the real one. Remove them again: a linux release must fail rather than
        // ship a bundle that promises a desktop entry it cannot install.
        File.Delete(Path.Combine(repo, "tools", "install-desktop.sh"));
        File.Delete(Path.Combine(repo, "src", "SenderNG", "Assets", "appIcon.png"));

        var engine = new ReleaseEngine(repo, cli, new SilentLog());
        await Assert.ThrowsAsync<StagingException>(() =>
            engine.ReleaseAsync(new ReleaseOptions { OutputDir = output, Platform = ReleasePlatform.Linux }));
    }

    [Fact]
    public async Task Release_without_the_repo_license_fails_loudly()
    {
        var (repo, output, cli) = Setup();
        cli.AppPublishDir = CompleteAppPublish();
        // The license is a release contract, not a nicety: staging must fail
        // rather than produce a bundle that omits it.
        File.Delete(Path.Combine(repo, "LICENSE"));

        var engine = new ReleaseEngine(repo, cli, new SilentLog());
        await Assert.ThrowsAsync<StagingException>(() =>
            engine.ReleaseAsync(new ReleaseOptions { OutputDir = output }));

        var platformRoot = Path.Combine(output, PlatformInfo.FolderName(PlatformInfo.Current));
        Assert.Empty(Directory.GetFiles(platformRoot, "*.zip"));
    }

    // The engine resolves these two files from the repo root, and the tests run
    // against a scratch repo, so Setup() seeds them for every test. The only
    // test that wants them absent deletes them again.
    private static void StageLinuxDesktopSources(string repo)
    {
        Directory.CreateDirectory(Path.Combine(repo, "tools"));
        File.WriteAllText(Path.Combine(repo, "tools", "install-desktop.sh"), "#!/bin/sh\n");
        Directory.CreateDirectory(Path.Combine(repo, "src", "SenderNG", "Assets"));
        File.WriteAllText(Path.Combine(repo, "src", "SenderNG", "Assets", "appIcon.png"), "png");
    }

    private static (string repo, string output, FakeCli cli) Setup()
    {
        var repo = TempRepo.New();
        var output = TempDir.New();
        var cli = new FakeCli { PluginVersions = { ["SenderNG.Modules.Excellon"] = "0.1.0" } };

        foreach (var id in new[] { "Excellon", "Hpgl", "Plotter" })
        {
            var projectDir = Path.Combine(repo, "src", $"SenderNG.Modules.{id}");
            Directory.CreateDirectory(projectDir);
            File.WriteAllText(Path.Combine(projectDir, $"SenderNG.Modules.{id}.csproj"), "<Project/>");
            Directory.CreateDirectory(Path.Combine(projectDir, "Locale", FirstPartyPlugins.First(id).Id));
        }

        File.WriteAllText(Path.Combine(repo, "src", "SenderNG.Modules.Excellon", "Locale", "excellon", "en.json"), "{}");

        // Seed here rather than per-test: a test that leaves options.Platform
        // unset releases for PlatformInfo.Current, so which platform it actually
        // exercises depends on the build host. On a Windows dev box that is
        // Win64 and the linux staging never runs; on the Linux CI runner it is
        // Linux and the scratch repo must already look like the real one.
        StageLinuxDesktopSources(repo);
        // The engine stages LICENSE from the repo root on the shared path, so
        // the scratch repo must resemble the real one for every test.
        File.WriteAllText(Path.Combine(repo, "LICENSE"), "BSD 3-Clause\n");

        return (repo, output, cli);
    }

    private static string CompleteAppPublish()
        => CompleteAppPublish(PlatformInfo.Current);

    private static string CompleteAppPublish(ReleasePlatform platform)
    {
        var publish = TempDir.New();
        Directory.CreateDirectory(Path.Combine(publish, "Modules"));
        Directory.CreateDirectory(Path.Combine(publish, "Modules", "excellon", "Locale"));
        File.WriteAllText(Path.Combine(publish, PlatformInfo.RunnerFileName(platform)), "exe");
        File.WriteAllText(Path.Combine(publish, "SenderNG.dll"), "dll");
        File.WriteAllText(Path.Combine(publish, "SenderNG.deps.json"), "{}");
        File.WriteAllText(Path.Combine(publish, "SenderNG.runtimeconfig.json"), "{}");
        File.WriteAllText(Path.Combine(publish, "SenderNG.Core.dll"), "core");
        File.WriteAllText(Path.Combine(publish, "Modules", "SenderNG.Modules.Common.dll"), "sdk");
        File.WriteAllText(Path.Combine(publish, "Modules", "SenderNG.Modules.Common.Render.dll"), "render");
        File.WriteAllText(Path.Combine(publish, "Modules", "Silk.NET.Core.dll"), "silk");
        File.WriteAllText(Path.Combine(publish, "Modules", "Silk.NET.Maths.dll"), "silk");
        File.WriteAllText(Path.Combine(publish, "Modules", "Silk.NET.OpenGL.dll"), "silk");
        File.WriteAllText(Path.Combine(publish, "Modules", "excellon", "Locale", "en.json"), "{}");
        File.WriteAllText(Path.Combine(publish, NativeLib(platform)), "n");
        return publish;
    }

    // A switch, not a ternary, for the same reason as PlatformInfo: `== Win64 ?
    // a : b` would keep working by accident for a new platform and never be
    // revisited. libcoreclr.so is the runtime's name on every Linux RID, so both
    // Linux entries are correct, not a copy-paste.
    private static string NativeLib(ReleasePlatform platform) => platform switch
    {
        ReleasePlatform.Win64 => "coreclr.dll",
        ReleasePlatform.Linux => "libcoreclr.so",
        ReleasePlatform.LinuxArm64 => "libcoreclr.so",
        _ => throw new ArgumentOutOfRangeException(nameof(platform), platform, null)
    };
}
