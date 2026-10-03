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

using CNC.Core.Services;
using SenderNG.Core.Services;

namespace SenderNG.Core.Tests;

public class HotkeyMapStoreTests
{
    [Fact]
    public void Read_absent_entry_returns_default()
    {
        var global = new GlobalSettings();
        Assert.Equal("Ctrl+S", HotkeyMapStore.Read(global, "x", "Ctrl+S", out var mutated));
        Assert.False(mutated);
    }

    [Theory]
    [InlineData("(none)")]
    [InlineData("")]
    [InlineData("   ")]
    public void Read_unbound_sentinels_return_null(string stored)
    {
        var global = new GlobalSettings { Hotkeys = { ["x"] = stored } };
        Assert.Null(HotkeyMapStore.Read(global, "x", "Ctrl+S", out _));
    }

    [Fact]
    public void Read_corrupt_entry_drops_to_default_and_reports_mutation()
    {
        var global = new GlobalSettings { Hotkeys = { ["x"] = "Ctrl+Nope" } };
        Assert.Equal("Ctrl+S", HotkeyMapStore.Read(global, "x", "Ctrl+S", out var mutated));
        Assert.True(mutated);
        Assert.False(global.Hotkeys.ContainsKey("x"));
    }

    [Fact]
    public void Write_Remove_Clear_round_trip()
    {
        var global = new GlobalSettings();
        HotkeyMapStore.Write(global, "x", null);
        Assert.Equal(HotkeyMapStore.Unbound, global.Hotkeys["x"]);
        HotkeyMapStore.Write(global, "x", "Ctrl+S");
        Assert.Equal("Ctrl+S", global.Hotkeys["x"]);
        HotkeyMapStore.Remove(global, "x");
        Assert.False(global.Hotkeys.ContainsKey("x"));
        global.Hotkeys["y"] = "Ctrl+Y";
        HotkeyMapStore.Clear(global);
        Assert.Empty(global.Hotkeys);
    }
}
