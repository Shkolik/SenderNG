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
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using CNC.Core.Abstractions;
using CNC.Core.Services;
using SenderNG.Modules.Plugins;
using SenderNG.Services;
using Xunit;

namespace SenderNG.Tests;

/// <summary>The editor's shortcut affordances: the list chip and the detail row
/// both read the positional key, and the short name is capped at six characters
/// (the toolbar's ellipsis is only the fallback for a hand-edited settings file).</summary>
public class MacroViewHeadlessTests
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

    private static (Window Window, MacroView View, MacroViewModel Vm) Show(int count = 2)
    {
        var dir = Path.Combine(Path.GetTempPath(), "macro-view-hl-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var vm = new MacroViewModel(new SettingsService(dir), new StubGrbl());
        for (var i = 0; i < count; i++)
            vm.Macros.Add(new MacroItem { Name = $"Macro {i + 1}", Code = "G0 X0" });
        vm.SelectedMacro = vm.Macros[0];

        var view = new MacroView { DataContext = vm };
        var window = new Window { Content = view, Width = 600, Height = 520 };
        window.Show();
        return (window, view, vm);
    }

    [AvaloniaFact]
    public void Editor_shows_the_F_key_of_the_selected_macro()
    {
        var (_, view, _) = Show();

        // One chip on the list row, one on the detail row: the selected macro's key
        // is visible without opening anything. Both must be there — the list chip
        // alone would satisfy a mere "contains" assertion.
        Assert.Equal(2, view.GetVisualDescendants().OfType<TextBlock>()
            .Count(t => t.Text == "F1" && t.IsEffectivelyVisible));
    }

    [AvaloniaFact]
    public void A_pending_code_edit_commits_without_waiting_for_lost_focus()
    {
        var (window, view, vm) = Show();
        var code = view.GetVisualDescendants().OfType<TextBox>().Single(t => t.AcceptsReturn);

        code.Focus();
        code.SelectAll();
        window.KeyTextInput("G28");

        // LostFocus keeps the model stale while the box still has focus...
        Assert.Equal("G0 X0", vm.Macros[0].Code);

        // ...and this is what an F-key press does before running the macro.
        BindingCommit.CommitFocused(window);

        Assert.Equal("G28", vm.Macros[0].Code);
    }

    [AvaloniaFact]
    public void Editor_caps_the_short_name_at_six_characters()
    {
        var (window, view, vm) = Show();
        var boxes = view.GetVisualDescendants().OfType<TextBox>().ToList();
        var shortName = boxes.Single(t => t.MaxLength == 6);

        shortName.Focus();
        window.KeyTextInput("HOMETOOL");

        // LostFocus is the binding trigger: move focus to the name box to commit.
        boxes.First(t => !ReferenceEquals(t, shortName)).Focus();

        Assert.Equal("HOMETO", vm.SelectedMacro!.ShortName);
    }
}
