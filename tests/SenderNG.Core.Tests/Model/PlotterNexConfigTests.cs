/*
 * PlotterNexConfigTests.cs - per-profile Plot 3D world-mapping settings section tests
 */

using CNC.Core.Services;
using SenderNG.Modules.Plotter.Nex.Settings;

namespace SenderNG.Core.Tests;

public class PlotterNexConfigTests
{
    private static MachineProfile Profile(string typeId)
    {
        var profile = new MachineProfile { MachineTypeId = typeId };
        return profile;
    }

    [Fact]
    public void Missing_section_yields_mill_identity_defaults()
    {
        var s = PlotterNexConfig.Load(Profile(MachineTypes.Mill), MachineTypeCatalog.Resolve(MachineTypes.Mill));

        Assert.Equal(100, s.WireLength);
        var plane = Assert.Single(s.Planes);
        Assert.Equal(new[] { 0, 1, 2 }, plane.WorldAxes);
        Assert.Equal(new[] { 0.0, 0, 0 }, plane.Origin);
    }

    [Fact]
    public void Missing_section_yields_foam_centered_defaults()
    {
        var s = PlotterNexConfig.Load(Profile(MachineTypes.Foam), MachineTypeCatalog.Resolve(MachineTypes.Foam));

        Assert.Equal(100, s.WireLength);
        Assert.Equal(2, s.Planes.Count);
        Assert.Equal(new[] { 1, 2 }, s.Planes[0].WorldAxes);
        Assert.Equal(new[] { 1, 2 }, s.Planes[1].WorldAxes);
        Assert.Equal(new[] { -50.0, 0, 0 }, s.Planes[0].Origin);
        Assert.Equal(new[] { 50.0, 0, 0 }, s.Planes[1].Origin);
    }

    [Fact]
    public void Save_roundtrips_through_the_profile_section()
    {
        var profile = Profile(MachineTypes.Foam);
        var settings = new PlotterNexSettings
        {
            WireLength = 200,
            Planes =
            [
                new NexPlaneGeometry { WorldAxes = [1, 2], Origin = [-100, 0, 0] },
                new NexPlaneGeometry { WorldAxes = [1, 2], Origin = [100, 0, 0] },
            ],
        };
        PlotterNexConfig.Save(profile, settings);

        var loaded = PlotterNexConfig.Load(profile, MachineTypeCatalog.Resolve(MachineTypes.Foam));
        Assert.Equal(200, loaded.WireLength);
        Assert.Equal(new[] { -100.0, 0, 0 }, loaded.Planes[0].Origin);
        Assert.Equal(new[] { 100.0, 0, 0 }, loaded.Planes[1].Origin);
    }

    [Fact]
    public void Corrupt_section_yields_defaults_not_an_exception()
    {
        var profile = Profile(MachineTypes.Mill);
        profile.ModuleSettings[PlotterNexConfig.SectionKey] = "{ not json";

        var s = PlotterNexConfig.Load(profile, MachineTypeCatalog.Resolve(MachineTypes.Mill));
        Assert.Equal(new[] { 0, 1, 2 }, s.Planes[0].WorldAxes);
    }

    [Fact]
    public void Plane_count_mismatch_yields_defaults()
    {
        var profile = Profile(MachineTypes.Foam);
        profile.ModuleSettings[PlotterNexConfig.SectionKey] =
            System.Text.Json.JsonSerializer.Serialize(new PlotterNexSettings { Planes = [new NexPlaneGeometry()] });

        var s = PlotterNexConfig.Load(profile, MachineTypeCatalog.Resolve(MachineTypes.Foam));
        Assert.Equal(2, s.Planes.Count);
    }

    [Fact]
    public void Recenter_on_wire_symmetrizes_the_origins()
    {
        var s = new PlotterNexSettings
        {
            WireLength = 200,
            Planes =
            [
                new NexPlaneGeometry { WorldAxes = [1, 2], Origin = [0, 0, 0] },
                new NexPlaneGeometry { WorldAxes = [1, 2], Origin = [0, 0, 0] },
            ],
        };

        PlotterNexConfig.RecenterOnWire(s);

        Assert.Equal(-100.0, s.Planes[0].Origin[0]);
        Assert.Equal(100.0, s.Planes[1].Origin[0]);
    }

    [Fact]
    public void Null_planes_array_yields_defaults_not_an_exception()
    {
        var profile = Profile(MachineTypes.Foam);
        profile.ModuleSettings[PlotterNexConfig.SectionKey] =
            System.Text.Json.JsonSerializer.Serialize(new PlotterNexSettings { Planes = null! });

        var s = PlotterNexConfig.Load(profile, MachineTypeCatalog.Resolve(MachineTypes.Foam));
        Assert.Equal(2, s.Planes.Count);
    }

    [Fact]
    public void Null_plane_element_yields_defaults_not_an_exception()
    {
        var profile = Profile(MachineTypes.Mill);
        profile.ModuleSettings[PlotterNexConfig.SectionKey] =
            System.Text.Json.JsonSerializer.Serialize(new PlotterNexSettings { Planes = [null!] });

        var s = PlotterNexConfig.Load(profile, MachineTypeCatalog.Resolve(MachineTypes.Mill));
        Assert.Equal(new[] { 0, 1, 2 }, s.Planes[0].WorldAxes);
    }

    [Fact]
    public void Recenter_pins_one_shared_wire_axis_when_planes_leave_different_axes_unmapped()
    {
        var s = new PlotterNexSettings
        {
            WireLength = 200,
            Planes =
            [
                new NexPlaneGeometry { WorldAxes = [2, 0], Origin = [0, 0, 0] }, // leaves Y unmapped
                new NexPlaneGeometry { WorldAxes = [1, 2], Origin = [0, 0, 0] }, // leaves X unmapped
            ],
        };

        PlotterNexConfig.RecenterOnWire(s);

        Assert.Equal(-100.0, s.Planes[0].Origin[0]);
        Assert.Equal(100.0, s.Planes[1].Origin[0]);
    }
}