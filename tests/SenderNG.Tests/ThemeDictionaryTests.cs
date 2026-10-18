/*
 * ThemeDictionaryTests.cs - pins the Sender theme token dictionary
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

using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Xunit;

namespace SenderNG.Tests;

public class ThemeDictionaryTests
{
    private static readonly string[] TokenKeys =
    [
        "Sender.Accent", "Sender.Accent.Border", "Sender.Accent.Border.Alt",
        "Sender.Bg.Button", "Sender.Bg.Button.Checked", "Sender.Bg.Card",
        "Sender.Bg.Chrome", "Sender.Bg.Chip", "Sender.Bg.Chip.Hover",
        "Sender.Bg.Panel", "Sender.Bg.Selected", "Sender.Bg.Sidebar", "Sender.Bg.Window",
        "Sender.Border", "Sender.Border.Button.Checked", "Sender.Border.Chip",
        "Sender.Border.Input", "Sender.Border.Selected", "Sender.Border.Soft",
        "Sender.Border.Soft.Alt",
        "Sender.Canvas.Bg",
        "Sender.Danger.Bg", "Sender.Danger.Border",
        "Sender.Divider", "Sender.Divider.Strong", "Sender.Divider.Strong.Alt",
        "Sender.Icon.Action", "Sender.Icon.Action.Hover", "Sender.Icon.Checked", "Sender.Icon.Hover",
        "Sender.Overlay.Busy", "Sender.Overlay.Pressed",
        "Sender.Plot.Status",
        "Sender.Signal.Dot", "Sender.State.Border",
        "Sender.Text.Caption", "Sender.Text.Disabled", "Sender.Text.Error",
        "Sender.Text.Error.Alt", "Sender.Text.Error.Strong", "Sender.Text.Link",
        "Sender.Text.Muted", "Sender.Text.Muted.Alt", "Sender.Text.OnAccent",
        "Sender.Text.OnButton", "Sender.Text.Primary", "Sender.Text.Secondary",
        "Sender.Text.Strong", "Sender.Text.Success", "Sender.Text.Warn",
        "Sender.Text.Warn.Strong",
        "Sender.Warn.Bg", "Sender.Warn.Border", "Sender.Warn.Text",
    ];

    [AvaloniaFact]
    public void Every_token_exists_in_both_variants_with_identical_key_sets()
    {
        var theme = ThemeTestHost.LoadSenderTheme();
        var light = ThemeTestHost.DictionaryFor(theme, ThemeVariant.Light);
        var dark = ThemeTestHost.DictionaryFor(theme, ThemeVariant.Dark);

        var lightKeys = light.Keys.OfType<string>().Where(k => k.StartsWith("Sender.")).OrderBy(k => k).ToList();
        var darkKeys = dark.Keys.OfType<string>().Where(k => k.StartsWith("Sender.")).OrderBy(k => k).ToList();

        Assert.Equal(TokenKeys.OrderBy(k => k), lightKeys);
        Assert.Equal(lightKeys, darkKeys);
        foreach (var key in TokenKeys)
        {
            Assert.NotNull(light[key]);
            Assert.NotNull(dark[key]);
        }
    }

    [AvaloniaFact]
    public void Anchor_values_are_frozen()
    {
        var theme = ThemeTestHost.LoadSenderTheme();

        Assert.Equal(Color.Parse("#f6f6f6"), ThemeTestHost.ColorOf(theme, ThemeVariant.Light, "Sender.Bg.Window"));
        Assert.Equal(Color.Parse("#ffffff"), ThemeTestHost.ColorOf(theme, ThemeVariant.Light, "Sender.Bg.Panel"));
        Assert.Equal(Color.Parse("#e04545"), ThemeTestHost.ColorOf(theme, ThemeVariant.Light, "Sender.Accent"));
        Assert.Equal(Color.Parse("#ffffff"), ThemeTestHost.ColorOf(theme, ThemeVariant.Light, "Sender.Canvas.Bg"));
        Assert.Equal(Color.Parse("#000000"), ThemeTestHost.ColorOf(theme, ThemeVariant.Light, "Sender.Text.OnButton"));

        Assert.Equal(Color.Parse("#262626"), ThemeTestHost.ColorOf(theme, ThemeVariant.Dark, "Sender.Bg.Window"));
        Assert.Equal(Color.Parse("#2f2f2f"), ThemeTestHost.ColorOf(theme, ThemeVariant.Dark, "Sender.Bg.Panel"));
        Assert.Equal(Color.Parse("#e05555"), ThemeTestHost.ColorOf(theme, ThemeVariant.Dark, "Sender.Accent"));
        Assert.Equal(Color.Parse("#2a2a2e"), ThemeTestHost.ColorOf(theme, ThemeVariant.Dark, "Sender.Canvas.Bg"));
        Assert.Equal(Color.Parse("#e4e4e4"), ThemeTestHost.ColorOf(theme, ThemeVariant.Dark, "Sender.Text.OnButton"));
    }
}
