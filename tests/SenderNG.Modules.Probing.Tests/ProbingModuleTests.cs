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

using SenderNG.Core.Modules;
using SenderNG.Modules.Probing.Core;
using SenderNG.Core.Services;
using SenderNG.Modules.Plugins;
using Xunit;

namespace SenderNG.Modules.Probing.Tests;

/// <summary>
/// The probing module's public contract (the footprint Task 3 expands): the
/// plugin registers as a flyout module claiming the 50 slot, exposes its
/// enabled-tool settings as module settings and its labels through
/// localization, and binds its probe hotkeys.
/// </summary>
public class ProbingModuleTests
{
    [Fact]
    public void ProbingModule_exposes_the_plugin_contract()
    {
        var m = new ProbingModule();

        Assert.Equal("probing", m.Id);
        Assert.Equal(PanelHint.Flyout, m.Panel);
        Assert.Equal(50, m.Order);
        Assert.True(m.IsVisibleByDefault);
    }

    [Fact]
    public void ProbingModule_implements_the_module_setting_interfaces()
    {
        var m = new ProbingModule();

        Assert.IsAssignableFrom<IModuleSettingsProvider>(m);
        Assert.IsAssignableFrom<ILocalizedModule>(m);
        Assert.IsAssignableFrom<IHotkeyActionProvider>(m);
    }

    [Fact]
    public void ProbingModule_hosts_the_height_map_source_and_companion_suffixes()
    {
        var m = new ProbingModule();

        Assert.IsAssignableFrom<IHeightMapSource>(m);
        Assert.IsAssignableFrom<IProfileCompanionSuffixes>(m);
        var suffix = Assert.Single(((IProfileCompanionSuffixes)m).Suffixes);
        Assert.Equal(ProbingProfileStore.CompanionSuffix, suffix);
    }
}