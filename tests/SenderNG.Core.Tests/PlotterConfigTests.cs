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

/// <summary>The <c>plotter</c> section after the world-mapping deletion: four
/// view toggles and a JSON bag that must KEEP TOLERATING a section written by the
/// previous model. The plane-geometry cases that used to live here
/// (identity/mirror/origin round trips, plane-count mismatch, malformed mapping
/// recovery) encode a model that no longer exists.</summary>
public class PlotterConfigTests
{
    private static MachineProfile NewProfile() => new()
    {
        Id = "p1",
        Name = "P1",
        MachineTypeId = MachineTypes.Mill,
    };

    [Fact]
    public void An_absent_section_yields_all_toggles_on()
    {
        var s = PlotterConfig.Load(NewProfile());
        Assert.True(s.ShowGrid);
        Assert.True(s.ShowOrigin);
        Assert.True(s.ShowEnvelope);
        Assert.True(s.ShowRunState);
    }

    [Fact]
    public void A_saved_section_round_trips()
    {
        var profile = NewProfile();
        var saved = PlotterConfig.Defaults();
        saved.ShowGrid = false;
        saved.ShowOrigin = false;
        saved.ShowEnvelope = false;
        saved.ShowRunState = false;

        PlotterConfig.Save(profile, saved);
        var loaded = PlotterConfig.Load(profile);

        Assert.False(loaded.ShowGrid);
        Assert.False(loaded.ShowOrigin);
        Assert.False(loaded.ShowEnvelope);
        Assert.False(loaded.ShowRunState);
    }

    /// <summary>A section saved BEFORE the model change carries a
    /// <c>Planes</c> array of world mappings. <see cref="JsonSerializer"/> ignores
    /// members the type no longer declares, so the section still reads back with
    /// its toggles — verified here rather than assumed, because the opposite
    /// (a throw) would turn every existing profile's plotter settings into
    /// defaults.</summary>
    [Fact]
    public void A_stale_section_carrying_planes_is_ignored_not_fatal()
    {
        var profile = NewProfile();
        profile.ModuleSettings[PlotterConfig.SectionKey] =
            """{"ShowGrid":false,"Planes":[{"WorldAxes":[1,0],"Origin":[-50,0,0]}]}""";

        var loaded = PlotterConfig.Load(profile);

        Assert.False(loaded.ShowGrid);
        Assert.True(loaded.ShowOrigin);
    }

    /// <summary>The same tolerance the other way: a save of the new model must
    /// not carry a <c>Planes</c> member forward, so a profile's section shrinks
    /// to the toggles the first time anything is written.</summary>
    [Fact]
    public void A_saved_section_carries_no_plane_member()
    {
        var profile = NewProfile();
        PlotterConfig.Save(profile, PlotterConfig.Defaults());

        var json = (string)profile.ModuleSettings[PlotterConfig.SectionKey]!;
        Assert.DoesNotContain("Planes", json, StringComparison.Ordinal);
        Assert.DoesNotContain("WorldAxes", json, StringComparison.Ordinal);
    }

    [Fact]
    public void A_corrupt_section_falls_back_to_defaults()
    {
        var profile = NewProfile();
        profile.ModuleSettings[PlotterConfig.SectionKey] = "{not json";
        var s = PlotterConfig.Load(profile);
        Assert.True(s.ShowGrid);
        Assert.True(s.ShowRunState);
    }

    [Fact]
    public void A_wrong_value_type_falls_back_to_defaults()
    {
        var profile = NewProfile();
        profile.ModuleSettings[PlotterConfig.SectionKey] = """{"ShowGrid":"yes"}""";
        var s = PlotterConfig.Load(profile);
        Assert.True(s.ShowGrid);
    }

    [Fact]
    public void A_null_section_value_falls_back_to_defaults()
    {
        var profile = NewProfile();
        profile.ModuleSettings[PlotterConfig.SectionKey] = null!;
        var s = PlotterConfig.Load(profile);
        Assert.True(s.ShowGrid);
    }

    /// <summary>The bag may hold the section as a plain string (the settings VM's
    /// shape) or as a <see cref="JsonElement"/> wrapping that string (a
    /// hand-migrated bag); both must read, because a loader that handled only one
    /// would silently reset the profiles holding the other.</summary>
    [Fact]
    public void The_section_may_be_a_json_string_directly_or_inside_an_element()
    {
        var profile = NewProfile();
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize("""{"ShowRunState":false}"""));
        profile.ModuleSettings[PlotterConfig.SectionKey] = doc.RootElement.Clone();

        Assert.False(PlotterConfig.Load(profile).ShowRunState);

        profile.ModuleSettings[PlotterConfig.SectionKey] = """{"ShowEnvelope":false}""";
        Assert.False(PlotterConfig.Load(profile).ShowEnvelope);
    }

    [Fact]
    public void The_section_key_is_plotter()
    {
        Assert.Equal("plotter", PlotterConfig.SectionKey);
    }
}
