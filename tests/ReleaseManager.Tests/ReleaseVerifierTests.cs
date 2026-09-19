/*
 * ReleaseVerifierTests.cs - tests for staged bundle verification
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

public class ReleaseVerifierTests
{
    [Fact]
    public void Verify_passes_for_a_complete_bundle()
    {
        var bundle = CompleteBundle(ReleasePlatform.Win64);
        ReleaseVerifier.Verify(bundle, ReleasePlatform.Win64);
    }

    [Fact]
    public void Verify_rejects_missing_runner()
    {
        var bundle = CompleteBundle(ReleasePlatform.Win64);
        File.Delete(Path.Combine(bundle, "SenderNG.exe"));
        var ex = Assert.Throws<StagingException>(() => ReleaseVerifier.Verify(bundle, ReleasePlatform.Win64));
        Assert.Contains("SenderNG.exe", ex.Message);
    }

    [Fact]
    public void Verify_rejects_pdb_leftover()
    {
        var bundle = CompleteBundle(ReleasePlatform.Win64);
        File.WriteAllText(Path.Combine(bundle, "SenderNG.pdb"), "pdb");
        Assert.Throws<StagingException>(() => ReleaseVerifier.Verify(bundle, ReleasePlatform.Win64));
    }

    [Fact]
    public void Verify_rejects_plugin_dll_left_in_modules()
    {
        var bundle = CompleteBundle(ReleasePlatform.Win64);
        File.WriteAllText(Path.Combine(bundle, "Modules", "SenderNG.Modules.Excellon.dll"), "mod");
        Assert.Throws<StagingException>(() => ReleaseVerifier.Verify(bundle, ReleasePlatform.Win64));
    }

    [Fact]
    public void Verify_rejects_designer_support()
    {
        var bundle = CompleteBundle(ReleasePlatform.Win64);
        File.WriteAllText(Path.Combine(bundle, "Avalonia.DesignerSupport.dll"), "dev");
        Assert.Throws<StagingException>(() => ReleaseVerifier.Verify(bundle, ReleasePlatform.Win64));
    }

    [Fact]
    public void Verify_rejects_bundle_without_native_runtime()
    {
        var bundle = CompleteBundle(ReleasePlatform.Win64);
        File.Delete(Path.Combine(bundle, "coreclr.dll"));
        var ex = Assert.Throws<StagingException>(() => ReleaseVerifier.Verify(bundle, ReleasePlatform.Win64));
        Assert.Contains("native runtime", ex.Message);
    }

    private static string CompleteBundle(ReleasePlatform platform)
    {
        var dir = Path.Combine(TempDir.New(), "SenderNG");
        Directory.CreateDirectory(Path.Combine(dir, "Modules"));
        File.WriteAllText(Path.Combine(dir, PlatformInfo.RunnerFileName(platform)), "exe");
        File.WriteAllText(Path.Combine(dir, "SenderNG.dll"), "dll");
        File.WriteAllText(Path.Combine(dir, "SenderNG.deps.json"), "{}");
        File.WriteAllText(Path.Combine(dir, "SenderNG.runtimeconfig.json"), "{}");
        File.WriteAllText(Path.Combine(dir, "SenderNG.Core.dll"), "core");
        File.WriteAllText(Path.Combine(dir, "Modules", "SenderNG.Modules.Common.dll"), "sdk");
        File.WriteAllText(Path.Combine(dir, platform == ReleasePlatform.Win64 ? "coreclr.dll" : "libcoreclr.so"), "native");
        return dir;
    }
}
