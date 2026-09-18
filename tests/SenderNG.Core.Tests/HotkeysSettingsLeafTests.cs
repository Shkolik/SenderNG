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
using CNC.Core.ViewModels;
using SenderNG.Core.Localization;
using SenderNG.Core.Modules;
using SenderNG.Core.Services;

namespace SenderNG.Core.Tests;

public class HotkeysSettingsLeafTests
{
    private static (AppSettingsViewModel Vm, HotkeysSettingsLeaf Leaf, HotkeyService Hotkeys) Make()
    {
        var settings = new SettingsService(
            Path.Combine(Path.GetTempPath(), "senderng-hotkeysleaf-" + Guid.NewGuid().ToString("N")));
        var hotkeys = new HotkeyService(settings);
        hotkeys.RegisterAction(new CommandHotkeyAction("cycle.start", "Cycle start", "app.hotkeys.cycle.start", () => { }), "Alt+R");
        hotkeys.RegisterAction(new CommandHotkeyAction("cycle.hold", "Feed hold", "app.hotkeys.cycle.hold", () => { }), "Space");
        hotkeys.RegisterAction(new CommandHotkeyAction("probe.start", "Start probing", "module.probing.hotkey.probe.start",
            () => { }, scope: HotkeyScope.Module, moduleId: "probing"), "Alt+R");

        var loc = LocalizationService.LoadFromDirectory(Path.Combine(AppContext.BaseDirectory, "Locale"));
        var vm = new AppSettingsViewModel(settings, Array.Empty<IModule>(), loc, hotkeys: hotkeys);
        var leaf = ((AppSettingsGroupItem)vm.Groups[0]).Children.OfType<HotkeysSettingsLeaf>().Single();
        vm.SelectedNode = leaf;
        return (vm, leaf, hotkeys);
    }

    [Fact]
    public void Leaf_groups_rows_by_scope_with_global_first()
    {
        var (vm, leaf, _) = Make();
        Assert.True(vm.IsHotkeysLeaf);
        Assert.Same(leaf, vm.SelectedHotkeysLeaf);
        Assert.Equal("Global", leaf.Groups[0].Name);
        Assert.Contains("probing", leaf.Groups.Select(g => g.Name));
        var cycles = leaf.Groups[0].Rows.Single(r => r.ActionId == "cycle.start");
        Assert.Equal("Alt+R", cycles.GestureText);
    }

    [Fact]
    public void Recording_applies_a_gesture_and_persists()
    {
        var (_, leaf, hotkeys) = Make();
        var row = leaf.Groups[0].Rows.Single(r => r.ActionId == "cycle.start");
        leaf.BeginRecord(row);
        Assert.True(row.IsRecording);
        leaf.ApplyRecordedGesture("Ctrl+Shift+K");

        Assert.False(row.IsRecording);
        Assert.Equal("Ctrl+Shift+K", hotkeys.GestureFor("cycle.start"));
    }

    [Fact]
    public void In_scope_conflict_unbinds_the_displaced_action()
    {
        var (_, leaf, hotkeys) = Make();
        var row = leaf.Groups[0].Rows.Single(r => r.ActionId == "cycle.hold");
        leaf.BeginRecord(row);
        leaf.ApplyRecordedGesture("Alt+R");

        Assert.Equal("Alt+R", hotkeys.GestureFor("cycle.hold"));
        Assert.Null(hotkeys.GestureFor("cycle.start"));
        Assert.True(row.HasMessage);
    }

    [Fact]
    public void Cross_scope_conflict_is_allowed()
    {
        var (_, leaf, hotkeys) = Make();
        var probeRow = leaf.Groups.SelectMany(g => g.Rows).Single(r => r.ActionId == "probe.start");
        leaf.BeginRecord(probeRow);
        leaf.ApplyRecordedGesture("Ctrl+Shift+K");

        Assert.Equal("Ctrl+Shift+K", hotkeys.GestureFor("probe.start"));
        Assert.Equal("Alt+R", hotkeys.GestureFor("cycle.start")); // untouched
        Assert.False(probeRow.HasMessage);
    }

    [Fact]
    public void Clear_and_restore_update_the_row()
    {
        var (_, leaf, hotkeys) = Make();
        var row = leaf.Groups[0].Rows.Single(r => r.ActionId == "cycle.start");
        leaf.Clear(row);
        Assert.True(row.IsUnbound);
        Assert.Null(hotkeys.GestureFor("cycle.start"));

        leaf.RestoreOne(row);
        Assert.Equal("Alt+R", hotkeys.GestureFor("cycle.start"));
    }

    [Fact]
    public void Restore_all_restores_every_row()
    {
        var (_, leaf, hotkeys) = Make();
        hotkeys.SetGesture("cycle.start", "Ctrl+D9");
        leaf.RestoreAllCommand.Execute(null);
        Assert.Equal("Alt+R", hotkeys.GestureFor("cycle.start"));
        Assert.Equal("Space", hotkeys.GestureFor("cycle.hold"));
    }

    [Fact]
    public void Search_filters_rows_and_hides_empty_groups()
    {
        var (_, leaf, _) = Make();
        leaf.SearchText = "probing";
        var visible = leaf.Groups.Where(g => g.IsVisible).SelectMany(g => g.Rows).Select(r => r.ActionId).ToList();
        Assert.Equal(["probe.start"], visible);
    }
}