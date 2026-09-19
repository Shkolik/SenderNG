/*
 * ReleasePackageTests.cs - tests for bundle/plugin zips and SHA-256 checksums
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

namespace ReleaseManager.Tests;

public class ReleasePackageTests
{
    [Fact]
    public void CreateBundleZip_roots_entries_under_SenderNG()
    {
        var parent = TempDir.New();
        var bundle = Path.Combine(parent, "SenderNG");
        Directory.CreateDirectory(Path.Combine(bundle, "Locale"));
        File.WriteAllText(Path.Combine(bundle, "SenderNG.exe"), "exe");
        File.WriteAllText(Path.Combine(bundle, "Locale", "en.json"), "{}");
        var zip = Path.Combine(TempDir.New(), "out.zip");

        ReleasePackage.CreateBundleZip(bundle, zip);

        using var archive = ZipFile.OpenRead(zip);
        var entries = archive.Entries.Select(e => e.FullName).OrderBy(x => x).ToArray();
        Assert.Equal(["SenderNG/Locale/en.json", "SenderNG/SenderNG.exe"], entries);
    }

    [Fact]
    public void CreatePluginZip_roots_entries_under_plugin_id()
    {
        var parent = TempDir.New();
        var plugin = Path.Combine(parent, "myplugin");
        Directory.CreateDirectory(Path.Combine(plugin, "Locale"));
        File.WriteAllText(Path.Combine(plugin, "SenderNG.Modules.X.dll"), "mod");
        File.WriteAllText(Path.Combine(plugin, "Locale", "en.json"), "{}");
        var zip = Path.Combine(tempZipDir(), "SenderNG-plugin-myplugin-0.1.0-win64.zip");

        ReleasePackage.CreatePluginZip(plugin, zip);

        using var archive = ZipFile.OpenRead(zip);
        var entries = archive.Entries.Select(e => e.FullName).OrderBy(x => x).ToArray();
        Assert.Equal(["myplugin/Locale/en.json", "myplugin/SenderNG.Modules.X.dll"], entries);
    }

    private static string tempZipDir()
    {
        var dir = TempDir.New();
        return dir;
    }

    [Fact]
    public void WriteChecksums_records_every_file_sorted()
    {
        var platform = TempDir.New();
        Directory.CreateDirectory(Path.Combine(platform, "SenderNG"));
        File.WriteAllText(Path.Combine(platform, "SenderNG", "SenderNG.exe"), "exe");
        File.WriteAllBytes(Path.Combine(platform, "SenderNG-0.1.0-win64.zip"), Encoding.ASCII.GetBytes("zip-bytes"));

        ReleasePackage.WriteChecksums(platform, "SHA256SUMS.txt");

        var lines = File.ReadAllLines(Path.Combine(platform, "SHA256SUMS.txt"));
        Assert.Equal(2, lines.Length);
        Assert.StartsWith("  ", lines[0].Substring(64));
        Assert.Contains("SenderNG-0.1.0-win64.zip", lines[0]);
        var expectedExeHash = Convert.ToHexString(SHA256.HashData("exe"u8.ToArray())).ToLowerInvariant();
        var expectedZipHash = Convert.ToHexString(SHA256.HashData("zip-bytes"u8.ToArray())).ToLowerInvariant();
        var hashByPath = lines.ToDictionary(l => l.Substring(66), l => l.Substring(0, 64));
        Assert.Equal(expectedExeHash, hashByPath["SenderNG/SenderNG.exe"]);
        Assert.Equal(expectedZipHash, hashByPath["SenderNG-0.1.0-win64.zip"]);
    }
}
