/*
 * HeightMapViewerSettingsTests.cs - headless tests for the viewer rendering settings
 */

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
using SenderNG.Modules.Common.Render;
using SenderNG.Modules.HeightMapViewer.Settings;

namespace SenderNG.Modules.HeightMapViewer.Tests;

public class HeightMapViewerSettingsTests
{
    [Fact]
    public void Load_defaults_when_section_absent()
    {
        var settings = HeightMapViewerConfig.Load(new MachineProfile());

        Assert.True(settings.Supersample);
        Assert.Equal(MsaaMode.None, settings.MsaaMode);
    }

    [Fact]
    public void Save_then_Load_round_trips()
    {
        var profile = new MachineProfile();
        HeightMapViewerConfig.Save(profile, new HeightMapViewerSettings
        {
            Supersample = false,
            MsaaMode = MsaaMode.X4,
        });

        var loaded = HeightMapViewerConfig.Load(profile);

        Assert.False(loaded.Supersample);
        Assert.Equal(MsaaMode.X4, loaded.MsaaMode);
    }

    [Fact]
    public void Load_clamps_an_out_of_range_msaa()
    {
        var profile = new MachineProfile();
        profile.ModuleSettings[HeightMapViewerConfig.SectionKey] =
            """{"Supersample":false,"MsaaMode":99}""";

        var loaded = HeightMapViewerConfig.Load(profile);

        Assert.False(loaded.Supersample);
        Assert.Equal(MsaaMode.None, loaded.MsaaMode);
    }

    [Fact]
    public void Load_falls_back_on_corrupt_json()
    {
        var profile = new MachineProfile();
        profile.ModuleSettings[HeightMapViewerConfig.SectionKey] = "{ not json";

        var loaded = HeightMapViewerConfig.Load(profile);

        Assert.True(loaded.Supersample);
        Assert.Equal(MsaaMode.None, loaded.MsaaMode);
    }

    [Fact]
    public void Settings_survive_a_profile_json_round_trip()
    {
        var dir = Path.Combine(Path.GetTempPath(), "hmv-settings-" + Guid.NewGuid().ToString("N"));
        try
        {
            var service = new SettingsService(dir);
            var profile = service.Load();
            HeightMapViewerConfig.Save(profile, new HeightMapViewerSettings
            {
                Supersample = false,
                MsaaMode = MsaaMode.X8,
            });
            service.Save(profile);

            var reloaded = new SettingsService(dir).Load();

            var loaded = HeightMapViewerConfig.Load(reloaded);
            Assert.False(loaded.Supersample);
            Assert.Equal(MsaaMode.X8, loaded.MsaaMode);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }
}
