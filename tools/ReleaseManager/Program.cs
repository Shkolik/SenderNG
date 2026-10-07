/*
 * Program.cs - release staging tool entry point
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

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        var repoRoot = Directory.GetCurrentDirectory();
        if (!File.Exists(Path.Combine(repoRoot, "SenderNG.sln")))
        {
            Console.WriteLine("Run ReleaseManager from the repo root (where SenderNG.sln lives).");
            return ExitCodes.Cli;
        }

        try
        {
            ReleaseOptions options;
            var parsed = Cli.Parse(args);
            if (parsed.Options is null)
            {
                Console.WriteLine(Cli.Usage);
                return ExitCodes.Success;
            }
            options = parsed.Options;

            if (args.Length == 0)
            {
                if (Console.IsInputRedirected)
                    throw new CliException("Interactive mode needs a terminal. Pass arguments instead (see --help).");

                var log0 = new ConsoleLog(options.Verbose);
                var cli0 = new DotnetCli(new ProcessRunner());
                string? prefill = null;
                try
                {
                    prefill = await cli0.ReadVersionAsync(repoRoot, Path.Combine(repoRoot, "src/SenderNG/SenderNG.csproj"));
                }
                catch (BuildException)
                {
                    log0.Detail("Could not prefill version; type it manually.");
                }
                var platform = PlatformInfo.Current;
                var engine0 = new ReleaseEngine(repoRoot, cli0, log0);
                var candidates = PluginLists.Resolve(repoRoot, options, log0) is { Count: > 0 } all
                    ? all.Select(p => p.Id).ToArray()
                    : FirstPartyPlugins.All.Select(p => p.Id).ToArray();
                options = Cli.InteractiveText(Console.In, Console.Out, prefill, platform, candidates);
            }

            Cli.EnsureOutputUnderRepo(repoRoot, options.OutputDir);
            var log = new ConsoleLog(options.Verbose);
            var cli = new DotnetCli(new ProcessRunner());
            var engine = new ReleaseEngine(repoRoot, cli, log);

            using var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true;
                cts.Cancel();
            };

            var report = await engine.ReleaseAsync(options, cts.Token);
            Console.WriteLine();
            Console.WriteLine($"Release complete: {report.Platform}, app {report.AppVersion}");
            Console.WriteLine($"  plugins: {(report.PluginIds.Count == 0 ? "(none)" : string.Join(", ", report.PluginIds))}");
            Console.WriteLine($"  files: {report.FileCount}");
            foreach (var artifact in report.Artifacts)
                Console.WriteLine($"  artifact: {artifact}");
            return ExitCodes.Success;
        }
        catch (ReleaseManagerException ex)
        {
            Console.Error.WriteLine($"ReleaseManager: {ex.Message}");
            return ex.ExitCode;
        }
        catch (OperationCanceledException)
        {
            return ExitCodes.Cancelled;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"ReleaseManager: unexpected error: {ex}");
            return ExitCodes.Cli;
        }
    }
}
