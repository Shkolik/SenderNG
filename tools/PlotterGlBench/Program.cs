/*
 * Program.cs - disposable segment-count benchmark harness for the Plotter.GL spike
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

// Usage: PlotterGlBench [--width 1280] [--height 720] [--frames 120]
//                        [--sizes 10000,50000,100000,500000,1000000]

using SenderNG.Modules.Common.Render;
using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Runtime.InteropServices;
using SenderNG.Modules.Plotter.GL.Rendering;
using SenderNG.Modules.Plotter.GL.Scene;

namespace PlotterGlBench;

/// <summary>Offscreen segment-count benchmark: for each synthetic toolpath
/// size, measures populate (AddSegment CPU), upload (first pump after
/// populate), per-frame render+readback FPS/avg/p95, isolated mean readback,
/// resize, and RSS — through the production GL pipeline (one shared context,
/// single-sample FBO, Avalonia skipped entirely). Any GL error fails the run
/// with a non-zero exit; a context-creation cold-start flake means re-run,
/// not a code change.</summary>
internal static unsafe class Program
{
    /// <summary>Near-white clear (checkpoint-1 ruling) — the view's opaque
    /// panel background equivalent.</summary>
    private static readonly byte[] ClearColor = [250, 250, 250, 255];

    private const int WarmupFrames = 10;

    // Synthetic serpentine geometry (mm): raster rows at a 20mm pitch, each
    // row split into 5mm cut segments, alternate Rapid return between rows.
    private const float RowPitchMm = 20f;
    private const float RowWidthMm = 200f;
    private const float SegmentLengthMm = 5f;

    private static int Main(string[] args)
    {
        int width = 1280, height = 720, frames = 120;
        int[] sizes = [10_000, 50_000, 100_000, 500_000, 1_000_000];
        try
        {
            for (var i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--width":
                        width = int.Parse(args[++i], CultureInfo.InvariantCulture);
                        break;
                    case "--height":
                        height = int.Parse(args[++i], CultureInfo.InvariantCulture);
                        break;
                    case "--frames":
                        frames = int.Parse(args[++i], CultureInfo.InvariantCulture);
                        break;
                    case "--sizes":
                        sizes = args[++i].Split(',')
                            .Select(s => int.Parse(s.Trim(), CultureInfo.InvariantCulture))
                            .ToArray();
                        break;
                    default:
                        throw new ArgumentException($"unknown argument '{args[i]}'");
                }
            }
            if (width <= 0 || height <= 0 || frames <= 0 || sizes.Length == 0 || sizes.Any(s => s <= 0))
                throw new ArgumentException("width/height/frames/sizes must be positive");
        }
        catch (Exception ex)
            when (ex is ArgumentException or FormatException or OverflowException or IndexOutOfRangeException)
        {
            Console.Error.WriteLine($"PlotterGlBench: bad arguments — {ex.Message}");
            Console.Error.WriteLine(
                "Usage: PlotterGlBench [--width 1280] [--height 720] [--frames 120] " +
                "[--sizes 10000,50000,100000,500000,1000000]");
            return 2;
        }

        try
        {
            using var ctx = GlContext.Create();
            PrintHeader(ctx, width, height, frames);

            var results = new List<RowResult>(sizes.Length);
            foreach (var n in sizes)
                results.Add(RunSize(ctx, n, width, height, frames));

            PrintTable(results);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"PlotterGlBench: FAILED — {ex.GetType().Name}: {ex.Message}");
            return 1;
        }
    }

    private static void PrintHeader(GlContext ctx, int width, int height, int frames)
    {
        Console.WriteLine();
        Console.WriteLine("PlotterGlBench — offscreen segment-count benchmark (SenderNG Plotter.GL spike)");
        Console.WriteLine($"GL_VERSION:  {ctx.Version}");
        Console.WriteLine($"GL_RENDERER: {ctx.Renderer}");
        Console.WriteLine($"GL_VENDOR:   {ctx.Vendor}");
        Console.WriteLine(
            $"OS:          {RuntimeInformation.OSDescription} ({RuntimeInformation.ProcessArchitecture})");
        Console.WriteLine(
            $"Viewport:    {width}x{height} px, offscreen FBO (RGBA8 + depth24, single sample)");
        Console.WriteLine(
            "Supersample: off — MsaaMode.None; the view-side supersample factor is not part of this bench");
        Console.WriteLine(
            $"Frames:      {frames} measured per size after {WarmupFrames} warm-up, one shared GL context");
        Console.WriteLine();
    }

    private static RowResult RunSize(GlContext ctx, int segments, int width, int height, int frames)
    {
        Console.WriteLine(
            $"--- {segments.ToString("N0", CultureInfo.InvariantCulture)} segments ---");
        using var rb = new GlReadback(ctx, (uint)width, (uint)height);
        using var scene = new GlPlotScene(ctx);
        ctx.MakeCurrent();
        scene.SetViewportSize(width, height);

        // 1a. Populate: the AddSegment loop is pure CPU staging.
        var layer = scene.AddCarriagePlane();
        var sw = Stopwatch.StartNew();
        Populate(layer, segments);
        sw.Stop();
        var populateMs = sw.Elapsed.TotalMilliseconds;

        // 1b. Camera once per size, after the content exists (auto-framing
        // reads the layer bounds).
        scene.SetViewPreset(ViewPreset.Isometric3D);

        // 2. Upload: the first pump after populate is granted unconditionally
        // (RebuildGate's first grant) and ships the whole plane.
        sw.Restart();
        scene.PumpCarriagePlanes();
        sw.Stop();
        var uploadMs = sw.Elapsed.TotalMilliseconds;

        var frame = new byte[width * height * 4];
        var resized = new byte[Math.Max(width, 1920) * Math.Max(height, 1080) * 4];
        var frameMs = new List<double>(frames);
        var copyMs = new List<double>(frames);
        unsafe
        {
            fixed (byte* p = frame)
            {
                for (var i = 0; i < WarmupFrames; i++)
                    RunFrame(rb, scene, width, height, p);
                ThrowIfGlError(ctx, "after warm-up frames");

                for (var i = 0; i < frames; i++)
                {
                    sw.Restart();
                    rb.BeginFrame(ClearColor);
                    scene.RenderFrame((uint)width, (uint)height);
                    var afterDraw = sw.Elapsed.TotalMilliseconds;
                    if (!rb.CopyFrame((nint)p, width * 4))
                        throw new InvalidOperationException("CopyFrame returned false");
                    sw.Stop();
                    frameMs.Add(sw.Elapsed.TotalMilliseconds);
                    copyMs.Add(sw.Elapsed.TotalMilliseconds - afterDraw);
                }
                ThrowIfGlError(ctx, "after measured frames");

                // 5. Resize round trip: 1920x1080 + one frame, back + one frame.
                // (With a run already at 1920x1080 the resizes no-op by design.)
                fixed (byte* big = resized)
                {
                    sw.Restart();
                    rb.Resize(1920, 1080);
                    rb.BeginFrame(ClearColor);
                    scene.RenderFrame(1920, 1080);
                    if (!rb.CopyFrame((nint)big, 1920 * 4))
                        throw new InvalidOperationException("CopyFrame returned false (1920x1080)");
                    rb.Resize((uint)width, (uint)height);
                    rb.BeginFrame(ClearColor);
                    scene.RenderFrame((uint)width, (uint)height);
                    if (!rb.CopyFrame((nint)p, width * 4))
                        throw new InvalidOperationException("CopyFrame returned false (restore)");
                    sw.Stop();
                    ThrowIfGlError(ctx, "after resize round trip");
                }
            }
        }
        var resizeMs = sw.Elapsed.TotalMilliseconds;

        // 6. Memory: process working set + managed heap at the end of this
        // size's run.
        var rssMb = Process.GetCurrentProcess().WorkingSet64 / (1024.0 * 1024.0);
        var heapMb = GC.GetTotalMemory(false) / (1024.0 * 1024.0);

        var sorted = frameMs.OrderBy(m => m).ToArray();
        return new RowResult(
            segments,
            populateMs,
            uploadMs,
            Fps: 1000.0 / frameMs.Average(),
            AvgMs: frameMs.Average(),
            P95Ms: NearestRankP95(sorted),
            ReadbackMs: copyMs.Average(),
            ResizeMs: resizeMs,
            RssMb: rssMb,
            HeapMb: heapMb,
            MinMs: sorted[0],
            MaxMs: sorted[^1]);
    }

    /// <summary>Serpentine raster: cut rows of 5mm segments at a 20mm pitch,
    /// alternating direction, one Rapid return per row — exactly N segments
    /// (the last row may be truncated).</summary>
    private static void Populate(ICarriagePlaneLayer layer, int segments)
    {
        var added = 0;
        var rowIndex = 0;
        var piecesPerRow = (int)(RowWidthMm / SegmentLengthMm);
        while (added < segments)
        {
            var y = rowIndex * RowPitchMm;
            var leftToRight = rowIndex % 2 == 0;
            var x0 = leftToRight ? 0f : RowWidthMm;
            var x1 = leftToRight ? RowWidthMm : 0f;
            for (var i = 0; i < piecesPerRow && added < segments; i++)
            {
                var a = x0 + (x1 - x0) * (i / (float)piecesPerRow);
                var b = x0 + (x1 - x0) * ((i + 1) / (float)piecesPerRow);
                layer.AddSegment(
                    new LineSegment(new Vector3(a, y, 0f), new Vector3(b, y, 0f)),
                    MotionType.Cut);
                added++;
            }
            if (added < segments)
            {
                // Rapid return: the row's end up to the next row's start
                // (same side — the next row runs the other way).
                var nextY = (rowIndex + 1) * RowPitchMm;
                layer.AddSegment(
                    new LineSegment(new Vector3(x1, y, 0f), new Vector3(x1, nextY, 0f)),
                    MotionType.Rapid);
                added++;
            }
            rowIndex++;
        }
    }

    /// <summary>One measured frame: clear → render → copy into the pinned
    /// destination (BeginFrame and RenderFrame are timed as the draw half,
    /// CopyFrame as the readback half).</summary>
    private static void RunFrame(GlReadback rb, GlPlotScene scene, int w, int h, byte* dest)
    {
        rb.BeginFrame(ClearColor);
        scene.RenderFrame((uint)w, (uint)h);
        if (!rb.CopyFrame((nint)dest, w * 4))
            throw new InvalidOperationException("CopyFrame returned false");
    }

    private static void ThrowIfGlError(GlContext ctx, string when)
    {
        var err = ctx.Api.GetError();
        if ((int)err != 0)
            throw new InvalidOperationException($"GL error {(int)err} (0x{(int)err:X4}) {when}");
    }

    /// <summary>Nearest-rank p95 (ceil(0.95·n)-th value of the sorted samples).</summary>
    private static double NearestRankP95(double[] sorted) =>
        sorted[(int)Math.Ceiling(0.95 * sorted.Length) - 1];

    private static void PrintTable(IReadOnlyList<RowResult> results)
    {
        Console.WriteLine();
        Console.WriteLine(
            "| Segments | Populate ms | Upload ms | FPS | Frame avg ms | Frame p95 ms | Readback ms | Resize ms | RSS MB |");
        Console.WriteLine("| ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |");
        foreach (var r in results)
        {
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"| {r.Segments} | {r.PopulateMs:F2} | {r.UploadMs:F2} | {r.Fps:F1} | " +
                $"{r.AvgMs:F2} | {r.P95Ms:F2} | {r.ReadbackMs:F2} | {r.ResizeMs:F2} | {r.RssMb:F0} |"));
        }
        Console.WriteLine();
        Console.WriteLine("Frame ms min/max per size: " + string.Join("; ",
            results.Select(r => string.Create(CultureInfo.InvariantCulture,
                $"{r.Segments} → {r.MinMs:F2}/{r.MaxMs:F2}"))));
        Console.WriteLine("Managed heap MB per size: " + string.Join("; ",
            results.Select(r => string.Create(CultureInfo.InvariantCulture,
                $"{r.Segments} → {r.HeapMb:F0}"))));
    }

    private sealed record RowResult(
        int Segments,
        double PopulateMs,
        double UploadMs,
        double Fps,
        double AvgMs,
        double P95Ms,
        double ReadbackMs,
        double ResizeMs,
        double RssMb,
        double HeapMb,
        double MinMs,
        double MaxMs);
}
