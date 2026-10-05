/*
 * FakeCli.cs - in-memory ICli double for engine tests
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

public sealed class FakeCli : ICli
{
    public string? AppPublishDir { get; set; }
    public string AppVersion { get; set; } = "0.1.0";
    public Dictionary<string, string> PluginVersions { get; } = [];
    public int TestCalls { get; private set; }
    public Dictionary<string, string> PluginPublishDirs { get; } = [];
    public CancellationToken ReceivedToken { get; private set; }

    public Task TestAsync(string repoRoot, CancellationToken ct = default)
    {
        TestCalls++;
        ReceivedToken = ct;
        ct.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    public Task<string> PublishAppAsync(string repoRoot, ReleasePlatform platform, string outputDir, CancellationToken ct = default)
    {
        Directory.CreateDirectory(outputDir);
        if (AppPublishDir is not null)
            CopyRecursive(AppPublishDir, outputDir);
        return Task.FromResult(outputDir);
    }

    public Task<string> PublishPluginAsync(string repoRoot, string csprojPath, ReleasePlatform platform, string outputDir, CancellationToken ct = default)
    {
        var id = Path.GetFileNameWithoutExtension(csprojPath);
        if (PluginPublishDirs.TryGetValue(id, out var source))
        {
            Directory.CreateDirectory(outputDir);
            CopyRecursive(source, outputDir);
        }
        else
        {
            Directory.CreateDirectory(outputDir);
            File.WriteAllText(Path.Combine(outputDir, Path.GetFileName(csprojPath).Replace(".csproj", ".dll")), "mod");
        }
        return Task.FromResult(outputDir);
    }

    public Task<string?> ReadVersionAsync(string repoRoot, string csprojPath, CancellationToken ct = default)
    {
        var id = Path.GetFileNameWithoutExtension(csprojPath);
        return Task.FromResult(id == "SenderNG" ? AppVersion
            : PluginVersions.TryGetValue(id, out var v) ? v : (string?)null);
    }

    internal static void CopyRecursive(string sourceDir, string destDir)
    {
        Directory.CreateDirectory(destDir);
        foreach (var source in Directory.EnumerateFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourceDir, source);
            var destination = Path.Combine(destDir, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(source, destination, overwrite: true);
        }
    }
}
