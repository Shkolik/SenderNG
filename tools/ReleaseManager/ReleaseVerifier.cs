/*
 * ReleaseVerifier.cs - staged bundle verification: required and forbidden files
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

public static class ReleaseVerifier
{
    public static void Verify(string bundleDir, ReleasePlatform platform)
    {
        var failures = new List<string>();

        var required = new[]
        {
            PlatformInfo.RunnerFileName(platform),
            "SenderNG.dll",
            "SenderNG.deps.json",
            "SenderNG.runtimeconfig.json",
            "SenderNG.Core.dll",
            Path.Combine("Modules", "SenderNG.Modules.Common.dll"),
            "LICENSE"
        };
        foreach (var relative in required)
        {
            if (!File.Exists(Path.Combine(bundleDir, relative)))
                failures.Add($"missing: {relative}");
        }

        var coreLib = platform == ReleasePlatform.Win64 ? "coreclr.dll" : "libcoreclr.so";
        var nativeFound = Directory.EnumerateFiles(bundleDir, coreLib, SearchOption.AllDirectories).Any();
        if (!nativeFound)
            failures.Add($"missing: native runtime binaries ({coreLib})");

        foreach (var file in Directory.EnumerateFiles(bundleDir, "*.pdb", SearchOption.AllDirectories))
            failures.Add($"forbidden: {Path.GetRelativePath(bundleDir, file)}");

        foreach (var file in Directory.EnumerateFiles(bundleDir, "*", SearchOption.TopDirectoryOnly))
        {
            var name = Path.GetFileName(file);
            if (name.StartsWith("Avalonia.DesignerSupport", StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith("Avalonia.Remote.Protocol", StringComparison.OrdinalIgnoreCase))
                failures.Add($"forbidden: {name}");
        }

        var modulesDir = Path.Combine(bundleDir, "Modules");
        if (Directory.Exists(modulesDir))
        {
            foreach (var file in Directory.EnumerateFiles(modulesDir, "*.dll", SearchOption.TopDirectoryOnly))
            {
                var name = Path.GetFileName(file);
                if (name != "SenderNG.Modules.Common.dll")
                    failures.Add($"forbidden: Modules/{name}");
            }
        }

        if (failures.Count > 0)
            throw new StagingException($"Bundle verification failed:\n{string.Join("\n", failures)}");
    }
}
