/*
 * ReleaseEngine.cs - release pipeline orchestration
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

namespace ReleaseManager;

public sealed record EngineReport(
    string Platform,
    string AppVersion,
    IReadOnlyList<string> PluginIds,
    IReadOnlyList<string> Artifacts,
    int FileCount);

public sealed class ReleaseEngine
{
    private const string AppCsproj = "src/SenderNG/SenderNG.csproj";
    private const string LinuxDesktopScript = "tools/install-desktop.sh";
    private const string AppIconPng = "src/SenderNG/Assets/appIcon.png";
    private const string LicenseFile = "LICENSE";

    private readonly string _repoRoot;
    private readonly ICli _cli;
    private readonly ILog _log;

    public ReleaseEngine(string repoRoot, ICli cli, ILog log)
    {
        _repoRoot = repoRoot;
        _cli = cli;
        _log = log;
    }

    public async Task<EngineReport> ReleaseAsync(ReleaseOptions options, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var platform = options.Platform ?? PlatformInfo.Current;
        var folder = PlatformInfo.FolderName(platform);
        var platformRoot = Path.Combine(options.OutputDir, folder);
        _log.Info($"ReleaseManager: platform {folder} ({PlatformInfo.Rid(platform)})");

        if (!options.SkipTests)
        {
            _log.Info("Running tests (--skip-tests to skip)...");
            await _cli.TestAsync(_repoRoot, ct);
        }

        ct.ThrowIfCancellationRequested();

        var plugins = PluginLists.Resolve(_repoRoot, options, _log, platform);
        var appVersion = options.VersionOverride
            ?? await _cli.ReadVersionAsync(_repoRoot, Path.Combine(_repoRoot, AppCsproj), ct)
            ?? throw new CliException($"No <Version> in {AppCsproj}; add one or pass --version.");

        var tmp = Path.Combine(Path.GetTempPath(), "ReleaseManager-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(tmp);
            ct.ThrowIfCancellationRequested();
            if (Directory.Exists(platformRoot))
                Directory.Delete(platformRoot, recursive: true);
            Directory.CreateDirectory(platformRoot);

            _log.Info("Publishing app...");
            ct.ThrowIfCancellationRequested();
            var appPublish = await _cli.PublishAppAsync(_repoRoot, platform, Path.Combine(tmp, "app"), ct);
            var bundleDir = Path.Combine(platformRoot, "SenderNG");
            Curation.CopyBundle(appPublish, bundleDir, _log);
            StageLicense(bundleDir);
            Curation.StripModulesToSdk(Path.Combine(bundleDir, "Modules"));
            if (platform is ReleasePlatform.Linux or ReleasePlatform.LinuxArm64)
                StageLinuxDesktopEntry(bundleDir);

            var stagedPlugins = new List<string>();
            if (plugins.Count > 0)
            {
                var pluginsRoot = Path.Combine(platformRoot, "plugins");
                Directory.CreateDirectory(pluginsRoot);
                foreach (var plugin in plugins)
                {
                    ct.ThrowIfCancellationRequested();
                    _log.Info($"Staging plugin {plugin.Id}...");
                    var publish = await _cli.PublishPluginAsync(_repoRoot, Path.Combine(_repoRoot, plugin.Project.Replace('/', Path.DirectorySeparatorChar)), platform, Path.Combine(tmp, "plugins", plugin.Id), ct);
                    var projectDir = Path.GetDirectoryName(Path.Combine(_repoRoot, plugin.Project.Replace('/', Path.DirectorySeparatorChar)))!;
                    var dest = Path.Combine(pluginsRoot, plugin.Id);
                    Directory.CreateDirectory(dest);
                    PluginStaging.Stage(publish, projectDir, plugin.Id, dest, _log);
                    WritePluginReadme(dest, plugin.Id);
                    stagedPlugins.Add(plugin.Id);
                }
            }

            ReleaseVerifier.Verify(bundleDir, platform);

            var artifacts = new List<string>();
            if (!options.NoZip)
            {
                var bundleZip = Path.Combine(platformRoot, $"SenderNG-{appVersion}-{PlatformInfo.ZipSuffix(platform)}.zip");
                ReleasePackage.CreateBundleZip(bundleDir, bundleZip);
                artifacts.Add(Path.GetFileName(bundleZip));
                _log.Info($"zip: {Path.GetRelativePath(options.OutputDir, bundleZip)}");

                foreach (var plugin in plugins)
                {
                    var pluginVersion = plugin.Version
                        ?? await _cli.ReadVersionAsync(_repoRoot, Path.Combine(_repoRoot, plugin.Project.Replace('/', Path.DirectorySeparatorChar)), ct)
                        ?? "1.0.0";
                    var pluginZip = Path.Combine(platformRoot, $"SenderNG-plugin-{plugin.Id}-{pluginVersion}-{PlatformInfo.ZipSuffix(platform)}.zip");
                    ReleasePackage.CreatePluginZip(Path.Combine(platformRoot, "plugins", plugin.Id), pluginZip);
                    artifacts.Add(Path.GetFileName(pluginZip));
                    _log.Info($"zip: {Path.GetRelativePath(options.OutputDir, pluginZip)}");
                }
            }

            ReleasePackage.WriteChecksums(platformRoot, "SHA256SUMS.txt");

            var fileCount = Directory.EnumerateFiles(platformRoot, "*", SearchOption.AllDirectories).Count();
            return new EngineReport(folder, appVersion, stagedPlugins, artifacts, fileCount);
        }
        finally
        {
            if (Directory.Exists(tmp))
                Directory.Delete(tmp, recursive: true);
        }
    }

    // The linux bundle is a portable zip, so it carries the desktop-entry
    // installer plus a loose PNG for it to install. Staged from the repo root
    // and gated on the target platform, never on the build OS: a win64 zip must
    // not grow a .sh, and the linux zip must be identical whether it was built
    // on a Linux host or through WSL from Windows.
    // BSD 3-Clause requires binary distributions to reproduce the license
    // text, so every core bundle carries the repo-root LICENSE. Staged
    // unconditionally on the shared path for all platforms; a release must
    // fail rather than ship without it.
    private void StageLicense(string bundleDir)
    {
        var source = Path.Combine(_repoRoot, LicenseFile);
        if (!File.Exists(source))
            throw new StagingException($"missing {LicenseFile}; a bundle cannot ship without the license text.");
        File.Copy(source, Path.Combine(bundleDir, LicenseFile), overwrite: true);
        _log.Info($"staged {LicenseFile}");
    }

    private void StageLinuxDesktopEntry(string bundleDir)
    {
        foreach (var relative in new[] { LinuxDesktopScript, AppIconPng })
        {
            var source = Path.Combine(_repoRoot, relative.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(source))
                throw new StagingException($"missing {relative}; a linux bundle cannot ship a desktop entry without it.");
            File.Copy(source, Path.Combine(bundleDir, Path.GetFileName(source)), overwrite: true);
            _log.Info($"staged {Path.GetFileName(source)}");
        }
    }

    private static void WritePluginReadme(string pluginDir, string pluginId)
    {
        var assembly = PluginStaging.OwnAssemblyFile(pluginDir);
        var readme = $"SenderNG plugin: {pluginId}\n\n" +
                     $"Copy everything in this folder EXCEPT README.txt into the app's Modules/ folder:\n" +
                     $"  {assembly}\t->\tModules/\n" +
                     $"  {pluginId}/\t->\tModules/{pluginId}/\n\n" +
                     "The app discovers plugins by scanning Modules/*.dll at startup.\n";
        File.WriteAllText(Path.Combine(pluginDir, "README.txt"), readme);
    }
}
