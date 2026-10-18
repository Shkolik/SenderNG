/*
 * ThemeShellHeadlessTests.cs - pins the MainWindow shell zones to their tokens
 * and verifies they follow a live theme-variant switch
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
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SenderNG.Themes;
using Xunit;

namespace SenderNG.Tests;

[Collection("LocalizationState")] // merges into Application.Current.Resources and switches RequestedThemeVariant
public class ThemeShellHeadlessTests
{
    [AvaloniaTheory]
    [InlineData("Light", "Dark")]
    [InlineData("Dark", "Light")]
    public async Task Shell_zones_use_tokens_and_follow_a_live_variant_switch(string start, string other)
    {
        ThemeTestHost.EnsureMerged();
        var app = Application.Current!;
        var previous = app.RequestedThemeVariant;
        var theme = ThemeTestHost.LoadSenderTheme();
        var window = new MainWindow();
        try
        {
            var startVariant = ThemeVariants.ToVariant(start);
            app.RequestedThemeVariant = startVariant;
            window.Show();
            for (var i = 0; i < 3; i++)
                await Pump();

            AssertZone(theme, startVariant, window);

            var otherVariant = ThemeVariants.ToVariant(other);
            app.RequestedThemeVariant = otherVariant;
            for (var i = 0; i < 3; i++)
                await Pump();

            AssertZone(theme, otherVariant, window);
        }
        finally
        {
            app.RequestedThemeVariant = previous;
            window.Close();
        }
    }

    private static void AssertZone(ResourceDictionary theme, ThemeVariant variant, MainWindow window)
    {
        var toolbar = window.FindControl<Border>("ToolbarBand")!;
        Assert.Equal(ThemeTestHost.ColorOf(theme, variant, "Sender.Bg.Chrome"),
            ((ISolidColorBrush)toolbar.Background!).Color);

        var button = toolbar.GetVisualDescendants().OfType<Button>().First();
        Assert.Equal(ThemeTestHost.ColorOf(theme, variant, "Sender.Bg.Button"),
            ((ISolidColorBrush)button.Background!).Color);

        var center = window.FindControl<Border>("CenterHost")!;
        Assert.Equal(ThemeTestHost.ColorOf(theme, variant, "Sender.Bg.Panel"),
            ((ISolidColorBrush)center.Background!).Color);

        var status = window.FindControl<Border>("StatusBar")!;
        Assert.Equal(ThemeTestHost.ColorOf(theme, variant, "Sender.Bg.Chrome"),
            ((ISolidColorBrush)status.Background!).Color);

        var statusText = window.FindControl<TextBlock>("StatusBarText")!;
        Assert.Equal(ThemeTestHost.ColorOf(theme, variant, "Sender.Text.Secondary"),
            ((ISolidColorBrush)statusText.Foreground!).Color);
    }

    private static async Task Pump()
    {
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(16);
        Dispatcher.UIThread.RunJobs();
        await Task.Yield();
    }
}
