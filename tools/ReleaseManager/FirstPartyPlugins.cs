/*
 * FirstPartyPlugins.cs - hardcoded set of in-solution (1st-party) plugin projects
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

public static class FirstPartyPlugins
{
    public static IReadOnlyList<PluginEntry> All { get; } =
    [
        new("excellon", "src/SenderNG.Modules.Excellon/SenderNG.Modules.Excellon.csproj", null),
        new("hpgl", "src/SenderNG.Modules.Hpgl/SenderNG.Modules.Hpgl.csproj", null),
        new("plotter", "src/SenderNG.Modules.Plotter/SenderNG.Modules.Plotter.csproj", null),
        new("plotter-nex", "src/SenderNG.Modules.Plotter.Nex/SenderNG.Modules.Plotter.Nex.csproj", null),
        new("probing", "src/SenderNG.Modules.Probing/SenderNG.Modules.Probing.csproj", null)
    ];

    /// <summary>
    /// The 1st-party plugins available on <paramref name="platform"/>.
    ///
    /// plotter-nex is excluded on linux-arm64. Two independent reasons, either
    /// sufficient on its own:
    ///
    ///   1. No aarch64 natives exist. Vortice.SPIRV.Reflect and
    ///      Vortice.VulkanMemoryAllocator are direct dependencies of
    ///      HelixToolkit.Nex.Graphics.Vulkan and P/Invoke into `spirv-reflect`
    ///      and `vma*`. Neither package ships a linux-arm64 native — only
    ///      linux-x64, android-arm64, osx-arm64 and win-arm64 — so staging
    ///      linux-x64 .so files into an arm64 bundle would crash at first use.
    ///   2. The driver is insufficient. Mesa v3dv on a Pi 4B reports
    ///      apiVersion 1.3.354 and passes all four Vulkan 1.3 feature checks,
    ///      but fails 10 of the 13 features CreateFeatures12 hard-throws on
    ///      (DeviceFeatures.cs:73-175) — including descriptorIndexing, which
    ///      the renderer needs structurally: every texture is bound through one
    ///      global bindless descriptor set (HeaderDefines.glsl:22-29,
    ///      VulkanContext.cs:1068-1148) and indexed per-fragment via
    ///      nonuniformEXT.
    ///
    /// Do not "fix" this omission without re-testing on real hardware. See
    /// docs/superpowers/specs/2026-09-26-arm64-linux-support-design.md.
    /// </summary>
    public static IReadOnlyList<PluginEntry> ForPlatform(ReleasePlatform platform)
    {
        if (platform != ReleasePlatform.LinuxArm64)
            return All;
        return All.Where(p => p.Id != "plotter-nex").ToList();
    }

    public static PluginEntry First(string id)
    {
        var normalized = id.ToLowerInvariant().Replace('.', '-');
        var match = All.FirstOrDefault(p => p.Id == normalized)
            ?? throw new CliException($"Unknown 1st-party plugin id '{id}'.");
        return match;
    }
}
