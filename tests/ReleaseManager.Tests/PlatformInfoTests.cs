/*
 * PlatformInfoTests.cs - tests for platform enumeration and platform info helpers
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

using System.Runtime.InteropServices;

namespace ReleaseManager.Tests;

public class PlatformInfoTests
{
    [Fact]
    public void FolderName_win64_for_Win64()
    {
        Assert.Equal("win64", PlatformInfo.FolderName(ReleasePlatform.Win64));
    }

    [Fact]
    public void FolderName_linux_for_Linux()
    {
        Assert.Equal("linux", PlatformInfo.FolderName(ReleasePlatform.Linux));
    }

    [Fact]
    public void Rid_is_win_x64_for_Win64()
    {
        Assert.Equal("win-x64", PlatformInfo.Rid(ReleasePlatform.Win64));
    }

    [Fact]
    public void Rid_is_linux_x64_for_Linux()
    {
        Assert.Equal("linux-x64", PlatformInfo.Rid(ReleasePlatform.Linux));
    }

    [Fact]
    public void ZipSuffix_matches_design()
    {
        Assert.Equal("win64", PlatformInfo.ZipSuffix(ReleasePlatform.Win64));
        Assert.Equal("linux-x64", PlatformInfo.ZipSuffix(ReleasePlatform.Linux));
        Assert.Equal("linux-arm64", PlatformInfo.ZipSuffix(ReleasePlatform.LinuxArm64));
    }

    [Fact]
    public void RunnerFileName_matches_design()
    {
        Assert.Equal("SenderNG.exe", PlatformInfo.RunnerFileName(ReleasePlatform.Win64));
        Assert.Equal("SenderNG", PlatformInfo.RunnerFileName(ReleasePlatform.Linux));
        Assert.Equal("SenderNG", PlatformInfo.RunnerFileName(ReleasePlatform.LinuxArm64));
    }

    [Fact]
    public void Current_is_Win64_on_Windows()
    {
        Assert.Equal(OperatingSystem.IsWindows(), PlatformInfo.Current == ReleasePlatform.Win64);
    }

    [Fact]
    public void Current_is_Linux_on_Linux()
    {
        Assert.Equal(OperatingSystem.IsLinux(), PlatformInfo.Current == ReleasePlatform.Linux);
    }

    [Fact]
    public void Rid_is_linux_arm64_for_LinuxArm64()
    {
        Assert.Equal("linux-arm64", PlatformInfo.Rid(ReleasePlatform.LinuxArm64));
    }

    [Fact]
    public void Rid_LinuxArm64_is_not_linux_x64()
    {
        // The exact regression the ternary-to-switch conversion exists to prevent.
        // Written as `platform == Win64 ? "win-x64" : "linux-x64"`, a new enum
        // member silently takes the else branch: an x64 bundle published under an
        // arm64 name, with no error raised anywhere.
        Assert.NotEqual("linux-x64", PlatformInfo.Rid(ReleasePlatform.LinuxArm64));
    }

    [Fact]
    public void FolderName_LinuxArm64_does_not_collide_with_Linux()
    {
        // The collision this prevents: both Linux variants resolving to "linux"
        // would make an arm64 build delete and overwrite output/linux.
        Assert.NotEqual(
            PlatformInfo.FolderName(ReleasePlatform.Linux),
            PlatformInfo.FolderName(ReleasePlatform.LinuxArm64));
    }

    [Fact]
    public void Every_helper_rejects_an_unknown_platform()
    {
        // A guard against a future helper being added as a ternary, which would
        // fall through silently instead of failing. One representative member is
        // enough: all four share the same _ => throw arm.
        var unknown = (ReleasePlatform)999;
        Assert.Throws<ArgumentOutOfRangeException>(() => PlatformInfo.Rid(unknown));
        Assert.Throws<ArgumentOutOfRangeException>(() => PlatformInfo.FolderName(unknown));
        Assert.Throws<ArgumentOutOfRangeException>(() => PlatformInfo.ZipSuffix(unknown));
        Assert.Throws<ArgumentOutOfRangeException>(() => PlatformInfo.RunnerFileName(unknown));
    }

    [Fact]
    public void Rid_is_osx_arm64_for_MacOsArm64()
        => Assert.Equal("osx-arm64", PlatformInfo.Rid(ReleasePlatform.MacOsArm64));

    [Fact]
    public void Rid_is_osx_x64_for_MacOsX64()
        => Assert.Equal("osx-x64", PlatformInfo.Rid(ReleasePlatform.MacOsX64));

    [Fact]
    public void FolderName_and_ZipSuffix_match_design_for_macOS()
    {
        Assert.Equal("osx-arm64", PlatformInfo.FolderName(ReleasePlatform.MacOsArm64));
        Assert.Equal("osx-x64", PlatformInfo.FolderName(ReleasePlatform.MacOsX64));
        Assert.Equal("osx-arm64", PlatformInfo.ZipSuffix(ReleasePlatform.MacOsArm64));
        Assert.Equal("osx-x64", PlatformInfo.ZipSuffix(ReleasePlatform.MacOsX64));
    }

    [Fact]
    public void RunnerFileName_is_SenderNG_for_macOS()
    {
        Assert.Equal("SenderNG", PlatformInfo.RunnerFileName(ReleasePlatform.MacOsArm64));
        Assert.Equal("SenderNG", PlatformInfo.RunnerFileName(ReleasePlatform.MacOsX64));
    }

    [Fact]
    public void macOS_values_collide_with_no_other_platform()
    {
        // The x64/arm64 collision guard, extended: neither mac value may emit
        // a folder, RID or zip suffix already used by another platform.
        var macs = new[] { ReleasePlatform.MacOsArm64, ReleasePlatform.MacOsX64 };
        var others = new[] { ReleasePlatform.Win64, ReleasePlatform.Linux, ReleasePlatform.LinuxArm64 };
        Assert.All(macs, mac => Assert.All(others, other =>
        {
            Assert.NotEqual(PlatformInfo.Rid(other), PlatformInfo.Rid(mac));
            Assert.NotEqual(PlatformInfo.FolderName(other), PlatformInfo.FolderName(mac));
            Assert.NotEqual(PlatformInfo.ZipSuffix(other), PlatformInfo.ZipSuffix(mac));
        }));
        Assert.NotEqual(PlatformInfo.Rid(ReleasePlatform.MacOsArm64), PlatformInfo.Rid(ReleasePlatform.MacOsX64));
        Assert.NotEqual(PlatformInfo.FolderName(ReleasePlatform.MacOsArm64), PlatformInfo.FolderName(ReleasePlatform.MacOsX64));
    }

    [Fact]
    public void Current_selects_the_macOS_platform_by_architecture()
    {
        if (!OperatingSystem.IsMacOS())
            return;
        var expected = RuntimeInformation.ProcessArchitecture == Architecture.Arm64
            ? ReleasePlatform.MacOsArm64
            : ReleasePlatform.MacOsX64;
        Assert.Equal(expected, PlatformInfo.Current);
    }
}
