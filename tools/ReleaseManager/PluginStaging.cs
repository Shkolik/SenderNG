/*
 * PluginStaging.cs - plugin drop-in staging rules: assembly flat, payload under plugin id
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

public static class PluginStaging
{
    public static string OwnAssemblyFile(string publishDir)
    {
        if (!Directory.Exists(publishDir))
            throw new StagingException($"Publish directory does not exist: {publishDir}");

        var candidates = Directory.EnumerateFiles(publishDir, "*.dll")
            .Select(Path.GetFileName)
            .OfType<string>()
            .Where(fileName => !Curation.IsHostOwned(fileName))
            .ToArray();

        if (candidates.Length == 1)
            return candidates[0];

        var ownMatch = Directory.EnumerateFiles(publishDir, "*.deps.json")
            .Select(deps => Path.GetFileName(deps).Replace(".deps.json", string.Empty, StringComparison.OrdinalIgnoreCase) + ".dll")
            .FirstOrDefault(name => candidates.Contains(name, StringComparer.OrdinalIgnoreCase));

        if (ownMatch is not null)
            return ownMatch;

        throw new StagingException(
            $"Cannot resolve the plugin's own assembly in {publishDir}: found {candidates.Length} candidate DLL(s).");
    }

    public static void Stage(string publishDir, string projectDir, string pluginId, string destDir, ILog log)
    {
        var assembly = OwnAssemblyFile(publishDir);
        File.Copy(Path.Combine(publishDir, assembly), Path.Combine(destDir, assembly), overwrite: true);
        log.Detail($"plugin {pluginId}: assembly {assembly}");

        var ownDir = Path.Combine(destDir, pluginId);
        Directory.CreateDirectory(ownDir);

        foreach (var file in Directory.EnumerateFiles(publishDir))
        {
            var fileName = Path.GetFileName(file);
            if (fileName == assembly || Curation.IsHostOwned(fileName))
                continue;
            if (fileName.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase) ||
                fileName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) ||
                fileName.EndsWith(".deps.json", StringComparison.OrdinalIgnoreCase))
                continue;
            File.Copy(file, Path.Combine(ownDir, fileName), overwrite: true);
        }

        var runtimes = Path.Combine(publishDir, "runtimes");
        if (Directory.Exists(runtimes))
        {
            foreach (var native in Directory.EnumerateFiles(runtimes, "*", SearchOption.AllDirectories))
            {
                var navSegment = Path.GetFileName(Path.GetDirectoryName(native)!);
                if (!string.Equals(navSegment, "native", StringComparison.OrdinalIgnoreCase))
                    continue;
                File.Copy(native, Path.Combine(ownDir, Path.GetFileName(native)), overwrite: true);
            }
        }

        var publishLocale = Path.Combine(publishDir, "Locale");
        if (Directory.Exists(publishLocale))
        {
            CopyRecursive(publishLocale, Path.Combine(ownDir, "Locale"));
        }

        var projectLocale = Path.Combine(projectDir, "Locale", pluginId);
        if (Directory.Exists(projectLocale))
        {
            Directory.CreateDirectory(Path.Combine(ownDir, "Locale"));
            CopyRecursive(projectLocale, Path.Combine(ownDir, "Locale"));
        }
    }

    private static void CopyRecursive(string sourceDir, string destDir)
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
