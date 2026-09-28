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
using CNC.Core.Services;
using SenderNG.Modules.Plotter;
using SenderNG.Modules.Plotter.Settings;
using Xunit;

// StubProgramSource and MakeVm live in the shared helper so the live-run file
// can use the same deterministic fixture; a `using static` import keeps every
// call site below spelled exactly as it was before the move.
using static SenderNG.Tests.PlotterTestStubs;

namespace SenderNG.Tests;

public class PlotterViewHeadlessTests
{
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
        for (var i = 0; i < 8; i++)
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
    /// within 8 of each other, and light. Outside the IsPathGrey band by value.
    /// It does NOT exclude the toolpath: the #808080 path's antialiased halo
    /// is light and neutral, and in the grid-OFF state about 2115 of the
    /// counted pixels are that halo, which is why the off figure is nowhere near
    /// zero. The band is a relative measure, and the toggle tests lean on the
    /// on/off ratio rather than on the absolute floor.</summary>
    private static bool IsChromeLight(byte r, byte g, byte b) =>
        Math.Abs(r - g) <= 8 && Math.Abs(g - b) <= 8 && r is >= 180 and <= 250;

    /// <summary>The pushed flags must SURVIVE a canvas rebuild.
    /// <c>PushChromeFlags</c> wrote <c>canvas.Chrome</c> and kept nothing at the
    /// view level, so every <c>RebuildCanvases</c> — and the iso toggle is
    /// exactly that — brought up canvases back at <c>ChromeFlags.Default</c> and
    /// silently dropped every toggle the user had set. This stops being cosmetic
    /// the moment the flags come from the shared settings model (Task 9): set
    /// the toggles in App Settings, flip iso, watch them disappear.
    /// <para>Deliberately NO second push between the rebuild and the reading —
    /// pushing again is exactly what the pre-fix code needed, and it would hide
    /// the drop this test exists to catch.</para></summary>
    [AvaloniaFact]
    public async Task Iso_rebuild_keeps_the_pushed_chrome_flags()
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

        // Flat, as a control: the push bites here, and the defaults paint.
        view.PushChromeFlags(ChromeFlags.Default with { Grid = false });
        for (var i = 0; i < 8; i++)
            await Pump();
        var flatOff = CountSurface(PlotterPixels.Snapshot(CaptureFrame(window)), IsChromeLight);
        view.PushChromeFlags(ChromeFlags.Default);
        for (var i = 0; i < 8; i++)
            await Pump();
        var flatOn = CountSurface(PlotterPixels.Snapshot(CaptureFrame(window)), IsChromeLight);
        Assert.True(flatOn > 400, $"the default grid must paint in the flat view, found {flatOn} light pixels");
        Assert.True(flatOff < flatOn / 2, $"the pushed grid-off must bite, {flatOff} of {flatOn} light pixels");

        // Grid off, THEN the iso toggle. RebuildCanvases swaps in a fresh
        // IsoCanvas and nothing is pushed after this point.
        view.PushChromeFlags(ChromeFlags.Default with { Grid = false });
        for (var i = 0; i < 8; i++)
            await Pump();
        window.GetVisualDescendants().OfType<CheckBox>().Single(c => c.Name == "IsoToggle").IsChecked = true;
        for (var i = 0; i < 8; i++)
            await Pump();
        var isoOff = CountSurface(PlotterPixels.Snapshot(CaptureFrame(window)), IsChromeLight);

