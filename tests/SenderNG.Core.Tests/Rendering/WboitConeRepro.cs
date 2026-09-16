using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Compression;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using CNC.Core.Services;
using HelixToolkit.Nex.Engine;
using HelixToolkit.Nex.Engine.Cameras;
using HelixToolkit.Nex.Graphics;
using HelixToolkit.Nex.Maths;
using SenderNG.Modules.Plotter.Nex.Model;
using SenderNG.Modules.Plotter.Nex.Rendering;
using SenderNG.Modules.Plotter.Nex.Scene;
using SenderNG.Modules.Plotter.Nex.Settings;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Xunit;

namespace SenderNG.Core.Tests.Rendering;

public static class WboitConeRepro
{
    private const int W = 960, H = 540;

    [Fact]
    public static void Probe_queue_topology()
    {
        var scene = new NexPlotScene();
        PoolDiagnostics.LogQueueTopologyOnce(scene.Context);
        var probe = PoolDiagnostics.QueueTopology(scene.Context);
        scene.Dispose();
        Trace.WriteLine("[queue.probe] " + probe);
        Assert.Contains("graphicsFamily", probe);
    }

    [Fact]
    public static void Bisect_cone_only()
    {
        var outDir = Path.Combine(Path.GetTempPath(), "nex_repro", "cone_only");
        Directory.CreateDirectory(outDir);

        var scene = new NexPlotScene();
        var machine = MachineTypeCatalog.Resolve(MachineTypes.Foam);
        var config = PlotterNexConfig.Defaults(machine);
        var geo0 = config.Planes[0];
        var idx0 = machine.PlaneAxisIndices(0);

        var tool = scene.CreateTool(ToolShape.Cone);
        var pos = PlaneWorldMapper.ToWorld(geo0, new double[] { 15, 5, 15, 18 }, idx0);
        tool.UpdatePosition(new Endpoint(new[] { pos }));

        scene.SetViewportSize(W, H);
        scene.PumpToolWireAndLofts();

        RenderFrames(scene, outDir, 3);
        scene.Dispose();
        Assert.True(Directory.Exists(outDir));
    }

    [Fact]
    public static void Bisect_sheet_only()
    {
        var outDir = Path.Combine(Path.GetTempPath(), "nex_repro", "sheet_only");
        Directory.CreateDirectory(outDir);

        var scene = new NexPlotScene();
        var machine = MachineTypeCatalog.Resolve(MachineTypes.Foam);
        var config = PlotterNexConfig.Defaults(machine);
        var geo0 = config.Planes[0];
        var geo1 = config.Planes[1];
        var idx0 = machine.PlaneAxisIndices(0);
        var idx1 = machine.PlaneAxisIndices(1);

        var sideA = new List<Vector3>
        {
            PlaneWorldMapper.ToWorld(geo0, new double[] { 0, 0, 0, 0 }, idx0),
            PlaneWorldMapper.ToWorld(geo0, new double[] { 10, 5, 10, 12 }, idx0),
            PlaneWorldMapper.ToWorld(geo0, new double[] { 20, 8, 20, 24 }, idx0),
        };
        var sideB = new List<Vector3>
        {
            PlaneWorldMapper.ToWorld(geo1, new double[] { 0, 0, 0, 0 }, idx1),
            PlaneWorldMapper.ToWorld(geo1, new double[] { 10, 5, 10, 12 }, idx1),
            PlaneWorldMapper.ToWorld(geo1, new double[] { 20, 8, 20, 24 }, idx1),
        };

        var loft = scene.AddLoftSurface();
        loft.SetLoftSheet(sideA, sideB);

        scene.SetViewportSize(W, H);
        scene.PumpToolWireAndLofts();

        RenderFrames(scene, outDir, 3);
        scene.Dispose();
        Assert.True(Directory.Exists(outDir));
    }

    [Fact]
    public static void Bisect_cone_plus_sheet()
    {
        var outDir = Path.Combine(Path.GetTempPath(), "nex_repro", "cone_plus_sheet");
        Directory.CreateDirectory(outDir);

        var scene = new NexPlotScene();
        var machine = MachineTypeCatalog.Resolve(MachineTypes.Foam);
        var config = PlotterNexConfig.Defaults(machine);
        var geo0 = config.Planes[0];
        var geo1 = config.Planes[1];
        var idx0 = machine.PlaneAxisIndices(0);
        var idx1 = machine.PlaneAxisIndices(1);

        var sideA = new List<Vector3>
        {
            PlaneWorldMapper.ToWorld(geo0, new double[] { 0, 0, 0, 0 }, idx0),
            PlaneWorldMapper.ToWorld(geo0, new double[] { 10, 5, 10, 12 }, idx0),
            PlaneWorldMapper.ToWorld(geo0, new double[] { 20, 8, 20, 24 }, idx0),
        };
        var sideB = new List<Vector3>
        {
            PlaneWorldMapper.ToWorld(geo1, new double[] { 0, 0, 0, 0 }, idx1),
            PlaneWorldMapper.ToWorld(geo1, new double[] { 10, 5, 10, 12 }, idx1),
            PlaneWorldMapper.ToWorld(geo1, new double[] { 20, 8, 20, 24 }, idx1),
        };

        var loft = scene.AddLoftSurface();
        loft.SetLoftSheet(sideA, sideB);

        var tool = scene.CreateTool(ToolShape.Cone);
        var pos = PlaneWorldMapper.ToWorld(geo0, new double[] { 15, 5, 15, 18 }, idx0);
        tool.UpdatePosition(new Endpoint(new[] { pos }));

        scene.SetViewportSize(W, H);
        scene.PumpToolWireAndLofts();

        RenderFrames(scene, outDir, 3);
        scene.Dispose();
        Assert.True(Directory.Exists(outDir));
    }

