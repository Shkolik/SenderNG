using System.Numerics;
using CNC.Core.Services;
using SenderNG.Modules.Plotter.Nex.Model;
using SenderNG.Modules.Plotter.Nex.Rendering;
using SenderNG.Modules.Plotter.Nex.Scene;
using SenderNG.Modules.Plotter.Nex.Settings;
using Xunit;

namespace SenderNG.Core.Tests.Rendering;

/// <summary>
/// Regression guard for the red-fan artifact: the engine repacks the shared
/// static index buffer (reassigning <c>Geometry.IndexOffset</c> for every
/// static geometry) whenever a static geometry is Added. If a layer re-registers
/// its static geometry during a run, the offsets baked into OTHER static draws
/// (cone, sheet) go stale and read garbage indices → the giant red fan. The app
/// fix is to give every churning layer ONE long-lived static geometry that is
/// only ever re-uploaded in place. These tests assert the contract directly:
/// once the build wave has pumped, run-state churn must NEVER replace the
/// registered static geometry behind any node.
/// </summary>
[Trait("Category", "GPU")]
public static class StaticGeometryStabilityTests
{
    [Fact]
    public static void Run_churn_never_swaps_static_geometries_after_the_build_wave()
    {
        using var scene = new NexPlotScene();
        var machine = MachineTypeCatalog.Resolve(MachineTypes.Foam);
        var config = PlotterNexConfig.Defaults(machine);
        var geo0 = config.Planes[0];
        var geo1 = config.Planes[1];
        var idx0 = machine.PlaneAxisIndices(0);
        var idx1 = machine.PlaneAxisIndices(1);

        var layer0 = (NexCarriagePlaneLayer)scene.AddCarriagePlane();
        var layer1 = (NexCarriagePlaneLayer)scene.AddCarriagePlane();

        // Small but real toolpath so both planes have content to color.
        var segments = new List<ToolpathSegment>
        {
            new(new double[] { 0, 0, 0, 0 }, new double[] { 10, 5, 10, 12 }, true, 2),
            new(new double[] { 10, 5, 10, 12 }, new double[] { 20, 8, 20, 24 }, false, 3),
            new(new double[] { 20, 8, 20, 24 }, new double[] { 30, 5, 30, 36 }, false, 4),
            new(new double[] { 30, 5, 30, 36 }, new double[] { 40, 7, 40, 42 }, false, 5),
            new(new double[] { 40, 7, 40, 42 }, new double[] { 50, 9, 50, 54 }, false, 6),
        };
        ToolpathSceneBuilder.PopulatePlane(layer0, segments, idx0, geo0);
        ToolpathSceneBuilder.PopulatePlane(layer1, segments, idx1, geo1);

        var sideA = new List<Vector3>();
        var sideB = new List<Vector3>();
        foreach (var seg in segments)
        {
            sideA.Add(PlaneWorldMapper.ToWorld(geo0, seg.From, idx0));
            sideB.Add(PlaneWorldMapper.ToWorld(geo1, seg.From, idx1));
        }
        var loft = (NexLoftSurface)scene.AddLoftSurface();
        loft.SetLoftSheet(sideA, sideB);

        var tool = scene.CreateTool(ToolShape.Cone);
        var p0 = PlaneWorldMapper.ToWorld(geo0, new double[] { 10, 5, 10, 12 }, idx0);
        var p1 = PlaneWorldMapper.ToWorld(geo1, new double[] { 10, 5, 10, 12 }, idx1);
        tool.UpdatePosition(new Endpoint(new[] { p0, p1 }));

        // Build wave: the run is populated but nothing has executed yet. This
        // is the moment the static pool is packed for the whole program.
        scene.PumpCarriagePlanes();
        scene.PumpToolWire();

        // Snapshot the registered static geometry identities the same way the
        // failed draws reference them: the draw stream caches the node geometry.
        var plane0Geo = layer0.Node.Geometry;
        var plane1Geo = layer1.Node.Geometry;
        Assert.NotNull(plane0Geo);
        Assert.NotNull(plane1Geo);

        // The executed overlay must ALSO be registered by the build-wave pump,
        // at its full segment budget (parked-degenerate until the first segment
        // runs). If it is instead registered lazily at the first executed
        // segment, that add fires during the run — the static pool repacks and
        // the sheet/cone draws keep stale FirstIndex → the one-frame spike +
        // mangled loft the user sees at the very first move after Start.
        var done0Geo = layer0.DoneNode.Geometry;
        var done1Geo = layer1.DoneNode.Geometry;
        Assert.NotNull(done0Geo);
        Assert.NotNull(done1Geo);

        // Run churn at app cadence: the executed frontier advances on both
        // planes every ~6 ticks while the per-poll wire/loop positions move.
        // Sleep past the RebuildGate's minimum interval so each round actually
        // reaches the pump (the app gets there naturally via GPU frame time).
        var runs = new[]
        {
            new double[] { 15, 6, 15, 18 },
            new double[] { 20, 8, 20, 24 },
            new double[] { 25, 7, 25, 30 },
            new double[] { 30, 5, 30, 36 },
        };
        for (var round = 0; round < runs.Length; round++)
        {
            Thread.Sleep(170);
            for (var e = 0; e <= round; e++)
            {
                layer0.SetSegmentRunState(e, RunState.Executed);
                layer1.SetSegmentRunState(e, RunState.Executed);
            }

            var q0 = PlaneWorldMapper.ToWorld(geo0, runs[round], idx0);
            var q1 = PlaneWorldMapper.ToWorld(geo1, runs[round], idx1);
            tool.UpdatePosition(new Endpoint(new[] { q0, q1 }));

            scene.PumpCarriagePlanes();
            scene.PumpToolWire();

            Assert.Same(plane0Geo, layer0.Node.Geometry);
            Assert.Same(plane1Geo, layer1.Node.Geometry);
            Assert.Same(done0Geo, layer0.DoneNode.Geometry);
            Assert.Same(done1Geo, layer1.DoneNode.Geometry);
        }
    }
}