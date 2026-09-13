using System.Numerics;
using HelixToolkit.Nex.Geometries;
using HelixToolkit.Nex.Graphics;
using HelixToolkit.Nex.Maths;
using SenderNG.Modules.Plotter.Nex.Rendering;
using SenderNG.Modules.Plotter.Nex.Scene;

namespace SenderNG.Core.Tests;

public class NexCarriagePlaneLayerTests
{
    [Fact]
    public void RapidColor_is_the_bright_rapid_red()
    {
        var c = NexCarriagePlaneLayer.RapidColor;
        Assert.Equal(0.9f, c.Red);
        Assert.Equal(0.12f, c.Green);
        Assert.Equal(0.12f, c.Blue);
        Assert.Equal(1f, c.Alpha);
    }

    [Fact]
    public void Executed_overlay_thickness_is_1_5x_the_base()
    {
        Assert.Equal(2.4f, NexCarriagePlaneLayer.ExecutedLineThickness);
        Assert.Equal(1.6f, NexCarriagePlaneLayer.BaseLineThickness);
        Assert.Equal(1.5f, NexCarriagePlaneLayer.ExecutedLineThickness / NexCarriagePlaneLayer.BaseLineThickness);
    }

    [Theory]
    [InlineData(RunState.Pending, false)]
    [InlineData(RunState.InFlight, false)]
    [InlineData(RunState.Error, false)]
    [InlineData(RunState.Executed, true)]
    public void IsExecuted_maps_only_the_acked_state(RunState s, bool expected)
    {
        Assert.Equal(expected, NexCarriagePlaneLayer.IsExecuted(s));
    }

    [Fact]
    public void BuildStatic_copies_staging_into_fresh_static_geometry_with_identity_indices()
    {
        var staging = new Geometry(Topology.Line, isDynamic: true);
        staging.Vertices.Add(new Vector4(0, 0, 0, 1));
        staging.Vertices.Add(new Vector4(1, 1, 1, 1));
        staging.VertexColors.Add(Vector4.UnitX);
        staging.VertexColors.Add(Vector4.UnitX);

        var geo = NexCarriagePlaneLayer.BuildStatic(staging);

        Assert.False(geo.IsDynamic);
        Assert.Equal(Topology.Line, geo.Topology);
        Assert.Equal(2, geo.Vertices.Count);
        Assert.Equal(2, geo.VertexColors.Count);
        Assert.Equal(2, geo.Indices.Count);
        Assert.Equal(0u, geo.Indices[0]);
        Assert.Equal(1u, geo.Indices[1]);
        // Staging left untouched; the copy is a fresh geometry.
        Assert.Equal(2, staging.Vertices.Count);
        Assert.Equal(2, staging.VertexColors.Count);
    }

    [Fact]
    public void BuildStatic_empty_staging_yields_empty_static_geometry()
    {
        var geo = NexCarriagePlaneLayer.BuildStatic(new Geometry(Topology.Line, isDynamic: true));

        Assert.False(geo.IsDynamic);
        Assert.Equal(Topology.Line, geo.Topology);
        Assert.Empty(geo.Vertices);
        Assert.Empty(geo.VertexColors);
        Assert.Empty(geo.Indices);
    }

    [Fact]
    public void BuildStatic_survives_a_torn_long_color_tail()
    {
        var staging = new Geometry(Topology.Line, isDynamic: true);
        staging.Vertices.Add(new Vector4(0, 0, 0, 1));
        staging.Vertices.Add(new Vector4(10, 5, 0, 1));
        // A concurrent snapshot of a growing source leaves extra colors behind;
        // the build must not throw and must size from the vertex count.
        for (var i = 0; i < 4; i++)
            staging.VertexColors.Add(new Vector4(0.9f, 0.9f, 0.9f, 1f));

        var geo = NexCarriagePlaneLayer.BuildStatic(staging);

        Assert.Equal(2u, geo.IndexCount);
        Assert.True(geo.VertexColors.Count >= geo.Vertices.Count);
    }

    [Fact]
    public void BuildStatic_pads_a_torn_short_color_tail()
    {
        var staging = new Geometry(Topology.Line, isDynamic: true);
        staging.Vertices.Add(new Vector4(0, 0, 0, 1));
        staging.Vertices.Add(new Vector4(10, 5, 0, 1));
        staging.VertexColors.Add(new Vector4(0.9f, 0.9f, 0.9f, 1f));

        var geo = NexCarriagePlaneLayer.BuildStatic(staging);

        // The renderer indexes colors by vertex index — never run off the tail.
        Assert.Equal(2u, geo.IndexCount);
        Assert.Equal(geo.Vertices.Count, geo.VertexColors.Count);
    }

    [Fact]
    public void Clean_rebuild_gate_never_rebuilds_even_after_a_long_interval()
    {
        var t = DateTime.UtcNow;
        var gate = new RebuildGate();

        Assert.False(gate.RebuildDue(t.AddHours(2)));
        Assert.False(gate.RebuildDue(t.AddDays(1)));
    }

    [Fact]
    public void First_mutation_rebuilds_immediately()
    {
        var t = DateTime.UtcNow;
        var gate = new RebuildGate();

        gate.MarkDirty();

        Assert.True(gate.RebuildDue(t));
    }

    [Fact]
    public void Bursts_within_the_min_interval_coalesce()
    {
        var t = DateTime.UtcNow;
        var gate = new RebuildGate();

        gate.MarkDirty();
        Assert.True(gate.RebuildDue(t), "first mutation should rebuild immediately");

        gate.MarkDirty();
        Assert.False(
            gate.RebuildDue(t.AddMilliseconds(10)),
            "repeat mutation inside the interval should defer to the next grant");

        gate.MarkDirty();
        Assert.True(
            gate.RebuildDue(t.AddMilliseconds(150)),
            "the interval elapsed, so the dirty layer may rebuild again");
    }

    [Fact]
    public void Dirty_layer_stays_dirty_until_the_threshold_or_interval_grant()
    {
        var t = DateTime.UtcNow;
        var gate = new RebuildGate();

        gate.MarkDirty();
        Assert.True(gate.RebuildDue(t)); // grant 1 at t0

        gate.MarkDirty();
        Assert.False(gate.RebuildDue(t.AddMilliseconds(100)));

        // Still no rebuild granted inside the interval: no consumption happened.
        gate.MarkDirty();
        Assert.False(gate.RebuildDue(t.AddMilliseconds(149)));

        Assert.True(
            gate.RebuildDue(t.AddMilliseconds(150)),
            "the dirty layer rebuilds exactly once the interval elapses");
    }

    [Fact]
    public void Mutation_burst_forces_an_immediate_rebuild()
    {
        var t = DateTime.UtcNow;
        var gate = new RebuildGate();

        gate.MarkDirty();
        Assert.True(gate.RebuildDue(t)); // grant 1 at t0

        for (var i = 0; i < 999; i++)
            gate.MarkDirty();
        Assert.False(gate.RebuildDue(t.AddMilliseconds(10)), "999 pending mutations are below the burst threshold");

        gate.MarkDirty();
        Assert.True(
            gate.RebuildDue(t.AddMilliseconds(11)),
            "the burst threshold forces the rebuild even inside the interval");

        Assert.False(
            gate.RebuildDue(t.AddMilliseconds(12)),
            "the burst was consumed: clean layer stays quiet after the flush");
    }
}