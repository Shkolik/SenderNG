/*
 * FirstPartyPluginsTests.cs - tests for the per-platform 1st-party plugin filter
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

public class FirstPartyPluginsTests
{
    [Fact]
    public void ForPlatform_Win64_returns_all_five()
    {
        var ids = FirstPartyPlugins.ForPlatform(ReleasePlatform.Win64).Select(p => p.Id).ToArray();
        Assert.Equal(["excellon", "hpgl", "plotter", "plotter-nex", "probing"], ids.OrderBy(x => x, StringComparer.Ordinal));
    }

    [Fact]
    public void ForPlatform_Linux_returns_all_five()
    {
        var ids = FirstPartyPlugins.ForPlatform(ReleasePlatform.Linux).Select(p => p.Id).ToArray();
        Assert.Equal(["excellon", "hpgl", "plotter", "plotter-nex", "probing"], ids.OrderBy(x => x, StringComparer.Ordinal));
    }

    [Fact]
    public void ForPlatform_LinuxArm64_omits_plotter_nex()
    {
        var ids = FirstPartyPlugins.ForPlatform(ReleasePlatform.LinuxArm64).Select(p => p.Id).ToArray();
        Assert.Equal(["excellon", "hpgl", "plotter", "probing"], ids.OrderBy(x => x, StringComparer.Ordinal));
    }

    [Fact]
    public void ForPlatform_does_not_mutate_All()
    {
        // ForPlatform returns All directly on the non-arm64 paths. If it ever
        // filtered in place, an arm64 build would silently strip plotter-nex
        // from every later x64 build in the same process.
        var before = FirstPartyPlugins.All.Count;
        FirstPartyPlugins.ForPlatform(ReleasePlatform.LinuxArm64);
        Assert.Equal(before, FirstPartyPlugins.All.Count);
        Assert.Contains(FirstPartyPlugins.All, p => p.Id == "plotter-nex");
    }
}
