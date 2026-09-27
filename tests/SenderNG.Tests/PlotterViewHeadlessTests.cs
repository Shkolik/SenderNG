/*
 * PlotterViewHeadlessTests.cs - headless render smoke tests for the plotter
 * view: the toolpath MUST actually draw (raster bake path), not just the tool
 * glyph. Regression guard for the M4 rewrite bug where the bake settle-check
 * overflowed its sentinel (long.MinValue subtraction wraps negative), so the
 * static raster was never created and every canvas rendered only the red dot.
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

using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CNC.Core.Abstractions;
using CNC.Core.Services;
using SenderNG.Core.Localization;
using SenderNG.Modules.Plotter;
using SenderNG.Modules.Plotter.Settings;
using Xunit;

namespace SenderNG.Tests;

public class PlotterViewHeadlessTests
{
    private sealed class StubProgramSource : ILoadedProgramSource
    {
        private ProgramRowState[] _states = [];
        public GCodeProgram? Program { get; set; }
        public event System.Action? ProgramChanged;
        public event System.Action? RowsChanged;
        public int RowCount => Program?.Entries.Count ?? 0;
        public int SendableCount => Program?.Entries.Count(e => e.IsSendable) ?? 0;
        public int DoneCount => _states.Count(s => s == ProgramRowState.Ok);
        public int TouchedRow { get; private set; }
        public ProgramRowState GetRowState(int index) =>
            index >= 0 && index < _states.Length ? _states[index] : ProgramRowState.Idle;

        public void Load(string gcode)
        {
            Program = GCodeProgram.Parse(gcode);
            _states = new ProgramRowState[Program.Entries.Count];
            TouchedRow = 0;
            ProgramChanged?.Invoke();
        }

        /// <summary>Mark rows <paramref name="from"/>..<paramref name="to"/>
        /// (exclusive) acked and raise RowsChanged — the live-run tick.</summary>
        public void AckRows(int from, int to, ProgramRowState state = ProgramRowState.Ok)
        {
            for (var i = from; i < to && i < _states.Length; i++)
                _states[i] = state;
            TouchedRow = Math.Max(TouchedRow, Math.Min(to - 1, _states.Length - 1));
            RowsChanged?.Invoke();
        }
    }

    private sealed class NeverObservable : IObservable<MachineState>
    {
        public IDisposable Subscribe(IObserver<MachineState> observer) => new Noop();
        private sealed class Noop : IDisposable
        {
            public void Dispose() { }
        }
    }

    private sealed class StubGrbl : IGrblService
    {
        public bool IsConnected => false;
        public ControllerCapabilities Capabilities => ControllerCapabilities.Full with { AxisLetters = "XYZA" };
        public IObservable<MachineState> State => new NeverObservable();
        public Task ConnectAsync(string port) => Task.CompletedTask;
        public void Disconnect() { }
        public Task SendAsync(string line) => Task.CompletedTask;
        public void SendRealtime(byte code) { }
    }

    private static PlotterViewModel MakeVm(StubProgramSource source, string machineTypeId)
    {
        var dir = Path.Combine(Path.GetTempPath(), "plotter-ui-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var settings = new SettingsService(dir);
        var profile = settings.Load();
        profile.MachineTypeId = machineTypeId;
        settings.Save(profile);

        return new PlotterViewModel(source, new StubGrbl(), settings,
            new LocalizationService(new MemoryStream("{ \"strings\": {} }"u8.ToArray())));
    }

    /// <summary>A long, dense toolpath so a "is the path drawn at all" pixel
    /// check is decisive (a broken raster bake renders ZERO path pixels).</summary>
    private static string SpiralProgram(int segments)
    {
        var sb = new StringBuilder();
        sb.AppendLine("G21");
        for (var i = 0; i < segments; i++)
        {
            var x = 10 + i * 0.1;
            var y = 10 + 5 * Math.Sin(i * 0.05);
            sb.AppendLine($"G1 X{x:F3} Y{y:F3} Z0.5 F240");
        }
        return sb.ToString();
    }

    [AvaloniaFact]
    public async Task Flat_canvas_renders_the_toolpath()
    {
        var source = new StubProgramSource();
        var vm = MakeVm(source, MachineTypes.Mill);
        source.Load(SpiralProgram(600));
        Assert.True(vm.HasGeometry);

        var view = new PlotterView { DataContext = vm };
        var window = new Window { Width = 920, Height = 560, Content = view };
        window.Show();
        for (var i = 0; i < 8; i++)
            await Pump();

        var frame = PlotterPixels.Snapshot(CaptureFrame(window));
        var grey = PlotterPixels.Count(frame, IsPathGrey);
        Assert.True(grey > 100,
            $"expected the grey toolpath in the frame, found {grey} path pixels (raster bake missing?)");
    }

    [AvaloniaFact]
    public async Task Iso_canvas_renders_the_toolpath_across_the_panel()
    {
        var source = new StubProgramSource();
        var vm = MakeVm(source, MachineTypes.Mill);
        source.Load(SpiralProgram(600));
        Assert.True(vm.HasGeometry);

        var view = new PlotterView { DataContext = vm };
        // The iso rebuild is driven by the CheckBox toggle (like the user), so
        // host the view in a window to reach the real control.
        var window = new Window { Width = 920, Height = 560, Content = view };
        window.Show();
        for (var i = 0; i < 6; i++)
            await Pump();

        // Flip the iso toggle exactly like the user does.
        var toggle = window.GetVisualDescendants().OfType<CheckBox>().Single();
        toggle.IsChecked = true;
        for (var i = 0; i < 6; i++)
            await Pump();
        Assert.True(vm.Isometric);

        var frame = PlotterPixels.Snapshot(CaptureFrame(window));
        var grey = PlotterPixels.Count(frame, IsPathGrey);
        Assert.True(grey > 100,
            $"expected the grey iso toolpath in the frame, found {grey} path pixels (raster bake missing?)");

        // M4 layout fix: the iso canvas must stretch over the WHOLE panel, not
        // linger in the flat layout's left column — so the path must reach the
        // right part of the frame.
        var right = PlotterPixels.Count(frame, IsPathGrey, xFilter: x => x > frame.Width * 0.6);
        Assert.True(right > 20,
            $"iso toolpath does not fill the panel: {right} path pixels in the right 40% (stale grid columns?)");
    }

    /// <summary>Regression guard for the stale-colors report: acked segments
    /// must recolor in real time from RowsChanged ticks alone — NO canvas
    /// rebuild, NO mode switch. The M4 raster kept its load-time colors and its
    /// recolor overlay walked the wrong direction (rows ABOVE the frontier are
    /// idle), so only a 2D↔iso switch (canvas rebuild) ever showed updates.</summary>
    [AvaloniaFact]
    public async Task Frontier_colors_update_live_without_a_mode_switch()
    {
        var source = new StubProgramSource();
        var vm = MakeVm(source, MachineTypes.Mill);
        source.Load(SpiralProgram(600));

        var view = new PlotterView { DataContext = vm };
        var window = new Window { Width = 920, Height = 560, Content = view };
        window.Show();
        for (var i = 0; i < 8; i++)
            await Pump();

        // Fresh load: everything idle (blue), nothing green.
        Assert.Equal(0, PlotterPixels.Count(PlotterPixels.Snapshot(CaptureFrame(window)), IsDoneGreen));

        // Ack the first half of the rows — exactly the live-run tick.
        source.AckRows(1, 301);
        for (var i = 0; i < 8; i++)
            await Pump();

        var green = PlotterPixels.Count(PlotterPixels.Snapshot(CaptureFrame(window)), IsDoneGreen);
        Assert.True(green > 200,
            $"acked segments must turn green from RowsChanged alone, found {green} green pixels");

        // And a later ack moves the green frontier forward without a rebuild.
        source.AckRows(301, 601);
        for (var i = 0; i < 8; i++)
            await Pump();
        var greenAll = PlotterPixels.Count(PlotterPixels.Snapshot(CaptureFrame(window)), IsDoneGreen);
        Assert.True(greenAll > green,
            $"green frontier must advance, was {green}, now {greenAll}");
    }

    /// <summary>Regression guard for the blank-after-zoom/pan report: content
    /// must stay visible while panning and zooming. The M4 raster baked only
    /// the viewport rect, so anything panned in after a zoom was blank (only a
    /// rebake could redraw it). Direct vector rendering draws the full path
    /// every frame — panning/zooming can never reveal empty raster edges.</summary>
    [AvaloniaFact]
    public async Task Pan_and_zoom_keep_the_path_visible()
    {
        var source = new StubProgramSource();
        var vm = MakeVm(source, MachineTypes.Mill);
        source.Load(SpiralProgram(600));

        var view = new PlotterView { DataContext = vm };
        var window = new Window { Width = 920, Height = 560, Content = view };
        window.Show();
        for (var i = 0; i < 8; i++)
            await Pump();

        var center = new Point(460, 280);
        // Zoom in at the center a few clicks, then drag far — the exact
        // reported repro (zoom pushes content off-screen, pan should bring it
        // back).
        for (var i = 0; i < 4; i++)
        {
            window.MouseWheel(center, new Vector(0, 1), RawInputModifiers.None);
            for (var j = 0; j < 2; j++)
                await Pump();
        }
        window.MouseDown(center, MouseButton.Left, RawInputModifiers.None);
        window.MouseMove(center + new Point(260, 140), RawInputModifiers.None);
        window.MouseUp(center + new Point(260, 140), MouseButton.Left, RawInputModifiers.None);
        for (var i = 0; i < 4; i++)
            await Pump();

        var grey = PlotterPixels.Count(PlotterPixels.Snapshot(CaptureFrame(window)), IsPathGrey);
        Assert.True(grey > 200,
            $"the path must stay visible after zoom+pan, found {grey} path pixels (blank raster edge?)");
    }

    /// <summary>The foam iso (M7 layout: 2D quadrants + semi-transparent loft
    /// wires, not the old stacked projection) must render a 4-axis program end
    /// to end: both carriage tracks in their diagonal quadrants plus the
    /// coupling wires. Guards the axis-slot truncation (a 4-axis file keeping
    /// its A data) and the flat-layout render path.</summary>
    [AvaloniaFact]
    public async Task Foam_iso_renders_the_four_axis_loft()
    {
        var source = new StubProgramSource();
        var vm = MakeVm(source, MachineTypes.Foam);
        // Wing-like program: span Z sweeps 0..279, chord X 0..110, thickness
        // A 0..26, tower-1 vertical Y constant — the airfoil lives in (X,A).
        var sb = new StringBuilder("G21\n");
        for (var i = 0; i <= 400; i++)
        {
            var t = i / 400d;
            var z = 279 * t;
            var x = 55 + 50 * Math.Sin(t * Math.PI * 4);
            var a = 13 + 12 * Math.Sin(t * Math.PI * 4);
            sb.AppendLine($"G1 X{x:F3} Y27 Z{z:F3} A{a:F3} F240");
        }
        source.Load(sb.ToString());

        // The A axis survived the build (both carriage planes carry motion).
        Assert.Equal(2, vm.Planes.Count);
        Assert.True(vm.Planes[0].Count > 0);
        Assert.True(vm.Planes[1].Count > 0);

        var view = new PlotterView { DataContext = vm };
        var window = new Window { Width = 920, Height = 560, Content = view };
        window.Show();
        for (var i = 0; i < 6; i++)
            await Pump();
        window.GetVisualDescendants().OfType<CheckBox>().Single().IsChecked = true;
        for (var i = 0; i < 6; i++)
            await Pump();
        Assert.True(vm.Isometric);

        var grey = PlotterPixels.Count(PlotterPixels.Snapshot(CaptureFrame(window)), IsPathGrey);
        Assert.True(grey > 100,
            $"expected the foam 2D+loft in the iso frame, found {grey} path pixels");
        // The loft layout: plane 1 top-left, plane 2 bottom-right — grey path
        // must appear in BOTH halves of the iso frame. 280 is the midpoint of
        // the Height = 560 window declared above; the live-run sibling splits
        // the same way off finalFrame.Height / 2.
        var top = PlotterPixels.Count(PlotterPixels.Snapshot(CaptureFrame(window)), IsPathGrey, yFilter: y => y < 280);
        var bottom = PlotterPixels.Count(PlotterPixels.Snapshot(CaptureFrame(window)), IsPathGrey, yFilter: y => y >= 280);
        Assert.True(top > 30, $"plane 1 must draw in the iso top-left, found {top} path pixels");
        Assert.True(bottom > 30, $"plane 2 must draw in the iso bottom-right, found {bottom} path pixels");
        // And the semi-transparent coupling mesh must be present (its blend is
        // a light blue-grey, distinct from the pure-grey path).
        var wire = PlotterPixels.Count(PlotterPixels.Snapshot(CaptureFrame(window)), IsWireBlend);
        Assert.True(wire > 20, $"the loft coupling mesh must draw, found {wire} wire pixels");
    }

    /// <summary>Regression guard for the tab-switch freeze: the view used to
    /// unsubscribe on DetachedFromVisualTree and NEVER re-subscribe — after one
    /// tab switch the plot stopped updating forever. Detaching and re-attaching
    /// the view must restore live frontier updates.</summary>
    [AvaloniaFact]
    public async Task Live_updates_survive_a_tab_switch()
    {
        var source = new StubProgramSource();
        var vm = MakeVm(source, MachineTypes.Mill);
        source.Load(SpiralProgram(600));

        var view = new PlotterView { DataContext = vm };
        var window = new Window { Width = 920, Height = 560, Content = view };
        window.Show();
        for (var i = 0; i < 8; i++)
            await Pump();
        Assert.Equal(0, PlotterPixels.Count(PlotterPixels.Snapshot(CaptureFrame(window)), IsDoneGreen));

        // Switch tabs: the view leaves the visual tree (the TabControl keeps
        // hidden tab content detached) and comes back.
        window.Content = null;
        for (var i = 0; i < 4; i++)
            await Pump();
        window.Content = view;
        for (var i = 0; i < 8; i++)
            await Pump();

        // Live frontier ticks after the re-attach must still recolor the path.
        source.AckRows(1, 301);
        for (var i = 0; i < 8; i++)
            await Pump();
        var green = PlotterPixels.Count(PlotterPixels.Snapshot(CaptureFrame(window)), IsDoneGreen);
        Assert.True(green > 200,
            $"live updates must survive a tab switch, found {green} green pixels");
    }

    /// <summary>The header must name the WORLD axes each plane feeds — the
    /// same mapping the pane below it draws — so the letters can never
    /// describe a different picture than the one on screen. Identity maps every
    /// plane's roles onto world X and Y in turn, hence "XY | XY" for a foam
    /// machine whose second carriage is mechanically Z/A.</summary>
    [AvaloniaFact]
    public async Task Header_letters_name_the_world_axes_each_plane_feeds()
    {
        var source = new StubProgramSource();
        // G21 + a feed word: a G1 with no feed in effect is a parse error, and
        // the program would carry no geometry at all.
        source.Load("G21\nG1 X10 Y10 F240");
        var vm = MakeVm(source, MachineTypes.Foam);
        vm.SetPlaneGeometries([
            PlotterPlaneMapper.Identity(2),
            PlotterPlaneMapper.Identity(2),
        ]);
        Assert.True(vm.HasGeometry);

        var view = new PlotterView { DataContext = vm };
        var window = new Window { Width = 920, Height = 560, Content = view };
        window.Show();
        await Pump();

        // Foam plane 1's roles are Z and A; identity maps them onto world X and Y.
        Assert.Equal("XY | XY", view.HeaderLettersForTest);

        // The letters FOLLOW the mapping: plane 1 fed by world Y then X reads
        // "YX" — not its machine roles' letters, which never move.
        vm.SetPlaneGeometries([
            PlotterPlaneMapper.Identity(2),
            new PlotterPlaneGeometry { WorldAxes = [1, 0] },
        ]);
        await Pump();
        Assert.Equal("XY | YX", view.HeaderLettersForTest);
    }

    /// <summary>The whole point of routing the panes through one mapping: a
    /// swap must TRANSPOSE the pane (a new tile extent, therefore a new fit
    /// zoom), not merely relabel the header. The program is anisotropic on
    /// purpose — plane 0 spans 100 in its first role and 20 in its second,
    /// plane 1 spans 20 then 100 — so the two compositions differ and the
    /// assertion cannot pass on a cancelled-out change.</summary>
    [AvaloniaFact]
    public async Task Swapping_a_planes_feeds_transposes_that_pane()
    {
        var source = new StubProgramSource();
        source.Load("G21\nG1 X100 Y20 Z20 A100 F240");
        var vm = MakeVm(source, MachineTypes.Foam);
        vm.SetPlaneGeometries([
            PlotterPlaneMapper.Identity(2),
            PlotterPlaneMapper.Identity(2),
        ]);
        // Both carriages carry motion, so both tiles are drawn before the swap.
        Assert.Equal(2, vm.Planes.Count);
        Assert.True(vm.Planes[0].Count > 0);
        Assert.True(vm.Planes[1].Count > 0);

        var view = new PlotterView { DataContext = vm };
        var window = new Window { Width = 920, Height = 560, Content = view };
        window.Show();
        for (var i = 0; i < 4; i++)
            await Pump();

        var before = view.CompositionForTest;
        // One canvas carries the whole diagonal composition (both foam tiles).
        Assert.Single(before);
        Assert.Equal([0, 1], view.DrawableSlotsForTest.ToArray());
        // The fit must have run: an unfitted (NaN) zoom would satisfy the
        // NotEqual below for the wrong reason (NaN != NaN).
        Assert.False(double.IsNaN(before[0].Zoom));

        // Swap plane 1's feeds: its wide axis now feeds world Y, so the shared
        // composition's vertical extent grows and the fit zoom must change.
        vm.SetPlaneGeometries([
            PlotterPlaneMapper.Identity(2),
            new PlotterPlaneGeometry { WorldAxes = [1, 0] },
        ]);
        await Pump();

        var after = view.CompositionForTest;
        Assert.Single(after);
        Assert.Equal([0, 1], view.DrawableSlotsForTest.ToArray());
        Assert.NotEqual(before[0].Zoom, after[0].Zoom, 6);
    }

    /// <summary>A plane feeding only X and Z has no 2D extent: the flat pane
    /// must refuse to draw it instead of plotting its first two roles as if
    /// they were world X and Y. The program moves BOTH carriages, so the only
    /// reason plane 1 is missing is its mapping — an empty plane would pass
    /// this test vacuously.</summary>
    [AvaloniaFact]
    public async Task A_plane_with_no_world_Y_is_not_drawn_in_the_flat_view()
    {
        var source = new StubProgramSource();
        source.Load("G21\nG1 X10 Y10 Z10 A10 F240");
        var vm = MakeVm(source, MachineTypes.Foam);
        vm.SetPlaneGeometries([
            PlotterPlaneMapper.Identity(2),
            new PlotterPlaneGeometry { WorldAxes = [0, 2] },
        ]);
        // The second carriage HAS motion — it is the mapping that removes it.
        Assert.True(vm.Planes[1].Count > 0);

        var view = new PlotterView { DataContext = vm };
        var window = new Window { Width = 920, Height = 560, Content = view };
        window.Show();
        await Pump();

        // The header still names what plane 1 feeds (world X and Z): the
        // mapping is live and reported, the pane simply has no extent for it.
        Assert.Equal("XY | XZ", view.HeaderLettersForTest);
        // Only plane 0 is drawable, so the composition holds a single tile.
        var frame = PlotterPixels.Snapshot(PlotterViewHeadlessTests.CaptureFrame(window));
        Assert.True(PlotterPixels.Count(frame, PlotterViewHeadlessTests.IsPathGrey) > 0);
        Assert.Single(view.DrawableSlotsForTest);
    }

    [AvaloniaFact]
    public async Task Grid_toggle_off_removes_the_lattice_but_keeps_the_path()
    {
        var source = new StubProgramSource();
        var vm = MakeVm(source, MachineTypes.Mill);
        source.Load(SpiralProgram(600));
        Assert.True(vm.HasGeometry);

        var view = new PlotterView { DataContext = vm };
        var window = new Window { Width = 920, Height = 560, Content = view };
        window.Show();
        await Pump();

        var on = PlotterPixels.Snapshot(CaptureFrame(window));
        var withGrid = CountSurface(on, IsChromeLight);
        Assert.True(withGrid > 400, $"the default grid must paint, found {withGrid} light pixels");

        view.PushChromeFlags(ChromeFlags.Default with { Grid = false });
        await Pump();
        var off = PlotterPixels.Snapshot(CaptureFrame(window));
        var without = CountSurface(off, IsChromeLight);
        Assert.True(without < withGrid / 2, $"the grid must go away, found {without} of {withGrid}");

        // The toolpath itself is untouched by the chrome toggles — it can only
        // GAIN pixels, never lose them: the lattice is drawn OVER the path, and
        // a lattice pixel sitting on a path pixel at high coverage pushes the
        // blend out of the IsPathGrey band, so removing it brings the path back.
        // A whole-frame count would not catch that (the #aaa view frame is itself
        // grey), which is why this one is scoped and thresholded.
        var greyOn = CountSurface(on, IsPathGrey);
        var greyOff = CountSurface(off, IsPathGrey);
        Assert.True(greyOn > 100, $"the grey toolpath must be on the surface, found {greyOn} path pixels");
        Assert.True(greyOff >= greyOn, $"the chrome toggles must not eat the path: {greyOff} of {greyOn} path pixels");
    }

    /// <summary>The grid palette (#e0e0e0 / #c0c0c0) — near-neutral, all channels
    /// within 8 of each other, and light. Outside the IsPathGrey band by value.</summary>
    private static bool IsChromeLight(byte r, byte g, byte b) =>
        Math.Abs(r - g) <= 8 && Math.Abs(g - b) <= 8 && r is >= 180 and <= 250;

    [AvaloniaFact]
    public async Task Envelope_toggle_draws_the_job_rectangle()
    {
        var source = new StubProgramSource();
        var vm = MakeVm(source, MachineTypes.Mill);
        source.Load(SpiralProgram(600));
        Assert.True(vm.HasGeometry);

        var view = new PlotterView { DataContext = vm };
        var window = new Window { Width = 920, Height = 560, Content = view };
        window.Show();
        await Pump();

        var drawn = CountSurface(PlotterPixels.Snapshot(CaptureFrame(window)), IsEnvelopeAmber);
        Assert.True(drawn > 100, $"the job rectangle must paint, found {drawn} amber pixels");

        view.PushChromeFlags(ChromeFlags.Default with { Envelope = false });
        await Pump();
        var left = CountSurface(PlotterPixels.Snapshot(CaptureFrame(window)), IsEnvelopeAmber);
        Assert.Equal(0, left);
    }

    /// <summary>The #c07a20 envelope, allowing for the antialiased edge pixels.</summary>
    private static bool IsEnvelopeAmber(byte r, byte g, byte b) =>
        r is >= 150 and <= 215 && g is >= 85 and <= 165 && b < 90 && r > g + 40;

    [AvaloniaFact]
    public async Task Axes_draw_the_world_X_and_Y_arrows()
    {
        var source = new StubProgramSource();
        var vm = MakeVm(source, MachineTypes.Mill);
        source.Load(SpiralProgram(600));
        Assert.True(vm.HasGeometry);

        var view = new PlotterView { DataContext = vm };
        var window = new Window { Width = 920, Height = 560, Content = view };
        window.Show();
        // Settle the pane before counting. A SINGLE pump is not enough and the
        // failure is silent rather than obvious: the grid and the envelope are
        // big enough to show up in the first frame whatever the pane's
        // intermediate viewport is, but the axis arrows are clipped to the slot
        // viewport and sit at a fractional pixel offset, so in an unsettled pane
        // they land on lighter antialiased coverage that falls outside the
        // #2f6fb0 band and the count reads 0. Every other pixel test in this
        // file settles with 8 pumps for the same reason.
        for (var i = 0; i < 8; i++)
            await Pump();

        var axes = CountSurface(PlotterPixels.Snapshot(CaptureFrame(window)), IsAxisBlue);
        Assert.True(axes > 100, $"the world axis arrows must paint, found {axes} blue pixels");
    }

    /// <summary>The #2f6fb0 axis color.</summary>
    private static bool IsAxisBlue(byte r, byte g, byte b) => b > 130 && b > r + 40 && g > r + 30;

    /// <summary>Counts on the PLOT SURFACE only — the canvas interior, below the
    /// 24 px header band and inside the 1 px frame. Every whole-frame count
    /// here is a trap: the header is #e8e8e8 (21.6k pixels INSIDE the light
    /// grid band), the frame is #aaa (inside the grey path band), and the
    /// header's subpixel-antialiased TEXT fringes leak 22 amber and 74 blue
    /// pixels per frame that have nothing to do with the canvas. Measured, not
    /// assumed: a whole-frame amber count reads 22 with the envelope OFF.</summary>
    private static int CountSurface(PlotterPixels.Frame frame, Func<byte, byte, byte, bool> match) =>
        PlotterPixels.Count(frame, match,
            xFilter: x => x > 0 && x < frame.Width - 1,
            yFilter: y => y > 24 && y < frame.Height - 1);

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

    /// <summary>The idle path is grey (#808080).</summary>
    private static bool IsPathGrey(byte r, byte g, byte b) =>
        Math.Abs(r - g) <= 14 && Math.Abs(g - b) <= 14 && r is >= 95 and <= 175;

    /// <summary>The semi-transparent loft wire over white blends to a light
    /// blue-grey (~#cbcbd6) — distinctly bluer than the grey path.</summary>
    private static bool IsWireBlend(byte r, byte g, byte b) =>
        b > r + 8 && b > 190 && g >= 180 && Math.Abs(r - g) <= 16;

    private static bool IsDoneGreen(byte r, byte g, byte b) =>
        g > 100 && r < 90 && b < 110;
}