    [Fact]
    public static void Bisect_full_scene()
    {
        var outDir = Path.Combine(Path.GetTempPath(), "nex_repro", "full");
        Directory.CreateDirectory(outDir);

        var scene = new NexPlotScene();
        var machine = MachineTypeCatalog.Resolve(MachineTypes.Foam);
        var config = PlotterNexConfig.Defaults(machine);
        var geo0 = config.Planes[0];
        var geo1 = config.Planes[1];
        var idx0 = machine.PlaneAxisIndices(0);
        var idx1 = machine.PlaneAxisIndices(1);

        var layer0 = scene.AddCarriagePlane();
        var layer1 = scene.AddCarriagePlane();

        var segments = new List<ToolpathSegment>
        {
            new(new double[] { 0, 0, 0, 0 }, new double[] { 10, 5, 10, 12 }, true, 2),
            new(new double[] { 10, 5, 10, 12 }, new double[] { 20, 8, 20, 24 }, false, 3),
            new(new double[] { 20, 8, 20, 24 }, new double[] { 30, 5, 30, 36 }, false, 4),
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
        var loft = scene.AddLoftSurface();
        loft.SetLoftSheet(sideA, sideB);

        var lastTo = segments[^1].To;
        var p0 = PlaneWorldMapper.ToWorld(geo0, lastTo, idx0);
        var p1 = PlaneWorldMapper.ToWorld(geo1, lastTo, idx1);
        var tool = scene.CreateTool(ToolShape.Cone);
        tool.UpdatePosition(new Endpoint(new[] { p0, p1 }));

        scene.SetViewportSize(W, H);
        scene.PumpCarriagePlanes();
        scene.PumpToolWireAndLofts();

        RenderFrames(scene, outDir, 3);
        scene.Dispose();
        Assert.True(Directory.Exists(outDir));
    }

    [Fact]
    public static void Bisect_dynamic_cone_rewrite_every_frame()
    {
        var outDir = Path.Combine(Path.GetTempPath(), "nex_repro", "dynamic");
        Directory.CreateDirectory(outDir);

        var scene = new NexPlotScene();
        var machine = MachineTypeCatalog.Resolve(MachineTypes.Foam);
        var config = PlotterNexConfig.Defaults(machine);
        var geo0 = config.Planes[0];
        var geo1 = config.Planes[1];
        var idx0 = machine.PlaneAxisIndices(0);
        var idx1 = machine.PlaneAxisIndices(1);

        var layer0 = scene.AddCarriagePlane();
        var layer1 = scene.AddCarriagePlane();

        var segments = new List<ToolpathSegment>
        {
            new(new double[] { 0, 0, 0, 0 }, new double[] { 10, 5, 10, 12 }, true, 2),
            new(new double[] { 10, 5, 10, 12 }, new double[] { 20, 8, 20, 24 }, false, 3),
            new(new double[] { 20, 8, 20, 24 }, new double[] { 30, 5, 30, 36 }, false, 4),
        };

        ToolpathSceneBuilder.PopulatePlane(layer0, segments, idx0, geo0);
        ToolpathSceneBuilder.PopulatePlane(layer1, segments, idx1, geo1);

        var loft = scene.AddLoftSurface();
        var sideA = new List<Vector3>();
        var sideB = new List<Vector3>();
        foreach (var seg in segments)
        {
            sideA.Add(PlaneWorldMapper.ToWorld(geo0, seg.From, idx0));
            sideB.Add(PlaneWorldMapper.ToWorld(geo1, seg.From, idx1));
        }
        loft.SetLoftSheet(sideA, sideB);

        var tool = scene.CreateTool(ToolShape.Cone);
        var pos = PlaneWorldMapper.ToWorld(geo0, new double[] { 15, 5, 15, 18 }, idx0);
        tool.UpdatePosition(new Endpoint(new[] { pos }));

        scene.SetViewportSize(W, H);
        scene.PumpCarriagePlanes();
        scene.PumpToolWireAndLofts();

        // Render frames; between renders, REWRITE the identical cone position the
        // way per-poll status updates do in the app (NotifyTransformChanged every tick).
        RenderFrames(scene, outDir, 8, () =>
        {
            tool.UpdatePosition(new Endpoint(new[] { pos }));
            scene.PumpToolWireAndLofts();
        });
        scene.Dispose();
        Assert.True(Directory.Exists(outDir));
    }

    [Fact]
    public static void Longrun_foam_scene_mirrors_app_ontick()
    {
        var outDir = Path.Combine(Path.GetTempPath(), "nex_repro", "longrun");
        Directory.CreateDirectory(outDir);

        var scene = new NexPlotScene();
        var machine = MachineTypeCatalog.Resolve(MachineTypes.Foam);
        var config = PlotterNexConfig.Defaults(machine);
        var geo0 = config.Planes[0];
        var geo1 = config.Planes[1];
        var idx0 = machine.PlaneAxisIndices(0);
        var idx1 = machine.PlaneAxisIndices(1);

        var layer0 = scene.AddCarriagePlane();
        var layer1 = scene.AddCarriagePlane();

        // Synthetic ~920-segment foam sheet path, sweeping both planes across the
        // machine like the user's real program (x -200..200, y 0..345).
        var segments = new List<ToolpathSegment>();
        for (var i = 0; i < 920; i++)
        {
            var t = i / 919.0;
            var from = new[]
            {
                -200 + 400 * (t - 0.5 * Math.Sin(t * 40)),
                5 + 340 * t,
                -200 + 400 * (t - 0.5 * Math.Sin(t * 40)),
                5 + 340 * t + 8,
            };
            var to = new[]
            {
                -200 + 400 * ((t + 1.0 / 919) - 0.5 * Math.Sin((t + 1.0 / 919) * 40)),
                5 + 340 * (t + 1.0 / 919),
                -200 + 400 * ((t + 1.0 / 919) - 0.5 * Math.Sin((t + 1.0 / 919) * 40)),
                5 + 340 * (t + 1.0 / 919) + 8,
            };
            segments.Add(new ToolpathSegment(from, to, i == 0, (uint)(i + 2)));
        }
        ToolpathSceneBuilder.PopulatePlane(layer0, segments, idx0, geo0);
        ToolpathSceneBuilder.PopulatePlane(layer1, segments, idx1, geo1);

        var sideA = new List<Vector3>();
        var sideB = new List<Vector3>();
        foreach (var seg in segments)
        {
            sideA.Add(PlaneWorldMapper.ToWorld(geo0, seg.From, idx0));
            sideB.Add(PlaneWorldMapper.ToWorld(geo1, seg.From, idx1));
        }
        var loft = scene.AddLoftSurface();
        loft.SetLoftSheet(sideA, sideB);

        var tool = scene.CreateTool(ToolShape.Cone);
        // Static pose exactly matching the app log: vm pos=26.112,8.056,-0.5,39.184
        var p0 = PlaneWorldMapper.ToWorld(geo0, new double[] { 26.112, 8.056 }, idx0);
        var p1 = PlaneWorldMapper.ToWorld(geo1, new double[] { -0.5, 39.184 }, idx1);
        tool.UpdatePosition(new Endpoint(new[] { p0, p1 }));

        scene.SetViewportSize(W, H);
        scene.PumpCarriagePlanes();
        scene.PumpToolWireAndLofts();

        // Mirror the app's OnTick: pump tool+wire+loft EVERY tick, planes every
        // 6th tick (~10Hz at 60fps), render+readback after each submission.
        // Also mirror the RUN dynamic: the acked end advances, so the loft sheet
        // is RE-FED with a growing segment set each tick (PulseAckedEnds) while
        // the VM pose stays parked (log: executedSegs grew 574->920 with a static
        // controller pose). App viewport was ~1937x1000 (nonbg full-frame count).
        var fanCount = 0;
        var maxRed = 0;
        var frames = 600;
        var lw = 1936;
        var lh = 1016;
        scene.SetViewportSize(lw, lh);
        var rc = scene.Engine.CreateRenderContext();
        var rb = new NexGpuReadback(scene.Context, (uint)lw, (uint)lh);

        // Mirror the app's OnTick: with DiagnosticDisableCulling default true the
        // view sets FrustumCullNode.Enabled=false â€” culling OFF. The harness left
        // it at the engine default (ON) before, a real difference.
        var cullNode = scene.Engine.GetRenderNode<HelixToolkit.Nex.Rendering.ComputeNodes.FrustumCullNode>();
        if (cullNode is not null)
            cullNode.Enabled = false;
        var grown = 2;
        for (var i = 0; i < frames; i++)
        {
            if (i % 6 == 0)
                scene.PumpCarriagePlanes();
            grown = Math.Min(segments.Count, grown + 1);
            loft.SetLoftSheet(sideA.Take(grown).ToList(), sideB.Take(grown).ToList());
            scene.PumpToolWireAndLofts();

            var cam = scene.UpdateViewport(rc, 1f / 60f);
            rc.Update(new HelixToolkit.Nex.Maths.Size(lw, lh), cam);
            rc.RenderParams.BackgroundColor = new Color4(5f, 5f, 5f, 1f);
            scene.Context.WaitAll(true);
            scene.Engine.BeginFrame();
            var cmd = scene.Engine.RenderOffscreen(rc, scene.WorldData, rb.EngineTarget, null);
            scene.Engine.Submit(cmd, default, rb.EngineSyncInfo);
            scene.Context.WaitAll(true);

            var ptr = Marshal.AllocHGlobal(lw * lh * 4);
            try
            {
                if (rb.CopyFrame(ptr, lw * 4))
                {
                    var red = CountRed(ptr, lw, lh);
                    maxRed = Math.Max(maxRed, red);
                    File.AppendAllText(Path.Combine(outDir, "probe.txt"), $"frame{i}: red={red}{Environment.NewLine}");
                    if (red > 30000)
                    {
                        fanCount++;
                        WritePNG(Path.Combine(outDir, $"fan_{i}.png"), ptr, lw, lh);
                        File.AppendAllText(
                            Path.Combine(outDir, "map.txt"),
                            $"frame{i}:{Environment.NewLine}{RedDensityMap(ptr, lw, lh)}{Environment.NewLine}");
                    }
                }
            }
            finally
            {
                Marshal.FreeHGlobal(ptr);
            }

            // App-only step: every 30th tick the view clears the SHARED texture
            // to pure red from the D3D11 side (DiagTestD3D11Write) with no
            // inter-API fence, then validates the readback. The harness never
            // did this before â€” it is the last unmirrored app behavior.
            if (i % 30 == 1)
                rb.DiagTestD3D11Write();
        }
        scene.Dispose();
        File.AppendAllText(Path.Combine(outDir, "probe.txt"), $"SUMMARY fans={fanCount} maxRed={maxRed}{Environment.NewLine}");
        // Harness NEVER reproduces the fan â€” this is a regression guard.
        Assert.True(fanCount == 0, $"unexpected fan in {frames} frames (maxRed={maxRed})");
    }

    /// <summary>Red-ish pixel count (strict red-biased classification).</summary>
    private static int CountRed(IntPtr rgba, int w, int h)
    {
        var count = 0;
        for (var y = 0; y < h; y++)
        {
            var row = rgba + y * w * 4;
            for (var x = 0; x < w; x++)
            {
                var r = Marshal.ReadByte(row + x * 4 + 0);
                var g = Marshal.ReadByte(row + x * 4 + 1);
                var b = Marshal.ReadByte(row + x * 4 + 2);
                if (r > 150 && r > g * 1.5 && r > b * 1.5)
                    count++;
            }
        }
        return count;
    }

    [Fact]
    public static void Race_readback_skips_waitall()
    {
        var outDir = Path.Combine(Path.GetTempPath(), "nex_repro", "race_nowait");
        Directory.CreateDirectory(outDir);

        var scene = new NexPlotScene();
        var machine = MachineTypeCatalog.Resolve(MachineTypes.Foam);
        var config = PlotterNexConfig.Defaults(machine);
        var geo0 = config.Planes[0];
        var geo1 = config.Planes[1];
        var idx0 = machine.PlaneAxisIndices(0);
        var idx1 = machine.PlaneAxisIndices(1);
        var layer0 = scene.AddCarriagePlane();
        var layer1 = scene.AddCarriagePlane();
        var segments = new List<ToolpathSegment>
        {
            new(new double[] { 0, 0, 0, 0 }, new double[] { 200, 300, 200, 308 }, true, 2),
            new(new double[] { 200, 300, 200, 308 }, new double[] { -200, 200, -200, 208 }, false, 3),
        };
        ToolpathSceneBuilder.PopulatePlane(layer0, segments, idx0, geo0);
        ToolpathSceneBuilder.PopulatePlane(layer1, segments, idx1, geo1);
        var loft = scene.AddLoftSurface();
        var sideA = new List<Vector3>();
        var sideB = new List<Vector3>();
        foreach (var seg in segments)
        {
            sideA.Add(PlaneWorldMapper.ToWorld(geo0, seg.From, idx0));
            sideB.Add(PlaneWorldMapper.ToWorld(geo1, seg.From, idx1));
        }
        loft.SetLoftSheet(sideA, sideB);
        var tool = scene.CreateTool(ToolShape.Cone);
        var p0 = PlaneWorldMapper.ToWorld(geo0, new double[] { 26.112, 8.056 }, idx0);
        var p1 = PlaneWorldMapper.ToWorld(geo1, new double[] { -0.5, 39.184 }, idx1);
        tool.UpdatePosition(new Endpoint(new[] { p0, p1 }));
        scene.SetViewportSize(W, H);
        scene.PumpCarriagePlanes();
        scene.PumpToolWireAndLofts();
        var cullNode = scene.Engine.GetRenderNode<HelixToolkit.Nex.Rendering.ComputeNodes.FrustumCullNode>();
        if (cullNode is not null)
            cullNode.Enabled = false;

        var frames = 200;
        var rc = scene.Engine.CreateRenderContext();
        var rb = new NexGpuReadback(scene.Context, (uint)W, (uint)H);
        var fanCount = 0;
        var maxRed = 0;
        for (var i = 0; i < frames; i++)
        {
            scene.PumpCarriagePlanes();
            scene.PumpToolWireAndLofts();
            var cam = scene.UpdateViewport(rc, 1f / 60f);
            rc.Update(new HelixToolkit.Nex.Maths.Size(W, H), cam);
            rc.RenderParams.BackgroundColor = new Color4(5f, 5f, 5f, 1f);
            // NOTE: no WaitAll before submit â€” the engine can still be mid-frame
            // from the previous tick when the D3D11 readback runs (in the app this
            // would happen if WaitAll(true) does not cover the whole frame graph).
            scene.Engine.BeginFrame();
            var cmd = scene.Engine.RenderOffscreen(rc, scene.WorldData, rb.EngineTarget, null);
            scene.Engine.Submit(cmd, default, rb.EngineSyncInfo);
            scene.Context.WaitAll(true);

            var ptr = Marshal.AllocHGlobal(W * H * 4);
            try
            {
                if (rb.CopyFrame(ptr, W * 4))
                {
                    var red = CountRed(ptr, W, H);
                    maxRed = Math.Max(maxRed, red);
                    File.AppendAllText(Path.Combine(outDir, "probe.txt"), $"frame{i}: red={red}{Environment.NewLine}");
                    if (red > 30000)
                    {
                        fanCount++;
                        WritePNG(Path.Combine(outDir, $"fan_{i}.png"), ptr, W, H);
                        File.AppendAllText(
                            Path.Combine(outDir, "map.txt"),
                            $"frame{i}:{Environment.NewLine}{RedDensityMap(ptr, W, H)}{Environment.NewLine}");
                    }
                }
            }
            finally
            {
                Marshal.FreeHGlobal(ptr);
            }
        }
        scene.Dispose();
        File.AppendAllText(Path.Combine(outDir, "probe.txt"), $"SUMMARY fans={fanCount} maxRed={maxRed}{Environment.NewLine}");
        // Even forced racy readback stays clean â€” regression guard.
        Assert.True(fanCount == 0, $"unexpected fan with racy readback in {frames} frames (maxRed={maxRed})");
    }

    [Fact]
    public static void Bisect_dynamic_cone_moves_every_frame()
    {
        var outDir = Path.Combine(Path.GetTempPath(), "nex_repro", "dynamic_move");
        Directory.CreateDirectory(outDir);

        var scene = new NexPlotScene();
        var machine = MachineTypeCatalog.Resolve(MachineTypes.Foam);
        var config = PlotterNexConfig.Defaults(machine);
        var geo0 = config.Planes[0];
        var geo1 = config.Planes[1];
        var idx0 = machine.PlaneAxisIndices(0);
        var idx1 = machine.PlaneAxisIndices(1);

        var layer0 = scene.AddCarriagePlane();
        var layer1 = scene.AddCarriagePlane();

        var segments = new List<ToolpathSegment>
        {
            new(new double[] { 0, 0, 0, 0 }, new double[] { 10, 5, 10, 12 }, true, 2),
            new(new double[] { 10, 5, 10, 12 }, new double[] { 20, 8, 20, 24 }, false, 3),
            new(new double[] { 20, 8, 20, 24 }, new double[] { 30, 5, 30, 36 }, false, 4),
        };

        ToolpathSceneBuilder.PopulatePlane(layer0, segments, idx0, geo0);
        ToolpathSceneBuilder.PopulatePlane(layer1, segments, idx1, geo1);

        var loft = scene.AddLoftSurface();
        var sideA = new List<Vector3>();
        var sideB = new List<Vector3>();
        foreach (var seg in segments)
        {
            sideA.Add(PlaneWorldMapper.ToWorld(geo0, seg.From, idx0));
            sideB.Add(PlaneWorldMapper.ToWorld(geo1, seg.From, idx1));
        }
        loft.SetLoftSheet(sideA, sideB);

        var tool = scene.CreateTool(ToolShape.Cone);

        scene.SetViewportSize(W, H);
        scene.PumpCarriagePlanes();
        scene.PumpToolWireAndLofts();

        // Sweep the cone across a range of positions, exactly like the status poll
        // during a real run (new position â†’ UpdatePosition â†’ NodeInfo rewrite).
        var positions = new[]
        {
            new double[] { 10, 5, 10, 12 },
            new double[] { 13, 6, 13, 15 },
            new double[] { 16, 7, 16, 18 },
            new double[] { 20, 8, 20, 24 },
            new double[] { 23, 7, 23, 27 },
            new double[] { 26, 6, 26, 30 },
            new double[] { 30, 5, 30, 36 },
            new double[] { 33, 4, 33, 39 },
        };
        var pi = 0;
        RenderFrames(scene, outDir, positions.Length, () =>
        {
            var p = PlaneWorldMapper.ToWorld(geo0, positions[pi++ % positions.Length], idx0);
            tool.UpdatePosition(new Endpoint(new[] { p }));
            scene.PumpToolWireAndLofts();
        });
        scene.Dispose();
        Assert.True(Directory.Exists(outDir));
    }

    /// <summary>The app's Run combo the earlier mirrors never exercised together:
    /// the loft sheet is RE-FED (immediate-dispose swap) with a growing executed
    /// set EVERY tick while BOTH cones translate every frame (NodeInfo rewrites)
    /// and the wire re-stages on endpoint changes. Loft-off A/B proved the loft
    /// is the trigger; this reproduces the app's exact per-tick churn to get a
    /// deterministic in-harness repro. Logs only (no assert) until the repro is
    /// stable, then it flips into a regression guard.</summary>
    [Fact]
    public static void Repro_sheet_resync_every_tick_plus_moving_cones()
    {
        var outDir = Path.Combine(Path.GetTempPath(), "nex_repro", "sheet_move");
        Directory.CreateDirectory(outDir);

        var scene = new NexPlotScene();
        var machine = MachineTypeCatalog.Resolve(MachineTypes.Foam);
        var config = PlotterNexConfig.Defaults(machine);
        var geo0 = config.Planes[0];
        var geo1 = config.Planes[1];
        var idx0 = machine.PlaneAxisIndices(0);
        var idx1 = machine.PlaneAxisIndices(1);

        var layer0 = scene.AddCarriagePlane();
        var layer1 = scene.AddCarriagePlane();

        var segments = new List<ToolpathSegment>();
        for (var i = 0; i < 920; i++)
        {
            var t = i / 919.0;
            var from = new[]
            {
                -200 + 400 * (t - 0.5 * Math.Sin(t * 40)),
                5 + 340 * t,
                -200 + 400 * (t - 0.5 * Math.Sin(t * 40)),
                5 + 340 * t + 8,
            };
            var to = new[]
            {
                -200 + 400 * ((t + 1.0 / 919) - 0.5 * Math.Sin((t + 1.0 / 919) * 40)),
                5 + 340 * (t + 1.0 / 919),
                -200 + 400 * ((t + 1.0 / 919) - 0.5 * Math.Sin((t + 1.0 / 919) * 40)),
                5 + 340 * (t + 1.0 / 919) + 8,
            };
            segments.Add(new ToolpathSegment(from, to, i == 0, (uint)(i + 2)));
        }
        ToolpathSceneBuilder.PopulatePlane(layer0, segments, idx0, geo0);
        ToolpathSceneBuilder.PopulatePlane(layer1, segments, idx1, geo1);

        var sideA = new List<Vector3>();
        var sideB = new List<Vector3>();
        foreach (var seg in segments)
        {
            sideA.Add(PlaneWorldMapper.ToWorld(geo0, seg.From, idx0));
            sideB.Add(PlaneWorldMapper.ToWorld(geo1, seg.From, idx1));
        }
        var loft = scene.AddLoftSurface();
        loft.SetLoftSheet(sideA, sideB);

        var tool = scene.CreateTool(ToolShape.Cone);

        scene.SetViewportSize(W, H);
        scene.PumpCarriagePlanes();
        scene.PumpToolWireAndLofts();

        var frames = 600;
        var lw = 1936;
        var lh = 1016;
        scene.SetViewportSize(lw, lh);
        var rc = scene.Engine.CreateRenderContext();
        var rb = new NexGpuReadback(scene.Context, (uint)lw, (uint)lh);
        var cullNode = scene.Engine.GetRenderNode<HelixToolkit.Nex.Rendering.ComputeNodes.FrustumCullNode>();
        if (cullNode is not null)
            cullNode.Enabled = false;

        var fanCount = 0;
        var maxRed = 0;
        var grown = 2;
        for (var i = 0; i < frames; i++)
        {
            if (i % 6 == 0)
                scene.PumpCarriagePlanes();
            grown = Math.Min(segments.Count, grown + 1);
            loft.SetLoftSheet(sideA.Take(grown).ToList(), sideB.Take(grown).ToList());
            var ti = Math.Min(segments.Count - 1, (int)(i / (double)frames * segments.Count));
            var p0 = PlaneWorldMapper.ToWorld(geo0, segments[ti].From, idx0);
            var p1 = PlaneWorldMapper.ToWorld(geo1, segments[ti].From, idx1);
            // The app's per-poll loft loop: SetWirePlane([p0,p1]) re-stages the
            // line-loop LineNode just like PlotterNexViewModel does every tick.
            loft.SetWirePlane([p0, p1]);
            tool.UpdatePosition(new Endpoint(new[] { p0, p1 }));
            scene.PumpToolWireAndLofts();

            var cam = scene.UpdateViewport(rc, 1f / 60f);
            rc.Update(new HelixToolkit.Nex.Maths.Size(lw, lh), cam);
            rc.RenderParams.BackgroundColor = new Color4(5f, 5f, 5f, 1f);
            scene.Context.WaitAll(true);
            scene.Engine.BeginFrame();
            var cmd = scene.Engine.RenderOffscreen(rc, scene.WorldData, rb.EngineTarget, null);
            scene.Engine.Submit(cmd, default, rb.EngineSyncInfo);
            scene.Context.WaitAll(true);

            var ptr = Marshal.AllocHGlobal(lw * lh * 4);
            try
            {
                if (rb.CopyFrame(ptr, lw * 4))
                {
                    var red = CountRed(ptr, lw, lh);
                    maxRed = Math.Max(maxRed, red);
                    File.AppendAllText(Path.Combine(outDir, "probe.txt"), $"frame{i}: red={red}{Environment.NewLine}");
                    if (red > 30000)
                    {
                        fanCount++;
                        WritePNG(Path.Combine(outDir, $"fan_{i}.png"), ptr, lw, lh);
                        File.AppendAllText(
                            Path.Combine(outDir, "map.txt"),
                            $"frame{i}:{Environment.NewLine}{RedDensityMap(ptr, lw, lh)}{Environment.NewLine}");
                    }
                }
            }
            finally
            {
                Marshal.FreeHGlobal(ptr);
            }
        }
        scene.Dispose();
        File.AppendAllText(Path.Combine(outDir, "probe.txt"), $"SUMMARY fans={fanCount} maxRed={maxRed}{Environment.NewLine}");
    }

    /// <summary>A/B #3 on the real machine: the user's wing program's loft sheet
    /// SELF-INTERSECTS (the wire towers swap relative sweep while the path zig-zags
    /// back on itself). The synthetic monotone sheet never folds, so the harness
    /// stayed clean while the app fanned. Feed the REAL parsed path (app's exact
    /// GCodeProgram.Parse + ToolpathBuilder.Build + PopulateLoftSheet pipeline)
    /// with the app's loop diagnostic set the way the app currently runs
    /// (loop OFF, sheet ON) and render headless with culling off — prove whether
    /// the self-intersecting static sheet reproduces the fan.</summary>
    [Fact]
    public static void Repro_real_wing_gcode_static_sheet_loop_off()
    {
        var gcodePath = "C:\\Users\\Andrew\\Downloads\\test\\left_wing_FG2.gcode";
        if (!File.Exists(gcodePath))
            return;

        var outDir = Path.Combine(Path.GetTempPath(), "nex_repro", "real_wing");
        Directory.CreateDirectory(outDir);

        var text = File.ReadAllText(gcodePath);
        var program = GCodeProgram.Parse(text);
        var segments = ToolpathBuilder.Build(program.Tokens);
        File.AppendAllText(Path.Combine(outDir, "probe.txt"),
            $"parsed tokens={program.Tokens.Count} errors={program.Errors.Count} segs={segments.Count}{Environment.NewLine}");

        var scene = new NexPlotScene();
        // App currently runs: loop OFF, sheet ON (the sheet is the suspect; the
        // loop has already been exonerated by the T655 fansave at lc=0).
        scene.DiagnosticDisableLoftLoop = true;
        scene.DiagnosticDisableLoftSheet = false;
        var machine = MachineTypeCatalog.Resolve(MachineTypes.Foam);
        // App log shows cones at world x=±200 => WireLength 400 in the profile.
        var cfg = PlotterNexConfig.Defaults(machine);
        cfg.WireLength = 400;
        cfg = PlotterNexConfig.RecenterOnWire(cfg);
        var geo0 = cfg.Planes[0];
        var geo1 = cfg.Planes[1];
        var idx0 = machine.PlaneAxisIndices(0);
        var idx1 = machine.PlaneAxisIndices(1);

        var layer0 = scene.AddCarriagePlane();
        var layer1 = scene.AddCarriagePlane();
        ToolpathSceneBuilder.PopulatePlane(layer0, segments, idx0, geo0);
        ToolpathSceneBuilder.PopulatePlane(layer1, segments, idx1, geo1);

        var sideA = new List<Vector3>(segments.Count);
        var sideB = new List<Vector3>(segments.Count);
        for (var i = 0; i < segments.Count; i++)
        {
            sideA.Add(PlaneWorldMapper.ToWorld(geo0, segments[i].From, idx0));
            sideB.Add(PlaneWorldMapper.ToWorld(geo1, segments[i].From, idx1));
        }

        // CPU-side self-intersection probe: count consecutive sheet quads whose
        // normals point in opposite hemispheres (the fold the user suspects).
        var folds = 0;
        for (var i = 0; i + 2 < sideA.Count; i++)
        {
            var n = Vector3.Cross(sideA[i + 1] - sideA[i], sideB[i] - sideA[i]);
            var n2 = Vector3.Cross(sideA[i + 2] - sideA[i + 1], sideB[i + 1] - sideA[i + 1]);
            if (n.LengthSquared() > 1e-9f && n2.LengthSquared() > 1e-9f
                && Vector3.Dot(n, n2) < 0)
                folds++;
        }
        File.AppendAllText(Path.Combine(outDir, "probe.txt"), $"sheetPairs={sideA.Count - 1} foldEvents={folds}{Environment.NewLine}");

        var loft = scene.AddLoftSurface();
        loft.SetLoftSheet(sideA, sideB); // STATIC — fed once, like ApplyBuild

        var tool = scene.CreateTool(ToolShape.Cone);
        scene.SetViewportSize(W, H);
        scene.PumpCarriagePlanes();
        scene.PumpToolWireAndLofts();

        var lw = 1936;
        var lh = 1016;
        scene.SetViewportSize(lw, lh);
        var rc = scene.Engine.CreateRenderContext();
        var rb = new NexGpuReadback(scene.Context, (uint)lw, (uint)lh);
        var cullNode = scene.Engine.GetRenderNode<HelixToolkit.Nex.Rendering.ComputeNodes.FrustumCullNode>();
        if (cullNode is not null)
            cullNode.Enabled = false;

        var frames = 400;
        var fanCount = 0;
        var maxRed = 0;
        var executed = 0;
        for (var i = 0; i < frames; i++)
        {
            // Mirror ApplyRunStateWindow's ~10Hz cadence: mark the executed
            // frontier on BOTH plane layers (staging vertex-color mutation +
            // done-overlay append) then swap-pump fresh plane geometries —
            // the per-tick plane churn the app does during a Run that the
            // earlier synthetic repros never applied.
            if (i % 6 == 0)
            {
                var target = Math.Min(segments.Count, executed + 3);
                for (var e = executed; e < target; e++)
                {
                    layer0.SetSegmentRunState(e, RunState.Executed);
                    layer1.SetSegmentRunState(e, RunState.Executed);
                }
                executed = target;
                scene.PumpCarriagePlanes();
            }
            // Advance the tool along the real path like the controller poll does.
            var ti = Math.Min(segments.Count - 1, (int)(i / (double)frames * (segments.Count - 1)));
            var p0 = PlaneWorldMapper.ToWorld(geo0, segments[ti].From, idx0);
            var p1 = PlaneWorldMapper.ToWorld(geo1, segments[ti].From, idx1);
            tool.UpdatePosition(new Endpoint(new[] { p0, p1 }));
            scene.PumpToolWireAndLofts();

            var cam = scene.UpdateViewport(rc, 1f / 60f);
            rc.Update(new HelixToolkit.Nex.Maths.Size(lw, lh), cam);
            rc.RenderParams.BackgroundColor = new Color4(5f, 5f, 5f, 1f);
            scene.Context.WaitAll(true);
            scene.Engine.BeginFrame();
            var cmd = scene.Engine.RenderOffscreen(rc, scene.WorldData, rb.EngineTarget, null);
            scene.Engine.Submit(cmd, default, rb.EngineSyncInfo);
            scene.Context.WaitAll(true);

            var ptr = Marshal.AllocHGlobal(lw * lh * 4);
            try
            {
                if (rb.CopyFrame(ptr, lw * 4))
                {
                    var red = CountRed(ptr, lw, lh);
                    maxRed = Math.Max(maxRed, red);
                    File.AppendAllText(Path.Combine(outDir, "probe.txt"), $"frame{i}: red={red}{Environment.NewLine}");
                    if (red > 30000)
                    {
                        fanCount++;
                        WritePNG(Path.Combine(outDir, $"fan_{i}.png"), ptr, lw, lh);
                        File.AppendAllText(
                            Path.Combine(outDir, "map.txt"),
                            $"frame{i}:{Environment.NewLine}{RedDensityMap(ptr, lw, lh)}{Environment.NewLine}");
                    }
                }
            }
            finally
            {
                Marshal.FreeHGlobal(ptr);
            }
        }
        scene.Dispose();
        File.AppendAllText(Path.Combine(outDir, "probe.txt"), $"SUMMARY fans={fanCount} maxRed={maxRed}{Environment.NewLine}");
    }

    /// App A/B #4: the fan is camera/viewport-dependent (T625/T655 fan frames
    /// used a user-moved camera; default-iso T0/T1030 frames stay ~2k). The
    /// static-sheet swipes stayed clean at auto-framed iso, so now render the
    /// EXACT T625 fan scene — same camera, same cone positions, same sheet —
    /// headlessly to prove whether the Real wing sheet + that view reproduces it.
    [Fact]
    public static void Repro_real_wing_exact_t625_fan_camera()
    {
        var gcodePath = "C:\\Users\\Andrew\\Downloads\\test\\left_wing_FG2.gcode";
        if (!File.Exists(gcodePath))
            return;

        var outDir = Path.Combine(Path.GetTempPath(), "nex_repro", "real_wing_t625");
        Directory.CreateDirectory(outDir);

        var text = File.ReadAllText(gcodePath);
        var program = GCodeProgram.Parse(text);
        var segments = ToolpathBuilder.Build(program.Tokens);

        var scene = new NexPlotScene();
        scene.DiagnosticDisableLoftLoop = true;
        scene.DiagnosticDisableLoftSheet = false;
        var machine = MachineTypeCatalog.Resolve(MachineTypes.Foam);
        var cfg = PlotterNexConfig.Defaults(machine);
        cfg.WireLength = 400;
        cfg = PlotterNexConfig.RecenterOnWire(cfg);
        var geo0 = cfg.Planes[0];
        var geo1 = cfg.Planes[1];
        var idx0 = machine.PlaneAxisIndices(0);
        var idx1 = machine.PlaneAxisIndices(1);

        var layer0 = scene.AddCarriagePlane();
        var layer1 = scene.AddCarriagePlane();
        ToolpathSceneBuilder.PopulatePlane(layer0, segments, idx0, geo0);
        ToolpathSceneBuilder.PopulatePlane(layer1, segments, idx1, geo1);

        var sideA = new List<Vector3>(segments.Count);
        var sideB = new List<Vector3>(segments.Count);
        for (var i = 0; i < segments.Count; i++)
        {
            sideA.Add(PlaneWorldMapper.ToWorld(geo0, segments[i].From, idx0));
            sideB.Add(PlaneWorldMapper.ToWorld(geo1, segments[i].From, idx1));
        }
        var loft = scene.AddLoftSurface();
        loft.SetLoftSheet(sideA, sideB);

        var tool = scene.CreateTool(ToolShape.Cone);
        // T625 machine pos 169.72,100,92.816,100 -> coneA<-200,169.72,100> coneB<200,92.816,100>
        var p0 = PlaneWorldMapper.ToWorld(geo0, new double[] { 169.72, 100, 92.816, 100 }, idx0);
        var p1 = PlaneWorldMapper.ToWorld(geo1, new double[] { 169.72, 100, 92.816, 100 }, idx1);
        tool.UpdatePosition(new Endpoint(new[] { p0, p1 }));

        var lw = 1920;
        var lh = 1009;
        scene.SetViewportSize(lw, lh);
        scene.PumpCarriagePlanes();
        scene.PumpToolWireAndLofts();

        var rc = scene.Engine.CreateRenderContext();
        var rb = new NexGpuReadback(scene.Context, (uint)lw, (uint)lh);
        var cullNode = scene.Engine.GetRenderNode<HelixToolkit.Nex.Rendering.ComputeNodes.FrustumCullNode>();
        if (cullNode is not null)
            cullNode.Enabled = false;

        // T625/fan and T695/clean cameras verbatim from pool_diag.log fanscene.
        var cams = new (string name, Vector3 pos, Vector3 target, Vector3 up)[]
        {
            ("t625_fan", new Vector3(-535.04596f, -49.26967f, 354.68652f), new Vector3(-86.890205f, 201.26712f, 60.54587f), Vector3.Normalize(new Vector3(0.5f, 0.5f, 1f))),
            ("t655_fan2", new Vector3(-599.45593f, 33.350746f, 326.92355f), new Vector3(-151.3002f, 283.88766f, 32.782715f), Vector3.Normalize(new Vector3(0.5f, 0.5f, 1f))),
            ("t695_clean", new Vector3(-777.5605f, 329.9971f, 57.731606f), new Vector3(-197.05505f, 215.7776f, 67.865425f), Vector3.Normalize(new Vector3(0.5f, 0.5f, 1f))),
            ("iso_default", new Vector3(-341.6283f, -168.8833f, 391.6283f), new Vector3(0f, 172.745f, 50f), Vector3.Normalize(new Vector3(0.5f, 0.5f, 1f))),
        };

        foreach (var (name, pos, target, up) in cams)
        {
            var persp = new PerspectiveCamera
            {
                NearPlane = 0.01f,
                FarPlane = 2689.6252f,
                Position = pos,
                Target = target,
                Up = up,
                Fov = 0.8f,
            };
            rc.Update(new HelixToolkit.Nex.Maths.Size(lw, lh), persp);
            rc.RenderParams.BackgroundColor = new Color4(5f, 5f, 5f, 1f);
            scene.Context.WaitAll(true);
            scene.Engine.BeginFrame();
            var cmd = scene.Engine.RenderOffscreen(rc, scene.WorldData, rb.EngineTarget, null);
            scene.Engine.Submit(cmd, default, rb.EngineSyncInfo);
            scene.Context.WaitAll(true);

            var ptr = Marshal.AllocHGlobal(lw * lh * 4);
            try
            {
                if (rb.CopyFrame(ptr, lw * 4))
                {
                    var red = CountRed(ptr, lw, lh);
                    File.AppendAllText(Path.Combine(outDir, "probe.txt"), $"cam={name} red={red}{Environment.NewLine}");
                    WritePNG(Path.Combine(outDir, $"cam_{name}.png"), ptr, lw, lh);
                    File.AppendAllText(Path.Combine(outDir, "map.txt"),
                        $"cam={name}:{Environment.NewLine}{RedDensityMap(ptr, lw, lh)}{Environment.NewLine}");
                }
            }
            finally
            {
                Marshal.FreeHGlobal(ptr);
            }
        }

        scene.Dispose();
        Assert.True(File.Exists(Path.Combine(outDir, "cam_t625_fan.png")));
    }

    [Fact]
    public static void Repro_real_wing_cone_wire_only_no_planes_no_loft()
    {
        var gcodePath = "C:\\Users\\Andrew\\Downloads\\test\\left_wing_FG2.gcode";
        if (!File.Exists(gcodePath))
            return;

        var outDir = Path.Combine(Path.GetTempPath(), "nex_repro", "real_wing_cone_only");
        Directory.CreateDirectory(outDir);

        var text = File.ReadAllText(gcodePath);
        var program = GCodeProgram.Parse(text);
        var segments = ToolpathBuilder.Build(program.Tokens);

        var scene = new NexPlotScene();
        scene.DiagnosticDisableLoft = true;
        var machine = MachineTypeCatalog.Resolve(MachineTypes.Foam);
        var cfg = PlotterNexConfig.Defaults(machine);
        cfg.WireLength = 400;
        cfg = PlotterNexConfig.RecenterOnWire(cfg);
        var geo0 = cfg.Planes[0];
        var geo1 = cfg.Planes[1];
        var idx0 = machine.PlaneAxisIndices(0);
        var idx1 = machine.PlaneAxisIndices(1);

        var tool = scene.CreateTool(ToolShape.Cone);
        var p0 = PlaneWorldMapper.ToWorld(geo0, new double[] { 169.72, 100, 92.816, 100 }, idx0);
        var p1 = PlaneWorldMapper.ToWorld(geo1, new double[] { 169.72, 100, 92.816, 100 }, idx1);
        tool.UpdatePosition(new Endpoint(new[] { p0, p1 }));

        var lw = 1920;
        var lh = 1009;
        scene.SetViewportSize(lw, lh);
        scene.PumpToolWireAndLofts();

        var rc = scene.Engine.CreateRenderContext();
        var rb = new NexGpuReadback(scene.Context, (uint)lw, (uint)lh);
        var cullNode = scene.Engine.GetRenderNode<HelixToolkit.Nex.Rendering.ComputeNodes.FrustumCullNode>();
        if (cullNode is not null)
            cullNode.Enabled = false;

        var persp = new PerspectiveCamera
        {
            NearPlane = 0.01f,
            FarPlane = 2689.6252f,
            Position = new Vector3(-535.04596f, -49.26967f, 354.68652f),
            Target = new Vector3(-86.890205f, 201.26712f, 60.54587f),
            Up = Vector3.Normalize(new Vector3(0.5f, 0.5f, 1f)),
            Fov = 0.8f,
        };
        rc.Update(new HelixToolkit.Nex.Maths.Size(lw, lh), persp);
        rc.RenderParams.BackgroundColor = new Color4(5f, 5f, 5f, 1f);
        scene.Context.WaitAll(true);
        scene.Engine.BeginFrame();
        var cmd = scene.Engine.RenderOffscreen(rc, scene.WorldData, rb.EngineTarget, null);
        scene.Engine.Submit(cmd, default, rb.EngineSyncInfo);
        scene.Context.WaitAll(true);

        var ptr = Marshal.AllocHGlobal(lw * lh * 4);
        try
        {
            if (rb.CopyFrame(ptr, lw * 4))
            {
                var red = CountRed(ptr, lw, lh);
                File.AppendAllText(Path.Combine(outDir, "probe.txt"), $"cone-only t625-cam red={red}{Environment.NewLine}");
                WritePNG(Path.Combine(outDir, "cone_only_t625.png"), ptr, lw, lh);
                File.AppendAllText(Path.Combine(outDir, "map.txt"),
                    $"cone-only t625-cam:{Environment.NewLine}{RedDensityMap(ptr, lw, lh)}{Environment.NewLine}");
            }
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }

        scene.Dispose();
        Assert.True(File.Exists(Path.Combine(outDir, "cone_only_t625.png")));
    }

    [Fact]
    public static void Bisect_dynamic_cone_nan_position_explodes_like_user_screenshot()
    {
        var outDir = Path.Combine(Path.GetTempPath(), "nex_repro", "dynamic_nan");
        Directory.CreateDirectory(outDir);

        var scene = new NexPlotScene();
        var machine = MachineTypeCatalog.Resolve(MachineTypes.Foam);
        var config = PlotterNexConfig.Defaults(machine);
        var geo0 = config.Planes[0];
        var geo1 = config.Planes[1];
        var idx0 = machine.PlaneAxisIndices(0);
        var idx1 = machine.PlaneAxisIndices(1);

        var layer0 = scene.AddCarriagePlane();
        var layer1 = scene.AddCarriagePlane();

        var segments = new List<ToolpathSegment>
        {
            new(new double[] { 0, 0, 0, 0 }, new double[] { 10, 5, 10, 12 }, true, 2),
            new(new double[] { 10, 5, 10, 12 }, new double[] { 20, 8, 20, 24 }, false, 3),
            new(new double[] { 20, 8, 20, 24 }, new double[] { 30, 5, 30, 36 }, false, 4),
        };

        ToolpathSceneBuilder.PopulatePlane(layer0, segments, idx0, geo0);
        ToolpathSceneBuilder.PopulatePlane(layer1, segments, idx1, geo1);

        var loft = scene.AddLoftSurface();
        var sideA = new List<Vector3>();
        var sideB = new List<Vector3>();
        foreach (var seg in segments)
        {
            sideA.Add(PlaneWorldMapper.ToWorld(geo0, seg.From, idx0));
            sideB.Add(PlaneWorldMapper.ToWorld(geo1, seg.From, idx1));
        }
        loft.SetLoftSheet(sideA, sideB);

        var tool = scene.CreateTool(ToolShape.Cone);

        scene.SetViewportSize(W, H);
        scene.PumpCarriagePlanes();
        scene.PumpToolWireAndLofts();

        // Valid sweep like the live run, EXCEPT one frame injects a NaN/Inf position â€”
        // mirroring a controller poll that reports an invalid/incomplete state.
        var valid = new[]
        {
            new double[] { 10, 5, 10, 12 },
            new double[] { 13, 6, 13, 15 },
            new double[] { 16, 7, 16, 18 },
            new double[] { 20, 8, 20, 24 },
            new double[] { 23, 7, 23, 27 },
            new double[] { 26, 6, 26, 30 },
            new double[] { 30, 5, 30, 36 },
            new double[] { 33, 4, 33, 39 },
        };
        var bad = new double[] { double.NaN, 5, double.NaN, 18 };
        var pi = 0;
        RenderFrames(scene, outDir, 10, () =>
        {
            var p = PlaneWorldMapper.ToWorld(geo0, pi == 5 ? bad : valid[pi % valid.Length], idx0);
            tool.UpdatePosition(new Endpoint(new[] { p }));
            scene.PumpToolWireAndLofts();
            pi++;
        });
        scene.Dispose();
        Assert.True(Directory.Exists(outDir));
    }

    [Fact]
    public static void Bisect_dynamic_cone_inf_position_with_culling_off()
    {
        var outDir = Path.Combine(Path.GetTempPath(), "nex_repro", "dynamic_inf");
        Directory.CreateDirectory(outDir);

        var scene = new NexPlotScene();
        var machine = MachineTypeCatalog.Resolve(MachineTypes.Foam);
        var config = PlotterNexConfig.Defaults(machine);
        var geo0 = config.Planes[0];
        var geo1 = config.Planes[1];
        var idx0 = machine.PlaneAxisIndices(0);
        var idx1 = machine.PlaneAxisIndices(1);

        var layer0 = scene.AddCarriagePlane();
        var layer1 = scene.AddCarriagePlane();

        var segments = new List<ToolpathSegment>
        {
            new(new double[] { 0, 0, 0, 0 }, new double[] { 10, 5, 10, 12 }, true, 2),
            new(new double[] { 10, 5, 10, 12 }, new double[] { 20, 8, 20, 24 }, false, 3),
            new(new double[] { 20, 8, 20, 24 }, new double[] { 30, 5, 30, 36 }, false, 4),
        };

        ToolpathSceneBuilder.PopulatePlane(layer0, segments, idx0, geo0);
        ToolpathSceneBuilder.PopulatePlane(layer1, segments, idx1, geo1);

        var loft = scene.AddLoftSurface();
        var sideA = new List<Vector3>();
        var sideB = new List<Vector3>();
        foreach (var seg in segments)
        {
            sideA.Add(PlaneWorldMapper.ToWorld(geo0, seg.From, idx0));
            sideB.Add(PlaneWorldMapper.ToWorld(geo1, seg.From, idx1));
        }
        loft.SetLoftSheet(sideA, sideB);

        var tool = scene.CreateTool(ToolShape.Cone);

        scene.SetViewportSize(W, H);
        scene.PumpCarriagePlanes();
        scene.PumpToolWireAndLofts();

        // App state: DiagnosticDisableCulling=true -> FrustumCullNode OFF.
        // With culling ON a border vertex would be skipped as out-of-bounds;
        // with culling OFF a huge/Inf vertex triangle survives to the rasterizer.
        var cullNode = scene.Engine.GetRenderNode<HelixToolkit.Nex.Rendering.ComputeNodes.FrustumCullNode>();
        if (cullNode is not null)
            cullNode.Enabled = false;

        var valid = new[]
        {
            new double[] { 10, 5, 10, 12 },
            new double[] { 16, 7, 16, 18 },
            new double[] { 20, 8, 20, 24 },
            new double[] { 26, 6, 26, 30 },
        };
        var bad = new double[] { double.PositiveInfinity, -5, double.PositiveInfinity, 18 };
        var pi = 0;
        RenderFrames(scene, outDir, 12, () =>
        {
            var p = PlaneWorldMapper.ToWorld(geo0, pi == 4 ? bad : valid[pi % valid.Length], idx0);
            tool.UpdatePosition(new Endpoint(new[] { p }));
            scene.PumpToolWireAndLofts();
            pi++;
        });
        scene.Dispose();
        Assert.True(Directory.Exists(outDir));
    }

    [Fact]
    public static void Dump_red_pixels_of_user_artifacts_screenshot()
    {
        var path = "C:\\Projects\\Workshop\\SenderNG\\docs\\screenshots\\artifacts.png";
        using var img = Image.Load<Rgba32>(path);
        var outDir = Path.Combine(Path.GetTempPath(), "nex_repro", "user_artifacts");
        Directory.CreateDirectory(outDir);
        var sb = ProbeStaticImage(img, "user-artifacts");
        File.WriteAllText(Path.Combine(outDir, "probe.txt"), sb.ToString());
        Assert.True(sb.ToString().Contains("red-%") && !sb.ToString().Contains("red-% = 0.00"));
    }

    [Fact]
    public static void Dump_red_pixels_of_healthy_foam_screenshot()
    {
        var path = "C:\\Projects\\Workshop\\SenderNG\\docs\\screenshots\\foam rendering.png";
        using var img = Image.Load<Rgba32>(path);
        var outDir = Path.Combine(Path.GetTempPath(), "nex_repro", "healthy_foam");
        Directory.CreateDirectory(outDir);
        var sb = ProbeStaticImage(img, "healthy-foam");
        File.WriteAllText(Path.Combine(outDir, "probe.txt"), sb.ToString());
        Assert.True(true);
    }

    [Fact]
    public static void Dump_red_pixels_of_two_cones_screenshot()
    {
        var path = "C:\\Projects\\Workshop\\SenderNG\\docs\\screenshots\\2cones.png";
        using var img = Image.Load<Rgba32>(path);
        var outDir = Path.Combine(Path.GetTempPath(), "nex_repro", "two_cones");
        Directory.CreateDirectory(outDir);
        var sb = ProbeStaticImage(img, "2cones");
        File.WriteAllText(Path.Combine(outDir, "probe.txt"), sb.ToString());
        Assert.True(true);
    }

    [Fact]
    public static void Dump_run_frames_bmp()
    {
        var dir = "C:\\Users\\Andrew\\AppData\\Local\\SenderNG\\fans";
        var outDir = Path.Combine(Path.GetTempPath(), "nex_repro", "app_fan_capture");
        Directory.CreateDirectory(outDir);
        foreach (var file in Directory.GetFiles(dir, "*.bmp")
                     .OrderByDescending(f => new FileInfo(f).LastWriteTime)
                     .Take(2))
        {
            using var img = Image.Load<Rgba32>(file);
            var name = Path.GetFileNameWithoutExtension(file);
            var sb = ProbeStaticImage(img, name);
            File.WriteAllText(Path.Combine(outDir, $"{name}.txt"), sb.ToString());
        }
        Assert.True(true);
    }

    /// <summary>DIAGNOSTIC: scan every BMP in the app fans folder and report the
    /// red fill-ratio (solid triangles have high bbox density, thin wire low).
    /// Flags components whose bbox is large AND fill ratio is high.</summary>
    [Fact]
    public static void Dump_all_fan_capture_red_fill_ratios()
    {
        var dir = "C:\\Users\\Andrew\\AppData\\Local\\SenderNG\\fans";
        var sb = new StringBuilder();
        foreach (var file in Directory.GetFiles(dir, "*.bmp").OrderBy(f => f))
        {
            try
            {
                using var img = Image.Load<Rgba32>(file);
                var w = img.Width;
                var h = img.Height;
                var red = 0;
                var cells = new int[56 * 24];
                for (var y = 0; y < h; y++)
                {
                    for (var x = 0; x < w; x++)
                    {
                        var px = img[x, y];
                        if (px.R > 150 && px.R > px.G * 1.5 && px.R > px.B * 1.5)
                        {
                            red++;
                            cells[(y * 24 / h) * 56 + (x * 56 / w)]++;
                        }
                    }
                }
                var filled = 0;
                var maxCell = 0;
                var maxRow = 0;
                var maxCol = 0;
                for (var cy = 0; cy < 24; cy++)
                {
                    for (var cx = 0; cx < 56; cx++)
                    {
                        var c = cells[cy * 56 + cx];
                        if (c > 0)
                            filled++;
                        if (c > maxCell)
                        {
                            maxCell = c;
                            maxRow = cy;
                            maxCol = cx;
                        }
                    }
                }
                // fill ratio: red / (cells present * cell area) is hard; instead
                // use the max cell fill: a solid triangle has a cell ~100% red.
                var cellArea = 4 * 4; // approx per 56x24 cell
                var maxCellFill = maxCell * 100 / cellArea;
                sb.AppendLine($"{Path.GetFileName(file)} red={red} occupiedCells={filled}/{56 * 24} maxCellFill={maxCellFill}% at({maxCol},{maxRow})");
            }
            catch (Exception ex)
            {
                sb.AppendLine($"{Path.GetFileName(file)} ERR {ex.Message}");
            }
        }
        var outPath = Path.Combine(Path.GetTempPath(), "nex_repro", "app_fan_capture", "all_fill_ratios.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
        File.WriteAllText(outPath, sb.ToString());
        Assert.True(true);
    }

    /// <summary>DIAGNOSTIC: scan every BMP in the app fans folder for solid red
    /// components. A giant filled triangle (the fan) = one connected component
    /// with a large area and high solidity (area/bboxArea near 1). A thin wire
    /// = many tiny line components. Reports the largest component per frame.</summary>
    [Fact]
    public static void Dump_all_fan_capture_red_components()
    {
        var dir = "C:\\Users\\Andrew\\AppData\\Local\\SenderNG\\fans";
        var sb = new StringBuilder();
        foreach (var file in Directory.GetFiles(dir, "*.bmp").OrderBy(f => f))
        {
            try
            {
                using var img = Image.Load<Rgba32>(file);
                var w = img.Width;
                var h = img.Height;
                var redSet = new HashSet<long>();
                for (var y = 0; y < h; y++)
                    for (var x = 0; x < w; x++)
                    {
                        var px = img[x, y];
                        if (px.R > 150 && px.R > px.G * 1.5 && px.R > px.B * 1.5)
                            redSet.Add((long)y * w + x);
                    }
                // Flood-fill components (4-connected).
                var best = new List<(int area, int minX, int minY, int maxX, int maxY)>();
                var visited = new HashSet<long>();
                var q = new Queue<long>();
                foreach (var seed in redSet)
                {
                    if (visited.Contains(seed))
                        continue;
                    visited.Add(seed);
                    q.Enqueue(seed);
                    int area = 0, mnx = w, mny = h, mxx = -1, mxy = -1;
                    while (q.Count > 0)
                    {
                        var cur = q.Dequeue();
                        var cx = (int)(cur % w);
                        var cy = (int)(cur / w);
                        area++;
                        mnx = Math.Min(mnx, cx); mxx = Math.Max(mxx, cx);
                        mny = Math.Min(mny, cy); mxy = Math.Max(mxy, cy);
                        foreach (var d in new[] { -1L, 1L, -w, w })
                        {
                            var nb = cur + d;
                            if (d == -1 && cx == 0) continue;
                            if (d == 1 && cx == w - 1) continue;
                            if (d == -w && cy == 0) continue;
                            if (d == w && cy == h - 1) continue;
                            if (visited.Contains(nb) || !redSet.Contains(nb))
                                continue;
                            visited.Add(nb);
                            q.Enqueue(nb);
                        }
                    }
                    best.Add((area, mnx, mny, mxx, mxy));
                }
                var top = best.OrderByDescending(c => c.area).Take(3).ToList();
                foreach (var c in top)
                {
                    var bboxArea = (c.maxX - c.minX + 1L) * (c.maxY - c.minY + 1L);
                    var solidity = c.area * 100.0 / Math.Max(1, bboxArea);
                    sb.AppendLine($"{Path.GetFileName(file)} red={redSet.Count} comp={c.area} bbox=({c.minX},{c.minY})-({c.maxX},{c.maxY}) solidity={solidity:F0}%");
                }
                sb.AppendLine($"{Path.GetFileName(file)} COMPONENTS={best.Count}");
            }
            catch (Exception ex)
            {
                sb.AppendLine($"{Path.GetFileName(file)} ERR {ex.Message}");
            }
        }
        var outPath = Path.Combine(Path.GetTempPath(), "nex_repro", "app_fan_capture", "all_components.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
        File.WriteAllText(outPath, sb.ToString());
        Assert.True(true);
    }

    /// <summary>DIAGNOSTIC: crop the largest red component of a run frame to a PNG
    /// for visual inspection, plus save a full-frame downsized PNG.</summary>
    [Fact]
    public static void Dump_crop_largest_run_component()
    {
        var dir = "C:\\Users\\Andrew\\AppData\\Local\\SenderNG\\fans";
        var newest = Directory.GetFiles(dir, "*.bmp")
            .Select(f => new FileInfo(f))
            .OrderByDescending(f => f.LastWriteTime)
            .First();
        using var img = Image.Load<Rgba32>(newest.FullName);
        var w = img.Width;
        var h = img.Height;
        // bbox of the largest component found in the components scan:
        // comp=8479 bbox=(168,336)-(708,514)
        var minX = 168 - 12;
        var minY = 336 - 12;
        var maxX = 708 + 12;
        var maxY = 514 + 12;
        var outDir = Path.Combine(Path.GetTempPath(), "nex_repro", "app_fan_capture");
        Directory.CreateDirectory(outDir);
        using (var crop = img.Clone(x => x.Crop(new SixLabors.ImageSharp.Rectangle(minX, minY, maxX - minX + 1, maxY - minY + 1)))) { crop.SaveAsPng(Path.Combine(outDir, "crop_run_newest.png")); }
        using (var small = img.Clone(x => x.Resize(960, 504))) { small.SaveAsPng(Path.Combine(outDir, "full_run_newest.png")); }
        Assert.True(true);
    }

    /// <summary>DIAGNOSTIC: color histogram of a region of pixels (the fan band vs
    /// the idle thin wire) to fingerprint the rendering path.</summary>
    [Fact]
    public static void Dump_band_color_histogram()
    {
        var outDir = Path.Combine(Path.GetTempPath(), "nex_repro", "app_fan_capture");
        Directory.CreateDirectory(outDir);
        var dir = "C:\\Users\\Andrew\\AppData\\Local\\SenderNG\\fans";
        var work = new (string name, string file, int minX, int minY, int maxX, int maxY, int redLo, int redHi)[]
        {
            // newest app capture resolved dynamically (spike-renamed), band region
            // relative to the 56x24 grid from the components scan
            ("newest-band", Path.Combine(dir, Directory.GetFiles(dir, "*.bmp").Select(f => new FileInfo(f)).OrderByDescending(f => f.LastWriteTime).First().Name), 168, 336, 708, 514, 120, 220),
            // idle wire bbox (comp(746,459)-(964,613), comp(965,615)-(1124,906)) of the
            // newest idle capture
            ("newest-wire", Path.Combine(dir, Directory.GetFiles(dir, "*.bmp").Select(f => new FileInfo(f)).OrderByDescending(f => f.LastWriteTime).First().Name), 726, 439, 1144, 926, 120, 220),
        };
        var sb = new StringBuilder();
        foreach (var (name, file, minX, minY, maxX, maxY, redLo, redHi) in work)
        {
            using var img = Image.Load<Rgba32>(file);
            var hist = new Dictionary<int, int>();
            var n = 0;
            for (var y = minY; y <= maxY; y++)
                for (var x = minX; x <= maxX; x++)
                {
                    var px = img[x, Math.Min(y, img.Height - 1)];
                    if (px.R >= redLo && px.R <= redHi && px.R > px.G * 1.25 && px.R > px.B * 1.25)
                    {
                        var key = (px.R << 16) | (px.G << 8) | px.B;
                        hist[key] = hist.GetValueOrDefault(key) + 1;
                        n++;
                    }
                }
            sb.AppendLine($"{name} red-ish px={n}");
            foreach (var kv in hist.OrderByDescending(k => k.Value).Take(8))
            {
                var r = (kv.Key >> 16) & 0xFF;
                var g = (kv.Key >> 8) & 0xFF;
                var b = kv.Key & 0xFF;
                sb.AppendLine($"   #{r:X2}{g:X2}{b:X2} x{kv.Value}");
            }
        }
        File.WriteAllText(Path.Combine(outDir, "band_hist.txt"), sb.ToString());
        Assert.True(true);
    }

    /// <summary>DIAGNOSTIC: histogram the harness's own rendered cone frame to see
    /// whether the app-run band color #C3374C is the normal cone fill or a
    /// wire-billboard artifact.</summary>
    [Fact]
    public static void Dump_harness_cone_histogram()
    {
        var outDir = Path.Combine(Path.GetTempPath(), "nex_repro", "app_fan_capture");
        Directory.CreateDirectory(outDir);
        using var img = Image.Load<Rgba32>(Path.Combine(Path.GetTempPath(), "nex_repro", "cone_only", "frame_0.png"));
        var hist = new Dictionary<int, int>();
        var n = 0;
        for (var y = 0; y < img.Height; y++)
            for (var x = 0; x < img.Width; x++)
            {
                var px = img[x, y];
                if (px.R >= 120 && px.R > px.G * 1.25 && px.R > px.B * 1.25)
                {
                    var key = (px.R << 16) | (px.G << 8) | px.B;
                    hist[key] = hist.GetValueOrDefault(key) + 1;
                    n++;
                }
            }
        var sb = new StringBuilder("harness cone_only frame_0:\n");
        sb.AppendLine($"red-biased px={n}");
        foreach (var kv in hist.OrderByDescending(k => k.Value).Take(8))
        {
            var r = (kv.Key >> 16) & 0xFF;
            var g = (kv.Key >> 8) & 0xFF;
            var b = kv.Key & 0xFF;
            sb.AppendLine($"   #{r:X2}{g:X2}{b:X2} x{kv.Value}");
        }
        File.WriteAllText(Path.Combine(outDir, "harness_cone_hist.txt"), sb.ToString());
        Assert.True(true);
    }

    [Fact]
    public static void Dump_fan_frame_bmp()
    {
        var dir = "C:\\Users\\Andrew\\AppData\\Local\\SenderNG\\fans";
        var outDir = Path.Combine(Path.GetTempPath(), "nex_repro", "app_fan_capture");
        Directory.CreateDirectory(outDir);
        foreach (var file in Directory.GetFiles(dir, "*.bmp").OrderBy(f => f))
        {
            try
            {
                using var img = Image.Load<Rgba32>(file);
                var name = Path.GetFileNameWithoutExtension(file);
                var sb = ProbeStaticImage(img, name);
                File.WriteAllText(Path.Combine(outDir, $"{name}.props.txt"), sb.ToString());
            }
            catch (Exception ex)
            {
                File.AppendAllText(Path.Combine(outDir, "errors.txt"), $"{Path.GetFileName(file)} {ex.Message}{Environment.NewLine}");
            }
        }
        Assert.True(true);
    }

    [Fact]
    public static void Dump_clean_frame_bmp()
    {
        var dir = "C:\\Users\\Andrew\\AppData\\Local\\SenderNG\\fans";
        var outDir1 = Path.Combine(Path.GetTempPath(), "nex_repro", "app_fan_capture");
        Directory.CreateDirectory(outDir1);
        foreach (var file in Directory.GetFiles(dir, "*.bmp").OrderBy(f => f))
        {
            try
            {
                using var img = Image.Load<Rgba32>(file);
                var name = Path.GetFileNameWithoutExtension(file);
                var sb = ProbeStaticImage(img, name);
                File.WriteAllText(Path.Combine(outDir1, $"{name}.props.txt"), sb.ToString());
            }
            catch (Exception ex)
            {
                File.AppendAllText(Path.Combine(outDir1, "errors.txt"), $"{Path.GetFileName(file)} {ex.Message}{Environment.NewLine}");
            }
        }
        Assert.True(true);
    }

    [Fact]
    public static void Dump_fan_vs_clean_diff()
    {
        var outDir = Path.Combine(Path.GetTempPath(), "nex_repro", "app_fan_capture");
        Directory.CreateDirectory(outDir);
        var dir = "C:\\Users\\Andrew\\AppData\\Local\\SenderNG\\fans";
        var all = Directory.GetFiles(dir, "*.bmp").Select(f => new FileInfo(f)).OrderByDescending(f => f.LastWriteTime).Select(f => f.FullName).ToList();
        var fanPath = all[0];
        var cleanPath = all.Count > 1 ? all[1] : all[0];
        using var fan = Image.Load<Rgba32>(fanPath);
        using var clean = Image.Load<Rgba32>(cleanPath);
        var w = Math.Min(fan.Width, clean.Width);
        var h = Math.Min(fan.Height, clean.Height);
        var sb = new StringBuilder($"diff {w}x{h}{Environment.NewLine}");
        var diffCells = new int[56 * 24];
        var redAddCells = new int[56 * 24];
        var colors = new Dictionary<string, int>();
        int addMinX = w, addMinY = h, addMaxX = -1, addMaxY = -1;
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                var f = fan[x, y];
                var c = clean[x, y];
                var same = f.R == c.R && f.G == c.G && f.B == c.B;
                if (same)
                    continue;
                diffCells[(y * 24 / h) * 56 + (x * 56 / w)]++;
                var isRedF = f.R > 150 && f.R > f.G * 1.5 && f.R > f.B * 1.5;
                var isRedC = c.R > 150 && c.R > c.G * 1.5 && c.R > c.B * 1.5;
                if (isRedF && !isRedC)
                {
                    redAddCells[(y * 24 / h) * 56 + (x * 56 / w)]++;
                    addMinX = Math.Min(addMinX, x); addMaxX = Math.Max(addMaxX, x);
                    addMinY = Math.Min(addMinY, y); addMaxY = Math.Max(addMaxY, y);
                    colors[$"{f.R:X2}{f.G:X2}{f.B:X2}"] = colors.GetValueOrDefault($"{f.R:X2}{f.G:X2}{f.B:X2}") + 1;
                }
                else if (isRedC && !isRedF)
                    sb.AppendLine($"lost-red at ({x},{y}) fan=({f.R},{f.G},{f.B}) clean=({c.R},{c.G},{c.B})");
            }
        }
        sb.AppendLine($"red-ADD bbox[({addMinX},{addMinY})-({addMaxX},{addMaxY})] cells={redAddCells.Sum()}");
        foreach (var color in colors.Keys.OrderByDescending(k => colors[k]))
            sb.AppendLine($"fan-color {color} x{colors[color]}");
        var redMap = new StringBuilder("added-red density map (56x24):" + Environment.NewLine);
        for (var cy = 0; cy < 24; cy++)
        {
            for (var cx = 0; cx < 56; cx++)
            {
                var count = redAddCells[cy * 56 + cx];
                var chars = " .:-=+*#%@";
                var idx = count == 0 ? 0 : Math.Min(9, 1 + (int)Math.Log10(count));
                redMap.Append(chars[idx]);
            }
            redMap.AppendLine();
        }
        sb.AppendLine(redMap.ToString());
        Directory.CreateDirectory(outDir);
        File.WriteAllText(Path.Combine(outDir, "fan_vs_clean_diff.txt"), sb.ToString());
        Assert.True(true);
    }

    private static string ProbeStaticImage(Image<Rgba32> img, string label)    {
        var w = img.Width;
        var h = img.Height;
        var sb = new StringBuilder();
        sb.AppendLine($"{label} size {w}x{h}");
        var counts = new Dictionary<string, int>
        {
            ["pure-red"] = 0, ["red-biased"] = 0, ["white"] = 0, ["grey"] = 0, ["dark"] = 0, ["other"] = 0,
        };
        int pureMinX = w, pureMinY = h, pureMaxX = -1, pureMaxY = -1;
        int biMinX = w, biMinY = h, biMaxX = -1, biMaxY = -1;
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                var px = img[x, y];
                var r = px.R;
                var g = px.G;
                var b = px.B;
                string cls;
                if (r > 200 && g < 80 && b < 80)
                {
                    cls = "pure-red";
                    pureMinX = Math.Min(pureMinX, x); pureMaxX = Math.Max(pureMaxX, x);
                    pureMinY = Math.Min(pureMinY, y); pureMaxY = Math.Max(pureMaxY, y);
                }
                else if (r > 150 && r > g * 1.5 && r > b * 1.5)
                {
                    cls = "red-biased";
                    biMinX = Math.Min(biMinX, x); biMaxX = Math.Max(biMaxX, x);
                    biMinY = Math.Min(biMinY, y); biMaxY = Math.Max(biMaxY, y);
                }
                else if (r > 200 && g > 200 && b > 200)
                    cls = "white";
                else if (Math.Abs(r - g) < 8 && Math.Abs(g - b) < 8 && r < 150)
                    cls = "grey";
                else if (r < 30 && g < 30 && b < 30)
                    cls = "dark";
                else
                    cls = "other";
                counts[cls]++;
            }
        }
        static string Box(int minX, int minY, int maxX, int maxY) =>
            maxX < 0 ? "none" : $"({minX},{minY})-({maxX},{maxY})";
        sb.AppendLine($"pure-red={counts["pure-red"]} bbox[{Box(pureMinX, pureMinY, pureMaxX, pureMaxY)}]");
        sb.AppendLine($"red-biased={counts["red-biased"]} bbox[{Box(biMinX, biMinY, biMaxX, biMaxY)}]");
        sb.AppendLine($"white={counts["white"]} grey={counts["grey"]} dark={counts["dark"]} other={counts["other"]}");
        sb.AppendLine($"red-% = {counts["red-biased"] * 100.0 / (w * h):F2}");
        var cells = new int[56 * 24];
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                var px = img[x, y];
                if (px.R > 150 && px.R > px.G * 1.5 && px.R > px.B * 1.5)
                    cells[(y * 24 / h) * 56 + (x * 56 / w)]++;
            }
        }
        var redMap = new StringBuilder("red-density map (56x24 cells):" + Environment.NewLine);
        for (var cy = 0; cy < 24; cy++)
        {
            for (var cx = 0; cx < 56; cx++)
            {
                var count = cells[cy * 56 + cx];
                var chars = " .:-=+*#%@";
                var idx = count == 0 ? 0 : Math.Min(9, 1 + (int)Math.Log10(count));
                redMap.Append(chars[idx]);
            }
            redMap.AppendLine();
        }
        sb.AppendLine(redMap.ToString());
        return sb.ToString();
    }

    private static void RenderFrames(NexPlotScene scene, string outDir, int frames, Action? perFrame = null)
    {
        var rc = scene.Engine.CreateRenderContext();
        var rb = new NexGpuReadback(scene.Context, (uint)W, (uint)H);

        for (var i = 0; i < frames; i++)
        {
            var cam = scene.UpdateViewport(rc, 1f / 60f);
            rc.Update(new HelixToolkit.Nex.Maths.Size(W, H), cam);
            rc.RenderParams.BackgroundColor = new Color4(5f, 5f, 5f, 1f);

            scene.Context.WaitAll(true);

            scene.Engine.BeginFrame();
            var cmd = scene.Engine.RenderOffscreen(rc, scene.WorldData, rb.EngineTarget, null);
            scene.Engine.Submit(cmd, default, rb.EngineSyncInfo);

            scene.Context.WaitAll(true);

            var ptr = Marshal.AllocHGlobal(W * H * 4);
            try
            {
                if (rb.CopyFrame(ptr, W * 4))
                {
                    WritePNG(Path.Combine(outDir, $"frame_{i}.png"), ptr, W, H);
                    File.AppendAllText(
                        Path.Combine(outDir, "probe.txt"),
                        $"frame{i}: {ProbePixels(ptr, W, H)}{Environment.NewLine}");
                    File.AppendAllText(
                        Path.Combine(outDir, "map.txt"),
                        $"frame{i}:{Environment.NewLine}{RedDensityMap(ptr, W, H)}{Environment.NewLine}");
                }
            }
            finally
            {
                Marshal.FreeHGlobal(ptr);
            }

            // After readback, do the per-frame "poll" work the app does (rewrites,
            // pumps) so the NEXT frame sees whatever the introspection could do.
            perFrame?.Invoke();
        }
    }

    /// <summary>Classify every pixel: near-pure red (tool tint), substantial
    /// red (anything biased red vs green/blue), white, grey, other. Returns a
    /// compact summary with bounding boxes for the red classes.</summary>
    private static string ProbePixels(IntPtr rgba, int w, int h)
    {
        var counts = new Dictionary<string, int>
        {
            ["pure-red"] = 0, ["red-biased"] = 0, ["white"] = 0, ["grey"] = 0, ["dark"] = 0, ["other"] = 0,
        };
        int pureMinX = w, pureMinY = h, pureMaxX = -1, pureMaxY = -1;
        int biMinX = w, biMinY = h, biMaxX = -1, biMaxY = -1;
        for (var y = 0; y < h; y++)
        {
            var row = rgba + y * w * 4;
            for (var x = 0; x < w; x++)
            {
                var r = Marshal.ReadByte(row + x * 4 + 0);
                var g = Marshal.ReadByte(row + x * 4 + 1);
                var b = Marshal.ReadByte(row + x * 4 + 2);
                string cls;
                if (r > 200 && g < 80 && b < 80)
                {
                    cls = "pure-red";
                    pureMinX = Math.Min(pureMinX, x); pureMaxX = Math.Max(pureMaxX, x);
                    pureMinY = Math.Min(pureMinY, y); pureMaxY = Math.Max(pureMaxY, y);
                }
                else if (r > 150 && r > g * 1.5 && r > b * 1.5)
                {
                    cls = "red-biased";
                    biMinX = Math.Min(biMinX, x); biMaxX = Math.Max(biMaxX, x);
                    biMinY = Math.Min(biMinY, y); biMaxY = Math.Max(biMaxY, y);
                }
                else if (r > 200 && g > 200 && b > 200)
                    cls = "white";
                else if (Math.Abs(r - g) < 8 && Math.Abs(g - b) < 8 && r < 150)
                    cls = "grey";
                else if (r < 30 && g < 30 && b < 30)
                    cls = "dark";
                else
                    cls = "other";
                counts[cls]++;
            }
        }
        static string Box(int minX, int minY, int maxX, int maxY) =>
            maxX < 0 ? "none" : $"({minX},{minY})-({maxX},{maxY})";
        return $"pure-red={counts["pure-red"]} bbox[{Box(pureMinX, pureMinY, pureMaxX, pureMaxY)}] "
            + $"red-biased={counts["red-biased"]} bbox[{Box(biMinX, biMinY, biMaxX, biMaxY)}] "
            + $"white={counts["white"]} grey={counts["grey"]} dark={counts["dark"]} other={counts["other"]}";
    }

    /// <summary>ASCII density map (24 cols x 14 rows) of red(ish) pixels â€” lets us
    /// see the SHAPE of the red content (cone cluster vs. full-screen fan)
    /// without an image viewer.</summary>
    private static string RedDensityMap(IntPtr rgba, int w, int h)
    {
        const int cols = 24, rows = 14;
        var buckets = new int[cols * rows];
        for (var y = 0; y < h; y++)
        {
            var row = rgba + y * w * 4;
            for (var x = 0; x < w; x++)
            {
                var r = Marshal.ReadByte(row + x * 4 + 0);
                var g = Marshal.ReadByte(row + x * 4 + 1);
                var b = Marshal.ReadByte(row + x * 4 + 2);
                if (r > 150 && r > g * 1.5 && r > b * 1.5)
                    buckets[y * rows / h * cols + x * cols / w]++;
            }
        }
        var sb = new StringBuilder();
        for (var y = 0; y < rows; y++)
        {
            for (var x = 0; x < cols; x++)
            {
                var n = buckets[y * cols + x];
                sb.Append(n == 0 ? '.' : n < 5 ? '+' : n < 25 ? '#' : '@');
            }
            sb.AppendLine();
        }
        return sb.ToString();
    }

    // â”€â”€â”€ Minimal PNG writer (RGB, no dependencies) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private static void WritePNG(string path, IntPtr rgba, int w, int h)
    {
        using var fs = File.Create(path);

        fs.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);

        var ihdr = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(0), w);
        BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(4), h);
        ihdr[8] = 8;
        ihdr[9] = 2;
        ihdr[10] = 0;
        ihdr[11] = 0;
        ihdr[12] = 0;
        WritePNGChunk(fs, "IHDR", ihdr);

        using var idatBuf = new MemoryStream();
        using (var zlib = new ZLibStream(idatBuf, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            for (var y = 0; y < h; y++)
            {
                zlib.WriteByte(0);
                var row = rgba + y * w * 4;
                for (var x = 0; x < w; x++)
                {
                    zlib.WriteByte(Marshal.ReadByte(row + x * 4 + 0));
                    zlib.WriteByte(Marshal.ReadByte(row + x * 4 + 1));
                    zlib.WriteByte(Marshal.ReadByte(row + x * 4 + 2));
                }
            }
        }
        idatBuf.Position = 0;
        WritePNGChunk(fs, "IDAT", idatBuf.ToArray());

        WritePNGChunk(fs, "IEND", []);
    }

    private static void WritePNGChunk(Stream s, string type, byte[] data)
    {
        Span<byte> lenBuf = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(lenBuf, data.Length);
        s.Write(lenBuf);

        var typeBytes = Encoding.ASCII.GetBytes(type);
        s.Write(typeBytes);
        s.Write(data);

        var crc = Crc32(typeBytes, data);
        Span<byte> crcBuf = stackalloc byte[4];
        crcBuf[0] = (byte)(crc >> 24);
        crcBuf[1] = (byte)(crc >> 16);
        crcBuf[2] = (byte)(crc >> 8);
        crcBuf[3] = (byte)crc;
        s.Write(crcBuf);
    }

    private static uint Crc32(byte[] a, byte[] b)
    {
        Span<uint> table = stackalloc uint[256];
        for (uint n = 0; n < 256; n++)
        {
            var c = n;
            for (var k = 0; k < 8; k++)
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            table[(int)n] = c;
        }

        uint crc = 0xFFFFFFFF;
        foreach (var x in a)
            crc = table[(int)((crc ^ x) & 0xFF)] ^ (crc >> 8);
        foreach (var x in b)
            crc = table[(int)((crc ^ x) & 0xFF)] ^ (crc >> 8);
        return crc ^ 0xFFFFFFFF;
    }
}
