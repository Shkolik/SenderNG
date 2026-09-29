/*
 * PlotterLiveRunHeadlessTests.cs - FULL-CHAIN live-run repro for the plotter:
 * the real app pipeline (DemoGrblService -> ProgramViewModel streamer ->
 * PlotterViewModel state/row subscriptions -> rendered frames) must move the
 * tool glyph and recolor the path in real time, with no mode switch, and the
 * header (axis letters / blocks counter) must refresh on close + reload.
 * The unit-level smoke tests poked row states directly; these drive a real
 * streamed run through the demo controller so the whole event chain is
 * exercised the way the user runs it.
 */

/*

Copyright (c) 2026 Andrew Shkolik

All rights reserved.

Redistribution and use in source and binary forms, with or without modification,
are permitted provided that the following conditions are met:

Â· Redistributions of source code must retain the above copyright notice, this
list of conditions and the following disclaimer.

Â· Redistributions in binary form must reproduce the above copyright notice, this
list of conditions and the following disclaimer in the documentation and/or
other materials provided with the distribution.

Â· Neither the name of the copyright holder nor the names of its contributors may
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

using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CNC.Core.Abstractions;
using CNC.Core.Services;
using SenderNG.Modules.Fixed;
using SenderNG.Modules.Plotter;
using Xunit;

// The deterministic program stub + view-model factory, shared with
// PlotterViewHeadlessTests. This file's own Setup(...) is a streamed
// DemoGrblService run and is deliberately NOT what the flag tests use.
using static SenderNG.Tests.PlotterTestStubs;

namespace SenderNG.Tests;

public class PlotterLiveRunHeadlessTests
{
    /// <summary>A long square spiral so the tool glyph travels far across the
    /// fitted viewport and the green frontier has room to advance.</summary>
    private static string SpiralProgram(int segments)
    {
        var sb = new StringBuilder("G21\nG90\n");
        for (var i = 0; i < segments; i++)
        {
            var x = 10 + 80 * (i % 100) / 100d;
            var y = 10 + 80 * ((i / 100) % 10) / 10d;
            sb.AppendLine($"G1 X{x:F3} Y{y:F3} Z0.5 F240");
        }
        return sb.ToString();
    }

    /// <summary>Wing-like 4-axis program: span Z sweeps, chord X oscillates,
    /// thickness A oscillates, tower-1 vertical Y constant.</summary>
    private static string WingProgram(int segments)
    {
        var sb = new StringBuilder("G21\nG90\n");
        for (var i = 0; i <= segments; i++)
        {
            var t = i / (double)segments;
            var z = 279 * t;
            var x = 55 + 50 * Math.Sin(t * Math.PI * 4);
            var a = 13 + 12 * Math.Sin(t * Math.PI * 4);
            sb.AppendLine($"G1 X{x:F3} Y27 Z{z:F3} A{a:F3} F240");
        }
        return sb.ToString();
    }

    /// <summary>Delays each streamed line's reply (the demo otherwise streams
    /// thousands of lines in a few milliseconds â€” faster than frames can be
    /// sampled). The run then spans a sampleable window while every other
    /// behavior stays identical to the plain demo.</summary>
    private sealed class SlowSendWrapper : IGrblService
    {
        private readonly DemoGrblService _inner;
        public SlowSendWrapper(DemoGrblService inner) => _inner = inner;

        public bool IsConnected => _inner.IsConnected;
        public Task ConnectAsync(string port) => _inner.ConnectAsync(port);
        public void Disconnect() => _inner.Disconnect();
        public Task SendAsync(string line) => _inner.SendAsync(line);
        public void SendRealtime(byte code) => _inner.SendRealtime(code);
        public IObservable<MachineState> State => _inner.State;
        public ControllerCapabilities Capabilities => _inner.Capabilities;
        public IObservable<string> ReceivedLines => _inner.ReceivedLines;
        public IObservable<WorkParameters> WorkParametersChanges => _inner.WorkParametersChanges;
        public string ConnectionId => _inner.ConnectionId;
        public Task RefreshWorkParametersAsync() => _inner.RefreshWorkParametersAsync();

        public async Task<string> SendProgramLineAsync(string line, CancellationToken cancellationToken = default)
        {
            await Task.Delay(2, cancellationToken).ConfigureAwait(false);
            return await _inner.SendProgramLineAsync(line, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Same delay, but the VM/streamer State stream is fully owned by
    /// the test: the demo's line replies still ACK the run, while status is
    /// whatever the test publishes (grbl check mode = frozen machine position
    /// + "Check" status). The streamer only reads Status for alarm/hold cancel,
    /// so severing it from the demo is safe.</summary>
    private sealed class StateDrivenWrapper : IGrblService
    {
        private readonly DemoGrblService _inner;
        private readonly System.Reactive.Subjects.BehaviorSubject<MachineState> _state;

        public StateDrivenWrapper(DemoGrblService inner)
        {
            _inner = inner;
            _state = new System.Reactive.Subjects.BehaviorSubject<MachineState>(new MachineState(inner.Capabilities.NumAxes));
        }

        public void Emit(MachineState state) => _state.OnNext(state);

        public bool IsConnected => _inner.IsConnected;
        public Task ConnectAsync(string port) => _inner.ConnectAsync(port);
        public void Disconnect() => _inner.Disconnect();
        public Task SendAsync(string line) => _inner.SendAsync(line);
        public void SendRealtime(byte code) => _inner.SendRealtime(code);
        public IObservable<MachineState> State => _state;
        public ControllerCapabilities Capabilities => _inner.Capabilities;
        public IObservable<string> ReceivedLines => _inner.ReceivedLines;
        public IObservable<WorkParameters> WorkParametersChanges => _inner.WorkParametersChanges;
        public string ConnectionId => _inner.ConnectionId;
        public Task RefreshWorkParametersAsync() => _inner.RefreshWorkParametersAsync();

        public async Task<string> SendProgramLineAsync(string line, CancellationToken cancellationToken = default)
        {
            await Task.Delay(2, cancellationToken).ConfigureAwait(false);
            return await _inner.SendProgramLineAsync(line, cancellationToken).ConfigureAwait(false);
        }
    }

    private static (DemoGrblService Demo, IGrblService Service, ProgramViewModel Program, PlotterViewModel Plotter, Window Window) Setup(
        string machineTypeId, int demoAxes, string gcode, Func<DemoGrblService, IGrblService>? wrapper = null)
    {
        // Mirror the app: both marshaling helpers bind to the Avalonia UI
        // dispatcher, so streamer-thread ticks land on the UI thread (the
        // default uninitialized behavior runs them inline on the caller).
        SenderNG.UiThread.Initialize();
        PlotterUi.Initialize();

        var dir = Path.Combine(Path.GetTempPath(), "plotter-live-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var settings = new SettingsService(dir);
        var profile = settings.Load();
        profile.MachineTypeId = machineTypeId;
        profile.DemoAxes = demoAxes;
        settings.Save(profile);

        var demo = new DemoGrblService(profile, localization: HeadlessTestCatalog.Locale);
        demo.ConnectAsync("demo").GetAwaiter().GetResult();
        var service = wrapper?.Invoke(demo) ?? new SlowSendWrapper(demo);
        var program = new ProgramViewModel(service);
        program.LoadGCode(gcode);
        var plotter = new PlotterViewModel(program, service, settings, HeadlessTestCatalog.Locale);
        var view = new PlotterView { DataContext = plotter };
        var window = new Window { Width = 920, Height = 560, Content = view };
        window.Show();
        return (demo, service, program, plotter, window);
    }

    /// <summary>Mill live run: the red tool glyph must move with the run and the
    /// green frontier must advance â€” from streamed rows and controller state
    /// ticks alone, no mode switch, no canvas rebuild.</summary>
    [AvaloniaFact]
    public async Task Mill_live_run_moves_glyph_and_recolors_in_realtime()
    {
        var (demo, service, program, plotter, window) = Setup(MachineTypes.Mill, 3, SpiralProgram(600));
        for (var i = 0; i < 6; i++)
            await Pump();

        // The 2ms-per-line wrapper makes the run span ~1.2s â€” sample frames
        // densely across it: early frames must be mostly blue, later frames
        // green, and the glyph must ride the frontier from start to end.
        var run = program.RunAsync();
        var frames = new List<WriteableBitmap>();
        var start = Environment.TickCount64;
        var tick = 0;
        while (!run.IsCompleted && Environment.TickCount64 - start < 8000)
        {
            await Pump();
            if ((tick++ & 1) == 0)
                frames.Add(CaptureFrame(window));
        }
        await run;
        for (var i = 0; i < 8; i++)
            await Pump();
        frames.Add(CaptureFrame(window));

        Assert.True(frames.Count >= 10, $"expected many mid-run frames, got {frames.Count}");

        // Colors: green grows monotonically with the frontier â€” the earliest
        // frame is near-blue, the last is fully green.
        var greenFirst = PlotterPixels.Count(PlotterPixels.Snapshot(frames[0]), IsDoneGreen);
        var greenLast = PlotterPixels.Count(PlotterPixels.Snapshot(frames[^1]), IsDoneGreen);
        Assert.True(greenLast > 300,
            $"the completed run must leave a green frontier, found {greenLast} green pixels");
        Assert.True(greenLast > greenFirst + 100,
            $"green frontier must advance during the run: first frame {greenFirst}, last {greenLast}");

        // Glyph: the red tool mark rides the frontier â€” it must travel from
        // near the path start to the path end.
        var first = frames.Select(Glyph).First(g => g.Count > 10);
        var last = Glyph(frames[^1]);
        Assert.True(last.Count > 10, $"the tool glyph must be visible ({last.Count} red pixels)");
        var span = Math.Sqrt(Math.Pow(last.X - first.X, 2) + Math.Pow(last.Y - first.Y, 2));
        Assert.True(span > 20,
            $"the tool glyph must travel with the run, moved {span:F1} px from ({first.X:F0},{first.Y:F0}) to ({last.X:F0},{last.Y:F0})");
    }

    /// <summary>Foam live run in the 2D view: both quadrants must recolor in
    /// real time (the wing's ZA plane is NOT empty) and the coupling-wire glyph
    /// must follow the run without a 2Dâ†”iso switch.</summary>
    [AvaloniaFact]
    public async Task Foam_2d_live_run_updates_both_quadrants_and_wire()
    {
        var (demo, service, program, plotter, window) = Setup(MachineTypes.Foam, 4, WingProgram(400));
        for (var i = 0; i < 6; i++)
            await Pump();

        Assert.False(plotter.Isometric);
        Assert.Equal(2, plotter.Planes.Count);
        Assert.True(plotter.Planes[0].Count > 0 && plotter.Planes[1].Count > 0,
            "both foam planes must carry the wing's motion");

        var run = program.RunAsync();
        var frames = new List<(int Done, WriteableBitmap Bitmap)>();
        var start = Environment.TickCount64;
        var lastDone = -1;
        while (!run.IsCompleted && Environment.TickCount64 - start < 8000)
        {
            await Pump();
            var done = plotter.DoneCount;
            if (done != lastDone)
            {
                lastDone = done;
                frames.Add((done, CaptureFrame(window)));
            }
        }
        await run;
        for (var i = 0; i < 8; i++)
            await Pump();
        frames.Add((plotter.DoneCount, CaptureFrame(window)));

        Assert.True(frames.Count >= 8, $"expected many mid-run frames, got {frames.Count}");

        // Both quadrants recolor: green must appear above AND below the middle
        // of the final frame (plane 1 is the bottom-right quadrant), and the
        // mid-run frames must already show real-time green.
        var mid = frames[frames.Count / 2].Bitmap;
        var final = frames[^1].Bitmap;
        var midFrame = PlotterPixels.Snapshot(mid);
        var finalFrame = PlotterPixels.Snapshot(final);
        var greenTop = PlotterPixels.Count(finalFrame, IsDoneGreen, yFilter: y => y < finalFrame.Height / 2);
        var greenBottom = PlotterPixels.Count(finalFrame, IsDoneGreen, yFilter: y => y >= finalFrame.Height / 2);
        Assert.True(greenTop > 100, $"plane 1 (top-left) must recolor, found {greenTop} green pixels");
        Assert.True(greenBottom > 100, $"plane 2 (bottom-right) must recolor, found {greenBottom} green pixels");
        Assert.True(PlotterPixels.Count(midFrame, IsDoneGreen) > 50,
            $"foam 2D must recolor in real time mid-run, found {PlotterPixels.Count(midFrame, IsDoneGreen)} green pixels");

        // The wire glyph (red) must be visible mid-run and travel to the end.
        // Sample it at a done-fraction anchor (the first captured frame past
        // ~15% of the run) rather than by frame index â€” frame cadence depends
        // on load, but the done counter advances deterministically with the run.
        var anchor = frames.FirstOrDefault(f => f.Done > plotter.TotalCount / 6);
        var midGlyph = Glyph(anchor.Bitmap);
        var finalGlyph = Glyph(final);
        Assert.True(midGlyph.Count > 10, $"the foam wire glyph must be visible mid-run ({midGlyph.Count} red pixels)");
        var span = Math.Sqrt(Math.Pow(finalGlyph.X - midGlyph.X, 2) + Math.Pow(finalGlyph.Y - midGlyph.Y, 2));
        Assert.True(span > 15,
            $"the wire glyph must travel with the run, moved {span:F1} px");
    }

    /// <summary>The tool glyph must follow the ACKED segment end when check mode
    /// is active ($C: grbl verifies blocks but never moves â€” the reported
    /// machine position stays frozen while the send frontier advances).</summary>
    [AvaloniaFact]
    public async Task Check_mode_glyph_follows_the_acked_end_not_the_frozen_position()
    {
        var (demo, service, program, plotter, window) = Setup(MachineTypes.Mill, 3, SpiralProgram(400), d => new StateDrivenWrapper(d));
        var wrap = (StateDrivenWrapper)service;
        for (var i = 0; i < 6; i++)
            await Pump();

        // Frozen machine position for the whole run (grbl in check mode: no
        // motion). At 15,35 it sits a long way from the spiral's far corner, so
        // a glyph following it would stay put while the acked end must travel.
        wrap.Emit(new MachineState { Status = "Check", Positions = new double[] { 15, 35, 10 } });
        for (var i = 0; i < 6; i++)
            await Pump();
        Assert.True(plotter.CheckMode, "a Check status must flip the VM into check mode");

        var run = program.RunAsync();
        var frames = new List<WriteableBitmap>();
        var start = Environment.TickCount64;
        var lastGlyph = Glyph(CaptureFrame(window));
        var traveled = 0d;
        while (!run.IsCompleted && Environment.TickCount64 - start < 8000)
        {
            await Pump();
            var g = Glyph(CaptureFrame(window));
            if (g.Count > 0 && lastGlyph.Count > 0)
                traveled = Math.Max(traveled, Math.Sqrt(Math.Pow(g.X - lastGlyph.X, 2) + Math.Pow(g.Y - lastGlyph.Y, 2)));
            lastGlyph = g;
        }
        await run;
        for (var i = 0; i < 8; i++)
            await Pump();
        frames.Add(CaptureFrame(window));

        var finalGlyph = Glyph(frames[^1]);
        Assert.True(finalGlyph.Count > 10,
            $"the tool glyph must be visible with a frozen position ({finalGlyph.Count} red pixels)");
        Assert.True(traveled > 20,
            $"a frozen machine position in check mode must NOT freeze the glyph â€” the acked end must travel, max hop {traveled:F1} px");
    }

    /// <summary>Contrast: with the SAME frozen machine position but check mode
    /// OFF the glyph must stay frozen (live position has priority).</summary>
    [AvaloniaFact]
    public async Task Idle_glyph_follows_the_live_position()
    {
        var (demo, service, program, plotter, window) = Setup(MachineTypes.Mill, 3, SpiralProgram(400), d => new StateDrivenWrapper(d));
        var wrap = (StateDrivenWrapper)service;
        for (var i = 0; i < 6; i++)
            await Pump();

        wrap.Emit(new MachineState { Status = "Idle", Positions = new double[] { 15, 35, 10 } });
        for (var i = 0; i < 6; i++)
            await Pump();
        Assert.False(plotter.CheckMode, "an Idle status must keep the VM out of check mode");

        var first = Glyph(CaptureFrame(window));
        var run = program.RunAsync();
        var start = Environment.TickCount64;
        while (!run.IsCompleted && Environment.TickCount64 - start < 8000)
            await Pump();
        await run;
        for (var i = 0; i < 8; i++)
            await Pump();
        var last = Glyph(CaptureFrame(window));

        Assert.True(first.Count > 0 && last.Count > 0, "the tool glyph must be visible at a frozen live position");
        var span = Math.Sqrt(Math.Pow(last.X - first.X, 2) + Math.Pow(last.Y - first.Y, 2));
        Assert.True(span < 5,
            $"idle (non-check) must drive the glyph from the live position â€” frozen position => frozen glyph, moved {span:F1} px");
    }

    /// <summary>Close + reload must refresh the header (blocks counter) and the
    /// canvas must keep rendering the new program.</summary>
    [AvaloniaFact]
    public async Task Header_refreshes_on_clear_and_reload()
    {
        var (demo, service, program, plotter, window) = Setup(MachineTypes.Mill, 3, SpiralProgram(100));
        for (var i = 0; i < 8; i++)
            await Pump();

        // The header reads the sendable total (G21/G90 header lines are
        // sendable too), so derive the expectation from the source itself.
        var total = plotter.TotalCount;
        Assert.Equal($"0/{total}", plotter.ProgressText);

        // Close the program: counter resets, the view must show it.
        program.ClearProgram();
        for (var i = 0; i < 8; i++)
            await Pump();
        Assert.Equal("0/0", plotter.ProgressText);

        // Reload a different program: counter must reflect the new total.
        program.LoadGCode(SpiralProgram(240));
        for (var i = 0; i < 8; i++)
            await Pump();
        var total2 = plotter.TotalCount;
        Assert.NotEqual(total, total2);
        Assert.Equal($"0/{total2}", plotter.ProgressText);

        var grey = PlotterPixels.Count(PlotterPixels.Snapshot(CaptureFrame(window)), IsPathGrey);
        Assert.True(grey > 100,
            $"the reloaded program must render, found {grey} path pixels");
    }

/// <summary>The iso loft's wire glyph must travel with a foam run â€” the
    /// carriage dots and coupling wire ride the live machine position down the
    /// shared composition. Guards the foam run in the ISO canvas.</summary>
    [AvaloniaFact]
    public async Task Iso_foam_live_run_moves_the_wire_glyph()
    {
        var (demo, service, program, plotter, window) = Setup(MachineTypes.Foam, 4, WingProgram(400));
        for (var i = 0; i < 6; i++)
            await Pump();
        window.GetVisualDescendants().OfType<CheckBox>().Single(c => c.Name == "IsoToggle").IsChecked = true;
        for (var i = 0; i < 6; i++)
            await Pump();

        var run = program.RunAsync();
        var start = Environment.TickCount64;
        var traveled = 0d;
        var lastGlyph = Glyph(CaptureFrame(window));
        var ticks = 0;
        while (!run.IsCompleted && Environment.TickCount64 - start < 8000)
        {
            await Pump();
            ticks++;
            if ((ticks & 1) == 0)
            {
                var g = Glyph(CaptureFrame(window));
                if (g.Count > 4 && lastGlyph.Count > 4)
                    traveled = Math.Max(traveled, Math.Sqrt(Math.Pow(g.X - lastGlyph.X, 2) + Math.Pow(g.Y - lastGlyph.Y, 2)));
                lastGlyph = g;
            }
        }
        await run;
        for (var i = 0; i < 8; i++)
            await Pump();

        Assert.True(plotter.DoneCount == plotter.TotalCount, "run must complete");
        Assert.True(traveled > 30,
            $"the iso wire glyph must travel with the run, max hop {traveled:F1} px");
        Assert.True(Glyph(CaptureFrame(window)).Count > 10, "the finished wing must still show the wire glyph");
    }

    /// <summary>Check mode on the iso loft: grbl $C freezes the machine
    /// position, so the wire glyph must follow the ACKED frontier instead.
    /// Guards the check-mode glyph source in the ISO canvas.</summary>
    [AvaloniaFact]
    public async Task Iso_check_mode_wire_follows_the_acked_end()
    {
        var (demo, service, program, plotter, window) = Setup(MachineTypes.Foam, 4, WingProgram(400), d => new StateDrivenWrapper(d));
        var wrap = (StateDrivenWrapper)service;
        for (var i = 0; i < 6; i++)
            await Pump();
        window.GetVisualDescendants().OfType<CheckBox>().Single(c => c.Name == "IsoToggle").IsChecked = true;
        for (var i = 0; i < 6; i++)
            await Pump();
        wrap.Emit(new CNC.Core.Services.MachineState { Status = "Check", Positions = new double[] { 15, 35, 10, 8 } });
        for (var i = 0; i < 6; i++)
            await Pump();
        Assert.True(plotter.CheckMode, "a Check status must flip the VM into check mode");

        var run = program.RunAsync();
        var start = Environment.TickCount64;
        var traveled = 0d;
        var lastGlyph = Glyph(CaptureFrame(window));
        var ticks = 0;
        while (!run.IsCompleted && Environment.TickCount64 - start < 8000)
        {
            await Pump();
            ticks++;
            if ((ticks & 1) == 0)
            {
                var g = Glyph(CaptureFrame(window));
                if (g.Count > 4 && lastGlyph.Count > 4)
                    traveled = Math.Max(traveled, Math.Sqrt(Math.Pow(g.X - lastGlyph.X, 2) + Math.Pow(g.Y - lastGlyph.Y, 2)));
                lastGlyph = g;
            }
        }
        await run;
        for (var i = 0; i < 8; i++)
            await Pump();

        Assert.True(plotter.DoneCount == plotter.TotalCount, "run must complete");
        Assert.True(traveled > 30,
            $"a frozen machine position in check mode must NOT freeze the iso wire glyph, max hop {traveled:F1} px");
    }

    /// <summary>Wing-like program whose tower-1 (Y) also wiggles so the XY
    /// plane spans a real 2D tile (a Y-constant wing collapses into the top
    /// raster row and anti-aliases into near-white, which no pixel probe can
    /// see).</summary>
    private static string WingVaryingProgram(int segments)
    {
        var sb = new StringBuilder("G21\nG90\n");
        for (var i = 0; i <= segments; i++)
        {
            var t = i / (double)segments;
            var z = 279 * t;
            var x = 55 + 50 * Math.Sin(t * Math.PI * 4);
            var y = 27 + 10 * Math.Sin(t * Math.PI * 4);
            var a = 13 + 12 * Math.Sin(t * Math.PI * 4);
            sb.AppendLine($"G1 X{x:F3} Y{y:F3} Z{z:F3} A{a:F3} F240");
        }
        return sb.ToString();
    }

    /// <summary>The foam 2D canvas composes both planes as TRUE-SCALE diagonal
    /// tiles (plane 0 top-left, plane 1 bottom-right) with an empty gap between
    /// them, and draws a machine-origin cross at each tile's origin corner —
    /// even though the origin cross sits at a tile corner that a per-quadrant
    /// clip would cut off.</summary>
    [AvaloniaFact]
    public async Task Foam_2d_renders_toscale_tiles_and_origin_crosses()
    {
        var (_, _, prog2, plotter2, window2) = Setup(MachineTypes.Foam, 4, WingVaryingProgram(400));
        for (var i = 0; i < 8; i++)
            await Pump();

        // The first render bakes+blits only plane0 (one bake per frame is rate
        // limited to 500ms). Wait past the limiter, ghost a second render so
        // plane1's raster is baked and blitted too.
        await Task.Delay(600);
        var planeCanvas = window2.GetVisualDescendants().Single(c => c.GetType().Name == "PlaneCanvas");
        planeCanvas.InvalidateVisual();
        for (var i = 0; i < 3; i++)
            await Pump();
        var frame = PlotterPixels.Snapshot(CaptureFrame(window2));

        // Introspect the live layout transform + maps so the probes are
        // computed from the real canvas geometry, not window-relative guesses.
        var pcType = planeCanvas.GetType();
        static object? GF(object o, string n) =>
            o.GetType().GetField(n, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.GetValue(o)
            ?? o.GetType().BaseType!.GetField(n, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.GetValue(o);
        var zoom = (double)GF(planeCanvas, "_zoom")!;
        var pan = (Avalonia.Vector)GF(planeCanvas, "_pan")!;

        // The baked path renders the feed-grey (0x80) stroke AA-washed against
        // white; at ~0.5px stroke most hits fall in the 140..225 range. The
        // machine-origin cross (0x88, 1px) similarly AA-washes to 120..190 and
        // its center is covered by the red tool glyph at the default position.
        bool LineGrey(byte r, byte g, byte b) =>
            Math.Abs(r - g) <= 14 && Math.Abs(g - b) <= 14 && r is >= 100 and <= 220;
        bool CrossGrey(byte r, byte g, byte b) =>
            Math.Abs(r - g) <= 10 && Math.Abs(g - b) <= 10 && r is >= 115 and <= 195;

        // Project each plane's world tile + machine-origin cross to screen from
        // the LIVE transform and per-plane maps, so the probes land exactly.
        var swb = pcType.GetMethod("SlotWorldBounds", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        var w0 = ((double MinX, double MinY, double MaxX, double MaxY))swb.Invoke(planeCanvas, new object[] { 0 })!;
        var w1 = ((double MinX, double MinY, double MaxX, double MaxY))swb.Invoke(planeCanvas, new object[] { 1 })!;
        var maps = (System.Collections.IList?)pcType.GetField("_maps", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(planeCanvas)!;
        double[] MapProps(object m)
        {
            var t = m.GetType();
            return new[] {
            (double)t.GetProperty("MinX")!.GetValue(m)!, (double)t.GetProperty("SpanX")!.GetValue(m)!,
            (double)t.GetProperty("MinY")!.GetValue(m)!, (double)t.GetProperty("SpanY")!.GetValue(m)!,
            (double)t.GetProperty("Sx")!.GetValue(m)!, (double)t.GetProperty("Sy")!.GetValue(m)!,
            (double)t.GetProperty("Ox")!.GetValue(m)!, (double)t.GetProperty("Oy")!.GetValue(m)! };
        }
        var mp0 = MapProps(maps[0]!);
        var mp1 = MapProps(maps[1]!);
        //   cross0: Wx(0) = 0 - t0.MinX,       Wy(0) = Oy - t0.MinY
        //   cross1: Wx(0) = Ox - t1.MinX,       Wy(0) = -t1.MinY
        (double Sx0, double Sy0) P(double wx, double wy) => (zoom * wx + pan.X, -zoom * wy + pan.Y);
        var bx0 = (int)Math.Round(P(-mp0[0], mp0[7] - mp0[2]).Sx0);
        var by0 = (int)Math.Round(P(-mp0[0], mp0[7] - mp0[2]).Sy0);
        var bx1 = (int)Math.Round(P(mp1[6] - mp1[0], -mp1[2]).Sx0);
        var by1 = (int)Math.Round(P(mp1[6] - mp1[0], -mp1[2]).Sy0);
        var stl0 = P(w0.MinX, w0.MaxY);
        var sbr0 = P(w0.MaxX, w0.MinY);
        var stl1 = P(w1.MinX, w1.MaxY);
        var sbr1 = P(w1.MaxX, w1.MinY);

        // The capture is the WHOLE window while the canvas is a bit smaller
        // inside it — the window chrome (title bar) pushes it down a few px.
        // Detect the canvas top = first fully-bright row in the middle columns
        // (canvas background is white; the chrome isn't) and offset all probes.
        var coy = FirstBrightRow(frame);
        var cox = 1;
        (int X0, int X1, int Y0, int Y1) W(int x0, int x1, int y0, int y1) =>
            (cox + x0, cox + x1, coy + y0, coy + y1);

        var (sx0, sx1, sy0, sy1) = W((int)Math.Round(stl0.Sx0) + 6, (int)Math.Round(stl0.Sx0) + 230, (int)Math.Round(stl0.Sy0) + 5, (int)Math.Round(sbr0.Sy0) - 5);
        int streak = CountRectHalfOpen(frame, LineGrey, sx0, sx1, sy0, sy1);
        var (b0_, b1_, c0_, c1_) = W((int)Math.Round(stl1.Sx0) + 6, (int)Math.Round(sbr1.Sx0) - 6, (int)Math.Round(stl1.Sy0) + 5, (int)Math.Round(sbr1.Sy0) - 5);
        int band = CountRectHalfOpen(frame, LineGrey, b0_, b1_, c0_, c1_);
        var (g0_, g1_, i0_, i1_) = W((int)Math.Round(sbr0.Sx0) + 2, (int)Math.Round(stl1.Sx0) - 2, (int)Math.Round(stl0.Sy0), (int)Math.Round(sbr1.Sy0));
        int gapPixels = CountRectHalfOpen(frame, LineGrey, g0_, g1_, i0_, i1_);
        var (j0_, j1_, k0_, k1_) = W(bx0 - 10, bx0 + 10, by0 - 10, by0 + 12);
        int cross1 = CountRectHalfOpen(frame, CrossGrey, j0_, j1_, k0_, k1_);
        var (m0_, m1_, n0_, n1_) = W(bx1 - 10, bx1 + 10, by1 - 10, by1 + 12);
        int cross2 = CountRectHalfOpen(frame, CrossGrey, m0_, m1_, n0_, n1_);

        Assert.True(coy >= 20 && coy <= 32, $"canvas chrome offset must be sane, got y={coy}");
        Assert.True(streak > 150, "plane0 must draw as a true-scale band in the top-left tile");
        Assert.True(band > 150, "plane1 must draw as a true-scale band in the bottom-right tile");
        Assert.True(gapPixels < 20, "the tile gap must stay empty (no full-width stretched layout)");
        Assert.True(cross1 > 4, "plane0 machine-origin cross must be drawn");
        Assert.True(cross2 > 4, "plane1 machine-origin cross must be drawn");

        // The iso canvas composes the same way; its plane1 loft + cross must
        // render too (checked via the same predicates against its own window
        // offset).
        window2.GetVisualDescendants().OfType<CheckBox>().Single(c => c.Name == "IsoToggle").IsChecked = true;
        for (var i = 0; i < 8; i++)
            await Pump();
        await Task.Delay(600);
        var isoCanvas = window2.GetVisualDescendants().Single(c => c.GetType().Name == "IsoCanvas");
        isoCanvas.InvalidateVisual();
        for (var i = 0; i < 3; i++)
            await Pump();
        var isoFrame = PlotterPixels.Snapshot(CaptureFrame(window2));
        var icoy = FirstBrightRow(isoFrame);
        int Rect(PlotterPixels.Frame f, Func<byte, byte, byte, bool> m, int x0, int x1, int y0, int y1) =>
            CountRectHalfOpen(f, m, 1 + x0, 1 + x1, icoy + y0, icoy + y1);
        var isoStreak = Rect(isoFrame, LineGrey, 12, 252, 196, 272);
        var isoBand = Rect(isoFrame, LineGrey, 268, 900, 300, 352);
        var isoCross2 = Rect(isoFrame, CrossGrey, 262, 282, 337, 359);
        Assert.True(icoy >= 20 && icoy <= 32, $"iso canvas chrome offset must be sane, got y={icoy}");
        Assert.True(isoStreak > 150, "iso plane0 must draw its small tile top-left");
        Assert.True(isoBand > 150, "iso plane1 must draw its loft across the bottom tile");
        Assert.True(isoCross2 > 4, "iso plane1 machine-origin cross must be drawn");
    }

    /// <summary>The quadrant viewports place each foam plane's tile, but the
    /// shared pan must carry the true-scale profile PAST the screen-center
    /// line — the per-slot render clip used to trim the raster right at the
    /// quadrant boundary (aux geometry drawn outside the clip survived the
    /// move; the profile did not).</summary>
    [AvaloniaFact]
    public async Task Foam_2d_pan_keeps_profiles_beyond_their_quadrant()
    {
        var (_, _, prog2, plotter2, window2) = Setup(MachineTypes.Foam, 4, WingVaryingProgram(400));
        for (var i = 0; i < 8; i++)
            await Pump();
        var planeCanvas = window2.GetVisualDescendants().Single(c => c.GetType().Name == "PlaneCanvas");

        var t = planeCanvas.GetType();
        static object? GF(object o, string n) =>
            o.GetType().GetField(n, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.GetValue(o)
            ?? o.GetType().BaseType!.GetField(n, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.GetValue(o);
        var zoom = (double)GF(planeCanvas, "_zoom")!;
        var pan = (Avalonia.Vector)GF(planeCanvas, "_pan")!;
        var w0 = ((double MinX, double MinY, double MaxX, double MaxY))
            t.GetMethod("SlotWorldBounds", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .Invoke(planeCanvas, new object[] { 0 })!;
        var left0 = (int)Math.Round(zoom * w0.MinX + pan.X);
        var right0 = (int)Math.Round(zoom * w0.MaxX + pan.X);
        var top0 = (int)Math.Round(-zoom * w0.MaxY + pan.Y);
        var bottom0 = (int)Math.Round(-zoom * w0.MinY + pan.Y);
        var area = new Rect(planeCanvas.Bounds.Size);
        var centerX = (int)Math.Round(area.Center.X);
        Assert.True(right0 < centerX, $"plane 0 must start fully inside its quadrant, right={right0} center={centerX}");

        // Pan plane 0 right so its right half crosses the canvas center line.
        var dx = centerX + 120 - right0;
        var panField = t.GetField("_pan", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        panField.SetValue(planeCanvas, new Avalonia.Vector(pan.X + dx, pan.Y));
        planeCanvas.InvalidateVisual();
        for (var i = 0; i < 3; i++)
            await Pump();
        var frame = PlotterPixels.Snapshot(CaptureFrame(window2));

        var cox = 1;
        var coy = FirstBrightRow(frame);

        // Idle feed is grey #808080, AA-washed against white.
        bool LineGrey(byte r, byte g, byte b) =>
            Math.Abs(r - g) <= 14 && Math.Abs(g - b) <= 14 && r is >= 100 and <= 220;

        var rightMoved = (int)Math.Round(zoom * w0.MaxX + pan.X + dx);
        // Probe the vertical strip just right of the canvas center, still inside
        // plane 0's own tile (the ~8mm gap keeps plane 1 some pixels further).
        var (sx0, sx1, sy0, sy1) = (cox + centerX + 6, cox + rightMoved - 6, coy + top0 + 6, coy + bottom0 - 6);
        var pastCenter = CountRectHalfOpen(frame, LineGrey, sx0, Math.Max(sx0 + 1, sx1), sy0, Math.Max(sy0 + 1, sy1));
        Assert.True(pastCenter > 30,
            $"dragging plane 0 across the center must keep the profile visible beyond it, got {pastCenter} px right of x={centerX}");
    }

    /// <summary>Rapids are RED, cuts are grey — the same split the Nex plotter
    /// draws (<c>NexCarriagePlaneLayer.RapidColor</c> 0.9/0.12/0.12). The Skia
    /// plotter used to paint rapids #c8c8c8 dashed, i.e. a paler grey than the
    /// cuts, so a G0 read as "more toolpath" rather than "a move that cuts
    /// nothing".
    /// <para>Both halves are needed. "Red appears" alone is satisfied by a
    /// red-tinted painter that floods the frame; "grey appears" alone is
    /// satisfied by the pre-fix palette, which drew rapids a paler grey. The
    /// control frame — the identical program with the G0s stripped — is what
    /// pins the red to the rapid flag specifically.</para>
    /// <para>Both plotters' rapids are red, so this is the same contract the
    /// Nex layer test pins; this one holds the Skia side.</para>
    /// <para>The fixture is the shared deterministic stub, not this file's
    /// streamed <c>Setup</c>: the assertion is about pen colour, and a streamed
    /// run would buy only timing flake.</para></summary>
    [AvaloniaFact]
    public async Task Rapids_paint_red_and_cuts_paint_grey()
    {
        // The rapid red, and specifically it. Getting this gate right took three
        // tries, and each failure taught something about the frame.
        //
        // The panel's own chrome is warm and anti-aliases into the same
        // neighbourhood: header text reads as browns (#9e5a33, #c7924f) and the
        // in-flight amber button is #c07a20. A loose "reddish" probe counts all
        // of them. Gating on both colour channels being LOW is not enough either
        // — every one of those has a low blue.
        //
        // What separates them is that pure red has only ONE non-white channel,
        // so anti-aliasing it toward white leaves g and b EQUAL. A brown or an
        // amber has a real green in it, and no amount of blending erases that
        // asymmetry: the g-b gap is 39 for #9e5a33, 67 for #c7924f, 90 for
        // #c07a20, against 0 for the toolpath. A rapid stroke is only ~1px wide,
        // so most of its pixels are partial coverage and read as pink
        // (240,143,143) — which is why the gate must accept a HIGH g too, and
        // why a "saturated red only" probe measures stroke coverage instead of
        // colour.
        static bool IsRapidRed(byte r, byte g, byte b) =>
            r >= 130 && r > g + 15 && Math.Abs(g - b) <= 12;

        var source = new StubProgramSource();
        var vm = MakeVm(source, MachineTypes.Mill);
        // A real program ping-pongs: G0 across to a new rail, a run of G1s
        // cutting it, G0 back. One G0 is NOT enough — it is a single short
        // stroke, it anti-aliases down to a handful of pixels, and the test
        // would be measuring stroke coverage rather than colour.
        source.Load(PingPongProgram(24));
        Assert.True(vm.HasGeometry, "the rapid/feed split needs a real path to paint");

        var view = new PlotterView { DataContext = vm };
        var window = new Window { Width = 920, Height = 560, Content = view };
        window.Show();
        for (var i = 0; i < 8; i++)
            await Pump();
        // Origin chrome off: the world X arrow is a red line and would be
        // indistinguishable from a rapid to the probe. It is a baked-raster
        // flag, so this has to reach a rebake.
        view.PushChromeFlags(ChromeFlags.Default with { Origin = false });
        for (var i = 0; i < 8; i++)
            await Pump();
        await DrainRebakeQueue(view);

        var frame = PlotterPixels.Snapshot(CaptureFrame(window));
        var red = PlotterPixels.Count(frame, IsRapidRed);
        var grey = PlotterPixels.Count(frame, IsPathGrey);

        Assert.True(red > 0, $"G0 rapids must paint red, found {red} red pixels");
        Assert.True(grey > 100, $"G1 cuts must still paint grey, found {grey} path pixels");

        // The control: the SAME program with the G0s stripped. If the red came
        // from anything but the rapid flag, this frame would match the first.
        var cutOnly = new StubProgramSource();
        var cutVm = MakeVm(cutOnly, MachineTypes.Mill);
        cutOnly.Load(PingPongProgram(24, withRapids: false));
        var cutView = new PlotterView { DataContext = cutVm };
        var cutWindow = new Window { Width = 920, Height = 560, Content = cutView };
        cutWindow.Show();
        for (var i = 0; i < 8; i++)
            await Pump();
        cutView.PushChromeFlags(ChromeFlags.Default with { Origin = false });
        for (var i = 0; i < 8; i++)
            await Pump();
        await DrainRebakeQueue(cutView);
        var cutRed = PlotterPixels.Count(PlotterPixels.Snapshot(CaptureFrame(cutWindow)), IsRapidRed);

        // The control is a DIFFERENCE, not a zero, and that is deliberate. An
        // exact-zero red count is unachievable in this frame for reasons that
        // have nothing to do with rapids: the tool glyph is a legitimate
        // #d02020 red, and the panel's own chrome contributes a stray dark red
        // or two near the window edge. Chasing either one to zero means
        // shrinking the probe until it stops seeing the toolpath at all. The
        // 24 rails draw 24 cross-window rapid strokes, so the delta is two
        // orders of magnitude above the chrome noise it has to survive.
        Assert.True(red > cutRed * 4 + 200,
            $"the rapids must be what puts red in the frame: {red} red pixels with G0 vs {cutRed} without");
    }

    /// <summary>Rapids are drawn THINNER than cuts. A rapid is not cutting, so
    /// it should sit behind the geometry instead of competing with it — the
    /// red says what a rapid is, the weight says it matters less.
    /// <para>Asserted as a RELATION, not as the constant, because the
    /// relation is the design intent and the number is a tuning choice: a
    /// mutation that made rapids as heavy as cuts fails here, and so does one
    /// that made them fainter than the wire mesh.
    /// <para>Worth knowing before anyone widens a rapid for visibility: it does
    /// not work. Measured on the 24-rail ping-pong fixture, red pixel count
    /// moves only 19933 -&gt; 18309 -&gt; 16890 across widths 1.1 -&gt; 0.8 -&gt; 0.6,
    /// because these are long cross-window strokes whose LENGTH, not width,
    /// dominates the area. A rapid at 0.6px is as visible as one at 1.1px, so
    /// there is nothing to buy by making it heavy — and the heaviness costs the
    /// read it was meant to fix.</para></summary>
    [Fact]
    public void Rapid_pen_is_thinner_than_the_feed_pen()
    {
        Assert.True(PlotterPens.RapidW < PlotterPens.FeedW,
            $"a rapid is not cutting — its pen must be lighter than a cut's ({PlotterPens.RapidW} vs {PlotterPens.FeedW})");
        Assert.True(PlotterPens.RapidW >= PlotterPens.WireW,
            $"a rapid is real motion, not a coupling wire — it must stay at least as heavy ({PlotterPens.RapidW} vs {PlotterPens.WireW})");
    }

    /// <summary>A cutting program shaped like a real one: a run of G1s along a
    /// rail, then a G0 across to the next rail. <paramref name="withRapids"/>
    /// off strips the G0s but keeps the identical cut path, so the control frame
    /// differs from the subject frame in exactly one thing — whether the
    /// cross-rail moves are rapid or feed. That is the whole test: if the red
    /// came from anywhere but the rapid flag, the stripped frame would be red
    /// too.</summary>
    private static string PingPongProgram(int rails, bool withRapids = true)
    {
        var sb = new StringBuilder("G21\r\nG90\r\n");
        var y = 10d;
        for (var r = 0; r < rails; r++)
        {
            var target = 10 + 80d * r / Math.Max(1, rails - 1);
            if (withRapids)
                sb.AppendLine($"G0 X{target:F3} Y{y:F3}");
            for (var i = 0; i < 12; i++)
                sb.AppendLine($"G1 X{10 + 70d * i / 11d:F3} Y{10 + 40d * r / Math.Max(1, rails - 1):F3} F240");
            y = 10 + 40d * r / Math.Max(1, rails - 1);
        }
        return sb.ToString();
    }

    /// <summary>The run-state toggle collapses every row to Idle, so
    /// <c>RowStateFor</c> stops reaching for the row state at all and
    /// <c>BakePens.For</c> falls through to <c>_ =&gt; rapid ? Rapid : Feed</c>
    /// — cuts take the neutral feed pen, rapids keep theirs. The toggle does NOT
    /// gate <c>PenForRow</c>: that one colors the executed frontier, which is
    /// what the user watches advance, so it stays live with the toggle off.
    /// <para>This file hosts the test because it is the regression net for BOTH
    /// plotters, so it also flips the iso canvas and pins the twin of
    /// <c>RowStateFor</c> — a mutation that removed the guard from only one of
    /// the two copies would otherwise pass. The iso half measures the OFF state
    /// AND the ON state: a zero is satisfied just as well by an iso canvas that
    /// never colors anything, so without the positive control the whole iso half
    /// of this test is unfalsifiable.</para>
    /// <para>The fixture is the shared deterministic stub, NOT this file's
    /// <c>Setup</c>: the assertion is a two-line flag collapse and streaming a
    /// real 1.2 s run to observe it would buy only timing flake.</para></summary>
    [AvaloniaFact]
    public async Task Run_state_off_paints_no_state_colors()
    {
        var source = new StubProgramSource();
        var vm = MakeVm(source, MachineTypes.Mill);
        // The F600 is load-bearing: a G1 with no feed word parses to zero motion
        // tokens and the error is swallowed per line, so the test would silently
        // run against an empty program.
        source.Load("G21\r\nG1 X100 Y50 Z20 F600");
        Assert.True(vm.HasGeometry, "the run-state colors need a real path to recolor");
        // Ack EVERY row. `DoneCount + 1` is 1 here — DoneCount is 0 until the
        // first ack — so it acked row 0 alone, the `G21` header, which carries
        // no segment, and the green count read 0 for a reason that had nothing
        // to do with the toggle.
        source.AckRows(0, source.RowCount);

        var view = new PlotterView { DataContext = vm };
        var window = new Window { Width = 920, Height = 560, Content = view };
        window.Show();
        for (var i = 0; i < 8; i++)
            await Pump();

        // Sanity: the toggle is ON first, so the count is non-zero and the later
        // zero is a real change rather than a never-green fixture.
        // 854 green pixels, and 854 on all six sampling runs — the fixture is
        // pixel-deterministic, so the 400 floor is 47% of the measured value
        // and cannot be a render-cadence flake. It is not a light-pixel count
        // like Task 6's, so the blank-canvas reading here is 0, not ~1250: a
        // floor anywhere above 0 discriminates, and 400 leaves 2x headroom.
        var on = PlotterPixels.Count(PlotterPixels.Snapshot(CaptureFrame(window)), IsDoneGreen);
        Assert.True(on > 400, $"run-state colors must show when the toggle is on, found {on} green pixels");

        // The run-state colors are BAKED into the raster, not drawn per frame
        // like the lattice, so the flag has to reach a rebake — see the Chrome
        // setter. 8 pumps, per this file's settling convention.
        view.PushChromeFlags(ChromeFlags.Default with { RunState = false });
        for (var i = 0; i < 8; i++)
            await Pump();
        Assert.Equal(0, PlotterPixels.Count(PlotterPixels.Snapshot(CaptureFrame(window)), IsDoneGreen));

        // The iso canvas is a REBUILD, and a fresh PlotCanvas starts at
        // ChromeFlags.Default — so the flag has to be pushed again or the iso
        // copy of RowStateFor is measured with the toggle on.
        window.GetVisualDescendants().OfType<CheckBox>().Single(c => c.Name == "IsoToggle").IsChecked = true;
        for (var i = 0; i < 8; i++)
            await Pump();
        view.PushChromeFlags(ChromeFlags.Default with { RunState = false });
        for (var i = 0; i < 8; i++)
            await Pump();
        Assert.True(PlotterPixels.Count(PlotterPixels.Snapshot(CaptureFrame(window)), IsDoneGreen) == 0,
            "the iso canvas's RowStateFor must collapse to Idle too");

        // ...which is only a statement about the OFF state, and a zero asserts
        // just as happily about a canvas that paints no run-state colors at
        // all. So the iso half also needs the ON control: without it the
        // assertion above would still hold if RowStateFor were deleted outright.
        // The OFF push above already spent this window's early open, so this
        // one is queued rather than immediate — the surface is read only after
        // the queue drains.
        view.PushChromeFlags(ChromeFlags.Default);
        for (var i = 0; i < 8; i++)
            await Pump();
        await DrainRebakeQueue(view);
        var isoOn = PlotterPixels.Count(PlotterPixels.Snapshot(CaptureFrame(window)), IsDoneGreen);
        // 430 green pixels measured on the iso surface (430 on all three
        // sampling runs) and 0 with the painter gone. The floor is 200:
        // 2.15x over the mutational zero, and still 46% of the measured
        // count, so neither a weaker painter nor a hairier sample passes.
        Assert.True(isoOn > 200,
            $"the iso canvas must paint run-state colors with the toggle ON, found {isoOn} green pixels");
    }

    /// <summary>Count matching pixels in a HALF-OPEN rectangle
    /// <c>[x0,x1) x [y0,y1)</c> — the convention every probe rectangle in
    /// this file was written in, and the one the literal arguments above
    /// express. The one shared reader takes INCLUSIVE bounds, so the whole
    /// conversion lives here, once, instead of a per-call-site <c>- 1</c>
    /// that could silently shrink a probe. Out-of-frame coordinates are
    /// clipped by <c>At</c>, which reports them black.</summary>
    private static int CountRectHalfOpen(
        PlotterPixels.Frame frame, Func<byte, byte, byte, bool> match,
        int x0, int x1, int y0, int y1) =>
        PlotterPixels.CountRect(frame, match, x0, y0, x1 - 1, y1 - 1);

    /// <summary>Row index of the first fully-bright band in the first 60 rows,
    /// or -1. The capture is the whole WINDOW while the canvas sits a few px
    /// lower inside it, so this is how the probes learn the canvas's top edge;
    /// the sampled columns 200..720 are canvas, never window chrome.</summary>
    private static int FirstBrightRow(PlotterPixels.Frame frame)
    {
        for (var y = 0; y < 60 && y < frame.Height; y++)
        {
            var bright = PlotterPixels.Count(frame, IsCanvasWhite,
                xFilter: x => x >= 200 && x < 720 && (x - 200) % 8 == 0,
                yFilter: yy => yy == y);
            if (bright >= 60)
                return y;
        }
        return -1;
    }

    /// <summary>Canvas background is white; the window chrome around it is
    /// not, which is what makes the row above a usable canvas-top probe.</summary>
    private static bool IsCanvasWhite(byte r, byte g, byte b) => r > 245 && g > 245 && b > 245;

    private static (double X, double Y) GreyCentroid(WriteableBitmap bmp, Func<int, bool>? xFilter = null)
    {
        var centroid = PlotterPixels.Centroid(PlotterPixels.Snapshot(bmp), IsPathGrey, xFilter: xFilter);
        return (centroid.X, centroid.Y);
    }

    private static WriteableBitmap CaptureFrame(Window window)
    {
        var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        return frame;
    }

    private static async Task Pump()
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(16);
        await Task.Yield();
    }

    /// <summary>Waits the plotter's rebake queue out.
    /// <para>A bake is limited to one per <c>BakeMinIntervalMs</c> per window,
    /// and only ONE slot may open that window early. A chrome toggle therefore
    /// repaints immediately only for the first slot of the first push; a second
    /// push inside the same window is queued and served by the retry timer. Any
    /// test that flips a flag twice in a row and then reads the surface has to
    /// wait for that, which is why the "no extra wait" reasoning no longer
    /// holds.
    /// <para>Each round is a real delay plus pumps, because the retry timer
    /// needs real time to elapse AND a pumped dispatcher to be dispatched — a
    /// delay on its own observes nothing. Bounded so a stuck queue fails the
    /// assertion that follows instead of hanging.</para></summary>
    private static async Task DrainRebakeQueue(PlotterView view, int maxRounds = 6)
    {
        for (var round = 0; round < maxRounds; round++)
        {
            await Task.Delay(560);
            for (var i = 0; i < 8; i++)
                await Pump();
            if (!view.HasPendingRebake)
                return;
        }
    }

    /// <summary>The idle path is grey (#808080).</summary>
    private static bool IsPathGrey(byte r, byte g, byte b) =>
        Math.Abs(r - g) <= 14 && Math.Abs(g - b) <= 14 && r is >= 95 and <= 175;

    /// <summary>The run-state "done" green, <c>PlotterPens.DoneBrush</c> =
    /// #1b9e3e — and specifically that colour, not green in general: the world
    /// Y axis arrow is also green (#33cc33) and is painted unconditionally, so
    /// the loose predicate counted the arrow as recoloured work. Done leans blue
    /// (b - r = 35), the arrow does not (b - r = 0). See the same probe in
    /// <see cref="PlotterViewHeadlessTests"/>.</summary>
    private static bool IsDoneGreen(byte r, byte g, byte b) =>
        g > 100 && r < 90 && b < 110 && b > r + 15;

    private static bool IsToolRed(byte r, byte g, byte b) =>
        r > 150 && g < 90 && b < 90;

    /// <summary>The red tool mark: how many pixels it covers and where its
    /// center of mass sits, so a probe can follow it across frames.</summary>
    private static (int Count, double X, double Y) Glyph(WriteableBitmap bmp) =>
        PlotterPixels.Centroid(PlotterPixels.Snapshot(bmp), IsToolRed);
}
