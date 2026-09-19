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
        File.WriteAllText(Path.Combine(appPublish, "Modules", "SenderNG.Modules.Dummy.dll"), "mod");
        Directory.CreateDirectory(Path.Combine(appPublish, "Modules", "dummy", "Locale"));
        File.WriteAllText(Path.Combine(appPublish, "Modules", "dummy", "Locale", "en.json"), "{}");
        File.WriteAllText(Path.Combine(appPublish, NativeLib(platform)), "n");
        cli.AppPublishDir = appPublish;

        var engine = new ReleaseEngine(repo, cli, new SilentLog());
        var report = await engine.ReleaseAsync(new ReleaseOptions { OutputDir = output });

        var bundle = Path.Combine(output, folder, "SenderNG");
        Assert.True(File.Exists(Path.Combine(bundle, PlatformInfo.RunnerFileName(platform))));
        Assert.False(File.Exists(Path.Combine(bundle, "SenderNG.pdb")));
        Assert.False(File.Exists(Path.Combine(bundle, "Modules", "SenderNG.Modules.Dummy.dll")));
        Assert.False(Directory.Exists(Path.Combine(bundle, "Modules", "dummy")));
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
        var options = new ReleaseOptions { OutputDir = output, Selection = PluginSelection.All };
        var report = await engine.ReleaseAsync(options);

        var platform = PlatformInfo.Current;
        var pluginsRoot = Path.Combine(output, PlatformInfo.FolderName(platform), "plugins");
        Assert.True(File.Exists(Path.Combine(pluginsRoot, "dummy", "SenderNG.Modules.Dummy.dll")));
        Assert.True(File.Exists(Path.Combine(pluginsRoot, "dummy", "dummy", "Locale", "en.json")));
        Assert.True(File.Exists(Path.Combine(pluginsRoot, "dummy", "README.txt")));
        Assert.True(File.Exists(Path.Combine(output, PlatformInfo.FolderName(platform), $"SenderNG-plugin-dummy-0.1.0-{PlatformInfo.ZipSuffix(platform)}.zip")));
        Assert.Contains("dummy", report.PluginIds);
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

    private static (string repo, string output, FakeCli cli) Setup()
    {
        var repo = TempRepo.New();
        var output = TempDir.New();
        var cli = new FakeCli { PluginVersions = { ["SenderNG.Modules.Dummy"] = "0.1.0" } };

        foreach (var id in new[] { "Dummy", "Excellon", "Hpgl", "Plotter", "Plotter.Nex" })
        {
            var projectDir = Path.Combine(repo, "src", $"SenderNG.Modules.{id}");
            Directory.CreateDirectory(projectDir);
            File.WriteAllText(Path.Combine(projectDir, $"SenderNG.Modules.{id}.csproj"), "<Project/>");
            Directory.CreateDirectory(Path.Combine(projectDir, "Locale", FirstPartyPlugins.First(id).Id));
        }

        Directory.CreateDirectory(Path.Combine(repo, "src", "SenderNG.Modules.Dummy", "Locale", "dummy"));
        File.WriteAllText(Path.Combine(repo, "src", "SenderNG.Modules.Dummy", "Locale", "dummy", "en.json"), "{}");

        return (repo, output, cli);
    }

    private static string CompleteAppPublish()
    {
        var platform = PlatformInfo.Current;
        var publish = TempDir.New();
        Directory.CreateDirectory(Path.Combine(publish, "Modules"));
        Directory.CreateDirectory(Path.Combine(publish, "Modules", "dummy", "Locale"));
        File.WriteAllText(Path.Combine(publish, PlatformInfo.RunnerFileName(platform)), "exe");
        File.WriteAllText(Path.Combine(publish, "SenderNG.dll"), "dll");
        File.WriteAllText(Path.Combine(publish, "SenderNG.deps.json"), "{}");
        File.WriteAllText(Path.Combine(publish, "SenderNG.runtimeconfig.json"), "{}");
        File.WriteAllText(Path.Combine(publish, "SenderNG.Core.dll"), "core");
        File.WriteAllText(Path.Combine(publish, "Modules", "SenderNG.Modules.Common.dll"), "sdk");
        File.WriteAllText(Path.Combine(publish, "Modules", "dummy", "Locale", "en.json"), "{}");
        File.WriteAllText(Path.Combine(publish, NativeLib(platform)), "n");
        return publish;
    }

    private static string NativeLib(ReleasePlatform platform)
        => platform == ReleasePlatform.Win64 ? "coreclr.dll" : "libcoreclr.so";
}
