/*
 *
 * Copyright (c) 2026 Andrew Shkolik
 *
 * All rights reserved.
 *
 * Redistribution and use in source and binary forms, with or without modification,
 * are permitted provided that the following conditions are met:
 *
 * · Redistributions of source code must retain the above copyright notice, this
 * list of conditions and the following disclaimer.
 *
 * · Redistributions in binary form must reproduce the above copyright notice, this
 * list of conditions and the following disclaimer in the documentation and/or
 * other materials provided with the distribution.
 *
 * · Neither the name of the copyright holder nor the names of its contributors may
 * be used to endorse or promote products derived from this software without
 * specific prior written permission.
 *
 * THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND
 * ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
 * WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
 * DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE FOR
 * ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
 * (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
 * LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON
 * ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
 * (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
 * SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
 *
 */

using System.Reactive.Subjects;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using CNC.Core.Abstractions;
using CNC.Core.Services;
using SenderNG.Modules.Plugins;
using Xunit;

namespace SenderNG.Tests;

/// <summary>The toolbar block is the only place the M1–M12 labels exist: it binds
/// <c>ToolbarMacros</c> and nothing else, so these tests pin the label fallback
/// (short name vs. M label), the 12-macro cap, the hidden empty macro and the
/// 24 px separation from the file buttons.</summary>
public class MacroToolbarHeadlessTests
{
    private sealed class StubGrbl : IGrblService
    {
        private readonly BehaviorSubject<MachineState> _subject = new(new MachineState(3));
        public bool IsConnected => true;
        public IObservable<MachineState> State => _subject;
        public Task ConnectAsync(string port) => Task.CompletedTask;
        public void Disconnect() { }
        public Task SendAsync(string line) => Task.CompletedTask;
        public void SendRealtime(byte code) { }
    }

    private static MacroViewModel MakeVm(int count, string? firstShortName = null)
    {
        var dir = Path.Combine(Path.GetTempPath(), "macro-toolbar-hl-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var vm = new MacroViewModel(new SettingsService(dir), new StubGrbl());
        for (var i = 0; i < count; i++)
        {
            vm.Macros.Add(new MacroItem
            {
                Name = $"Macro {i + 1}",
                Code = "G0 X0",
                ShortName = i == 0 && firstShortName is not null ? firstShortName : string.Empty,
            });
        }
        return vm;
    }

    private static MacroToolbarView Show(MacroViewModel vm)
    {
        var view = new MacroToolbarView { DataContext = vm };
        var window = new Window { Content = view, Width = 1000, Height = 120 };
        window.Show();
        return view;
    }

    private static string TextOf(Button button) =>
        button.GetVisualDescendants().OfType<TextBlock>().First().Text ?? string.Empty;

    [AvaloniaFact]
    public void Toolbar_shows_one_button_per_shortcut_macro_using_M_labels()
    {
        // 13 macros, 12 keys: the 13th is not a shortcut and has no button.
        var view = Show(MakeVm(13));

        var buttons = view.GetVisualDescendants().OfType<Button>().ToList();

        Assert.Equal(12, buttons.Count);
        Assert.Equal("M1", TextOf(buttons[0]));
        Assert.Equal("M12", TextOf(buttons[11]));
        Assert.Equal("Macro 1", ToolTip.GetTip(buttons[0]));
    }

    [AvaloniaFact]
    public void Toolbar_shows_the_short_name_and_its_small_number_when_set()
    {
        var view = Show(MakeVm(2, firstShortName: "HOM"));

        var buttons = view.GetVisualDescendants().OfType<Button>().ToList();
        var labels = buttons[0].GetVisualDescendants().OfType<TextBlock>().ToList();

        Assert.Equal("HOM", labels[0].Text);
        Assert.Equal("1", labels[1].Text);
        Assert.True(labels[1].IsVisible);          // small number beside a short name
        Assert.Equal("M2", TextOf(buttons[1]));    // the macro without a short name keeps the M label
    }

    [AvaloniaFact]
    public void Toolbar_is_hidden_with_no_macros_and_keeps_the_24px_gap()
    {
        var empty = Show(MakeVm(0));
        Assert.False(empty.IsVisible);

        var view = Show(MakeVm(2));
        Assert.True(view.IsVisible);
        // StackPanel spacing 6 + margin 18 = the agreed 24 px separation from the file buttons.
        Assert.Equal(new Thickness(18, 0, 0, 0), ((Border)view.Content!).Margin);
    }

    [AvaloniaFact]
    public void Toolbar_skips_macros_without_code_and_keeps_their_slots_numbered()
    {
        var vm = MakeVm(3);
        vm.Macros[0].Code = string.Empty;   // F1 exists but has nothing to run

        var view = Show(vm);
        var buttons = view.GetVisualDescendants().OfType<Button>().ToList();

        Assert.Equal(2, buttons.Count);
        Assert.Equal("M2", TextOf(buttons[0]));
        Assert.Equal("M3", TextOf(buttons[1]));

        foreach (var macro in vm.Macros)
            macro.Code = string.Empty;

        Assert.False(view.IsVisible);
    }

    [AvaloniaFact]
    public void Removing_a_macro_does_not_move_the_survivors_onto_other_keys()
    {
        // Ten macros; delete the fourth. The F4 slot disappears, but the macro
        // named "Macro 5" keeps M5 — label and tooltip stay together.
        var vm = MakeVm(10);
        var view = Show(vm);

        vm.Macros.RemoveAt(3);

        var buttons = view.GetVisualDescendants().OfType<Button>().ToList();

        Assert.Equal(9, buttons.Count);
        Assert.Equal(["M1", "M2", "M3", "M5", "M6", "M7", "M8", "M9", "M10"],
            buttons.Select(TextOf));
        Assert.Equal("Macro 5", ToolTip.GetTip(buttons[3]));
    }
}
