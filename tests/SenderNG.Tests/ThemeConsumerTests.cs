/*
 * ThemeConsumerTests.cs - pins the code-side token consumers (dialogs, plotter canvas)
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

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using SenderNG.Modules.Plotter;
using SenderNG.Themes;
using Xunit;

namespace SenderNG.Tests;

[Collection("LocalizationState")] // merges into Application.Current.Resources and switches RequestedThemeVariant
public class ThemeConsumerTests
{
    [AvaloniaTheory]
    [InlineData("Light", "#ffffff")]
    [InlineData("Dark", "#2a2a2e")]
    public async Task Plotter_canvas_brush_resolves_the_canvas_token(string variantName, string expected)
    {
        ThemeTestHost.EnsureMerged();
        var app = Application.Current!;
        var previous = app.RequestedThemeVariant;
        var border = new Border();
        var window = new Window { Content = border };
        try
        {
            app.RequestedThemeVariant = ThemeVariants.ToVariant(variantName);
            window.Show();
            await Pump();

            var brush = PlotterCanvas.Brush(border, border.ActualThemeVariant, Brushes.White);
            Assert.Equal(Color.Parse(expected), ((ISolidColorBrush)brush).Color);
        }
        finally
        {
            app.RequestedThemeVariant = previous;
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData("Light", "#f6f6f6")]
    [InlineData("Dark", "#262626")]
    public void ThemeResources_finds_tokens_with_the_active_variant(string variantName, string expected)
    {
        ThemeTestHost.EnsureMerged();
        var app = Application.Current!;
        var previous = app.RequestedThemeVariant;
        try
        {
            app.RequestedThemeVariant = ThemeVariants.ToVariant(variantName);
            Assert.Equal(Color.Parse(expected),
                ThemeResources.Find("Sender.Bg.Window", Color.Parse("#ff00ff")));
        }
        finally
        {
            app.RequestedThemeVariant = previous;
        }
    }

    [Fact]
    public void ThemeResources_falls_back_when_the_key_is_missing() =>
        Assert.Equal(Color.Parse("#ff00ff"),
            ThemeResources.Find("Sender.Does.Not.Exist", Color.Parse("#ff00ff")));

    private static async Task Pump()
    {
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(16);
        Dispatcher.UIThread.RunJobs();
        await Task.Yield();
    }
}
