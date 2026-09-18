
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
    [Fact]
    public void Load_with_no_section_returns_defaults()
    {
        var config = JogConfigStore.Load(new MachineProfile());

        Assert.Equal([0.1, 1.0, 10.0, -1], config.Distances);
        Assert.Equal([500.0, 1000.0, 2000.0], config.Speeds);
        Assert.Equal(1, config.DistanceIndex);
        Assert.Equal(1, config.SpeedIndex);
    }

    [Fact]
    public void Save_then_load_roundtrips_per_profile()
    {
        // Since the settings split each machine keeps its own Jog section: two
        // profiles with different step sets roundtrip independently.
        var mill = new MachineProfile();
        JogConfigStore.Save(mill, new JogConfig
        {
            Distances = [0.01, 0.5, 5],
            Speeds = [100, 200, 300],
            DistanceIndex = 2,
            SpeedIndex = 2,
        });

        var foam = new MachineProfile();
        JogConfigStore.Save(foam, new JogConfig
        {
            Distances = [1, 2, 4],
            Speeds = [6000, 7000, 8000],
            DistanceIndex = 0,
            SpeedIndex = 1,
        });

        Assert.Equal([0.01, 0.5, 5, -1], JogConfigStore.Load(mill).Distances);
        Assert.Equal(2, JogConfigStore.Load(mill).DistanceIndex);

        Assert.Equal([1, 2, 4, -1], JogConfigStore.Load(foam).Distances);
        Assert.Equal([6000, 7000, 8000], JogConfigStore.Load(foam).Speeds);
        Assert.Equal(1, JogConfigStore.Load(foam).SpeedIndex);
    }

    [Fact]
    public void Handles_json_element_shaped_section()
    {
        // Simulate a profile.json roundtrip: in-memory dict -> JSON -> JsonElement.
        var section = new Dictionary<string, object>
        {
            ["Distances"] = new List<double> { 0.2, 2, 20 },
            ["Speeds"] = new List<double> { 550, 1100, 2200 },
            ["DistanceIndex"] = 2,
            ["SpeedIndex"] = 2,
        };

        var json = JsonSerializer.Serialize(section);
        using var doc = JsonDocument.Parse(json);
        var element = doc.RootElement.Clone();

        var settings = new MachineProfile();
        settings.ModuleSettings["Jog"] = element;

        var config = JogConfigStore.Load(settings);

        Assert.Equal([0.2, 2, 20, -1], config.Distances);
        Assert.Equal([550, 1100, 2200], config.Speeds);
        Assert.Equal(2, config.DistanceIndex);
        Assert.Equal(2, config.SpeedIndex);
    }

    [Fact]
    public void Load_clamps_short_lists_and_indexes()
    {
        var settings = new MachineProfile();
        settings.ModuleSettings["Jog"] = new Dictionary<string, object>
        {
            ["Distances"] = new List<double> { 0.5, 1 },
            ["DistanceIndex"] = 99,
            ["SpeedIndex"] = -7,
        };

        var config = JogConfigStore.Load(settings);

        Assert.Equal(4, config.Distances.Length);
        Assert.Equal([0.5, 1, 1, -1], config.Distances); // padded with last value, sentinel forced
        Assert.Equal([500.0, 1000.0, 2000.0], config.Speeds); // defaults
        Assert.Equal(3, config.DistanceIndex); // 99 clamps to max 4-slot index
        Assert.Equal(0, config.SpeedIndex); // -7 clamps to 0
    }

    [Fact]
    public void Legacy_three_slot_profile_migrates_with_sentinel_in_slot4()
    {
        // A pre-continuous-jog profile stores only 3 distance slots; loading pads
        // slot 4 with the Continuous sentinel and keeps the legacy index valid
        // within the new 0..3 range.
        var section = new Dictionary<string, object>
        {
            ["Distances"] = new List<double> { 0.1, 1, 10 },
            ["Speeds"] = new List<double> { 500, 1000, 2000 },
            ["DistanceIndex"] = 2,
            ["SpeedIndex"] = 1,
        };

        var json = JsonSerializer.Serialize(section);
        using var doc = JsonDocument.Parse(json);
        var element = doc.RootElement.Clone();

        var settings = new MachineProfile();
        settings.ModuleSettings["Jog"] = element;

        var config = JogConfigStore.Load(settings);

        Assert.Equal([0.1, 1, 10, -1], config.Distances);
        Assert.Equal(2, config.DistanceIndex); // clamped into 0..3
        Assert.Equal(1, config.SpeedIndex);
    }

    [Fact]
    public void Save_then_load_preserves_continuous_sentinel()
    {
        var settings = JogViewModelTests.MakeSettings();
        var first = new JogViewModel(new JogViewModelTests.RecordingGrbl([]), settings);
        first.IsDistance3 = true; // select the Continuous slot
        first.Save();

        var second = new JogViewModel(new JogViewModelTests.RecordingGrbl([]), settings);

        Assert.Equal(3, second.SelectedDistanceIndex);
        Assert.Equal(-1.0, second.SelectedDistance);
        Assert.True(second.IsContinuousStep);

        var stored = JogConfigStore.Load(settings.Load());
        Assert.Equal([0.1, 1.0, 10.0, -1], stored.Distances);
        Assert.Equal(3, stored.DistanceIndex);
    }
}

public class JogDiagonalsConfigTests
{
    [Fact]
    public void Diagonals_roundtrip_per_profile()
    {
        var mill = new MachineProfile();
        var millConfig = JogConfigStore.Load(mill);
        Assert.True(millConfig.Diagonals); // default on
        millConfig.Diagonals = false;
        JogConfigStore.Save(mill, millConfig);

        // A different machine's profile is unaffected.
        var foam = new MachineProfile();
        Assert.True(JogConfigStore.Load(foam).Diagonals);

        JogConfigStore.Save(foam, JogConfigStore.Load(foam));
        Assert.False(JogConfigStore.Load(mill).Diagonals);
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
