/*
 * ModuleSettingsScopeTests.cs - SettingsScope declarations on built-in
 * modules (docs/settings-migration.md §6.4): profile is the default, and the
 * Console module — whose options persist in global.json — declares Global.
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

using SenderNG.Core.Modules;
using SenderNG.Modules.Fixed;
using SenderNG.Modules.Plugins;
using Xunit;

namespace SenderNG.Tests;

public class ModuleSettingsScopeTests
{
    [Fact]
    public void Console_module_declares_global_scope()
    {
        IModuleSettingsProvider console = new ConsoleModule();
        Assert.Equal(SettingsScope.Global, console.Scope);
    }

    [Fact]
    public void Machine_coupled_modules_declare_profile_scope()
    {
        Assert.Equal(SettingsScope.Profile, AsProvider(new JogModule()).Scope);
        Assert.Equal(SettingsScope.Profile, AsProvider(new ProbingModule()).Scope);
    }

    private static IModuleSettingsProvider AsProvider(IModuleSettingsProvider provider) => provider;
}
