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