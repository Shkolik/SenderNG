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

using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using CNC.Core.Services;
using CNC.Core.ViewModels;
using SenderNG.Core.Localization;
using SenderNG.Core.Modules;
using SenderNG.Core.Services;
using SenderNG.Modules.Fixed;
using Xunit;

namespace SenderNG.Tests;

public class HotkeysEditorHeadlessTests
{
    private static readonly LocalizationService Loc =
        LocalizationService.LoadFromDirectory(Path.Combine(AppContext.BaseDirectory, "Locale"));

    private static (AppSettingsView View, HotkeysSettingsLeaf Leaf, HotkeyService Hotkeys) Make()
    {
        LocalizationService.Current = Loc;
        var settings = new SettingsService(
            Path.Combine(Path.GetTempPath(), "senderng-hotkeysui-" + Guid.NewGuid().ToString("N")));
        var hotkeys = new HotkeyService(settings);
        hotkeys.RegisterAction(new CommandHotkeyAction("cycle.start", "Cycle start", "app.hotkeys.cycle.start", () => { }), "Alt+R");
        var vm = new AppSettingsViewModel(settings, Array.Empty<IModule>(), Loc, hotkeys: hotkeys);
        var leaf = ((AppSettingsGroupItem)vm.Groups[0]).Children.OfType<HotkeysSettingsLeaf>().Single();
        vm.SelectedNode = leaf;
        return (new AppSettingsView { DataContext = vm }, leaf, hotkeys);
    }

    private static async Task Pump()
    {
        await Task.Yield();
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public async Task Hotkeys_pane_renders_rows_and_records_a_chord()
    {
        var (view, leaf, hotkeys) = Make();
        var window = new Window { Width = 920, Height = 560, Content = view };
        window.Show();
        await Pump();

        var row = leaf.Groups[0].Rows.Single(r => r.ActionId == "cycle.start");
        leaf.BeginRecord(row);
        await Pump();

        view.RaiseEvent(new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Key = Key.K,
            KeyModifiers = KeyModifiers.Control | KeyModifiers.Shift,
        });
        await Pump();

        Assert.Equal("Ctrl+Shift+K", hotkeys.GestureFor("cycle.start"));
    }

    [AvaloniaFact]
    public async Task Escape_cancels_recording()
    {
        var (view, leaf, hotkeys) = Make();
        var window = new Window { Width = 920, Height = 560, Content = view };
        window.Show();
        await Pump();

        var row = leaf.Groups[0].Rows.Single(r => r.ActionId == "cycle.start");
        leaf.BeginRecord(row);
        view.RaiseEvent(new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Key = Key.Escape,
            KeyModifiers = KeyModifiers.None,
        });
        await Pump();

        Assert.False(row.IsRecording);
        Assert.Equal("Alt+R", hotkeys.GestureFor("cycle.start"));
    }
}