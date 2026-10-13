/*
 * Cli.cs - CLI argument parsing and interactive checklist
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

public sealed record CliResult(ReleaseOptions? Options, string? Usage);

public static class Cli
{
    public const string Usage =
        """
        Usage: dotnet run --project tools/ReleaseManager [options]

          --include-plugins        stage all 1st-party and manifest 3rd-party plugins
          --plugins <id,id>        stage only the listed plugin ids (implies --include-plugins)
          --plugin <csproj>        add an ad-hoc 3rd-party plugin (repeatable)
          --version <ver>          override the app version read from the csproj
          --date <yyyyMMdd>        date suffix for weekly builds; ignored with --stable
          --stable                 weekly builds get a date suffix; --stable omits it
          --out <dir>              output directory (default: ./output)
          --platform <name>        target: win64 | linux | linux-arm64
                                    (default: the host OS)
          --skip-tests             do not run the test suite first
          --no-zip                  stage folders + checksums, no zips
          --verbose                per-file detail
          -h, --help               show this help

        With no arguments, ReleaseManager starts an interactive checklist.
        Platform defaults to the host OS. --platform cross-publishes, so
        linux-arm64 can be built from an x64 Linux host.
        """;

    public static CliResult Parse(string[] args)
    {
        if (args.Length > 0 && (args[0] == "--help" || args[0] == "-h"))
            return new CliResult(null, Usage);

        string outputDir = "output";
        string? versionOverride = null;
        string? dateOverride = null;
        string? platformName = null;
        bool skipTests = false;
        bool noZip = false;
        bool verbose = false;
        bool stable = false;
        var selection = PluginSelection.None;
        var pluginIds = new List<string>();
        var adHoc = new List<string>();

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            switch (arg)
            {
                case "--include-plugins":
                    selection = PluginSelection.All;
                    break;
                case "--plugins":
                    selection = PluginSelection.Subset;
                    pluginIds.AddRange(RequireValue(args, ref i, arg).Split(',', StringSplitOptions.RemoveEmptyEntries));
                    break;
                case "--plugin":
                    adHoc.Add(RequireValue(args, ref i, arg));
                    break;
                case "--version":
                    versionOverride = RequireValue(args, ref i, arg);
                    break;
                case "--date":
                    dateOverride = RequireValue(args, ref i, arg);
                    break;
                case "--stable":
                    stable = true;
                    break;
                case "--out":
                    outputDir = RequireValue(args, ref i, arg);
                    break;
                case "--skip-tests":
                    skipTests = true;
                    break;
                case "--no-zip":
                    noZip = true;
                    break;
                case "--verbose":
                    verbose = true;
                    break;
                case "--platform":
                    platformName = RequireValue(args, ref i, arg);
                    break;
                default:
                    throw new CliException($"Unknown option '{arg}'.\n\n{Cli.Usage}");
            }
        }

        ReleasePlatform? platform = platformName is null ? null : platformName switch
        {
            "win64" => ReleasePlatform.Win64,
            "linux" => ReleasePlatform.Linux,
            "linux-arm64" => ReleasePlatform.LinuxArm64,
            _ => throw new CliException(
                $"Unknown platform '{platformName}'. Valid values: win64, linux, linux-arm64.")
        };

        var options = new ReleaseOptions(
            Platform: platform,
            VersionOverride: versionOverride,
            SkipTests: skipTests,
            NoZip: noZip,
            Verbose: verbose,
            Selection: selection,
            PluginIds: pluginIds,
            AdHocPluginPaths: adHoc,
            OutputDir: outputDir,
            Stable: stable,
            DateOverride: dateOverride);

        return new CliResult(options, null);
    }

    public static ReleaseOptions InteractiveText(
        TextReader input,
        TextWriter output,
        string? prefillVersion,
        ReleasePlatform platform,
        IReadOnlyList<string> candidatePluginIds)
    {
        output.WriteLine("SenderNG Release Manager");
        output.WriteLine($"  Platform: {PlatformInfo.FolderName(platform)} (host)");

        var version = Prompt(input, output, $"  Version [{prefillVersion ?? "?"}]", allowEmpty: true);
        if (string.IsNullOrWhiteSpace(version))
            version = prefillVersion;
        output.WriteLine($"  Using version: {version}");

        var include = Prompt(input, output, "  Stage plugins? [y/N]", allowEmpty: true);
        var selection = PluginSelection.None;
        var pluginIds = new List<string>();
        if (string.Equals(include, "y", StringComparison.OrdinalIgnoreCase))
        {
            output.WriteLine("  Plugin selection — type an id to toggle, 'all'/'none', empty to continue:");
            foreach (var id in candidatePluginIds)
                output.WriteLine($"    [ ] {id}");
            var toggled = new HashSet<string>();
            while (true)
            {
                var line = Prompt(input, output, "  >", allowEmpty: true);
                if (string.IsNullOrWhiteSpace(line))
                    break;
                if (line == "all")
                {
                    toggled.UnionWith(candidatePluginIds);
                }
                else if (line == "none")
                {
                    toggled.Clear();
                }
                else if (candidatePluginIds.Contains(line))
                {
                    if (!toggled.Add(line))
                        toggled.Remove(line);
                }
                output.WriteLine($"    selected: {string.Join(", ", candidatePluginIds.Where(toggled.Contains))}");
            }
            selection = toggled.Count == candidatePluginIds.Count
                ? PluginSelection.All
                : PluginSelection.Subset;
            pluginIds = toggled.ToList();
        }

        var zipChoice = Prompt(input, output, "  Create zips + checksums? [Y/n]", allowEmpty: true);
        var createZip = !string.Equals(zipChoice, "n", StringComparison.OrdinalIgnoreCase);

        var final = Prompt(input, output, "  Stage release [Enter]", allowEmpty: true);

        return new ReleaseOptions(
            VersionOverride: string.IsNullOrWhiteSpace(version) ? null : version,
            NoZip: !createZip,
            Selection: selection,
            PluginIds: pluginIds);
    }

    internal static void EnsureOutputUnderRepo(string repoRoot, string outputDir)
    {
        var allowed = Path.GetFullPath(Path.Combine(repoRoot, "output"));
        var resolved = Path.GetFullPath(Path.Combine(repoRoot, outputDir));

        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        var prefix = allowed[^1] == Path.DirectorySeparatorChar
            ? allowed
            : allowed + Path.DirectorySeparatorChar;

        if (!string.Equals(resolved, allowed, comparison) &&
            !resolved.StartsWith(prefix, comparison))
        {
            throw new CliException(
                $"--out must resolve inside the repo's output/ directory. " +
                $"'{resolved}' is outside '{allowed}'.");
        }
    }

    private static string RequireValue(string[] args, ref int i, string flag)
    {
        if (i + 1 >= args.Length)
            throw new CliException($"Option '{flag}' requires a value.");
        i++;
        return args[i];
    }

    private static string Prompt(TextReader input, TextWriter output, string prompt, bool allowEmpty)
    {
        output.Write($"{prompt}: ");
        output.Flush();
        return (input.ReadLine() ?? string.Empty).Trim();
    }
}
