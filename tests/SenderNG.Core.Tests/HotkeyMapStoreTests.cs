using CNC.Core.Services;
using SenderNG.Core.Services;

namespace SenderNG.Core.Tests;

public class HotkeyMapStoreTests
{
    [Fact]
    public void Read_absent_entry_returns_default()
    {
        var global = new GlobalSettings();
        Assert.Equal("Ctrl+S", HotkeyMapStore.Read(global, "x", "Ctrl+S", out var mutated));
        Assert.False(mutated);
    }

    [Theory]
    [InlineData("(none)")]
    [InlineData("")]
    [InlineData("   ")]
    public void Read_unbound_sentinels_return_null(string stored)
    {
        var global = new GlobalSettings { Hotkeys = { ["x"] = stored } };
        Assert.Null(HotkeyMapStore.Read(global, "x", "Ctrl+S", out _));
    }

    [Fact]
    public void Read_corrupt_entry_drops_to_default_and_reports_mutation()
    {
        var global = new GlobalSettings { Hotkeys = { ["x"] = "Ctrl+Nope" } };
        Assert.Equal("Ctrl+S", HotkeyMapStore.Read(global, "x", "Ctrl+S", out var mutated));
        Assert.True(mutated);
        Assert.False(global.Hotkeys.ContainsKey("x"));
    }

    [Fact]
    public void Write_Remove_Clear_round_trip()
    {
        var global = new GlobalSettings();
        HotkeyMapStore.Write(global, "x", null);
        Assert.Equal(HotkeyMapStore.Unbound, global.Hotkeys["x"]);
        HotkeyMapStore.Write(global, "x", "Ctrl+S");
        Assert.Equal("Ctrl+S", global.Hotkeys["x"]);
        HotkeyMapStore.Remove(global, "x");
        Assert.False(global.Hotkeys.ContainsKey("x"));
        global.Hotkeys["y"] = "Ctrl+Y";
        HotkeyMapStore.Clear(global);
        Assert.Empty(global.Hotkeys);
    }
}