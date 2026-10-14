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
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using CNC.Core.Services;
using SenderNG.Modules.Plotter.GL.Settings;
using SenderNG.Modules.Plotter.Settings;
using SenderNG.Modules.Plugins;
using SenderNG.Modules.Shared;
using SenderNG.Views;
using Xunit;

namespace SenderNG.Tests;

public class NumericInputFilterPresenceTests
{
    private static readonly string[] FilterClasses = ["num", "udec", "int"];

    private static void RaiseText(TextBox box, string text)
    {
        var evt = new TextInputEventArgs { RoutedEvent = InputElement.TextInputEvent, Text = text };
        box.RaiseEvent(evt);
    }

    private static (Window Window, List<TextBox> Boxes) ShowAndGetFilteredBoxes(UserControl view)
    {
        var window = new Window { Content = view };
        window.Show();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(16);

        var boxes = window.GetVisualDescendants().OfType<TextBox>()
            .Where(t => FilterClasses.Any(c => t.Classes.Contains(c)))
            .ToList();
        return (window, boxes);
    }

    private static void AssertAllRejectLetters(UserControl view)
    {
        var (window, boxes) = ShowAndGetFilteredBoxes(view);
        Assert.NotEmpty(boxes);
        foreach (var box in boxes)
        {
            box.Text = string.Empty;
            box.CaretIndex = 0;
            RaiseText(box, "a");
            Assert.Equal(string.Empty, box.Text);
        }
        window.Close();
    }

    [AvaloniaFact]
    public void Jog_settings_boxes_reject_letters()
    {
        var view = new JogSettingsView();
        AssertAllRejectLetters(view);
    }

    [AvaloniaFact]
    public void Jog_settings_box_accepts_digits()
    {
        var (window, boxes) = ShowAndGetFilteredBoxes(new JogSettingsView());
        var box = boxes.First();
        box.Text = string.Empty;
        box.CaretIndex = 0;
        RaiseText(box, "12");
        Assert.Equal("12", box.Text);
        window.Close();
    }

    [AvaloniaFact]
    public void Cutting_spindle_rpm_rejects_decimal_point()
    {
        var (window, boxes) = ShowAndGetFilteredBoxes(new CuttingView());
        var box = boxes.Single(t => t.Classes.Contains("int"));
        box.Text = string.Empty;
        box.CaretIndex = 0;
        RaiseText(box, ".");
        Assert.Equal(string.Empty, box.Text);
        window.Close();
    }

    [AvaloniaFact]
    public void Rotate_view_boxes_reject_letters() =>
        AssertAllRejectLetters(new RotateProgramView());

    [AvaloniaFact]
    public void Arcs_to_lines_view_boxes_reject_letters() =>
        AssertAllRejectLetters(new ArcsToLinesProgramView());

    [AvaloniaFact]
    public void Wrap_view_boxes_reject_letters() =>
        AssertAllRejectLetters(new WrapProgramView());

    [AvaloniaFact]
    public void Outline_feed_rate_rejects_letters()
    {
        var (window, boxes) = ShowAndGetFilteredBoxes(new OutlineView());
        var box = boxes.Single(t => t.Classes.Contains("int"));
        box.Text = string.Empty;
        box.CaretIndex = 0;
        RaiseText(box, "a");
        Assert.Equal(string.Empty, box.Text);
        window.Close();
    }

    [AvaloniaFact]
    public void Converter_settings_boxes_reject_letters() =>
        AssertAllRejectLetters(new ConverterSettingsView());

    [AvaloniaFact]
    public void Converter_parameters_boxes_reject_letters() =>
        AssertAllRejectLetters(new ConverterParametersView());

    [AvaloniaFact]
    public void Plotter_settings_boxes_reject_letters()
    {
        var dir = Path.Combine(Path.GetTempPath(), "senderng-presence-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var vm = new PlotterSettingsViewModel(new SettingsService(dir), null);
        AssertAllRejectLetters(new PlotterSettingsView { DataContext = vm });
    }

    [AvaloniaFact]
    public void Plotter_gl_settings_boxes_reject_letters() =>
        AssertAllRejectLetters(new PlotterGlSettingsView());

    [AvaloniaFact]
    public async Task Valid_paste_lands_via_SelectedText()
    {
        var (window, boxes) = ShowAndGetFilteredBoxes(new JogSettingsView());
        var box = boxes.First(t => t.Classes.Contains("num"));
        var clipboard = TopLevel.GetTopLevel(window)?.Clipboard;
        Assert.NotNull(clipboard);

        await clipboard!.SetTextAsync("-1.5");
        box.Text = string.Empty;
        box.CaretIndex = 0;
        box.RaiseEvent(new RoutedEventArgs(TextBox.PastingFromClipboardEvent));
        await Task.Delay(50); // OnPasting reads the clipboard asynchronously

        Assert.Equal("-1.5", box.Text);
        window.Close();
    }

    [AvaloniaFact]
    public async Task Invalid_paste_is_dropped()
    {
        var (window, boxes) = ShowAndGetFilteredBoxes(new JogSettingsView());
        var box = boxes.First(t => t.Classes.Contains("num"));
        var clipboard = TopLevel.GetTopLevel(window)?.Clipboard;
        Assert.NotNull(clipboard);

        await clipboard!.SetTextAsync("abc");
        box.Text = string.Empty;
        box.CaretIndex = 0;
        box.RaiseEvent(new RoutedEventArgs(TextBox.PastingFromClipboardEvent));
        await Task.Delay(50);

        Assert.Equal(string.Empty, box.Text);
        window.Close();
    }
}