        // The iso lattice uses the SAME two pens as the flat one
        // (PlotterChromePens.GridMajor/Minor, #c0c0c0 / #e0e0e0) and nothing
        // else in the iso view lands in this band: the cross is #888888 (r=136,
        // below the 180 floor), the box #c07a20 and the arrows #2f6fb0 are both
        // far from neutral. So the same predicate isolates the grid in both
        // views, and the comparison is the on/off RATIO rather than a floor.
        view.PushChromeFlags(ChromeFlags.Default);
        for (var i = 0; i < 8; i++)
            await Pump();
        var isoOn = CountSurface(PlotterPixels.Snapshot(CaptureFrame(window)), IsChromeLight);
        Assert.True(isoOn > 400, $"the iso lattice must paint, found {isoOn} light pixels");
        Assert.True(isoOff < isoOn / 2,
            $"the iso rebuild dropped the pushed flags: {isoOff} light pixels after the rebuild against {isoOn} with the grid on");
    }

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
        for (var i = 0; i < 8; i++)
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
        // Settle the pane before counting, the same 8 pumps every pixel test in
        // this file uses. A non-foam pane's chrome clip is the whole control, so
        // the arrows are not waiting on a viewport here; they land with the
        // first frame and the pumps are simply this file's settling convention.
        for (var i = 0; i < 8; i++)
            await Pump();

        // Count with the envelope OFF. This is deliberate; do NOT "simplify" it
        // back to the default flags, which makes this test fail at 48 pixels.
        //
        // G-code is modal from the machine origin, so the job rectangle's
        // bottom-left corner IS the world origin and its 1.5px stroke runs
        // exactly along the two axis shafts. The z-order draws the arrows
        // BEFORE the envelope (see PlaneCanvas.DrawSlotChrome), so at the
        // defaults the rectangle's own edges bury the shafts and leave only the
        // two arrowheads: 48 blue pixels, under this test's 100 threshold. The
        // arrows carry no flag of their own, so the envelope toggle is the only
        // lever that isolates them, and this state measures them cleanly at 568
        // blue pixels. The grid toggle makes no difference (568 either way) —
        // the arrows are neither toggleable nor occluded by the lattice.
        //
        // The test is not vacuous in this state. Removing the DrawAxes call from
        // DrawSlotChrome drops it to 0 blue pixels — that is a mutation, not a
        // flag state, and it is the number that proves the test still fails if
        // the arrows stop painting.
        view.PushChromeFlags(ChromeFlags.Default with { Envelope = false });
        for (var i = 0; i < 8; i++)
            await Pump();

        var axes = CountSurface(PlotterPixels.Snapshot(CaptureFrame(window)), IsAxisBlue);
        Assert.True(axes > 100, $"the world axis arrows must paint, found {axes} blue pixels");
    }

    /// <summary>The #2f6fb0 axis color.</summary>
    private static bool IsAxisBlue(byte r, byte g, byte b) => b > 130 && b > r + 40 && g > r + 30;

    /// <summary>The iso view paints the world lattice through the projection:
    /// the same world grid the flat pane draws face-on, run through
    /// <c>PlotterProjection.Project</c>, so zooming the iso view makes the
    /// lattice recede. Two things carry the test: the pixels must be there, and
    /// the grid toggle must REMOVE them — an absolute count alone cannot tell
    /// the lattice from the toolpath's own light halo.</summary>
    [AvaloniaFact]
    public async Task The_iso_view_paints_the_world_grid()
    {
        var source = new StubProgramSource();
        var vm = MakeVm(source, MachineTypes.Mill);
        // The F600 is load-bearing: a G1 with no feed word parses to zero motion
        // tokens and the error is swallowed per line, so the test would silently
        // run against an empty program.
        source.Load("G21\r\nG1 X100 Y50 Z20 F600");
        Assert.True(vm.HasGeometry, "the iso chrome needs real motion to wrap a job box around");

        var view = new PlotterView { DataContext = vm };
        var window = new Window { Width = 920, Height = 560, Content = view };
        window.Show();
        for (var i = 0; i < 8; i++)
            await Pump();

        // 8 pumps, not one: a toggle-then-count on a single pump is the latent
        // flake Task 5 found, and the iso toggle has more to settle than the flat
        // chrome's flags do.
        window.GetVisualDescendants().OfType<CheckBox>()
            .Single(c => c.Name == "IsoToggle")
            .IsChecked = true;
        for (var i = 0; i < 8; i++)
            await Pump();

        var frame = PlotterPixels.Snapshot(CaptureFrame(window));
        // IsChromeLight also matches the toolpath's own antialiased halo, and
        // that halo's raster drifts with the 500 ms bake rate limiter: the same
        // predicate read 1249 / 1345 / 1540 light pixels with no painter at all
        // across three runs. A floor under ~1.5k would therefore be a test that
        // cannot fail. The lattice itself measured 11104 and 13949, so 5000 sits
        // 3.6x clear of the worst blank-canvas reading and under half of the
        // smallest painted one — and removing the DrawGrid call drops it to 1540,
        // so this floor discriminates the painter.
        var grid = CountSurface(frame, IsChromeLight);
        Assert.True(grid > 5000, $"the iso grid must paint, found {grid} light pixels");

        // `PushChromeFlags` walks every `PlotCanvas` in `_canvases` and
        // invalidates, so this reaches the iso surface too - confirmed, not
        // assumed.
        view.PushChromeFlags(ChromeFlags.Default with { Grid = false });
        for (var i = 0; i < 8; i++)
            await Pump();
        var off = CountSurface(PlotterPixels.Snapshot(CaptureFrame(window)), IsChromeLight);
        Assert.True(off < grid / 2, $"the iso lattice must go away: {off} light pixels of {grid}");
    }

    /// <summary>The iso job box and the three world axis arrows. The axes
    /// carry no toggle (the flat pane's do not either), and the box is gated by
    /// <c>Chrome.Envelope</c>, so this measures both at the defaults.</summary>
    [AvaloniaFact]
    public async Task The_iso_view_paints_the_job_box_and_the_three_world_axes()
    {
        var source = new StubProgramSource();
        var vm = MakeVm(source, MachineTypes.Mill);
        source.Load("G21\r\nG1 X100 Y50 Z20 F600");
        Assert.True(vm.HasGeometry, "the iso chrome needs real motion to wrap a job box around");

        var view = new PlotterView { DataContext = vm };
        var window = new Window { Width = 920, Height = 560, Content = view };
        window.Show();
        for (var i = 0; i < 8; i++)
            await Pump();

        window.GetVisualDescendants().OfType<CheckBox>().Single(c => c.Name == "IsoToggle").IsChecked = true;
        for (var i = 0; i < 8; i++)
            await Pump();

        var frame = PlotterPixels.Snapshot(CaptureFrame(window));
        // 4084 amber pixels with DrawEnvelope, 0 without it: a 40x margin, and
        // the mutation (not a flag state) is what proves the floor bites.
        var box = CountSurface(frame, IsEnvelopeAmber);
        Assert.True(box > 100, $"the iso job box must paint, found {box} amber pixels");
        // 106 blue pixels with the arrows, 0 without them. The floor is 40, not
        // 100: the job box is drawn after the arrows and its 1.5px edges bury
        // the two shafts, so 106 is the whole visible budget in this state and a
        // 100 floor would leave 6% of headroom — a flake waiting for a different
        // window size. 40 is 38% of the measured count and still 2.6x over the
        // mutational zero, so it can only pass when the arrows are painted.
        var axes = CountSurface(frame, IsAxisBlue);
        Assert.True(axes > 40, $"the three world axes must paint, found {axes} blue pixels");
    }

    /// <summary>Records the loft decision, because it is a decision and not an
    /// omission. The foam loft's two panes are FLAT true-scale tiles sharing one
    /// composition, drawn in each plane's role order — NOT a projection — so the
    /// projected lattice, the projected job box and the projected arrows are
    /// deliberately not drawn there: over true-scale tiles a foreshortened
    /// 30-degree grid is a wrong measurement, not a style, and the flat view
    /// already draws the chrome for exactly these two tiles. What the loft keeps
    /// is the one machine-origin cross per carriage plane it has always drawn,
    /// and that cross is gated by <c>Chrome.Origin</c> like every other cross.
    /// <para>The mill side is covered by the two tests above; this one pins the
    /// other half, with the toolpath asserted present so "no chrome" cannot be
    /// satisfied by a canvas that failed to draw anything.</para></summary>
    [AvaloniaFact]
    public async Task The_foam_loft_keeps_one_origin_cross_and_no_projected_chrome()
    {
        var source = new StubProgramSource();
        var vm = MakeVm(source, MachineTypes.Foam);
        // Four axes, so both foam carriages carry motion and the iso loft is
        // live. The F600 is load-bearing (see above).
        source.Load("G21\r\nG1 X100 Y20 Z20 A100 F600");
        Assert.Equal(2, vm.PlaneCount);
        Assert.True(vm.Planes[0].Count > 0);
        Assert.True(vm.Planes[1].Count > 0);
        vm.SetPlaneGeometries([
            PlotterPlaneMapper.Identity(2),
            PlotterPlaneMapper.Identity(2),
        ]);

        var view = new PlotterView { DataContext = vm };
        var window = new Window { Width = 920, Height = 560, Content = view };
        window.Show();
        for (var i = 0; i < 8; i++)
            await Pump();
        window.GetVisualDescendants().OfType<CheckBox>().Single(c => c.Name == "IsoToggle").IsChecked = true;
        for (var i = 0; i < 8; i++)
            await Pump();

        // Both carriages still draw, so the surface is live.
        var withCross = PlotterPixels.Snapshot(CaptureFrame(window));
        Assert.True(CountSurface(withCross, IsPathGrey) > 100,
            $"the loft must still draw its toolpath, found {CountSurface(withCross, IsPathGrey)} path pixels");
        Assert.Equal(0, CountSurface(withCross, IsEnvelopeAmber));
        Assert.Equal(0, CountSurface(withCross, IsAxisBlue));

        // The cross is grey (#888888, 1 px) so it lands in the path's own band:
        // it is isolated by the toggle, not by colour. Removing it must take
        // pixels out of that band, which is what makes this non-vacuous.
        view.PushChromeFlags(ChromeFlags.Default with { Origin = false });
        for (var i = 0; i < 8; i++)
            await Pump();
        var withoutCross = CountSurface(PlotterPixels.Snapshot(CaptureFrame(window)), IsPathGrey);
        Assert.True(withoutCross < CountSurface(withCross, IsPathGrey),
            $"the origin cross must be gated by Chrome.Origin, found {CountSurface(withCross, IsPathGrey)} with it and {withoutCross} without");
    }

    /// <summary>A foam program whose plane 0 (X,Y) Y-extent dwarfs plane 1's
    /// (X,Z): Y climbs 0..60 while Z only plunges 0..2, and X sweeps, so both
    /// planes are drawable. That is the normal foam XY-vs-XZ case, and the one
    /// that matters here — plane 0's Y extent exceeds plane 1's by far more
    /// than the 8 mm <c>FoamTileGap</c>, so plane 0's tile reaches past the
    /// canvas midline into the bottom-left quadrant that its own slot viewport
    /// stops at.</summary>
    private static string TallPlaneProgram()
    {
        var sb = new StringBuilder("G21\nG90\n");
        for (var i = 0; i <= 240; i++)
        {
            var t = i / 240.0;
            var x = 30 + 25 * Math.Sin(t * Math.PI * 4);
            sb.AppendLine($"G1 X{x:F3} Y{60 * t:F3} Z{2 * t:F3} F240");
        }
        return sb.ToString();
    }

    /// <summary>The foam lattice must not be sliced at the canvas midline.
    /// The flat pane lays its two tiles out on the diagonal (plane 0 top-left,
    /// plane 1 bottom-right), but the shared transform fits the WHOLE
    /// composition into the control, so a tile that is taller than its
    /// neighbour — the normal XY-vs-XZ case — reaches into the quadrant its
    /// slot viewport excludes. Clipping the lattice to that quadrant drew a hard
    /// horizontal edge through plane 0's grid while the same tile's envelope and
    /// cross, which take no clip, carried on past it, and nothing tested it.
    /// <para>This asserts the lattice is PRESENT in the region the per-quadrant
    /// clip cut off, measured against the same region with the grid toggled off
    /// so the toolpath's own light halo cannot stand in for it.</para></summary>
    [AvaloniaFact]
    public async Task Foam_lattice_is_not_cut_at_the_canvas_midline()
    {
        var source = new StubProgramSource();
        var vm = MakeVm(source, MachineTypes.Foam);
        source.Load(TallPlaneProgram());
        Assert.True(vm.PlaneCount > 1, $"the foam profile must give the flat pane two planes, got {vm.PlaneCount}");

        var view = new PlotterView { DataContext = vm };
        var window = new Window { Width = 920, Height = 560, Content = view };
        window.Show();
        for (var i = 0; i < 8; i++)
            await Pump();

        // Below the canvas midline AND left of it: exactly the bottom-left
        // quadrant, which is not slot 0's viewport (top-left) and not slot 1's
        // either (bottom-right). Under the per-quadrant clip NO lattice could
        // reach it, from either plane.
        static int BelowMidlineLeft(PlotterPixels.Frame f, Func<byte, byte, byte, bool> match) =>
            PlotterPixels.Count(f, match,
                xFilter: x => x > 0 && x < f.Width / 2,
                yFilter: y => y > f.Height / 2 && y < f.Height - 1);

        var on = BelowMidlineLeft(PlotterPixels.Snapshot(CaptureFrame(window)), IsChromeLight);

        view.PushChromeFlags(ChromeFlags.Default with { Grid = false });
        for (var i = 0; i < 8; i++)
            await Pump();
        var off = BelowMidlineLeft(PlotterPixels.Snapshot(CaptureFrame(window)), IsChromeLight);

        // The lattice has to ADD pixels there, not merely sit near zero: the
        // measured pair is 3788 on against 446 off, and under the per-quadrant
        // clip it collapses to 553 against 424 — a delta of 129, well under this
        // floor. So the margin is roughly 25x over the clipped reading and the
        // test cannot pass on the toolpath's own halo.
        Assert.True(on - off > 500,
            $"plane 0's lattice must reach past the canvas midline: {on} light pixels with the grid on, {off} with it off");

        // The axis arrows share that clip, so the bottom-left quadrant must
        // carry blue too: 40 blue pixels with the canvas-wide clip and 0 under
        // the per-quadrant one. Threshold 20 sits between the two, so it
        // discriminates the clip rather than the stroke's antialiasing.
        var arrows = PlotterPixels.Count(PlotterPixels.Snapshot(CaptureFrame(window)), IsAxisBlue,
            xFilter: x => x > 0 && x < window.Width / 2,
            yFilter: y => y > window.Height / 2 && y < window.Height - 1);
        Assert.True(arrows > 20,
            $"the world axis arrows must reach past the canvas midline too, found {arrows} blue pixels");
    }

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