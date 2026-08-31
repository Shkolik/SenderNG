
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

using System.Text.Json;
using CNC.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using SenderNG.Modules.Plugins;

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

        Assert.Equal([0.1, 1.0, 10.0], config.Distances);
        Assert.Equal([500.0, 1000.0, 2000.0], config.Speeds);
        Assert.Equal(1, config.DistanceIndex);
        Assert.Equal(1, config.SpeedIndex);
    }

    [Fact]
    public void Save_then_load_roundtrips_per_mode()
    {
        var settings = NewSettings("Mill");
        JogConfigStore.Save(settings, new JogConfig
        {
            Distances = [0.01, 0.5, 5],
            Speeds = [100, 200, 300],
            DistanceIndex = 2,
            SpeedIndex = 2,
        });

        settings.Mode = AppSettings.FoamMode;
        JogConfigStore.Save(settings, new JogConfig
        {
            Distances = [1, 2, 4],
            Speeds = [6000, 7000, 8000],
            DistanceIndex = 0,
            SpeedIndex = 1,
        });

        var mill = new AppSettings { Mode = AppSettings.MillMode, Modules = settings.Modules };
        Assert.Equal([0.01, 0.5, 5], JogConfigStore.Load(mill).Distances);
        Assert.Equal(2, JogConfigStore.Load(mill).DistanceIndex);

        var foam = new AppSettings { Mode = AppSettings.FoamMode, Modules = settings.Modules };
        Assert.Equal([1, 2, 4], JogConfigStore.Load(foam).Distances);
        Assert.Equal([6000, 7000, 8000], JogConfigStore.Load(foam).Speeds);
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
                ["Distances"] = new List<double> { 0.2, 2, 20 },
                ["Speeds"] = new List<double> { 550, 1100, 2200 },
                ["DistanceIndex"] = 2,
                ["SpeedIndex"] = 2,
            },
        };

        var json = JsonSerializer.Serialize(section);
        using var doc = JsonDocument.Parse(json);
        var element = doc.RootElement.Clone();

        var settings = NewSettings("Mill");
        settings.Modules["Jog"] = element;

        var config = JogConfigStore.Load(settings);

        Assert.Equal([0.2, 2, 20], config.Distances);
        Assert.Equal([550, 1100, 2200], config.Speeds);
        Assert.Equal(2, config.DistanceIndex);
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

        Assert.Equal(3, config.Distances.Length);
        Assert.Equal([0.5, 1, 1], config.Distances); // padded with last value
        Assert.Equal([500.0, 1000.0, 2000.0], config.Speeds); // defaults
        Assert.Equal(2, config.DistanceIndex);
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

public class JogModuleSettingsFieldsTests
{
    [Fact]
    public void Settings_view_available_after_attach_services()
    {
        var module = new SenderNG.Modules.Plugins.JogModule();
        Assert.Null(module.CreateSettingsView()); // not attached yet

        var dir = Path.Combine(Path.GetTempPath(), "senderng-tests-" + Guid.NewGuid().ToString("N"));
        try
        {
            var settingsService = new SettingsService(dir);
            var services = new ServiceCollection()
                .AddSingleton(settingsService)
                .AddSingleton(settingsService.Load())
                .AddSingleton<CNC.Core.Abstractions.IGrblService>(new JogViewModelTests.RecordingGrbl([]))
                .AddSingleton<JogViewModel>()
                .BuildServiceProvider();
            module.AttachServices(services);

            Assert.NotNull(module.CreateSettingsView());
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }
}
