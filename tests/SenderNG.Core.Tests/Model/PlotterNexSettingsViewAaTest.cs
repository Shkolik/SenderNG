/*
 * PlotterNexSettingsViewAaTest.cs - view toggles + anti-aliasing settings round-trip tests
 */

using SenderNG.Modules.Plotter.Nex.Settings;

namespace SenderNG.Core.Tests;

public class PlotterNexSettingsViewAaTest
{
    [Fact]
    public void View_and_AA_settings_round_trip()
    {
        var s = new PlotterNexSettings
        {
            ShowOrigin = false,
            ShowEnvelope = false,
            ShowRunState = false,
            ShowGrid = false,
            AaMode = 2,      // FXAA
            AaQuality = 3,   // Ultra
        };
        var json = System.Text.Json.JsonSerializer.Serialize(s);
        var back = System.Text.Json.JsonSerializer.Deserialize<PlotterNexSettings>(json)!;
        Assert.False(back.ShowOrigin);
        Assert.False(back.ShowEnvelope);
        Assert.False(back.ShowRunState);
        Assert.False(back.ShowGrid);
        Assert.Equal(2, back.AaMode);
        Assert.Equal(3, back.AaQuality);
    }
}