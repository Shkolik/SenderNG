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

using SenderNG.Core.Services;

namespace SenderNG.Core.Tests;

public class HotkeyGrammarTests
{
    [Theory]
    [InlineData("ctrl+s", "Ctrl+S")]
    [InlineData("ShiFt+S", "Shift+S")]
    [InlineData("alt+shift+space", "Alt+Shift+Space")]
    [InlineData("Ctrl+Alt+Shift+Win+X", "Ctrl+Alt+Shift+Win+X")]
    [InlineData("  Ctrl+S  ", "Ctrl+S")]
    [InlineData("PageUp", "PageUp")]
    [InlineData("NumPad1", "NumPad1")]
    [InlineData("D0", "D0")]
    [InlineData("OemPlus", "OemPlus")]
    public void Normalize_accepts_and_canonicalizes(string input, string expected)
        => Assert.Equal(expected, HotkeyGrammar.Normalize(input));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Ctrl")]
    [InlineData("Foo")]
    [InlineData("Ctrl+Foo")]
    [InlineData("Ctrl+Ctrl+S")]
    [InlineData("Ctrl+S+X")]
    public void Normalize_rejects_invalid(string? input)
        => Assert.Null(HotkeyGrammar.Normalize(input));

    [Fact]
    public void Build_orders_modifiers_ctrl_alt_shift_win()
        => Assert.Equal("Ctrl+Alt+Shift+Win+Home",
            HotkeyGrammar.Build("Home", HotkeyModifiers.Ctrl | HotkeyModifiers.Alt | HotkeyModifiers.Shift | HotkeyModifiers.Win));

    [Fact]
    public void TryBuild_rejects_unknown_key()
    {
        Assert.False(HotkeyGrammar.TryBuild("Nope", HotkeyModifiers.Ctrl, out var gesture));
        Assert.Null(gesture);
    }

    [Fact]
    public void HasModifiers_and_IsModifierKey()
    {
        Assert.True(HotkeyGrammar.HasModifiers("Ctrl+S"));
        Assert.False(HotkeyGrammar.HasModifiers("S"));
        Assert.True(HotkeyGrammar.IsModifierKey("Ctrl"));
        Assert.False(HotkeyGrammar.IsModifierKey("S"));
    }
}