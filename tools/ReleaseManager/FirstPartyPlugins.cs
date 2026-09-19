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
        new("dummy", "src/SenderNG.Modules.Dummy/SenderNG.Modules.Dummy.csproj", null),
        new("excellon", "src/SenderNG.Modules.Excellon/SenderNG.Modules.Excellon.csproj", null),
        new("hpgl", "src/SenderNG.Modules.Hpgl/SenderNG.Modules.Hpgl.csproj", null),
        new("plotter", "src/SenderNG.Modules.Plotter/SenderNG.Modules.Plotter.csproj", null),
        new("plotter-nex", "src/SenderNG.Modules.Plotter.Nex/SenderNG.Modules.Plotter.Nex.csproj", null)
    ];

    public static PluginEntry First(string id)
    {
        var normalized = id.ToLowerInvariant().Replace('.', '-');
        var match = All.FirstOrDefault(p => p.Id == normalized)
            ?? throw new CliException($"Unknown 1st-party plugin id '{id}'.");
        return match;
    }
}
