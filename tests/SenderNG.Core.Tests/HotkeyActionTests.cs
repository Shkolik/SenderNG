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

namespace SenderNG.Core.Tests;

public class HotkeyActionTests
{
    [Fact]
    public void CommandHotkeyAction_defaults_to_global_and_noop_up()
    {
        var down = 0;
        var action = new CommandHotkeyAction("a", "A", null, () => down++);

        Assert.Equal(HotkeyScope.Global, action.Scope);
        Assert.Null(action.ModuleId);
        Assert.False(action.RepeatOnHold);
        Assert.True(action.CanExecute());
        action.OnKeyDown();
        action.OnKeyUp();
        Assert.Equal(1, down);
    }

    [Fact]
    public void CommandHotkeyAction_honors_scope_module_canexecute_and_up()
    {
        var up = 0;
        var action = new CommandHotkeyAction(
            "probe.start", "Probe start", "module.probing.hotkey.probe.start",
            () => { }, () => false, () => up++,
            scope: HotkeyScope.Module, moduleId: "probing");

        Assert.Equal(HotkeyScope.Module, action.Scope);
        Assert.Equal("probing", action.ModuleId);
        Assert.Equal("module.probing.hotkey.probe.start", action.LocaleKey);
        Assert.False(action.CanExecute());
        action.OnKeyUp();
        Assert.Equal(1, up);
    }
}