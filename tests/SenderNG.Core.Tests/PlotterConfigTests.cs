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
using SenderNG.Modules.Plotter.Settings;

namespace SenderNG.Core.Tests;

public class PlotterConfigTests
{
    private static MachineProfile NewProfile() => new()
    {
        Id = "p1",
        Name = "P1",
        MachineTypeId = MachineTypes.Mill,
    };

    [Fact]
    public void An_absent_section_yields_all_toggles_on_and_identity_planes()
    {
        var s = PlotterConfig.Load(NewProfile(), MachineTypeCatalog.Resolve(MachineTypes.Mill));
        Assert.True(s.ShowGrid);
        Assert.True(s.ShowOrigin);
        Assert.True(s.ShowEnvelope);
        Assert.True(s.ShowRunState);
        Assert.Single(s.Planes);
        Assert.Equal([0, 1, 2], s.Planes[0].WorldAxes);
    }

    [Fact]
    public void Foam_defaults_map_plane_one_Z_A_onto_world_X_Y()
    {
        var s = PlotterConfig.Defaults(MachineTypeCatalog.Resolve(MachineTypes.Foam));
        Assert.Equal(2, s.Planes.Count);
        Assert.Equal([0, 1], s.Planes[0].WorldAxes);
        Assert.Equal([0, 1], s.Planes[1].WorldAxes);
    }

    [Fact]
    public void A_saved_section_round_trips()
    {
        var profile = NewProfile();
        var saved = PlotterConfig.Defaults(MachineTypeCatalog.Resolve(MachineTypes.Mill));
        saved.ShowGrid = false;
        saved.ShowOrigin = false;
        saved.ShowEnvelope = false;
        saved.ShowRunState = false;
        saved.Planes[0] = new PlotterPlaneGeometry { WorldAxes = [1, 2, 0], Origin = [1, 2, 3] };

        PlotterConfig.Save(profile, saved);
        var loaded = PlotterConfig.Load(profile, MachineTypeCatalog.Resolve(MachineTypes.Mill));

        Assert.False(loaded.ShowGrid);
        Assert.False(loaded.ShowOrigin);
        Assert.False(loaded.ShowEnvelope);
        Assert.False(loaded.ShowRunState);
        Assert.Equal([1, 2, 0], loaded.Planes[0].WorldAxes);
        Assert.Equal([1, 2, 3], loaded.Planes[0].Origin);
    }

    [Fact]
    public void A_two_plane_section_round_trips_each_plane_independently()
    {
        var profile = new MachineProfile { Id = "p1", Name = "P1", MachineTypeId = MachineTypes.Foam };
        var saved = PlotterConfig.Defaults(MachineTypeCatalog.Resolve(MachineTypes.Foam));
        saved.Planes[0] = new PlotterPlaneGeometry { WorldAxes = [1, 0], Origin = [-50, 0, 0] };
        saved.Planes[1] = new PlotterPlaneGeometry { WorldAxes = [0, 1], Origin = [50, 25, 0] };

        PlotterConfig.Save(profile, saved);
        var loaded = PlotterConfig.Load(profile, MachineTypeCatalog.Resolve(MachineTypes.Foam));

        Assert.Equal(2, loaded.Planes.Count);
        Assert.Equal([1, 0], loaded.Planes[0].WorldAxes);
        Assert.Equal([-50d, 0, 0], loaded.Planes[0].Origin);
        Assert.Equal([0, 1], loaded.Planes[1].WorldAxes);
        Assert.Equal([50d, 25, 0], loaded.Planes[1].Origin);
    }

    [Fact]
    public void A_corrupt_section_falls_back_to_defaults()
    {
        var profile = NewProfile();
        profile.ModuleSettings[PlotterConfig.SectionKey] = "{not json";
        var s = PlotterConfig.Load(profile, MachineTypeCatalog.Resolve(MachineTypes.Mill));
        Assert.True(s.ShowGrid);
        Assert.Equal([0, 1, 2], s.Planes[0].WorldAxes);
    }

