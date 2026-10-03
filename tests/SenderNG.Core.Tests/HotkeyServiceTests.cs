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
using SenderNG.Core.Modules;
using SenderNG.Core.Services;

namespace SenderNG.Core.Tests;

public class HotkeyServiceTests
{
    private static SettingsService MakeSettings()
    {
        var dir = Path.Combine(Path.GetTempPath(), "senderng-hotkeys-" + Guid.NewGuid().ToString("N"));
        return new SettingsService(dir);
    }

    private static CommandHotkeyAction Global(string id, Action? down = null, Func<bool>? can = null, bool repeat = false)
        => new(id, id, null, down ?? (() => { }), can, repeatOnHold: repeat);

    private static CommandHotkeyAction Module(string id, string moduleId, Action? down = null)
        => new(id, id, null, down ?? (() => { }), scope: HotkeyScope.Module, moduleId: moduleId);

    [Fact]
    public void RegisterAction_first_id_wins_and_default_applies()
    {
        var service = new HotkeyService(MakeSettings());
        service.RegisterAction(Global("a"), "Ctrl+A");
        service.RegisterAction(Global("a"), "Ctrl+B");

        Assert.Single(service.Bindings);
        Assert.Equal("Ctrl+A", service.GestureFor("a"));
        Assert.Equal("Ctrl+A", service.DefaultGestureFor("a"));
    }

    [Fact]
    public void RegisterAction_unbound_default_is_null()
    {
        var service = new HotkeyService(MakeSettings());
        service.RegisterAction(Global("a"), null);
        Assert.Null(service.GestureFor("a"));
        Assert.Null(service.DefaultGestureFor("a"));
    }

    [Fact]
    public void SetGesture_persists_and_normalizes()
    {
        var settings = MakeSettings();
        var service = new HotkeyService(settings);
        service.RegisterAction(Global("a"), "Ctrl+A");
        service.SetGesture("a", "ctrl+shift+q");

        Assert.Equal("Ctrl+Shift+Q", service.GestureFor("a"));
        Assert.Equal("Ctrl+Shift+Q", settings.Global.Hotkeys["a"]);
    }

    [Fact]
    public void ClearGesture_writes_unbound_sentinel()
    {
        var settings = MakeSettings();
        var service = new HotkeyService(settings);
        service.RegisterAction(Global("a"), "Ctrl+A");
        service.ClearGesture("a");

        Assert.Null(service.GestureFor("a"));
        Assert.Equal(HotkeyMapStore.Unbound, settings.Global.Hotkeys["a"]);
    }

    [Fact]
    public void ResetOne_removes_entry_and_restores_default()
    {
        var settings = MakeSettings();
        var service = new HotkeyService(settings);
        service.RegisterAction(Global("a"), "Ctrl+A");
        service.SetGesture("a", "Ctrl+B");
        service.ResetOne("a");

        Assert.Equal("Ctrl+A", service.GestureFor("a"));
        Assert.False(settings.Global.Hotkeys.ContainsKey("a"));
    }

    [Fact]
    public void ResetAll_clears_map()
    {
        var settings = MakeSettings();
        var service = new HotkeyService(settings);
        service.RegisterAction(Global("a"), "Ctrl+A");
        service.SetGesture("a", "Ctrl+B");
        service.ResetAll();

        Assert.Equal("Ctrl+A", service.GestureFor("a"));
        Assert.Empty(settings.Global.Hotkeys);
    }

    [Fact]
    public void Persisted_values_survive_a_new_service_instance()
    {
        var settings = MakeSettings();
        var first = new HotkeyService(settings);
        first.RegisterAction(Global("a"), "Ctrl+A");
        first.SetGesture("a", "Ctrl+B");

        var second = new HotkeyService(settings);
        second.RegisterAction(Global("a"), "Ctrl+A");
        Assert.Equal("Ctrl+B", second.GestureFor("a"));
    }

    [Fact]
    public void FindConflict_is_scope_aware()
    {
        var service = new HotkeyService(MakeSettings());
        service.RegisterAction(Global("cycle.start"), "Alt+R");
        service.RegisterAction(Module("probe.start", "probing"), "Alt+R");

        Assert.Equal("probe.start", service.FindConflict("Alt+R", HotkeyScope.Module, "probing", "other"));
        Assert.Equal("cycle.start", service.FindConflict("Alt+R", HotkeyScope.Global, null, "other"));
        Assert.Null(service.FindConflict("Alt+R", HotkeyScope.Module, "probing", "probe.start"));
    }

    [Fact]
    public void Resolve_prefers_focused_module_over_global()
    {
        var service = new HotkeyService(MakeSettings());
        var global = Global("cycle.start");
        var probe = Module("probe.start", "probing");
        service.RegisterAction(global, "Alt+R");
        service.RegisterAction(probe, "Alt+R");

        Assert.Same(probe, service.Resolve("Alt+R", id => id == "probing"));
        Assert.Same(global, service.Resolve("Alt+R", _ => false));
        Assert.Null(service.Resolve("Ctrl+Q", _ => true));
    }

    [Fact]
    public void Changed_fires_on_mutations()
    {
        var service = new HotkeyService(MakeSettings());
        service.RegisterAction(Global("a"), "Ctrl+A");
        var fired = 0;
        service.Changed += (_, _) => fired++;

        service.SetGesture("a", "Ctrl+B");
        service.ClearGesture("a");
        service.ResetOne("a");
        service.ResetAll();
        Assert.Equal(4, fired);
    }
}
