/*
 * ReleasePackage.cs - bundle/plugin zips and SHA-256 checksums
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

using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace ReleaseManager;

public static class ReleasePackage
{
    public static void CreateBundleZip(string bundleDir, string zipPath)
        => ZipFile.CreateFromDirectory(bundleDir, zipPath, CompressionLevel.Optimal, includeBaseDirectory: true);

    public static void CreatePluginZip(string pluginDir, string zipPath)
        => ZipFile.CreateFromDirectory(pluginDir, zipPath, CompressionLevel.Optimal, includeBaseDirectory: true);

    public static void WriteChecksums(string platformDir, string fileName)
    {
        var records = new List<string>();
        foreach (var file in Directory.EnumerateFiles(platformDir, "*", SearchOption.AllDirectories)
                     .Select(f => Path.GetRelativePath(platformDir, f))
                     .Where(p => !p.Equals(fileName, StringComparison.Ordinal))
                     .OrderBy(p => p, StringComparer.Ordinal))
        {
            var fullPath = Path.Combine(platformDir, file);
            var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(fullPath))).ToLowerInvariant();
            records.Add($"{hash}  {file.Replace(Path.DirectorySeparatorChar, '/')}");
        }

        File.WriteAllText(Path.Combine(platformDir, fileName), string.Join('\n', records) + "\n", new UTF8Encoding(false));
    }
}