    [Fact]
    public void A_wrong_value_type_falls_back_to_defaults()
    {
        var profile = NewProfile();
        profile.ModuleSettings[PlotterConfig.SectionKey] = """{"ShowGrid":"yes"}""";
        var s = PlotterConfig.Load(profile, MachineTypeCatalog.Resolve(MachineTypes.Mill));
        Assert.True(s.ShowGrid);
        Assert.Equal([0, 1, 2], s.Planes[0].WorldAxes);
    }

    [Fact]
    public void A_plane_count_mismatch_falls_back_to_defaults()
    {
        var profile = new MachineProfile { Id = "p1", Name = "P1", MachineTypeId = MachineTypes.Foam };
        profile.ModuleSettings[PlotterConfig.SectionKey] =   // one plane on a foam
            JsonSerializer.Serialize(new PlotterSettings { Planes = [new PlotterPlaneGeometry()] });

        var s = PlotterConfig.Load(profile, MachineTypeCatalog.Resolve(MachineTypes.Foam));
        Assert.Equal(2, s.Planes.Count);
    }

    [Fact]
    public void A_malformed_plane_mapping_is_replaced_with_identity()
    {
        var profile = NewProfile();
        profile.ModuleSettings[PlotterConfig.SectionKey] =
            JsonSerializer.Serialize(new PlotterSettings
            {
                Planes = [new PlotterPlaneGeometry { WorldAxes = [0, 0, 0] }],
            });

        var loaded = PlotterConfig.Load(profile, MachineTypeCatalog.Resolve(MachineTypes.Mill));
        Assert.Equal([0, 1, 2], loaded.Planes[0].WorldAxes);
    }

    [Fact]
    public void A_malformed_origin_is_replaced_with_zeros()
    {
        var profile = NewProfile();
        profile.ModuleSettings[PlotterConfig.SectionKey] =
            JsonSerializer.Serialize(new PlotterSettings
            {
                Planes = [new PlotterPlaneGeometry { WorldAxes = [0, 1, 2], Origin = [5] }],
            });

        var loaded = PlotterConfig.Load(profile, MachineTypeCatalog.Resolve(MachineTypes.Mill));
        Assert.Equal([0d, 0d, 0d], loaded.Planes[0].Origin);
    }

    [Fact]
    public void A_malformed_second_plane_leaves_the_first_plane_untouched()
    {
        var profile = new MachineProfile { Id = "p1", Name = "P1", MachineTypeId = MachineTypes.Foam };
        profile.ModuleSettings[PlotterConfig.SectionKey] =
            JsonSerializer.Serialize(new PlotterSettings
            {
                Planes =
                [
                    new PlotterPlaneGeometry { WorldAxes = [1, 0], Origin = [-50, 0, 0] },
                    new PlotterPlaneGeometry { WorldAxes = [0, 0], Origin = [0, 0, 0] },
                ],
            });

        var loaded = PlotterConfig.Load(profile, MachineTypeCatalog.Resolve(MachineTypes.Foam));
        Assert.Equal([1, 0], loaded.Planes[0].WorldAxes);
        Assert.Equal([-50d, 0, 0], loaded.Planes[0].Origin);
        Assert.Equal([0, 1], loaded.Planes[1].WorldAxes);
    }

    [Fact]
    public void A_null_planes_array_falls_back_to_defaults()
    {
        var profile = new MachineProfile { Id = "p1", Name = "P1", MachineTypeId = MachineTypes.Foam };
        profile.ModuleSettings[PlotterConfig.SectionKey] =
            JsonSerializer.Serialize(new PlotterSettings { Planes = null! });

        var s = PlotterConfig.Load(profile, MachineTypeCatalog.Resolve(MachineTypes.Foam));
        Assert.Equal(2, s.Planes.Count);
    }

    [Fact]
    public void A_null_plane_element_falls_back_to_defaults()
    {
        var profile = NewProfile();
        profile.ModuleSettings[PlotterConfig.SectionKey] =
            JsonSerializer.Serialize(new PlotterSettings { Planes = [null!] });

        var loaded = PlotterConfig.Load(profile, MachineTypeCatalog.Resolve(MachineTypes.Mill));
        Assert.Equal([0, 1, 2], loaded.Planes[0].WorldAxes);
    }

    [Fact]
    public void The_section_key_is_plotter()
    {
        Assert.Equal("plotter", PlotterConfig.SectionKey);
    }
}
