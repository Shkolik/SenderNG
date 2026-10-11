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
using Avalonia.Input;
using Avalonia.Themes.Fluent;
using Avalonia.VisualTree;
using SenderNG.Modules.Plugins;

[assembly: AvaloniaTestApplication(typeof(SenderNG.Modules.Probing.Tests.ProbeHeadlessApp))]

namespace SenderNG.Modules.Probing.Tests;

// ProbeView cannot be built against the default style-less headless Application:
// its ItemContainerTheme resolves BasedOn="{StaticResource {x:Type TabItem}}" and
// Window/TextBox need real templates. Mirrors SenderNG.Tests UiHeadlessApp (no
// Skia here: no pixel assertions).
public sealed class ProbeHeadlessApp : Application
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<ProbeHeadlessApp>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions());

    public override void OnFrameworkInitializationCompleted()
    {
        Styles.Add(new FluentTheme());
        base.OnFrameworkInitializationCompleted();
    }
}

public class ProbeViewFilterTests
{
    [AvaloniaFact]
    public void Probe_diameter_rejects_letters()
    {
        var view = new ProbeView();
        var window = new Window { Content = view };
        window.Show();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(16);

        var box = window.GetVisualDescendants().OfType<TextBox>()
            .First(t => t.Classes.Contains("num"));
        box.Text = string.Empty;
        box.CaretIndex = 0;
        box.RaiseEvent(new TextInputEventArgs { RoutedEvent = InputElement.TextInputEvent, Text = "a" });
        Assert.Equal(string.Empty, box.Text);
        window.Close();
    }

    [AvaloniaFact]
    public void Probe_diameter_accepts_digits()
    {
        var view = new ProbeView();
        var window = new Window { Content = view };
        window.Show();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(16);

        var box = window.GetVisualDescendants().OfType<TextBox>()
            .First(t => t.Classes.Contains("num"));
        box.Text = string.Empty;
        box.CaretIndex = 0;
        box.RaiseEvent(new TextInputEventArgs { RoutedEvent = InputElement.TextInputEvent, Text = "6" });
        Assert.Equal("6", box.Text);
        window.Close();
    }
}
