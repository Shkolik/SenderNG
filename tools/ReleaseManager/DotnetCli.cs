/*
 * DotnetCli.cs - dotnet CLI facade backed by IProcessRunner
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

public sealed class DotnetCli : ICli
{
    private readonly IProcessRunner _runner;

    public DotnetCli(IProcessRunner runner)
    {
        _runner = runner;
    }

    public async Task TestAsync(string repoRoot, CancellationToken ct = default)
        => await RunAsync(
            "dotnet test SenderNG.sln -c Release (excluding Hardware and GPU)",
            _runner.RunAsync(repoRoot, "dotnet", ct, "test", "SenderNG.sln",
                "-c", "Release",
                "--filter", "Category!=Hardware&Category!=GPU",
                "--logger", "trx"));

    public async Task<string> PublishAppAsync(string repoRoot, ReleasePlatform platform, string outputDir, CancellationToken ct = default)
    {
        await RunAsync(
            $"publish app for {PlatformInfo.Rid(platform)}",
            _runner.RunAsync(repoRoot, "dotnet", ct, "publish",
                "src/SenderNG/SenderNG.csproj", "-c", "Release", "-r", PlatformInfo.Rid(platform),
                "--self-contained", "true", "-o", outputDir));
        return outputDir;
    }

    public async Task<string> PublishPluginAsync(string repoRoot, string csprojPath, ReleasePlatform platform, string outputDir, CancellationToken ct = default)
    {
        await RunAsync(
            $"publish plugin {csprojPath}",
            _runner.RunAsync(repoRoot, "dotnet", ct, "publish",
                csprojPath, "-c", "Release", "-r", PlatformInfo.Rid(platform), "-o", outputDir));
        return outputDir;
    }

    public async Task<string?> ReadVersionAsync(string repoRoot, string csprojPath, CancellationToken ct = default)
    {
        var result = await _runner.RunAsync(repoRoot, "dotnet", ct, "msbuild", csprojPath, "-getProperty:Version", "-nologo");
        if (!result.Succeeded)
            throw new BuildException($"Reading <Version> from {csprojPath} failed (exit {result.ExitCode}).");
        var version = result.StdOut.Select(line => line.Trim()).FirstOrDefault(line => !string.IsNullOrEmpty(line));
        return string.IsNullOrEmpty(version) ? null : version;
    }

    private static async Task RunAsync(string what, Task<ProcessResult> task)
    {
        var result = await task;
        if (!result.Succeeded)
            throw new BuildException($"{what} failed (exit {result.ExitCode}):\n{string.Join("\n", result.StdErr)}");
    }
}
