using System.Text.Json;
using CNC.Core.Services;

namespace SenderNG.Core.Tests;

public class JogConfigTests
{
    private static AppSettings NewSettings(string mode = "Mill")
    {
        var s = new AppSettings { Mode = mode };
        // Ensure both modes exist like SettingsService does.
        _ = s.Current;
        return s;
    }

    [Fact]
    public void Load_with_no_section_returns_defaults()
    {
        var config = JogConfigStore.Load(NewSettings());

        Assert.Equal([0.1, 1.0, 10.0, 50.0], config.Distances);
        Assert.Equal([500.0, 1000.0, 2000.0, 3000.0], config.Speeds);
        Assert.Equal(1, config.DistanceIndex);
        Assert.Equal(1, config.SpeedIndex);
    }

    [Fact]
    public void Save_then_load_roundtrips_per_mode()
    {
        var settings = NewSettings("Mill");
        JogConfigStore.Save(settings, new JogConfig
        {
            Distances = [0.01, 0.5, 5, 25],
            Speeds = [100, 200, 300, 400],
            DistanceIndex = 2,
            SpeedIndex = 3,
        });

        settings.Mode = AppSettings.FoamMode;
        JogConfigStore.Save(settings, new JogConfig
        {
            Distances = [1, 2, 4, 8],
            Speeds = [6000, 7000, 8000, 9000],
            DistanceIndex = 0,
            SpeedIndex = 1,
        });

        var mill = new AppSettings { Mode = AppSettings.MillMode, Modules = settings.Modules };
        Assert.Equal([0.01, 0.5, 5, 25], JogConfigStore.Load(mill).Distances);
        Assert.Equal(2, JogConfigStore.Load(mill).DistanceIndex);

        var foam = new AppSettings { Mode = AppSettings.FoamMode, Modules = settings.Modules };
        Assert.Equal([1, 2, 4, 8], JogConfigStore.Load(foam).Distances);
        Assert.Equal([6000, 7000, 8000, 9000], JogConfigStore.Load(foam).Speeds);
        Assert.Equal(1, JogConfigStore.Load(foam).SpeedIndex);
    }

    [Fact]
    public void Handles_json_element_shaped_section()
    {
        // Simulate a settings.json roundtrip: in-memory dict -> JSON -> JsonElement.
        var section = new Dictionary<string, object>
        {
            ["Mill"] = new Dictionary<string, object>
            {
                ["Distances"] = new List<double> { 0.2, 2, 20, 60 },
                ["Speeds"] = new List<double> { 550, 1100, 2200, 3300 },
                ["DistanceIndex"] = 3,
                ["SpeedIndex"] = 2,
            },
        };

        var json = JsonSerializer.Serialize(section);
        using var doc = JsonDocument.Parse(json);
        var element = doc.RootElement.Clone();

        var settings = NewSettings("Mill");
        settings.Modules["Jog"] = element;

        var config = JogConfigStore.Load(settings);

        Assert.Equal([0.2, 2, 20, 60], config.Distances);
        Assert.Equal([550, 1100, 2200, 3300], config.Speeds);
        Assert.Equal(3, config.DistanceIndex);
        Assert.Equal(2, config.SpeedIndex);
    }

    [Fact]
    public void Load_clamps_short_lists_and_indexes()
    {
        var settings = NewSettings("Mill");
        settings.Modules["Jog"] = new Dictionary<string, object>
        {
            ["Mill"] = new Dictionary<string, object>
            {
                ["Distances"] = new List<double> { 0.5, 1 },
                ["DistanceIndex"] = 99,
                ["SpeedIndex"] = -7,
            },
        };

        var config = JogConfigStore.Load(settings);

        Assert.Equal(4, config.Distances.Length);
        Assert.Equal([0.5, 1, 1, 1], config.Distances); // padded with last value
        Assert.Equal([500.0, 1000.0, 2000.0, 3000.0], config.Speeds); // defaults
        Assert.Equal(3, config.DistanceIndex);
        Assert.Equal(0, config.SpeedIndex); // -7 clamps to 0
    }
}

public class JogDiagonalsConfigTests
{
    [Fact]
    public void Diagonals_roundtrip_per_mode()
    {
        var settings = new AppSettings { Mode = "Mill" };
        _ = settings.Current;

        var mill = JogConfigStore.Load(settings);
        Assert.True(mill.Diagonals); // default on
        mill.Diagonals = false;
        JogConfigStore.Save(settings, mill);

        settings.Mode = AppSettings.FoamMode;
        Assert.True(JogConfigStore.Load(settings).Diagonals); // foam unaffected

        JogConfigStore.Save(settings, JogConfigStore.Load(settings));
        settings.Mode = AppSettings.MillMode;
        Assert.False(JogConfigStore.Load(settings).Diagonals);
    }
}
