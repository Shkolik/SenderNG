/*
 * PlatformInfo.cs - release platform enumeration and platform helpers
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

public enum ReleasePlatform
{
    Win64,
    Linux,
    LinuxArm64
}

public static class PlatformInfo
{
    public static ReleasePlatform Current
    {
        get
        {
            if (OperatingSystem.IsWindows())
                return ReleasePlatform.Win64;
            if (OperatingSystem.IsLinux())
                return ReleasePlatform.Linux;
            if (OperatingSystem.IsMacOS())
                throw new UnsupportedPlatformException("macOS");
            throw new UnsupportedPlatformException("unknown");
        }
    }

    // Switches, not `platform == Win64 ? a : b`. A ternary sends every member
    // that is not Win64 down the same branch, so adding LinuxArm64 to the enum
    // would have made Rid() emit "linux-x64" and ZipSuffix() emit "linux-x64" —
    // an x64 bundle published under an arm64 name, with no error anywhere. The
    // `_` arm throws, so a future platform cannot be added without a decision
    // being made about all four mappings.

    public static string FolderName(ReleasePlatform platform) => platform switch
    {
        ReleasePlatform.Win64 => "win64",
        ReleasePlatform.Linux => "linux",
        ReleasePlatform.LinuxArm64 => "linux-arm64",
        _ => throw new ArgumentOutOfRangeException(nameof(platform), platform, null)
    };

    public static string Rid(ReleasePlatform platform) => platform switch
    {
        ReleasePlatform.Win64 => "win-x64",
        ReleasePlatform.Linux => "linux-x64",
        ReleasePlatform.LinuxArm64 => "linux-arm64",
        _ => throw new ArgumentOutOfRangeException(nameof(platform), platform, null)
    };

    public static string ZipSuffix(ReleasePlatform platform) => platform switch
    {
        ReleasePlatform.Win64 => "win64",
        ReleasePlatform.Linux => "linux-x64",
        ReleasePlatform.LinuxArm64 => "linux-arm64",
        _ => throw new ArgumentOutOfRangeException(nameof(platform), platform, null)
    };

    public static string RunnerFileName(ReleasePlatform platform) => platform switch
    {
        ReleasePlatform.Win64 => "SenderNG.exe",
        ReleasePlatform.Linux => "SenderNG",
        ReleasePlatform.LinuxArm64 => "SenderNG",
        _ => throw new ArgumentOutOfRangeException(nameof(platform), platform, null)
    };
}
