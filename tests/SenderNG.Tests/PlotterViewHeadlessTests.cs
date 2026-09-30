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

using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
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

// Flip the iso toggle exactly like the user does. Named, not
          // Single(): the plot overlay's own chrome toggles used to be
          // checkboxes too (they are ToggleButtons now), so a bare Single()
          // here is a coin flip on how many controls the view hosts.
        var toggle = window.GetVisualDescendants().OfType<CheckBox>().Single(c => c.Name == "IsoToggle");
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
        Assert.Equal(0, PlotterPixels.Count(PlotterPixels.Snapshot(CaptureFrame(window)), IsRunStateGreen));

        // Ack the first half of the rows — exactly the live-run tick.
        source.AckRows(1, 301);
        for (var i = 0; i < 8; i++)
            await Pump();

        var green = PlotterPixels.Count(PlotterPixels.Snapshot(CaptureFrame(window)), IsRunStateGreen);
        Assert.True(green > 200,
            $"acked segments must turn green from RowsChanged alone, found {green} green pixels");

        // And a later ack moves the green frontier forward without a rebuild.
        source.AckRows(301, 601);
        for (var i = 0; i < 8; i++)
            await Pump();
        var greenAll = PlotterPixels.Count(PlotterPixels.Snapshot(CaptureFrame(window)), IsRunStateGreen);
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

    /// <summary>The view model owns the per-plane data the union is read from,
    /// so it owns the loop that assembles it: the machine type knows how many
    /// planes exist and which flattened axis indices back each, and
    /// <c>GeometryFor</c> sanitises the stored mapping. Written here, once,
    /// because the two renderer canvases must not each resolve the same inputs
    /// differently.
    /// <para>The program is the spec's worked example in real data: foam plane 0
    /// (X,Y) spans 0..100 by 0..30 and plane 1 (Z,A) spans 0..50 by 0..20 — Z
    /// runs from the program's own start at 0, not from an offset — so the job
    /// envelope is 0..100 by 0..30, not the second tile's own 50x20.
    /// <para>Note what this example cannot decide: plane 1's extent lies wholly
    /// inside plane 0's, so a loop that read only the first plane would produce
    /// the same numbers and this test would still pass. The companion test below
    /// is the one that fails when the loop is short-circuited.</summary>
    [AvaloniaFact]
    public async Task The_job_envelope_is_the_union_of_both_planes()
    {
        var source = new StubProgramSource();
        var vm = MakeVm(source, MachineTypes.Foam);
        // One move in each carriage, to different extents.
        source.Load("G21\r\nG1 X100 Y30 Z50 A20 F600");
        for (var i = 0; i < 6; i++)
            await Pump();
        Assert.True(vm.PlaneCount > 1, "the foam preset must have two carriage planes");
        Assert.True(vm.TryJobEnvelope(out var job));
        // Plane 0 alone is 0..100 by 0..30; plane 1 alone is 0..50 by 0..20.
        // The envelope is the union, so it is the LARGER of each axis and is
        // what both tiles are sized from.
        Assert.Equal(0.0, job.MinX, 6);
        Assert.Equal(0.0, job.MinY, 6);
        Assert.Equal(100.0, job.MaxX, 6);
        Assert.Equal(30.0, job.MaxY, 6);
    }

    /// <summary>The case the spec's worked example CANNOT be, and the reason it
    /// exists: there, plane 1's extent sits wholly inside plane 0's, so the
    /// union and plane 0 alone come out identical and a loop that stopped after
    /// the first plane would still pass. This program INVERTS the dominance —
    /// carriage 1 (X,Y) spans 0..30 by 0..10, carriage 2 (Z,A) spans 0..100 by
    /// 0..60 — so the envelope can only read 0..100 by 0..60 if plane 1 was
    /// genuinely folded in; a plane-0-only loop reports 30 by 10.
    /// <para>What this layer owns is the LOOP, not the arithmetic: the union
    /// itself is <see cref="PlotterPlaneMapper.TryJobExtent"/>'s and is covered
    /// by its own unit cases, so a second assertion on the first program would
    /// only re-test Task 1. This is the only assertion that can catch the view
    /// model resolving one plane's inputs and calling it a job envelope.</summary>
    [AvaloniaFact]
    public async Task A_second_plane_wider_than_the_first_widens_the_job_envelope()
    {
        var source = new StubProgramSource();
        var vm = MakeVm(source, MachineTypes.Foam);
        // Dominance reversed relative to the worked example: the SECOND
        // carriage is the large one, so the first cannot answer for the job.
        source.Load("G21\r\nG1 X30 Y10 Z100 A60 F600");
        for (var i = 0; i < 6; i++)
            await Pump();
        Assert.True(vm.TryJobEnvelope(out var job));
        Assert.Equal(0.0, job.MinX, 6);
        Assert.Equal(0.0, job.MinY, 6);
        Assert.Equal(100.0, job.MaxX, 6);
        Assert.Equal(60.0, job.MaxY, 6);
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
        window.GetVisualDescendants().OfType<CheckBox>().Single(c => c.Name == "IsoToggle").IsChecked = true;
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
        // And the coupling mesh must be present. This is measured with
        // Chrome.Grid OFF, and that is not incidental bookkeeping: the wire
        // FLOOR's antialiased edges run through the same near-white blue-grey
        // the 15% mesh blends to, so with the grid on this probe would score
        // floor pixels and could not fail if the mesh stopped drawing. The
        // grid-off frame leaves the mesh as the only thing in the band.
        // Measured with Chrome.Grid off, as the assertion below does: 3678 mesh
        // pixels here, and 120 for the same probe on a mill iso frame
        // (SpiralProgram(600), same window and chrome, no mesh because Loft is
        // false). Re-measured after the job envelope was unioned: the two tiles
        // are now the one 279x27 mm envelope, so the composition went from
        // 105+8+279 = 392 mm wide to 2*279+8 = 566 and the fit zoom fell from
        // 896/392 = 2.29 to 896/566 = 1.58 px/mm — this render is not the one
        // the old 14096 was taken on. The 2000 floor is ~17x the control, but
        // the signal is now only ~1.8x OVER the floor, so the union has left
        // much less headroom above it than there was: 2000 is kept because the
        // measurement and its control are recorded here, not because the
        // signal is far above it.
        view.PushChromeFlags(ChromeFlags.Default with { Grid = false });
        for (var i = 0; i < 8; i++)
            await Pump();
        var mesh = CountSurface(PlotterPixels.Snapshot(CaptureFrame(window)), IsLoftMeshBlend);
        Assert.True(mesh > 2000,
            $"the loft coupling mesh must draw, found {mesh} mesh pixels");
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
        Assert.Equal(0, PlotterPixels.Count(PlotterPixels.Snapshot(CaptureFrame(window)), IsRunStateGreen));

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
        var green = PlotterPixels.Count(PlotterPixels.Snapshot(CaptureFrame(window)), IsRunStateGreen);
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

        // The toolpath itself is untouched by the chrome toggles. The lattice
        // is a FLOOR now — it recedes UNDER the path, so it cannot push path
        // pixels out of the IsPathGrey band; the two counts can only differ by
        // the rebake's antialiasing jitter (the settle timer re-rasterizes at
        // the settled transform, and half a pixel of AA shifts move a few band
        // members). A whole-frame count would not catch a real loss anyway (the
        // #aaa view frame is itself grey), which is why this one is scoped and
        // thresholded: the on-state path may not EXCEED the off-state path by
        // more than the jitter, i.e. the grid must not have eaten the path or
        // invented one.
        var greyOn = CountSurface(on, IsPathGrey);
        var greyOff = CountSurface(off, IsPathGrey);
        Assert.True(greyOn > 100, $"the grey toolpath must be on the surface, found {greyOn} path pixels");
        Assert.True(greyOff >= greyOn - 60,
            $"the chrome toggles must not eat the path: {greyOff} with the grid off vs {greyOn} with it on");
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
        // below the 180 floor), the teal box and the arrows #2f6fb0 are both
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

    /// <summary>A no-argument <c>PushChromeFlags()</c> is a RE-APPLY, not a reset.
    /// <para>Task 9's first cut stored <c>_chromeFlags = flags</c> unconditionally,
    /// so a null argument wiped the memory. Nothing in Task 9 called it that way,
    /// which is why every test stayed green — but Task 10 is written against the
    /// opposite contract: its overlay-settings setter and its rebuild path both
    /// call it bare to mean "re-apply what is already in effect" (plan
    /// <c>canvas.Chrome = flags ?? _overlaySettings?.Flags ?? ChromeFlags.Default</c>).
    /// Under the old semantics the single most common call shape in the next task
    /// would have silently thrown away every toggle the user set.
    /// <para>So this pins the contract itself, not the pixels: push grid-off, call
    /// the method with no argument, and assert the flags did NOT revert. Returning
    /// to the defaults has to be explicit — <c>PushChromeFlags(ChromeFlags.Default)</c>.</para></summary>
    [AvaloniaFact]
    public void A_bare_PushChromeFlags_reapplies_and_does_not_reset()
    {
        var source = new StubProgramSource();
        var vm = MakeVm(source, MachineTypes.Mill);
        source.Load(SpiralProgram(600));
        var window = new Window { Width = 920, Height = 560 };
        var view = new PlotterView { DataContext = vm };
        window.Content = view;
        window.Show();

        view.PushChromeFlags(ChromeFlags.Default with { Grid = false });
        Assert.False(view.DebugStoredChromeFlags?.Grid ?? false,
            "the pushed grid-off must be the retained state");

        view.PushChromeFlags();
        Assert.False(view.DebugStoredChromeFlags?.Grid ?? false,
            "a bare PushChromeFlags() is a re-apply; it must not reset the flags to default");

        // And the explicit path really does return to the defaults.
        view.PushChromeFlags(ChromeFlags.Default);
        Assert.True(view.DebugStoredChromeFlags?.Grid ?? false,
            "ChromeFlags.Default must re-enable the grid");
    }

    /// <summary>A foam program whose two carriages are DISJOINT in the source:
    /// the first block moves X and Y (plane 1 = XY), the second moves Z and A
    /// (plane 2 = ZA). <c>PlotterViewModel.SlicePlanes</c> only files a segment
    /// under a plane when one of THAT plane's own axes moves, so a source row
    /// can be acked into one slot and never touch the other.
    /// <para>That disjointness is what lets the test below attribute a pixel
    /// count to a SLOT instead of splitting the frame: the two tiles are laid
    /// out on the diagonal around the canvas midpoint, so a tile regularly
    /// straddles the midline and any half-frame probe would be measuring the
    /// wrong slot half the time.</para></summary>
    /// <summary>Mirror of <c>PlotCanvas.OverlayCap</c> (private there). Spelled
    /// out here rather than reflected so a change to the cap has to be made
    /// deliberately in both places: the overflow test sizes its program against
    /// it, and a silently-shared value would let the test keep passing after
    /// the thing it tests moved.</summary>
    private const int OverlayCap = 2048;

    /// <summary>Pumps until the view model's geometry has landed.
    /// <para>A program over <c>PlotterViewModel.AsyncBuildThreshold</c> (4000
    /// tokens) builds on the THREAD POOL and posts the result back to the UI
    /// thread, so <c>Planes</c> stays empty until the dispatcher runs. That is
    /// why the older 120-line fixture can assert immediately after
    /// <c>Load</c> and a 2100-line one cannot — and why an unsettled one would
    /// measure a canvas with no geometry on it, which passes everything.</para>
    /// <para>Budgeted in milliseconds, not in rounds: a round count is a
    /// disguised wall-clock assumption, and a loaded machine spends longer
    /// inside a round. The original 400 rounds was four seconds' worth of
    /// headroom that happened to be enough on an idle box and not enough with
    /// the rest of the suite running. Failing loudly beats settling late —
    /// carrying on would measure an empty canvas and assert something
    /// meaningless.</para></summary>
    private static async Task SettleGeometry(PlotterViewModel vm, int timeoutMs = 30_000)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (vm.Planes.Count == 0)
        {
            Assert.True(Environment.TickCount64 < deadline,
                $"geometry did not build within {timeoutMs} ms; the canvas would be measured with nothing on it");
            await Pump();
            await Task.Delay(10);
        }
    }

    private static (string Program, int Plane0Rows) TwoCarriageProgram(int lines)
    {
        var sb = new StringBuilder("G21\nG90\n");
        // A boustrophedon fill of a 50x50 tile: dense enough that the baked
        // stroke lands thousands of pixels inside the green band, and one mm
        // stays one mm on both planes. The axis letters are written out rather
        // than taken from the schema, because a foam carriage's roles are the
        // whole point of the fixture: X,Y on tower 1 and Z,A on tower 2.
        void Block(char a, char b)
        {
            for (var i = 0; i < lines; i++)
            {
                var t = i / (lines - 1d);
                var u = i % 12 / 11d;
                var second = 50 * ((i / 12) % 2 == 0 ? u : 1 - u);
                sb.AppendLine($"G1 {a}{50 * t:F3} {b}{second:F3} F600");
            }
        }
        Block('X', 'Y'); // tower 1 (XY)
        var split = 2 + lines;
        Block('Z', 'A'); // tower 2 (ZA)
        return (sb.ToString(), split);
    }

    /// <summary>A slot whose world span is too big to rasterise at the viewport
    /// zoom must NOT rebake on every settled pass.
    /// <para><c>BakeSlot</c> stores <c>_cacheZoom[slot] = zc</c>, and
    /// <c>zc</c> is the CLAMPED scale:
    /// <c>min(zoom, CacheMaxPx/spanX, CacheMaxPx/spanY)</c>. The drift term in
    /// <c>BlitOrBake</c> then compares the VIEWPORT zoom against that stored
    /// value. When a cap is the binding term, <c>zc &lt; zoom</c> permanently,
    /// every bake re-stores the same clamped <c>zc</c>, and the drift ratio stays
    /// above <c>ZoomTolerance</c> forever — so <c>needBake</c> is permanently
    /// true and the slot rebakes on every limiter-eligible pass. That is the
    /// exact chaining <c>BakeMinIntervalMs</c> exists to prevent, and it is
    /// invisible in the smaller fixtures: measured there,
    /// <c>spanX = spanY = 50</c> against <c>CacheMaxPx = 2048</c> gives caps of
    /// 40.96 against a zoom of 4.72, so the clamp never binds and the drift
    /// ratio is exactly 0. The span has to exceed roughly
    /// <c>CacheMaxPx / zoom</c> (about 433 mm at that zoom) to expose it.</para>
    /// <para>Asserted as a bake COUNT over several limiter-eligible passes with
    /// nothing pending: bake once, then hold. A surface assertion cannot see
    /// this at all — a rebaked raster of an unchanged program looks identical to
    /// the one it replaced.</summary></summary>
    [AvaloniaFact]
    public async Task A_span_wider_than_the_raster_cap_does_not_rebake_on_a_settled_zoom()
    {
        var source = new StubProgramSource();
        var vm = MakeVm(source, MachineTypes.Foam);

        // 600 mm square per carriage: at the fitted zoom the world-to-pixel
        // scale is ~4.7 px/mm, so a 600 mm span wants ~2830 px on a raster
        // capped at 2048 — the cap binds and the clamped scale is strictly
        // below the viewport zoom.
        var sb = new StringBuilder("G21\nG90\n");
        void Block(char a, char b)
        {
            for (var i = 0; i < 900; i++)
            {
                var t = i / 899d;
                var u = i % 12 / 11d;
                var second = 600 * ((i / 12) % 2 == 0 ? u : 1 - u);
                sb.AppendLine($"G1 {a}{600 * t:F3} {b}{second:F3} F600");
            }
        }
        Block('X', 'Y');
        Block('Z', 'A');
        source.Load(sb.ToString());
        await SettleGeometry(vm);
        Assert.Equal(2, vm.Planes.Count);

        var view = new PlotterView { DataContext = vm };
        var window = new Window { Width = 920, Height = 560, Content = view };
        window.Show();
        for (var i = 0; i < 8; i++)
            await Pump();

        // Get both slots rastered, and the queue empty, so what follows is a
        // settled state with nothing owed.
        await DrainRebakeQueue(view);
        var fitted = view.BakeCountsForTest.ToArray();
        Assert.Equal([1, 1], fitted);

        // Now zoom in until the world is wider ON SCREEN than the raster cap.
        // The cap cannot bind at the fitted zoom however big the program is: fit
        // scales inversely with the span, so a 600 mm world at a fitted zoom of
        // ~0.39 px/mm still wants only ~234 px against a 2048 px cap. The clamp
        // binds on the product span x zoom, i.e. on ZOOM.
        for (var i = 0; i < 12; i++)
        {
            view.ZoomBy(1.6);
            for (var p = 0; p < 4; p++)
                await Pump();
        }
        await DrainRebakeQueue(view);
        var zoomed = view.BakeCountsForTest.ToArray();
        Assert.True(zoomed.Sum() > fitted.Sum(), "the zoom must have provoked at least one rebake");

        // Settle at the deep zoom: wait for the counts to hold still across two
        // consecutive windows, bounded so a permanently-rebaking slot fails the
        // assertion below instead of hanging here.
        int[] settled = zoomed;
        for (var round = 0; round < 8; round++)
        {
            await Task.Delay(560);
            foreach (var c in window.GetVisualDescendants().Where(c => c.GetType().Name == "PlaneCanvas"))
                c.InvalidateVisual();
            for (var i = 0; i < 8; i++)
                await Pump();
            var now = view.BakeCountsForTest.ToArray();
            if (now.SequenceEqual(settled))
            {
                settled = now;
                break;
            }
            settled = now;
        }

        // Five more limiter windows at that settled zoom, nothing changed.
        // Before the fix the counts climb every round.
        for (var round = 0; round < 5; round++)
        {
            await Task.Delay(560);
            foreach (var c in window.GetVisualDescendants().Where(c => c.GetType().Name == "PlaneCanvas"))
                c.InvalidateVisual();
            for (var i = 0; i < 8; i++)
                await Pump();
        }

        Assert.Equal(settled, view.BakeCountsForTest.ToArray());
    }

    /// <summary>A run-state flip must repaint EVERY slot, not the one that
    /// happens to be first.
    /// <para><c>ChromeFlags.RunState</c> is the one flag the raster BAKE reads,
    /// so flipping it requests a rebake — and those requests used to be single
    /// per-canvas booleans that <c>BakeSlot</c> cleared from inside the bake.
    /// On a two-slot canvas slot 0 baked, the booleans went false, slot 1
    /// recomputed <c>needBake</c> as "nothing to do" and blitted its STALE
    /// raster, so the toggle half-applied on foam: one carriage changed colour
    /// and the other kept the old one.</para>
    /// <para>The assertions are the positive form on purpose. "Slot 1 did not
    /// change" would pass just as well against a canvas that painted nothing at
    /// all, so the test instead measures green with the toggle ON and OFF for
    /// EACH carriage separately — by acking one carriage's rows while the
    /// other's stay idle, every green pixel on the surface provably belongs to
    /// the carriage under test.</para></summary>
    [AvaloniaFact]
    public async Task The_run_state_toggle_repaints_BOTH_carriages()
    {
        var source = new StubProgramSource();
        var vm = MakeVm(source, MachineTypes.Foam);
        var (gcode, plane0Rows) = TwoCarriageProgram(120);
        source.Load(gcode);
        Assert.Equal(2, vm.Planes.Count);
        // The two blocks must land in DIFFERENT carriages, or the whole
        // attribution below is void and the test passes for the wrong reason.
        Assert.True(vm.Planes[0].Count > 0, "the XY block must reach carriage 1");
        Assert.True(vm.Planes[1].Count > 0, "the ZA block must reach carriage 2");

        var view = new PlotterView { DataContext = vm };
        var window = new Window { Width = 920, Height = 560, Content = view };
        window.Show();
        for (var i = 0; i < 8; i++)
            await Pump();
        Assert.Equal([0, 1], view.DrawableSlotsForTest.ToArray());

        // Premise: the defect is only observable once BOTH slots hold a raster.
        // Slot 1's is born on the first bake the 500 ms rate limiter lets
        // through, so wait past the limiter and poke the canvas again.
        await Task.Delay(600);
        var planeCanvas = window.GetVisualDescendants()
            .Single(c => c.GetType().Name == "PlaneCanvas");
        planeCanvas.InvalidateVisual();
        for (var i = 0; i < 3; i++)
            await Pump();
        var caches = (Array?)Field(planeCanvas, "_cache");
        Assert.NotNull(caches);
        for (var slot = 0; slot < 2; slot++)
            Assert.True(caches!.GetValue(slot) is not null,
                $"slot {slot} must hold a baked raster before the toggle, or this test only ever measures one slot");

        // Carriage 1 alone: only its rows are acked, so any green on the
        // surface is carriage 1's and carriage 2 provably contributes none.
        source.AckRows(0, plane0Rows);
        for (var i = 0; i < 8; i++)
            await Pump();
        // The limiter defers all but the first slot's bake, and the queue it
        // leaves behind is served by a timer — so every flip pair below has to
        // be drained before the surface is read. Reading early is what made
        // this test unable to see a slot repaint at all.
        view.PushChromeFlags(ChromeFlags.Default with { RunState = false });
        for (var i = 0; i < 8; i++)
            await Pump();
        await DrainRebakeQueue(view);
        var offEmpty = CountSurface(PlotterPixels.Snapshot(CaptureFrame(window)), IsRunStateGreen);
        view.PushChromeFlags(ChromeFlags.Default);
        for (var i = 0; i < 8; i++)
            await Pump();
        await DrainRebakeQueue(view);
        var carriage1 = CountSurface(PlotterPixels.Snapshot(CaptureFrame(window)), IsRunStateGreen);
        Assert.True(offEmpty == 0, $"run-state off must paint no green at all, found {offEmpty}");
        Assert.True(carriage1 > 400,
            $"carriage 1's own rows must paint green with the toggle on, found {carriage1} green pixels");

        // Carriage 2 as well: the same two flips again. BOTH slots are rebaked
        // here - nothing in the request path skips one - so the whole
        // difference between the two readings is not carriage 2 having been
        // baked while carriage 1 was skipped. It is that carriage 1's re-render
        // comes out IDENTICAL, so it adds no green, while carriage 2's flips
        // from its previous reading. The attribution rests on that, so it is
        // stated here rather than left as a claim about the rebake path.
        source.AckRows(plane0Rows, source.RowCount);
        for (var i = 0; i < 8; i++)
            await Pump();
        view.PushChromeFlags(ChromeFlags.Default with { RunState = false });
        for (var i = 0; i < 8; i++)
            await Pump();
        await DrainRebakeQueue(view);
        view.PushChromeFlags(ChromeFlags.Default);
        for (var i = 0; i < 8; i++)
            await Pump();
        await DrainRebakeQueue(view);
        var both = CountSurface(PlotterPixels.Snapshot(CaptureFrame(window)), IsRunStateGreen);
        Assert.True(both > carriage1 + 400,
            $"carriage 2 must repaint with the toggle: {both} green pixels with both carriages against {carriage1} with carriage 1 alone");
    }

    /// <summary>Private-field read, with the base-type hop the private nested
    /// <c>PlotCanvas</c> base needs (<c>_cache</c> and friends are declared
    /// there, not on the derived canvas).</summary>
    private static object? Field(object o, string name) =>
        o.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(o)
        ?? o.GetType().BaseType!
            .GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(o);

    /// <summary>A recolor-window OVERFLOW must repaint EVERY carriage, and only
    /// the carriages that overflowed.
    /// <para>This is the path the existing run-state test cannot reach. That one
    /// flips a chrome flag, which sets the request <c>urgent</c>; this one
    /// overflows <c>OverlayCap</c> inside <c>DrawSlotFrontier</c>, which does
    /// NOT — and the two took different routes through the old code. The
    /// overflow wrote one canvas-wide boolean, so whichever slot baked next
    /// satisfied the request for both. Slot 0 is always evaluated first and
    /// always found the 500 ms window open, so it baked and slot 1 recomputed
    /// <c>needBake</c> as false and blitted its stale raster: a &gt;2048-row
    /// delta (routine on a 100k-segment file) left carriage 2 permanently
    /// stale. Worse, the re-arm kept the request true, so slot 0 rebaked a
    /// full world-sized raster every 500 ms for as long as the flag stood.</para>
    /// <para>Green is the observable, and it can only come from a BAKE here: the
    /// overflow branch returns before drawing any vectors, so the recolor
    /// window contributes nothing. Per-carriage counts, because the defect is
    /// asymmetric and a surface total would be satisfied by slot 0 alone.</para></summary>
    [AvaloniaFact]
    public async Task A_recolor_window_overflow_rebakes_BOTH_carriages_and_then_stops()
    {
        var source = new StubProgramSource();
        var vm = MakeVm(source, MachineTypes.Foam);
        // More than OverlayCap (2048) motion rows PER CARRIAGE, so acking
        // everything overflows the window in BOTH slots. Half the rows would
        // overflow only slot 0 and the assertion could not tell "slot 1 was
        // never asked" from "slot 1 was asked and starved".
        source.Load(TwoCarriageProgram(2400).Program);
        await SettleGeometry(vm);   // over AsyncBuildThreshold: the build is on the thread pool
        Assert.Equal(2, vm.Planes.Count);
        Assert.True(vm.Planes[0].Count > OverlayCap, $"carriage 1 must hold more than the cap of {OverlayCap} rows, got {vm.Planes[0].Count}");
        Assert.True(vm.Planes[1].Count > OverlayCap, $"carriage 2 must hold more than the cap of {OverlayCap} rows, got {vm.Planes[1].Count}");

        var view = new PlotterView { DataContext = vm };
        var window = new Window { Width = 920, Height = 560, Content = view };
        window.Show();
        for (var i = 0; i < 8; i++)
            await Pump();

        // Slot 1's first raster is born on the first bake the limiter lets
        // through, so wait past the window and poke the canvas again — the
        // limiter is real and this test is not entitled to skip it.
        var canvas = window.GetVisualDescendants().Single(c => c.GetType().Name == "PlaneCanvas");
        await PumpPastBakeWindow();
        canvas.InvalidateVisual();
        for (var i = 0; i < 3; i++)
            await Pump();

        // Ack EVERY row: both carriages' recolor windows now exceed the cap, so
        // the overflow branch is what asks for the rebake in both slots.
        source.AckRows(0, source.RowCount);
        for (var i = 0; i < 8; i++)
            await Pump();
        // Past the limiter, so slot 1 — queued behind slot 0 — is served too.
        await DrainRebakeQueue(view);

        var frame = PlotterPixels.Snapshot(CaptureFrame(window));
        var carriage0 = GreenInCarriage(frame, 0);
        var carriage1 = GreenInCarriage(frame, 1);
        Assert.True(carriage0 > 400, $"carriage 1 must be rebaked green, found {carriage0} green pixels");
        Assert.True(carriage1 > 400, $"carriage 2 must be rebaked green too, found {carriage1} green pixels");

        // And it has to STOP. With the canvas-wide request the re-arm never
        // cleared, so slot 0 rebaked every 500 ms forever; two windows with
        // nothing changing in between must produce no new bake at all.
        var bakes = view.BakeCountsForTest.Sum();
        await Task.Delay(1200);
        for (var i = 0; i < 8; i++)
            await Pump();
        var later = view.BakeCountsForTest.Sum();
        Assert.Equal(bakes, later);
    }

    /// <summary><see cref="BakeMinIntervalMs"/> has to bind the URGENT path too.
    /// <para>The old code short-circuited it — <c>immediate || CanBakeNow()</c>
    /// — so a single chrome flip on foam baked EVERY slot back to back, each one
    /// a full world-sized raster, against a constant whose doc says "bakes of
    /// huge paths take tens to hundreds of ms, so they must never chain". The
    /// short-circuit also skipped <c>CanBakeNow</c>'s side effect, so
    /// <c>_lastBakeTicks</c> was never stamped for the work just done and the
    /// limiter's window was blind to it: the very next pass started a fresh
    /// window.</para>
    /// <para>Asserted on a bake COUNT, not on pixels: a limiter that refuses a
    /// bake and a limiter that runs one are indistinguishable on the surface
    /// when the flags produce the same picture. The flip must still take effect
    /// on both carriages, so the limiter is not allowed to swallow the request
    /// either — the second half waits the window out and repaints.</para></summary>
    [AvaloniaFact]
    public async Task A_burst_of_chrome_flips_does_not_chain_one_bake_per_slot()
    {
        const int flips = 6;
        var source = new StubProgramSource();
        var vm = MakeVm(source, MachineTypes.Foam);
        var (gcode, _) = TwoCarriageProgram(600);
        source.Load(gcode);
        await SettleGeometry(vm);
        Assert.Equal(2, vm.Planes.Count);

        var view = new PlotterView { DataContext = vm };
        var window = new Window { Width = 920, Height = 560, Content = view };
        window.Show();
        for (var i = 0; i < 8; i++)
            await Pump();
        await PumpPastBakeWindow();
        var canvas = window.GetVisualDescendants().Single(c => c.GetType().Name == "PlaneCanvas");
        canvas.InvalidateVisual();
        for (var i = 0; i < 3; i++)
            await Pump();

        var before = view.BakeCountsForTest.Sum();
        for (var i = 0; i < flips; i++)
        {
            // Alternate, or the RunState setter sees no change and asks for
            // nothing — the burst would be a no-op and the count would prove
            // nothing.
            view.PushChromeFlags(ChromeFlags.Default with { RunState = i % 2 == 0 });
            for (var p = 0; p < 8; p++)
                await Pump();
        }
        var after = view.BakeCountsForTest.Sum();
        var chained = after - before;

        // The old behaviour was one bake per slot per flip: 12. The limiter lets
        // ONE slot open the window early, once per window, and queues the rest,
        // so a burst inside a single window costs one bake, not one per slot per
        // flip. The floor of 1 is the real one: the flips must not be swallowed.
        Assert.True(chained <= 2,
            $"{flips} chrome flips must not chain {flips * 2} bakes, but produced {chained}");
        Assert.True(chained >= 1,
            $"the flips still have to reach the bake path, but produced only {chained} bakes");

        // The limiter must not turn into a stranding: both carriages end up
        // repainted once the window opens.
        await PumpPastBakeWindow();
        source.AckRows(0, source.RowCount);
        for (var i = 0; i < 8; i++)
            await Pump();
        await DrainRebakeQueue(view);
        var frame = PlotterPixels.Snapshot(CaptureFrame(window));
        Assert.True(GreenInCarriage(frame, 0) > 400, $"carriage 1 must be rebaked, found {GreenInCarriage(frame, 0)} green pixels");
        Assert.True(GreenInCarriage(frame, 1) > 400, $"carriage 2 must be rebaked, found {GreenInCarriage(frame, 1)} green pixels");
    }

    /// <summary>Green pixels inside ONE carriage's diagonal tile of the foam
    /// composition — slot 0 is the top-left quadrant, slot 1 the bottom-right
    /// (<c>PlaneCanvas.SlotViewport</c>). The two are counted separately
    /// because the defect this exists for is ASYMMETRIC: slot 0 always won the
    /// bake limiter and slot 1 stayed stale, so a whole-surface total would be
    /// satisfied by slot 0 alone.</summary>
    private static int GreenInCarriage(PlotterPixels.Frame frame, int slot) =>
        PlotterPixels.Count(frame, IsRunStateGreen,
            xFilter: x => x > 0 && x < frame.Width - 1 && (slot == 0 ? x < frame.Width / 2 : x >= frame.Width / 2),
            yFilter: y => y > 24 && y < frame.Height - 1 && (slot == 0 ? y < frame.Height / 2 : y >= frame.Height / 2));

    /// <summary>Raises a named button's Click the way the user does. Avalonia has
    /// no <c>Button.Command.Execute</c> shortcut here (these are Click handlers,
    /// not commands), so the routed event is the honest equivalent.</summary>
    private static void Click(PlotterView view, string name) =>
        view.FindControl<Button>(name)!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    /// <summary>The zoom cluster scales the composition about its CENTER, not
    /// about the cursor: the two foam planes share one transform, so a
    /// cursor-anchored zoom slides the OTHER plane sideways per notch — the
    /// "zooming shrinks / clips the path" report. The button cluster is
    /// therefore center-anchored, and round-tripping in and back out has to
    /// land on the exact fitted zoom or the buttons would slowly drift the
    /// view.</summary>
    [AvaloniaFact]
    public async Task The_zoom_buttons_scale_the_composition_about_its_center()
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

        var start = view.CurrentZoom;
        Assert.True(start > 0, $"the fit must have run before the readout means anything, got {start}");

        // The anchor, measured through the pixels. The drawn path's world bounds
        // never change, so a center-anchored zoom shrinks the on-screen box
        // TOWARD the canvas center and leaves it sitting there. A zoom that
        // raised the zoom factor but left the pan alone scales the box just the
        // same — it slides it off the center instead, which is what the delta
        // below catches. Zooming OUT rather than in is deliberate: the fit has
        // no margin, so the very first zoom-in clips the box at the frame edge
        // and there is nothing left to measure. The overlay is hidden for the
        // measurement: the chrome's own grey button borders would widen the box
        // by themselves.
        view.FindControl<Grid>("OverlayHost")!.IsVisible = false;
        for (var i = 0; i < 8; i++)
            await Pump();
        var planes = view.FindControl<Control>("PlanesGrid")!;
        var topLeft = planes.TranslatePoint(default, window)
            ?? throw new InvalidOperationException("the plot grid is not in the window");
        var cx = topLeft.X + planes.Bounds.Width / 2;
        var cy = topLeft.Y + planes.Bounds.Height / 2;
        var before = PathBounds(CaptureFrame(window));
        Assert.True(before.Width > 50, $"the path must be drawn to measure it, got {before}");

        Click(view, "ZoomOutButton");
        for (var i = 0; i < 8; i++)
            await Pump();
        Assert.True(view.CurrentZoom < start, $"zoom out must scale down: {start} → {view.CurrentZoom}");

        var after = PathBounds(CaptureFrame(window));
        Assert.True(after.Width < before.Width * 0.9,
            $"the drawing must shrink: {before.Width:F1} px wide before, {after.Width:F1} after");
        Assert.True(Math.Abs(after.Cx - before.Cx) < 6 && Math.Abs(after.Cy - before.Cy) < 6,
            $"the center must hold still: the path box was centered on ({before.Cx:F0},{before.Cy:F0}) " +
            $"(the canvas center is ({cx:F0},{cy:F0})) and must still be there after the zoom, but it " +
            $"moved to ({after.Cx:F0},{after.Cy:F0}) — {before} then {after}");

        // Round-tripping in and back out has to land on the exact fitted zoom,
        // or the buttons would slowly drift the view.
        Click(view, "ZoomInButton");
        for (var i = 0; i < 8; i++)
            await Pump();
        Assert.Equal(start, view.CurrentZoom, 6);

        Click(view, "ZoomInButton");
        for (var i = 0; i < 8; i++)
            await Pump();
        Click(view, "ZoomFitButton");
        for (var i = 0; i < 8; i++)
            await Pump();
        Assert.Equal(start, view.CurrentZoom, 6);
    }

    /// <summary>The readout must never state something untrue about the zoom,
    /// in either direction: a view with no fit has no zoom to report, and a
    /// hair above 1:1 is not 1:1 and must not round up to claiming it is.
    /// <para>Both were live. <c>UpdateZoomReadout</c> guarded on "not NaN",
    /// and <c>CurrentZoom</c> reports 0 — not NaN — for a view that has never
    /// been fitted, so an unfitted view rendered a permanent visible <b>"0%"</b>
    /// claiming it was infinitely far out. And at 1.004 the readout is visible
    /// (that is not 1:1) while <c>(1.004 * 100).ToString("0")</c> prints
    /// <b>"100"</b> — visible, and claiming exactly the value it is defined to
    /// hide. Flooring the rounded value rather than the raw one is what makes
    /// the second honest: 1.004 then reads 99%.</para></summary>
    [AvaloniaFact]
    public async Task The_zoom_readout_claims_neither_an_unfitted_zero_nor_a_rounded_up_100()
    {
        // No program loaded, so nothing is ever fitted and the zoom stays 0.
        var source = new StubProgramSource();
        var vm = MakeVm(source, MachineTypes.Mill);
        var view = new PlotterView { DataContext = vm };
        var window = new Window { Width = 920, Height = 560, Content = view };
        window.Show();
        for (var i = 0; i < 8; i++)
            await Pump();

        var readout = view.FindControl<TextBlock>("ZoomPctText")!;
        Assert.Equal(0, view.CurrentZoom, 6);   // an unloaded view has no zoom
        view.Reframe();
        for (var i = 0; i < 8; i++)
            await Pump();
        Assert.False(readout.IsVisible, "an unfitted view has no zoom to report");
        Assert.Equal(string.Empty, readout.Text);

        // Now a real fit, and a target a hair above it. The baseline is the
        // FIT, not 1.0: the view scales to its content, so the fit is already a
        // scale of ten or more and 1.0 is an arbitrary pixel-per-unit that would
        // make every reading meaningless. Asking for fit * 1.004 is the same
        // "a hair off the default" question this test was written to ask.
        source.Load(SpiralProgram(600));
        for (var i = 0; i < 8; i++)
            await Pump();
        Assert.True(view.FittedZoom > 0, "a loaded program must have been fitted by now");
        view.ZoomTo(view.FittedZoom * 1.004);
        for (var i = 0; i < 8; i++)
            await Pump();
        Assert.True(readout.IsVisible, "1.004x the fit is not the fit, so there is something to say");
        // 99, not 100: 100% is exactly the claim the hidden-at-fit rule exists
        // to keep off the screen, and 1.004 * 100 is 100.4, which both Round
        // and Floor turn into 100.
        Assert.Equal("99%", readout.Text);
        Assert.DoesNotContain("100%", readout.Text);
    }

    /// <summary>The wheel has to feed the readout too, not just the buttons: the
    /// wheel is how a user zooms most of the time, and a percentage that only
    /// moves for the button cluster reads as a broken indicator. At the fitted
    /// view — 100% — the readout is hidden: the number is noise when the drawing
    /// is at the size the viewport asked for.</summary>
    /// <summary>The plot surface must be the wheel and drag target at EVERY zoom,
    /// including a low one.
    /// <para>Avalonia hit-tests a custom-drawn control over what it PAINTS, and
    /// <c>PlotCanvas</c> paints a cached raster, so the blank majority of the
    /// surface was not a target: the hit fell through to the white <c>Border</c> in
    /// the view, which has no handlers, and wheel-zoom plus drag-pan were dead over
    /// most of the plot. Probing a 5x5 grid found the canvas returned at zoom 2.0
    /// and the Border at 1.25 and below, at every point tried.</para>
    /// <para>The fix is <c>PlotCanvas : Control, ICustomHitTest</c> claiming its
    /// whole box (<c>Background = Brushes.Transparent</c> was tried first and does
    /// not port from the Nex view: that works on a <c>Panel</c>, while
    /// <c>Background</c> on a <c>TemplatedControl</c> is template-bound and these
    /// canvases have no template - measured, still falling through). This test
    /// asserts the canvas is in the hit chain at a zoom where it demonstrably was
    /// not, so <c>false &amp;&amp;</c> in that override turns it red.</para></summary>
    [AvaloniaFact]
    public async Task The_plot_canvas_is_the_hit_target_at_a_low_zoom()
    {
        var source = new StubProgramSource();
        var vm = MakeVm(source, MachineTypes.Mill);
        source.Load(SpiralProgram(600));
        var view = new PlotterView { DataContext = vm };
        var window = new Window { Width = 920, Height = 560, Content = view };
        window.Show();
        for (var i = 0; i < 8; i++)
            await Pump();

        // Zoom right down, so the world is as large on screen as the clamp allows -
        // the regime where the blank-area fall-through showed. The fit lands near 8x
        // for this program, so a handful of 1.25 steps is enough; the loop stops as
        // soon as the target is reached rather than assuming how many steps it takes.
        for (var i = 0; i < 24 && view.CurrentZoom > 0.35; i++)
        {
            view.ZoomBy(1 / 1.25);
            for (var p = 0; p < 8; p++)
                await Pump();
        }
        Assert.True(view.CurrentZoom <= 0.35,
            $"this test is about a LOW zoom; CurrentZoom is {view.CurrentZoom}");

        // Probe a grid across the surface's OWN box. Hard-coded window coordinates
        // landed on the 24px header and outside the planes area, which tests
        // something else entirely.
        var surface = window.GetVisualDescendants()
            .OfType<Control>()
            .FirstOrDefault(c => c.GetType().Name is "PlaneCanvas" or "IsoCanvas");
        Assert.NotNull(surface);
        // Bounds are in the canvas's own coordinates; InputHitTest wants window
        // coordinates, so translate the local box out before probing.
        var origin = surface!.TranslatePoint(new Point(0, 0), window)
            ?? throw new InvalidOperationException("the surface is not in the window");
        var box = new Rect(origin, surface.Bounds.Size);
        Assert.True(box.Width > 40 && box.Height > 40,
            $"the surface box is too small to probe: {box.Width}x{box.Height}");

        for (var x = box.X + 6; x < box.Right - 6; x += box.Width / 4)
        {
            for (var y = box.Y + 6; y < box.Bottom - 6; y += box.Height / 4)
            {
                var point = new Point(x, y);
                var hit = window.InputHitTest(point);
                Assert.NotNull(hit);
                Assert.True(hit!.IsHitTestVisible,
                    $"probe ({point}) resolved to something not hit-testable: {hit.GetType().Name}");

                // The wheel reaches the plot only if the thing under the cursor
                // chains up to a PlotCanvas. An overlay control or the wrapping
                // Border carries no handlers, so either one means dead input. Walk
                // out and look for the canvas rather than testing the leaf's type:
                // the invariant is that the canvas is in the chain, wherever in it
                // the hit lands.
                var chain = new List<string>();
                for (var node = hit as Avalonia.Visual; node is not null; node = node.GetVisualParent())
                    chain.Add(node.GetType().Name);
                Assert.True(
                    chain.Any(t => t.StartsWith("PlaneCanvas", StringComparison.Ordinal)
                                || t.StartsWith("IsoCanvas", StringComparison.Ordinal)),
                    $"at zoom {view.CurrentZoom:F2} probe ({point}) in box {box} the wheel and drag are dead "
                    + $"there: no canvas in the hit chain [{string.Join(" < ", chain)}]");
            }
        }
    }


    [AvaloniaFact]
    public async Task The_zoom_readout_hides_at_the_fitted_view()
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

        var readout = view.FindControl<TextBlock>("ZoomPctText")!;
        Assert.True(view.FittedZoom > 0, "a loaded program must have been fitted by now");

        // 100% is the FITTED view: the whole content in the viewport at the size
        // the viewport wants it. That is the state "back to normal", and a
        // percentage on it is noise — which is the rule that used to be stated
        // against 1.1 and read as a claim about physical size.
        view.ZoomTo(view.FittedZoom);
        for (var i = 0; i < 8; i++)
            await Pump();
        Assert.False(readout.IsVisible, "at the fit the readout is noise");

        view.ZoomTo(view.FittedZoom * 1.25);
        for (var i = 0; i < 8; i++)
            await Pump();
        Assert.True(readout.IsVisible);
        Assert.Contains("125", readout.Text);

        // The wheel reaches the same indicator: one notch is 1.15x, so the
        // number on screen follows the pointer. Driven from the FITTED view
        // (where the readout is already showing) rather than from 1:1.
        //
        // This comment used to blame 1:1 and call it a harness artefact, then
        // a second version blamed a "boundary between 1.25 and 2.0". Both were
        // describing the same real defect - the canvas was only a hit target
        // where it painted, so below ~2.0 the hit fell through to the white
        // Border and the wheel did nothing - and e7ea40d fixed the cause
        // (PlotCanvas implements ICustomHitTest and claims its whole box). The
        // boundary is gone, not merely unexplained. The probe below is still
        // asserted because the wheel handler lives on the canvas: driving the
        // wheel without proving the event can reach it would test nothing.
        view.Reframe();
        for (var i = 0; i < 8; i++)
            await Pump();
        // Reframe puts the view BACK ON THE FIT, which is 100% — so the readout
        // is now hidden. This assertion used to be the other way round, and the
        // reason it was written that way is the bug: the fit is a scale of ten or
        // more, "not 1:1" was true of the fitted view, and so a freshly fitted
        // view greeted the user with a percentage instead of silence.
        Assert.False(readout.IsVisible, "a re-fitted view is exactly 100%, so the readout is hidden");

        // The canvas type is private to the view, hence the name match rather
        // than a type test.
        var probe = new Point(460, 280);
        Assert.Equal("PlaneCanvas", window.GetVisualAt(probe)?.GetType().Name);

        // Zoom off the fit first, so there is a number for the wheel to move.
        view.ZoomTo(view.FittedZoom * 1.25);
        for (var i = 0; i < 8; i++)
            await Pump();
        Assert.True(readout.IsVisible, "off the fit there is a percentage to report");

        var before = readout.Text;
        window.MouseWheel(probe, new Vector(0, 1), RawInputModifiers.None);
        for (var i = 0; i < 8; i++)
            await Pump();
        Assert.NotEqual(before, readout.Text);
        // Floor, not Round, to match the readout: the readout never claims a
        // percentage it has rounded up to, so 1.004 reads 99% rather than a
        // visible "100%". Asserting the rounded figure here instead is how the
        // old rounding rule survived in this test after the readout was fixed.
        // Relative to the fit, for the same reason the readout is.
        Assert.Contains(
            ((int)Math.Floor(view.CurrentZoom / view.FittedZoom * 100)).ToString(CultureInfo.InvariantCulture),
            readout.Text);
    }

    /// <summary>Every overlay control exists and is an overlay: the PNG export
    /// (Task 12) paints through fresh canvases and never instantiates this
    /// XAML, so "a saved image has no header or buttons" is true by
    /// construction ONLY while these live here instead of in the header.</summary>
    [AvaloniaFact]
    public void The_overlay_chrome_exists_and_is_hidden_from_the_toolpath()
    {
        var view = new PlotterView();

        foreach (var name in new[]
        {
            "GridToggle", "OriginToggle", "EnvelopeToggle", "RunStateToggle",
            "ZoomInButton", "ZoomOutButton", "ZoomFitButton", "SavePngButton",
        })
            Assert.True(view.FindControl<Control>(name) is not null, $"the overlay is missing {name}");

        // The header must NOT grow them: the iso toggle was the header's only
        // control by design and the 24px row has no room for four more. The
        // toggles are ToggleButtons since the navBtn port; enumerating CheckBox
        // here would have gone on passing after they stopped being CheckBoxes,
        // which is exactly the vacuous pass this assertion exists to forbid.
        var chromeInHeader = view.GetVisualDescendants().OfType<ToggleButton>()
            .Where(c => c.Name is "GridToggle" or "OriginToggle" or "EnvelopeToggle" or "RunStateToggle")
            .Any(c => c.GetVisualAncestors().Any(a => a is Border { Name: "HeaderHost" }));
        Assert.False(chromeInHeader, "the chrome toggles belong in the overlay, not the header row");
    }

    /// <summary>The overlay and the App Settings leaf are two views onto ONE
    /// model — the same instance the module hands both. Two clones would let a
    /// flip on one surface leave the other stale, which is the whole class of
    /// bug the shared model exists to prevent.
    /// <para>It is asserted through the PIXELS, not through
    /// <c>DebugStoredChromeFlags</c>: the model reaching the setter proves
    /// nothing about the canvases, and the canvases are what the user sees.</para></summary>
    [AvaloniaFact]
    public async Task The_overlay_and_the_settings_leaf_share_one_model()
    {
        var dir = Path.Combine(Path.GetTempPath(), "plotter-overlay-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var settingsService = new SettingsService(dir);
            var mill = settingsService.Load();
            mill.MachineTypeId = MachineTypes.Mill;
            settingsService.Save(mill);

            var shared = new PlotterSettingsViewModel(settingsService);
            Assert.True(shared.ShowGrid, "the four toggles default to on");

            var source = new StubProgramSource();
            var vm = MakeVm(source, MachineTypes.Mill);
            source.Load(SpiralProgram(600));

            var view = new PlotterView { DataContext = vm, OverlaySettings = shared };
            var window = new Window { Width = 920, Height = 560, Content = view };
            window.Show();
            for (var i = 0; i < 8; i++)
                await Pump();

            // The model reached the overlay's toggles (ToggleButtons since the
            // navBtn port — CheckBox would no longer find them)...
            var gridToggle = window.GetVisualDescendants()
                .OfType<ToggleButton>().Single(c => c.Name == "GridToggle");
            Assert.True(gridToggle.IsChecked == true);

            // The lattice the shared model's default asked for is on the canvas.
            //
            // No `on > 0` precondition here, and that is a correction: it stood
            // in for "the grid painted" and proved nothing. IsChromeLight also
            // counts the toolpath's antialiased halo, so a view with the grid
            // entirely absent still measured ~16k light pixels — the red run
            // behind this test read on=16117 against without=16130, i.e. the
            // grid contributed nothing at all and the precondition sailed
            // through. The ratio assertion at the end is the one that carries
            // the claim, and it subsumes this anyway.
            var on = CountSurface(PlotterPixels.Snapshot(CaptureFrame(window)), IsChromeLight);

            // ...and a LEAF-side flip (the settings dialog, not the overlay)
            // has to repaint the plot canvases, which only happens if the view
            // is bound to the live model rather than to a copy of it. The
            // threshold is a RATIO, like the grid-toggle test: IsChromeLight
            // also catches the toolpath's antialiased halo, so the off figure
            // is nowhere near zero on its own.
            shared.ShowGrid = false;
            for (var i = 0; i < 8; i++)
                await Pump();
            Assert.False(gridToggle.IsChecked == true, "the overlay must follow the shared model");
            var without = CountSurface(PlotterPixels.Snapshot(CaptureFrame(window)), IsChromeLight);
            Assert.True(without < on / 2,
                $"the lattice must go away after the leaf-side flip, found {without} of {on} light pixels");

            // And the other direction: the overlay's own checkbox writes through
            // to the model, so the leaf is not left stale.
            gridToggle.IsChecked = true;
            for (var i = 0; i < 8; i++)
                await Pump();
            Assert.True(shared.ShowGrid, "the overlay must write through to the shared model");
            var back = CountSurface(PlotterPixels.Snapshot(CaptureFrame(window)), IsChromeLight);
            Assert.True(back > without,
                $"the lattice must come back after the overlay-side flip, {back} light pixels against {without}");
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
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

        // The overlay is hidden for the measurement: these assertions are
        // about the CANVAS painters, and the toolbar's own glyphs sit on the
        // same surface, so a visible overlay would put chrome in the frame
        // that this test is not counting. (Its glyphs are grey — #777/#444/
        // #222 — and so do not match the teal probe; the hiding is about
        // keeping the count to the envelope alone, not about a colour clash.)
        // The chrome has its own tests; this one stays about the drawing.
        view.FindControl<Grid>("OverlayHost")!.IsVisible = false;
        for (var i = 0; i < 8; i++)
            await Pump();

        var drawn = CountSurface(PlotterPixels.Snapshot(CaptureFrame(window)), IsEnvelopeTeal);
        Assert.True(drawn > 100, $"the job rectangle must paint, found {drawn} teal pixels");

        view.PushChromeFlags(ChromeFlags.Default with { Envelope = false });
        for (var i = 0; i < 8; i++)
            await Pump();
        var left = CountSurface(PlotterPixels.Snapshot(CaptureFrame(window)), IsEnvelopeTeal);
        Assert.Equal(0, left);
    }

    /// <summary>The teal envelope (<c>PlotterChromePens.Envelope</c> =
    /// <c>#00b2e6</c> at 50% alpha, Nex's <c>0, 0.7, 0.9</c>), allowing for
    /// the antialiased edge pixels AND for alpha blending against whatever the
    /// stroke crosses.
    ///
    /// <para>The two channels are tested as RATIOS, not as absolute values, and
    /// that is forced by the translucency. At 50% over white the stroke is
    /// (128, 216, 242) and over the grey toolpath it is (67, 156, 182), so a
    /// `b &gt; 190` test — the kind that works for the opaque wire — misses every
    /// envelope pixel that crosses the path. The <c>g &gt; r + 40</c> gap instead
    /// survives blending: teal's r-to-g separation is 178 at full opacity and
    /// 178*alpha wherever it lands, so it never approaches the grey band's
    /// <c>|r - g| &lt;= 14</c>. <c>b &gt; g</c> then separates it from the greys
    /// and the two greens, which have b below g.</para>
    ///
    /// <para>Checked against every other detector in this file: the axis
    /// colours (X/Y/Z), the grey path, the blended wire, the run-state green
    /// and the light chrome all fail at least one clause.</para></summary>
    private static bool IsEnvelopeTeal(byte r, byte g, byte b) =>
        g > r + 40 && b > g;

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
        // arrows are gated on Chrome.Origin — the default is ON, so they paint
        // here, and the envelope toggle is the only lever that isolates them
        // from the shafts; the dedicated
        // <c>The_origin_toggle_hides_the_world_axis_arrows_in_the_flat_pane</c>
        // pins the Origin side. This state measures them cleanly at 568 blue
        // pixels. The grid toggle makes no difference (568 either way) — the
        // lattice recedes beneath the path and never comes near the arrows.
        //
        // The test is not vacuous in this state. Removing the DrawAxes call from
        // DrawSlotChrome drops it to 0 blue pixels — that is a mutation, not a
        // flag state, and it is the number that proves the test still fails if
        // the arrows stop painting.
        view.PushChromeFlags(ChromeFlags.Default with { Envelope = false });
        for (var i = 0; i < 8; i++)
            await Pump();

        // Per colour, and the envelope is off here so nothing buries a shaft.
        // Measured in this state: X 456, Y 94, Z 0. The mutational zero for all
        // three is 0 — removing the DrawAxes call empties every count — and the
        // floors sit well under the measurements so a different window size
        // cannot tip them over. Z is asserted EQUAL to zero, which is a real
        // statement rather than a gap in the test: a face-on pane has no Z
        // direction to point, and the iso painter is the only one that draws it.
        var axesFrame = PlotterPixels.Snapshot(CaptureFrame(window));
        var flatX = CountSurface(axesFrame, IsAxisRed);
        Assert.True(flatX > 100, $"the X arrow must paint RED, found {flatX} red pixels");
        var flatY = CountSurface(axesFrame, IsAxisGreen);
        Assert.True(flatY > 40, $"the Y arrow must paint GREEN, found {flatY} green pixels");
        Assert.Equal(0, CountSurface(axesFrame, IsAxisBlue));
    }

    /// <summary>The three axis colours, one predicate each: #d93333 X red,
    /// #33cc33 Y green, #3359e6 Z blue. Separate rather than one "is it bluish"
    /// band, because a single band matched only the Z arrow the moment the
    /// palette became per-axis and the "the arrows must paint" assertion went on
    /// passing while two of the three arrows went unpainted. Each is checked
    /// separately below, so an unpainted arrow is a failing test.</summary>
    private static bool IsAxisRed(byte r, byte g, byte b) =>
        r > 150 && g < 110 && b < 110 && r > g + 60;

    private static bool IsAxisGreen(byte r, byte g, byte b) =>
        g > 140 && r < 110 && b < 110 && g > r + 60;

    private static bool IsAxisBlue(byte r, byte g, byte b) =>
        b > 150 && r < 110 && g < 130 && b > r + 100;

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

    /// <summary>The lattice stands on the work envelope's FLOOR, not on world
    /// Z = 0. For any job that cuts below the origin those are different planes,
    /// and the floor is the one the tool actually works on — so a lattice pinned
    /// to Z = 0 hangs in mid-air above the box, which is what the mill profile
    /// showed on a program whose Z span was -6.4 to +2.
    /// <para>The discriminator is a coincidence, not a pixel count. The lattice
    /// spans the same XY footprint as the box's bottom face and is drawn at the
    /// same Z, so its LOWEST row is the same projected point as the box's lowest
    /// teal row. Float it up to Z = 0 and the two separate by the height of the
    /// whole Z span. Counting is not the fragile part here; <c>IsChromeLight</c>
    /// also matches the toolpath's antialiased halo, and the halo reaches the
    /// floor here, so a raw "lowest light row" would match even with the bug.
    /// The grid-OFF render subtracts it: the envelope, the axes and the path are
    /// identical in both frames, so light-here-and-not-there IS the lattice.</para>
    [AvaloniaFact]
    public async Task The_iso_grid_stands_on_the_envelope_floor_not_on_world_zero()
    {
        var source = new StubProgramSource();
        var vm = MakeVm(source, MachineTypes.Mill);
        // Straddles the origin on purpose: JobZRange unions the span with 0, so a
        // job that only cuts below zero still gets a box up to Z = 0 and the bug
        // would hide in its ceiling. Here the floor is -20 and the ceiling +20,
        // so a Z = 0 lattice lands halfway up the box — unmistakable.
        source.Load("G21\r\nG1 X10 Y10 Z-20 F600\r\nG1 X100 Y50 Z20 F600");
        Assert.True(vm.HasGeometry, "the iso chrome needs real motion to wrap a job box around");

        var view = new PlotterView { DataContext = vm };
        var window = new Window { Width = 920, Height = 560, Content = view };
        window.Show();
        for (var i = 0; i < 8; i++)
            await Pump();
        window.GetVisualDescendants().OfType<CheckBox>()
            .Single(c => c.Name == "IsoToggle")
            .IsChecked = true;
        for (var i = 0; i < 8; i++)
            await Pump();

        // Both renders keep the envelope ON, so the teal reference is present
        // in the frame the lattice is measured against.
        var withGrid = PlotterPixels.Snapshot(CaptureFrame(window));
        view.PushChromeFlags(ChromeFlags.Default with { Grid = false });
        for (var i = 0; i < 8; i++)
            await Pump();
        var withoutGrid = PlotterPixels.Snapshot(CaptureFrame(window));

        var boxBottom = LowestRow(withGrid, IsEnvelopeTeal);
        var latticeBottom = LowestRowThatIs(withGrid, IsChromeLight, withoutGrid);
        Assert.True(boxBottom > 0, $"the iso job box must paint, no teal found in {withGrid.Width}x{withGrid.Height}");
        Assert.True(latticeBottom > 0, "the lattice must paint: nothing was light in one render and not the other");

        // Same plane, same footprint, same projection: the same point, so this is
        // a couple of rows of antialiasing and nothing more. Before the fix the
        // gap was the projected height of 40mm of Z, which is ~40 rows here.
        Assert.True(Math.Abs(latticeBottom - boxBottom) <= 6,
            $"the lattice must stand on the envelope floor: its lowest row is {latticeBottom}, the box's is {boxBottom}");
    }

    /// <summary>Lowest row (largest y) holding a matching pixel, or -1. Skips the
    /// 24 px header and the 1 px frame for the reason
    /// <see cref="PathBounds"/> records: the header's subpixel text fringes leak
    /// teal and blue pixels that have nothing to do with the canvas.</summary>
    private static int LowestRow(PlotterPixels.Frame frame, Func<byte, byte, byte, bool> match)
    {
        for (var y = frame.Height - 2; y >= 25; y--)
            for (var x = 1; x < frame.Width - 1; x++)
                if (match(PlotterPixels.At(frame, x, y).R, PlotterPixels.At(frame, x, y).G, PlotterPixels.At(frame, x, y).B))
                    return y;
        return -1;
    }

    /// <summary>Lowest row holding a pixel that matches in <paramref name="frame"/>
    /// but not in <paramref name="without"/>, which is how the lattice is told
    /// apart from the toolpath halo it shares a colour band with.</summary>
    private static int LowestRowThatIs(
        PlotterPixels.Frame frame, Func<byte, byte, byte, bool> match, PlotterPixels.Frame without)
    {
        for (var y = frame.Height - 2; y >= 25; y--)
            for (var x = 1; x < frame.Width - 1; x++)
            {
                var p = PlotterPixels.At(frame, x, y);
                if (!match(p.R, p.G, p.B))
                    continue;
                var q = PlotterPixels.At(without, x, y);
                if (!match(q.R, q.G, q.B))
                    return y;
            }
        return -1;
    }

    /// <summary>The iso job box and the three world axis arrows. The arrows are
    /// gated on <c>Chrome.Origin</c> — the default is ON, so they paint here
    /// (the <c>The_origin_toggle_hides_the_world_axis_arrows_in_iso</c> test
    /// pins the Origin side) — and the box is gated by <c>Chrome.Envelope</c>,
    /// so this measures both at the defaults.</summary>
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
        // The iso canvas now keeps the flat transform across the mode switch (a
        // toggle is not a refit), so these chrome-painting measurements run from
        // an explicit fit — the Fit button's seam — rather than the fit that the
        // pre-adoption rebuild happened to land on.
        view.Reframe();
        for (var i = 0; i < 8; i++)
            await Pump();

        var frame = PlotterPixels.Snapshot(CaptureFrame(window));
        // 5294 teal pixels with DrawEnvelope, 12 without it: a 40x margin, and
        // the mutation (not a flag state) is what proves the floor bites. The
        // residual 12 is NOT a small leak of the box — all twelve sit on row 3,
        // in the header's antialiased TEXT fringes, which this probe also
        // matches; see CountSurface's note on header contamination. The box's
        // own count is the two orders of magnitude between the two readings.
        var box = CountSurface(frame, IsEnvelopeTeal);
        Assert.True(box > 100, $"the iso job box must paint, found {box} teal pixels");
        // 106 blue pixels with the arrows, 0 without them. The floor is 40, not
        // 100: the job box is drawn after the arrows and its 1.5px edges bury
        // the two shafts, so 106 is the whole visible budget in this state and a
        // 100 floor would leave 6% of headroom — a flake waiting for a different
        // window size. 40 is 38% of the measured count and still 2.6x over the
        // mutational zero, so it can only pass when the arrows are painted.
        // The per-colour counts are below; the blue figure quoted here is the
        // pre-recolour measurement and is kept only to date the 40.
        // Each axis is counted in ITS OWN colour, and each must clear the same
        // 40-pixel floor the single blue count used. The job box is drawn after
        // the arrows and its 1.5px edges bury parts of the shafts, so these are
        // small budgets; the floor is ~38% of what the blue count measured in
        // this state, and 2.6x over the mutational zero, so a missing arrow
        // fails and a buried one still passes.
        // Each axis is counted in ITS OWN colour, so an unpainted or
        // mis-coloured arrow is a failing test rather than a blue count that
        // only ever proved the Z arrow. Measured in this state: X 15, Y 13,
        // Z 39 pixels. Small, because the job box is drawn AFTER the arrows and
        // its 1.5px edges lie along the X and Y shafts, burying most of them;
        // the Z arrow points out of the box's corner and stays nearly whole. The
        // floors are ~a third of each measurement. The arrows ARE toggleable —
        // Chrome.Origin gates them, default on — but this test keeps the
        // default and measures the painted state, so there is no per-colour
        // mutational zero to divide; the zero is 0, an unpainted arrow, and a
        // third of a measured 13 is as much headroom as this rendering leaves.
        var axisX = CountSurface(frame, IsAxisRed);
        Assert.True(axisX > 5, $"the X axis must paint RED, found {axisX} red pixels");
        var axisY = CountSurface(frame, IsAxisGreen);
        Assert.True(axisY > 5, $"the Y axis must paint GREEN, found {axisY} green pixels");
        var axisZ = CountSurface(frame, IsAxisBlue);
        Assert.True(axisZ > 10, $"the Z axis must paint BLUE, found {axisZ} blue pixels");
    }

    /// <summary>Records the loft decision, because it is a decision and not an
    /// omission. The foam loft's two panes are FLAT true-scale tiles sharing one
    /// composition, drawn in each plane's role order — NOT a projection — so the
    /// projected job box and the projected arrows are deliberately not drawn
    /// there: over true-scale tiles a foreshortened 30-degree box or arrow is a
    /// wrong measurement, not a style. The grid is the wire floor that joins the
    /// two tiles' bottom edges plus the world-X-parallel horizontal family (see
    /// <c>The_foam_loft_connects_the_tiles_with_a_grid_wire_floor</c>), and the
    /// flat view's per-tile lattices are gone entirely (see
    /// <c>The_flat_foam_view_draws_no_tile_lattice</c>). What the loft does keep
    /// per carriage plane is the FLAT view's own chrome — the world-axis
    /// arrows, the machine-origin cross, and the flat tile frame that makes the
    /// floor's "bottom edge" visible — gated the same way the flat view gates
    /// them: <c>Chrome.Origin</c> and <c>Chrome.Envelope</c>. The arrows are
    /// the round-4 fix ("Origin axis arrows missing in Iso mode but present in
    /// 2d"); the iso box corner connectors are the round-4 task 4, part of the
    /// job envelope.
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
        // Hidden for the same reason as in the envelope test above: the chrome
        // is a different subject, and its glyph edges land in the teal band
        // this test asserts is empty.
        //
        // The overlay is hidden, not just ignored: what was measured putting
        // teal there was 8 px of it, and NO blue contamination was found at
        // all. The blue assertion below is kept as a guard, not as a report of
        // something that was ever observed.
        view.FindControl<Grid>("OverlayHost")!.IsVisible = false;
        for (var i = 0; i < 8; i++)
            await Pump();

        // Both carriages still draw, so the surface is live; the grey count
        // WITH chrome is the toggle probe below.
        var withChrome = PlotterPixels.Snapshot(CaptureFrame(window));
        Assert.True(CountSurface(withChrome, IsPathGrey) > 100,
            $"the loft must still draw its toolpath, found {CountSurface(withChrome, IsPathGrey)} path pixels");
        var greyOn = CountSurface(withChrome, IsPathGrey);
        // The envelope is NOT the zero the old loft drew: it is the two FLAT
        // tile rectangles AND the four iso-box corner connectors between them
        // (see the box test). Asserted together here because both are the job
        // envelope and share its gate; the box's own geometry is tested
        // separately.
        var tiles = CountSurface(withChrome, IsEnvelopeTeal);
        Assert.True(tiles > 60,
            $"the loft must draw one flat envelope rectangle per tile, found {tiles} teal pixels");

        // The loft draws the FLAT pane's WORLD-AXIS arrows — red X, green Y,
        // one pair per tile — NOT the projected arrows the mill iso draws. The
        // arrows are the round-4 regression fix, so they must paint; the one
        // colour that proves no PROJECTED chrome exists is blue (a face-on
        // tile has no Z direction to point; PlotterFlatChrome.DrawAxes draws no
        // Z). The envelope is turned OFF for the measurement, exactly like the
        // flat-pane axis test above: the teal frames run along the two shafts
        // at the origin corner and would bury them.
        view.PushChromeFlags(ChromeFlags.Default with { Envelope = false });
        for (var i = 0; i < 8; i++)
            await Pump();
        var axesFrame = PlotterPixels.Snapshot(CaptureFrame(window));
        var axisRed = CountSurface(axesFrame, IsAxisRed);
        Assert.True(axisRed > 50, $"the loft X arrows must paint RED per tile, found {axisRed} red pixels");
        var axisGreen = CountSurface(axesFrame, IsAxisGreen);
        Assert.True(axisGreen > 30, $"the loft Y arrows must paint GREEN per tile, found {axisGreen} green pixels");
        Assert.Equal(0, CountSurface(axesFrame, IsAxisBlue));
        // The tile frames are gated by Chrome.Envelope like every other
        // envelope, and their absence must be total — zero, not a reduction.
        // This assertion doubles as the box connectors' gate: they share the
        // envelope gate and vanish with the frames.
        Assert.True(CountSurface(axesFrame, IsEnvelopeTeal) == 0,
            "the tile envelope rectangles (and their box connectors) must be gated by Chrome.Envelope");

        // The cross is grey (#888888, 2 px, the shared OriginPen) so it lands
        // in the path's own band: it is isolated by the toggle, not by colour.
        // Turning Origin off removes the cross (and the arrows) from the band's
        // budget — the arrows are red/green and never in it — so the remaining
        // grey must drop. This is what makes the gate non-vacuous.
        view.PushChromeFlags(ChromeFlags.Default with { Origin = false, Envelope = false });
        for (var i = 0; i < 8; i++)
            await Pump();
        var withoutCross = CountSurface(PlotterPixels.Snapshot(CaptureFrame(window)), IsPathGrey);
        Assert.True(withoutCross < greyOn,
            $"the origin cross must be gated by Chrome.Origin, found {greyOn} grey pixels with it and {withoutCross} without");
    }

    /// <summary>A foam program whose plane 0 (X,Y) Y-extent dwarfs plane 1's
    /// (X,Z): Y climbs 0..60 while Z only plunges 0..2, and X sweeps, so both
    /// planes are drawable. That is the normal foam XY-vs-XZ case, and the one
    /// that matters here — the two planes' own extents differ by far more than
    /// the 8 mm <c>FoamTileGap</c>, which is what makes them TRANSPOSED rather
    /// than equal, so neither plane's own extent describes the job's. The
    /// envelope is 0..55 by 0..60: plane 0's extent, widened to the origin.
    /// <para>It used to matter for a second reason — plane 0's Y extent
    /// overhung the canvas midline into the bottom-left quadrant, which is what
    /// the flat view's arrow probe used to measure. Both tiles now measure the
    /// one envelope, so the gap straddles the midline instead and nothing
    /// overhangs; see <see cref="The_flat_foam_view_draws_no_tile_lattice"/>.</summary>
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

    /// <summary>The foam loft's grid is a FLOOR joining the two flat tiles'
    /// bottom edges — NOT the big projected lattice that used to sit under the
    /// composition. It has TWO families (the round-4 "Both families" choice);
    /// see <c>IsoCanvas.DrawWireFloor</c>: one wire per step along the shared
    /// primary axis, each from (xi, 0) on plane 0's tile to (xi, 0) on plane
    /// 1's tile (the coupling wire's own geometry drawn as a family), PLUS one
    /// horizontal line per level, strictly parallel to the world X axis. Because
    /// the composition is true-scale and its X is the shared world X, those
    /// lines are horizontal on screen — the red sketch on
    /// <c>plot_iso_grid.png</c>.
    /// <para>The horizontal family is bounded by the FLOOR, not by the
    /// composition's box: the two tiles fill opposite quadrants of that box, so
    /// the floor is the skewed band between them (closed by the two tiles' lower
    /// edges and the two lower-corner connectors), and each line runs from the
    /// BL connector to the BR connector at its own level. The longest-span check
    /// below is what pins that.
    /// Three things are pinned here: the wires paint and are gated by
    /// <c>Chrome.Grid</c>, and every wire lands WITHIN the composition box —
    /// the old projected floor spilled far left of and above the composition,
    /// and a floor that strays outside the two tiles' own rectangle is that
    /// positioning regression coming back.</summary>
    [AvaloniaFact]
    public async Task The_foam_loft_connects_the_tiles_with_a_grid_wire_floor()
    {
        var source = new StubProgramSource();
        var vm = MakeVm(source, MachineTypes.Foam);
        source.Load("G21\r\nG1 X100 Y20 Z20 A100 F600");
        Assert.True(vm.PlaneCount > 1);
        vm.SetPlaneGeometries([
            PlotterPlaneMapper.Identity(2),
            PlotterPlaneMapper.Identity(2),
        ]);

        var view = new PlotterView { DataContext = vm };
        var window = new Window { Width = 920, Height = 560, Content = view };
        window.Show();
        for (var i = 0; i < 8; i++)
            await Pump();
        window.GetVisualDescendants().OfType<CheckBox>()
            .Single(c => c.Name == "IsoToggle")
            .IsChecked = true;
        for (var i = 0; i < 8; i++)
            await Pump();
        // The overlay's glyph edges land in the teal neighbourhood and its
        // opaque buttons hide the canvas beneath them; the sibling loft tests
        // hide it for the same reason.
        view.FindControl<Grid>("OverlayHost")!.IsVisible = false;
        for (var i = 0; i < 8; i++)
            await Pump();

        var on = PlotterPixels.Snapshot(CaptureFrame(window));
        // Measured with the 15% coupling mesh in place: 4699 floor pixels here,
        // 0 with Chrome.Grid off. Re-measured after the job envelope was
        // unioned — the two tiles are now the same 100x100 mm rectangle instead
        // of 100x20 and 20x100, so the composition, the fit zoom and every count
        // on this frame moved, and the old 6952/381 pair no longer describes it.
        // The grid-off control is now an exact ZERO, not a residue, so the
        // margin is one-sided: the 300 floor is ~15.7x under the signal and the
        // grid toggle is 4699 against 0, which is what makes `floor` a floor
        // measurement rather than a chrome count. Threshold unchanged, and it
        // is still the original 300 — this test is about the floor's GEOMETRY,
        // not its weight. The floor's own pen is unchanged by the mesh's alpha;
        // see PlotterPens.WireFloorBrush for why it stays opaque.
        var floor = CountSurface(on, IsWireFloorBlend);
        Assert.True(floor > 300,
            $"the loft wire floor must paint, found {floor} wire pixels");

        // The count above only says the floor is there. "BOTH families" is the
        // part a count cannot see, and it is the part round 4 was raised over:
        // the ladder alone is a diagonal mesh that reads as hatching, not as a
        // grid. The second family is the giveaway — each of its lines is
        // horizontal on screen and spans the whole width of the floor at its
        // level, so it puts a wide stretch of wire pixels in a single row. The
        // ladder is a steep diagonal and contributes only a couple of pixels
        // per row it crosses. A row carrying a wide count is therefore the
        // horizontal family, and counting those rows pins the family rather
        // than the floor's existence.
        var wideRows = 0;
        var widest = 0;
        var widestSpan = 0;
        for (var y = 25; y < on.Height - 1; y++)
        {
            var row = 0;
            var first = -1;
            var last = -1;
            for (var x = 1; x < on.Width - 1; x++)
            {
                var p = PlotterPixels.At(on, x, y);
                if (!IsWireFloorBlend(p.R, p.G, p.B))
                    continue;
                row++;
                if (first < 0)
                    first = x;
                last = x;
            }
            if (row > widest)
                widest = row;
            // SPAN, not longest run: the toolpath is drawn over the floor, so a
            // horizontal wire is punched full of grey gaps where the path
            // crosses it, and a longest-run measure reads those interruptions
            // as short lines. First-to-last is the line's true length.
            if (row > 200 && last - first > widestSpan)
                widestSpan = last - first;
            if (row > 200)
                wideRows++;
        }
        Assert.True(wideRows >= 4,
            $"the horizontal world-X-parallel family must span the composition, found {wideRows} full-width rows (widest {widest}px)");

        // The wires connect the tiles' bottom edges, so every wire pixel must
        // sit inside the union of the two tile rectangles — and those
        // rectangles are the flat envelope frames, measured as pixels rather
        // than re-derived. The OLD projected floor spilled outside that box
        // (left of the top-left tile and above the composition), so a stray
        // wire pixel beyond the tiles is the positioning bug coming back.
        int left = on.Width, top = on.Height, right = -1, bottom = -1;
        for (var y = 25; y < on.Height - 1; y++)
            for (var x = 1; x < on.Width - 1; x++)
            {
                var p = PlotterPixels.At(on, x, y);
                if (!IsEnvelopeTeal(p.R, p.G, p.B))
                    continue;
                if (x < left) left = x;
                if (x > right) right = x;
                if (y < top) top = y;
                if (y > bottom) bottom = y;
            }
        Assert.True(right > left && bottom > top,
            "the tile envelope frames must paint for the confinement check to mean anything");

        // The horizontal family's lines must be no longer than the floor they
        // sit on. The two tiles fill OPPOSITE quadrants of the composition, so
        // the floor is the skewed band between them — bounded by the two tiles'
        // LOWER edges and by the two connectors joining their lower corners —
        // and the widest the band can ever be is one tile. A line drawn to the
        // full composition width is therefore always too long: the composition
        // is tile + gap + tile, while the band never exceeds one tile, so a
        // correct line is a fraction of the full width and a full-width line is
        // measurably longer. The teal bbox above measures that full width in
        // pixels, so this comparison needs no re-derivation of the layout to be
        // meaningful.
        //
        // The fraction is well clear of both answers, and the union made it
        // FIXED rather than a range: both tiles are now the one job envelope
        // rectangle, so the band is always about half the composition and can no
        // longer climb toward the full width in a lopsided program. Measured
        // here: longest span 242 px against a 530 px composition, 0.46. 0.9
        // therefore separates "clipped to the band" from "spans the composition"
        // with room on both sides, and the threshold is unchanged.
        var compositionW = right - left;
        Assert.True(widestSpan < compositionW * 0.9,
            $"a horizontal wire must stop at the floor's edge, not overhang it: longest span {widestSpan}px vs composition width {compositionW}px");

        var stray = 0;
        for (var y = 25; y < on.Height - 1; y++)
            for (var x = 1; x < on.Width - 1; x++)
            {
                var p = PlotterPixels.At(on, x, y);
                if (!IsWireFloorBlend(p.R, p.G, p.B))
                    continue;
                if (x < left - 2 || x > right + 2 || y < top - 2 || y > bottom + 2)
                    stray++;
            }
        Assert.True(stray <= 20,
            $"every wire must join the tiles' bottom edges inside their box, found {stray} wire pixels outside it");

        view.PushChromeFlags(ChromeFlags.Default with { Grid = false });
        for (var i = 0; i < 8; i++)
            await Pump();
        var off = CountSurface(PlotterPixels.Snapshot(CaptureFrame(window)), IsWireFloorBlend);
        Assert.True(off < floor / 2,
            $"the loft wire floor must be gated by Chrome.Grid: {off} wire pixels with it off vs {floor} with it on");
    }

    /// <summary>The iso box: four teal connectors joining MATCHING corners of
    /// the two flat tile envelopes — TL→TL, TR→TR, BL→BL, BR→BR — the round-4
    /// task-4 request ("connect each corner of one rectangle with corresponding
    /// corner of another with line (forming iso box)"), ruled part of the job
    /// envelope and gated by the same <c>Chrome.Envelope</c> as the frames it
    /// attaches to.
    /// <para>Frames and connectors are one teal, one gate, so a test that
    /// measured "the frame's bounding box" out of the combined render would
    /// measure the box too and derive every corner midpoint off its own line.
    /// They are separated by SHAPE instead, which is the real difference: the
    /// frames are axis-aligned rectangles whose edges are long straight runs,
    /// while the connectors are 45° diagonals that belong to no long horizontal
    /// or vertical run. A teal pixel with a long axis-aligned run through it
    /// is frame; the rest is box. The midpoints come from the frame pixels
    /// alone, so a connector drawn between MISMATCHED corners cannot pass
    /// through them — each probe point is the midpoint of a segment's own
    /// endpoints, and the diagonal pairing puts its midpoints elsewhere.</para>
    [AvaloniaFact]
    public async Task The_foam_loft_connects_the_envelope_corners_into_an_iso_box()
    {
        var source = new StubProgramSource();
        var vm = MakeVm(source, MachineTypes.Foam);
        source.Load("G21\r\nG1 X100 Y20 Z20 A100 F600");
        Assert.True(vm.PlaneCount > 1);
        vm.SetPlaneGeometries([
            PlotterPlaneMapper.Identity(2),
            PlotterPlaneMapper.Identity(2),
        ]);

        var view = new PlotterView { DataContext = vm };
        var window = new Window { Width = 920, Height = 560, Content = view };
        window.Show();
        for (var i = 0; i < 8; i++)
            await Pump();
        window.GetVisualDescendants().OfType<CheckBox>()
            .Single(c => c.Name == "IsoToggle")
            .IsChecked = true;
        for (var i = 0; i < 8; i++)
            await Pump();
        view.FindControl<Grid>("OverlayHost")!.IsVisible = false;
        for (var i = 0; i < 8; i++)
            await Pump();

        var frame = PlotterPixels.Snapshot(CaptureFrame(window));
        var (Teal, OnFrame) = TealFrameProbes(frame);
        var blobs = TealFrameRectangles(frame);
        Assert.True(blobs.Count == 2,
            $"the loft must draw exactly two rectangular tile frames, found {blobs.Count} blobs of that size");
        var (f0, f1) = blobs[0].L <= blobs[1].L ? (blobs[0], blobs[1]) : (blobs[1], blobs[0]);
        Assert.True(f0.R < f1.L || f1.R < f0.L || f0.B < f1.T || f1.B < f0.T,
            "the two tile frames must be disjoint rectangles for their corners to pair up");

        var (mxTl, myTl) = Mid((f0.L, f0.T), (f1.L, f1.T));
        var (mxTr, myTr) = Mid((f0.R, f0.T), (f1.R, f1.T));
        var (mxBl, myBl) = Mid((f0.L, f0.B), (f1.L, f1.B));
        var (mxBr, myBr) = Mid((f0.R, f0.B), (f1.R, f1.B));
        foreach (var (mx, my, what) in new[]
        {
            (mxTl, myTl, "top-left"),
            (mxTr, myTr, "top-right"),
            (mxBl, myBl, "bottom-left"),
            (mxBr, myBr, "bottom-right"),
        })
        {
            // Counted as BOX teal only, so a midpoint that happens to graze a
            // frame edge passes on the connector rather than on the frame.
            var near = 0;
            for (var y = my - 6; y <= my + 6; y++)
                for (var x = mx - 6; x <= mx + 6; x++)
                    if (!OnFrame(x, y) && Teal(x, y))
                        near++;
            Assert.True(near > 1,
                $"the iso box must connect the {what} corners, found {near} connector px at their midpoint ({mx},{my})");
        }

        static (int, int) Mid((int X, int Y) a, (int X, int Y) b) =>
            ((a.X + b.X) / 2, (a.Y + b.Y) / 2);
    }

    /// <summary>Both foam tiles are the SAME rectangle, carrying the job
    /// envelope's extents — the one behavioural assertion in this area, since
    /// every other foam test describes shape and shape survives the change.
    /// <para>The program is deliberately lopsided: plane 0 (X,Y) travels
    /// 0..100 by 0..20 and plane 1 (Z,A) travels 0..20 by 0..100, so the two
    /// planes' own tiles are 100x20 and 20x100 — TRANSPOSED, and therefore not
    /// congruent. The job envelope is 0..100 by 0..100, so both tiles must be
    /// square and the same size. On the old per-plane extents this fails twice
    /// over: measured, the frames came out 399 px and 80 px wide — a 5x
    /// difference, and neither square.
    /// <para>This is the test that would have caught the per-plane reading. The
    /// blob grouping and its 6 px dilation are inherited from the iso-box test
    /// and are re-used unchanged, so the two tests measure tiles the same
    /// way.
    /// <para>Congruence alone is not the whole of it, and the last assertion is
    /// why: a tile sized from HALF the envelope is still congruent with its
    /// partner and still square here, so the two live-run probes (both
    /// one-sided) could not see it. The measured magnitude, 246 px for the
    /// 100 mm envelope at this window's fit, is what the spec asks for — the
    /// size matches the union SCALED BY THE FIT, not merely the other tile.</para></summary>
    [AvaloniaFact]
    public async Task The_foam_tiles_are_both_the_job_envelope_rectangle()
    {
        var source = new StubProgramSource();
        var vm = MakeVm(source, MachineTypes.Foam);
        source.Load("G21\r\nG1 X100 Y20 Z20 A100 F600");
        for (var i = 0; i < 6; i++)
            await Pump();
        Assert.True(vm.PlaneCount > 1);
        // The union that both tiles are sized from, and the reason both frames
        // must come out square: plane 0 gives X 0..100 / Y 0..20, plane 1 gives
        // X 0..20 / Y 0..100, and the envelope takes the larger of each.
        Assert.True(vm.TryJobEnvelope(out var job));
        Assert.Equal(100.0, job.MaxX - job.MinX, 6);
        Assert.Equal(100.0, job.MaxY - job.MinY, 6);

        var view = new PlotterView { DataContext = vm };
        var window = new Window { Width = 920, Height = 560, Content = view };
        window.Show();
        for (var i = 0; i < 8; i++)
            await Pump();
        window.GetVisualDescendants().OfType<CheckBox>()
            .Single(c => c.Name == "IsoToggle")
            .IsChecked = true;
        for (var i = 0; i < 8; i++)
            await Pump();
        view.FindControl<Grid>("OverlayHost")!.IsVisible = false;
        for (var i = 0; i < 8; i++)
            await Pump();

        var blobs = TealFrameRectangles(PlotterPixels.Snapshot(CaptureFrame(window)));
        Assert.Equal(2, blobs.Count);
        var (f0, f1) = blobs[0].L <= blobs[1].L ? (blobs[0], blobs[1]) : (blobs[1], blobs[0]);
        // 2 px of slack for the antialiased corner nick the dilation bridges;
        // measured, the two frames come out 246x246 each, so the slack is not
        // carrying this test.
        const int Slack = 2;
        var w0 = f0.R - f0.L;
        var h0 = f0.B - f0.T;
        var w1 = f1.R - f1.L;
        var h1 = f1.B - f1.T;
        Assert.True(Math.Abs(w0 - w1) <= Slack,
            $"both tiles must be the same WIDTH, found {w0} and {w1} px");
        Assert.True(Math.Abs(h0 - h1) <= Slack,
            $"both tiles must be the same HEIGHT, found {h0} and {h1} px");
        // The envelope here is square, so each tile is too. This is the check
        // that ties the frames to the union rather than merely to each other.
        Assert.True(Math.Abs(w0 - h0) <= Slack,
            $"the envelope is 100x100, so each tile must be square, found {w0}x{h0} px");
        Assert.True(Math.Abs(w1 - h1) <= Slack,
            $"the envelope is 100x100, so each tile must be square, found {w1}x{h1} px");
        // The MAGNITUDE, which the three checks above cannot see: congruence and
        // squareness are both satisfied by a tile sized from HALF the envelope,
        // and the spec asks for the size to match the union scaled by the fit,
        // not merely to be consistent. Measured 246 px here for the 100 mm
        // envelope, i.e. this window's fit at 2.46 px/mm: the composition is
        // 2*100+8 = 208 mm square, the iso canvas is 920x536 inside the
        // 920x560 window declared above, and the fit takes the SMALLER of
        // (920-24)/208 = 4.31 and (536-24)/208 = 2.46 — the 24 being
        // FoamFitMargin, which is why the number is 2.46 and not the
        // margin-less 536/208 = 2.58. A tile sized from half the envelope would
        // land near 123 px and fail this. The range is wide on purpose
        // (200..290, about +-18% of 246) because it only has to exclude a halved
        // tile, not re-pin the fit: the exact value is this comment's job, and
        // the square and congruence assertions above are what hold it still.
        Assert.InRange(w0, 200, 290);
    }

    /// <summary>The foam grid is a floor of the work envelope and lives in the
    /// iso loft; the FLAT foam view's two tiles no longer draw per-tile
    /// lattices — those read as two side panes, and the floor is the only grid
    /// foam gets now. So <c>Chrome.Grid</c> must have nothing to take away in
    /// the flat foam view: the grid-OFF render differs from the grid-ON only by
    /// the rebake's antialiasing jitter, never by a lattice.
    /// <para>Kept here too is the reason the old test existed: the chrome
    /// (axes, envelope, cross) takes a canvas-wide clip, not a per-quadrant
    /// one. The clip is unchanged — <c>DrawSlotChrome</c> still hands the
    /// painters the whole canvas — but with both foam tiles measuring the one
    /// job envelope each tile's chrome now lands wholly inside its own
    /// quadrant, so there is no overhang left for a pixel probe to find. The
    /// tail of the method records the measurement; the two assertions above
    /// are what this test still guards.</para></summary>
    [AvaloniaFact]
    public async Task The_flat_foam_view_draws_no_tile_lattice()
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

        var on = CountSurface(PlotterPixels.Snapshot(CaptureFrame(window)), IsChromeLight);
        view.PushChromeFlags(ChromeFlags.Default with { Grid = false });
        for (var i = 0; i < 8; i++)
            await Pump();
        var off = CountSurface(PlotterPixels.Snapshot(CaptureFrame(window)), IsChromeLight);

        Assert.True(on > 300,
            $"the flat foam chrome must still paint, found {on} light pixels");
        Assert.True(off >= on - 300,
            $"no tile lattice may come from Chrome.Grid in the flat foam view: {on} light pixels with it on, {off} with it off");

        // The arrow-overhang probe that used to stand here is GONE, and the
        // measurement is why. It counted world-axis arrow pixels in the
        // bottom-left quadrant — the one no slot viewport covers — as proof
        // that the chrome is clipped to the whole canvas and not to a
        // quadrant. That overhang was incidental to the OLD per-plane tiles:
        // plane 0's tile was 55x60 mm while plane 1's was a degenerate 2 mm
        // strip, so plane 0's origin corner sat far down the composition and
        // its arrows ran along a line well BELOW the canvas midline.
        //
        // Both tiles now measure the one job envelope, so the composition is
        // 2*span + 8 mm and the 8 mm gap straddles its middle — which the fit
        // always lands on the canvas midline, because the composition is
        // centred. Tile 0's origin is therefore a half-gap ABOVE that midline
        // with its arrows pointing up and right into tile 0's own quadrant,
        // and tile 1's out of the bottom-right. Measured on
        // <see cref="TallPlaneProgram"/>: 3 red and 0 green arrow pixels
        // strictly below the midline, against 15 red / 18 green in the
        // top-left and 18 red / 14 green in the bottom-right — the three
        // below are antialiasing along the midline itself.
        //
        // No arrow can cross a midline any more, so this probe cannot tell a
        // canvas-wide clip from a per-quadrant one, and re-pinning its
        // threshold would only record a number with nothing behind it.
    }

    /// <summary>The grid is a FLOOR, so it paints BENEATH the toolpath: a grid
    /// line that crosses the path must disappear under it, never run over it.
    /// Counted as the actual overlap — pixels that are grey path with the grid
    /// off and light lattice with the grid on. The old z-order (lattice after
    /// the path, "drawn last so it never sits under a raster edge") counted
    /// every one of those crossings; the new one counts only antialiased
    /// fringes where a path edge blends over a grid line.</summary>
    [AvaloniaFact]
    public async Task The_flat_grid_draws_beneath_the_toolpath()
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

        var on = PlotterPixels.Snapshot(CaptureFrame(window));
        view.PushChromeFlags(ChromeFlags.Default with { Grid = false });
        for (var i = 0; i < 8; i++)
            await Pump();
        var off = PlotterPixels.Snapshot(CaptureFrame(window));

        // Control: the grid must be there at all, so "no overlap" is not "no
        // grid". And the path must be there, or removing the grid removed
        // nothing to uncover.
        Assert.True(CountSurface(on, IsChromeLight) > 400,
            $"the control grid must paint, found {CountSurface(on, IsChromeLight)} light pixels");
        Assert.True(CountSurface(off, IsPathGrey) > 100,
            $"the control path must paint, found {CountSurface(off, IsPathGrey)} grey pixels");

        var replaced = 0;
        for (var y = 25; y < off.Height - 1; y++)
            for (var x = 1; x < off.Width - 1; x++)
            {
                var q = PlotterPixels.At(off, x, y);
                if (!IsPathGrey(q.R, q.G, q.B))
                    continue;
                var p = PlotterPixels.At(on, x, y);
                if (IsChromeLight(p.R, p.G, p.B))
                    replaced++;
            }
        // The antialiased fringe of the path over a grid line can still blend
        // into the light band, so the floor is a tiny absolute, not zero.
        Assert.True(replaced <= 25,
            $"the lattice must not paint over the toolpath: {replaced} pixels were path with the grid off and light with it on");
    }

    /// <summary>Same z-order ask, iso side: the projected lattice must not run
    /// over the projected toolpath either.</summary>
    [AvaloniaFact]
    public async Task The_iso_grid_draws_beneath_the_toolpath()
    {
        var source = new StubProgramSource();
        var vm = MakeVm(source, MachineTypes.Mill);
        // A single projected segment crosses too few lattice lines to measure:
        // the same probe on a one-line program read ≤ 15 overlapping pixels
        // both with and without the fix. The spiral crosses the projected
        // lattice dozens of times, which is what makes the count discriminating.
        source.Load(SpiralProgram(600));
        Assert.True(vm.HasGeometry);

        var view = new PlotterView { DataContext = vm };
        var window = new Window { Width = 920, Height = 560, Content = view };
        window.Show();
        for (var i = 0; i < 8; i++)
            await Pump();
        window.GetVisualDescendants().OfType<CheckBox>()
            .Single(c => c.Name == "IsoToggle")
            .IsChecked = true;
        for (var i = 0; i < 8; i++)
            await Pump();

        var on = PlotterPixels.Snapshot(CaptureFrame(window));
        view.PushChromeFlags(ChromeFlags.Default with { Grid = false });
        for (var i = 0; i < 8; i++)
            await Pump();
        var off = PlotterPixels.Snapshot(CaptureFrame(window));

        Assert.True(CountSurface(on, IsChromeLight) > 400,
            $"the control lattice must paint, found {CountSurface(on, IsChromeLight)} light pixels");
        Assert.True(CountSurface(off, IsPathGrey) > 100,
            $"the control path must paint, found {CountSurface(off, IsPathGrey)} grey pixels");

        var replaced = 0;
        for (var y = 25; y < off.Height - 1; y++)
            for (var x = 1; x < off.Width - 1; x++)
            {
                var q = PlotterPixels.At(off, x, y);
                if (!IsPathGrey(q.R, q.G, q.B))
                    continue;
                var p = PlotterPixels.At(on, x, y);
                if (IsChromeLight(p.R, p.G, p.B))
                    replaced++;
            }
        Assert.True(replaced <= 25,
            $"the iso lattice must not paint over the toolpath: {replaced} pixels were path with the grid off and light with it on");
    }

    /// <summary>The origin toggle gates the world AXES, not just the origin
    /// cross. Nex maps <c>ShowOrigin</c> to axis visibility
    /// (<c>NexPlotScene.SetView → SetAxesVisible</c>); this view drew the
    /// arrows unconditionally, so toggling Origin hid only the small + cross
    /// under the shaft bases and the X/Y/Z arrows — the cross the user can
    /// actually see — never moved.</summary>
    [AvaloniaFact]
    public async Task The_origin_toggle_hides_the_world_axis_arrows_in_the_flat_pane()
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
        // Envelope off, as in the arrows test above: the job rectangle's edges
        // run along the two shafts, and counting with them present buries the
        // picture this toggle must move.
        view.PushChromeFlags(ChromeFlags.Default with { Envelope = false });
        for (var i = 0; i < 8; i++)
            await Pump();

        var arrowsOn = PlotterPixels.Snapshot(CaptureFrame(window));
        Assert.True(CountSurface(arrowsOn, IsAxisRed) > 100, "control: the X arrow must paint at the defaults");
        Assert.True(CountSurface(arrowsOn, IsAxisGreen) > 40, "control: the Y arrow must paint at the defaults");

        view.PushChromeFlags(ChromeFlags.Default with { Envelope = false, Origin = false });
        for (var i = 0; i < 8; i++)
            await Pump();
        var arrowsOff = PlotterPixels.Snapshot(CaptureFrame(window));
        Assert.Equal(0, CountSurface(arrowsOff, IsAxisRed));
        Assert.Equal(0, CountSurface(arrowsOff, IsAxisGreen));
    }

    /// <summary>Same gate, iso: the red X, green Y and blue Z arrows all come
    /// down with the origin toggle.</summary>
    [AvaloniaFact]
    public async Task The_origin_toggle_hides_the_world_axis_arrows_in_iso()
    {
        var source = new StubProgramSource();
        var vm = MakeVm(source, MachineTypes.Mill);
        source.Load("G21\r\nG1 X100 Y50 Z20 F600");
        Assert.True(vm.HasGeometry);

        var view = new PlotterView { DataContext = vm };
        var window = new Window { Width = 920, Height = 560, Content = view };
        window.Show();
        for (var i = 0; i < 8; i++)
            await Pump();
        window.GetVisualDescendants().OfType<CheckBox>()
            .Single(c => c.Name == "IsoToggle")
            .IsChecked = true;
        for (var i = 0; i < 8; i++)
            await Pump();
        // Same explicit fit baseline as the job-box test: the mode switch keeps
        // the flat transform now, and this test measures the chrome, not the
        // inheritance.
        view.Reframe();
        for (var i = 0; i < 8; i++)
            await Pump();

        var arrowsOn = PlotterPixels.Snapshot(CaptureFrame(window));;
        Assert.True(CountSurface(arrowsOn, IsAxisRed) > 5, "control: the X axis must paint at the defaults");
        Assert.True(CountSurface(arrowsOn, IsAxisGreen) > 5, "control: the Y axis must paint at the defaults");
        Assert.True(CountSurface(arrowsOn, IsAxisBlue) > 10, "control: the Z axis must paint at the defaults");

        view.PushChromeFlags(ChromeFlags.Default with { Origin = false });
        for (var i = 0; i < 8; i++)
            await Pump();
        var arrowsOff = PlotterPixels.Snapshot(CaptureFrame(window));
        Assert.Equal(0, CountSurface(arrowsOff, IsAxisRed));
        Assert.Equal(0, CountSurface(arrowsOff, IsAxisGreen));
        Assert.Equal(0, CountSurface(arrowsOff, IsAxisBlue));
    }

    /// <summary>The four visibility toggles go through the shared settings
    /// model, and the module re-applies the WHOLE settings section on any one
    /// of them — plane mappings included. The geometry has not changed, so the
    /// re-apply must be a no-op: a GeometryVersion bump is the "the toolpath
    /// moved, drop the view and re-fit" signal, and the visible effect of
    /// misreading it is the user's zoom snapping back to the fitted view the
    /// moment they toggle grid, origin, envelope or run-state.</summary>
    [AvaloniaFact]
    public async Task Reapplying_identical_plane_geometries_does_not_reset_the_view()
    {
        var source = new StubProgramSource();
        var vm = MakeVm(source, MachineTypes.Mill);
        source.Load(SpiralProgram(600));
        Assert.True(vm.HasGeometry);
        var geometries = vm.PlaneGeometries;

        var view = new PlotterView { DataContext = vm };
        var window = new Window { Width = 920, Height = 560, Content = view };
        window.Show();
        for (var i = 0; i < 8; i++)
            await Pump();

        // Off the fit, so a reset is visible: a refit returns to the fitted
        // zoom, so comparing two fitted zooms would prove nothing.
        view.ZoomBy(2.0);
        for (var i = 0; i < 8; i++)
            await Pump();
        var zoomBefore = view.CompositionForTest[0].Zoom;
        var versionBefore = vm.GeometryVersion;

        vm.SetPlaneGeometries(geometries);
        for (var i = 0; i < 8; i++)
            await Pump();

        Assert.Equal(versionBefore, vm.GeometryVersion);
        Assert.Equal(zoomBefore, view.CompositionForTest[0].Zoom);
    }

    /// <summary>A settings save re-raises the tab host's module list, and the
    /// host reparents the CACHED plot view into a fresh container — the view
    /// detaches and re-attaches, and the attach handler rebuilds the canvas
    /// set. A rebuilt canvas starts un-fitted (NaN zoom), so without the
    /// rebuild preserving the live transform every settings change (any chrome
    /// toggle) snaps the user's zoom back to the fitted view. This is the
    /// SECOND path to that reset: the first was the SetPlaneGeometries
    /// GeometryVersion bump, now fixed — hence the guard test above that no
    /// geometry version changes, and this test proving the view still keeps its
    /// zoom through the attach/rebuild.
    /// <para>The host here is a plain ContentControl reparenting the view,
    /// which fires the same <c>AttachedToVisualTree</c> the real tab host does
    /// when it re-binds its cached views.</para></summary>
    [AvaloniaFact]
    public async Task Reattaching_the_view_preserves_the_zoom()
    {
        var source = new StubProgramSource();
        var vm = MakeVm(source, MachineTypes.Mill);
        source.Load(SpiralProgram(600));
        Assert.True(vm.HasGeometry);

        var view = new PlotterView { DataContext = vm };
        var host = new ContentControl { Content = view };
        var window = new Window { Width = 920, Height = 560, Content = host };
        window.Show();
        for (var i = 0; i < 8; i++)
            await Pump();

        view.ZoomBy(2.0);
        for (var i = 0; i < 8; i++)
            await Pump();
        var zoomBefore = view.CompositionForTest[0].Zoom;

        // Detach, then re-attach — the tab host's reparenting of a cached view.
        host.Content = null;
        await Pump();
        host.Content = view;
        for (var i = 0; i < 8; i++)
            await Pump();

        Assert.Equal(zoomBefore, view.CompositionForTest[0].Zoom);
    }

    /// <summary>The rebuild hands the fresh canvas the live one's ZOOM, but
    /// <c>FittedZoom</c> — the "100%" baseline the readout divides by — lived
    /// only in the canvas that was just discarded. A re-attached canvas then
    /// carried the user's zoom with a 0 baseline, and the next time anything
    /// re-ran the readout (the very next wheel notch) it hid at the
    /// "never fitted" guard even though the zoom itself survived. The same
    /// rebuild <see cref="Reattaching_the_view_preserves_the_zoom"/> drives;
    /// this one asserts the readout rides through it and past it.</summary>
    [AvaloniaFact]
    public async Task Reattaching_the_view_preserves_the_zoom_readout()
    {
        var source = new StubProgramSource();
        var vm = MakeVm(source, MachineTypes.Mill);
        source.Load(SpiralProgram(600));
        Assert.True(vm.HasGeometry);

        var view = new PlotterView { DataContext = vm };
        var readout = view.FindControl<TextBlock>("ZoomPctText")!;
        var host = new ContentControl { Content = view };
        var window = new Window { Width = 920, Height = 560, Content = host };
        window.Show();
        for (var i = 0; i < 8; i++)
            await Pump();

        // A zoom visibly off the fit, so the readout has something to say.
        view.ZoomTo(view.FittedZoom * 1.25);
        for (var i = 0; i < 8; i++)
            await Pump();
        Assert.True(readout.IsVisible, "off the fit the readout must show before the re-attach");
        Assert.Contains("125", readout.Text);

        // Detach, then re-attach — the tab host's reparenting of a cached view.
        host.Content = null;
        await Pump();
        host.Content = view;
        for (var i = 0; i < 8; i++)
            await Pump();

        // The rebuilt canvas must read its 100% baseline back from the old one.
        // The regression was a 0 baseline on a fitted canvas, so this is the
        // precise property that changed.
        Assert.True(view.FittedZoom > 0,
            "the rebuilt canvas must inherit the previous fit as its readout baseline");

        // Drive the readout through a USER path after the rebuild — the moment
        // the regression bit. The wheel re-runs the readout, and a 0 baseline
        // hides it there even though the zoom survived the rebuild intact.
        window.MouseWheel(new Point(460, 280), new Vector(0, 1), RawInputModifiers.None);
        for (var i = 0; i < 8; i++)
            await Pump();
        Assert.True(readout.IsVisible,
            "a wheel notch after the rebuild must still report a percentage, not hide at a zero baseline");
        Assert.NotEqual(string.Empty, readout.Text);
    }

    /// <summary>Toggling the iso view rebuilds the canvas set, and until the
    /// rebuild adopted the live transform the fresh isometric canvas started
    /// un-fitted and the view snapped back to the fit — the flat zoom was
    /// thrown away. The iso and flat canvases share the same zoom semantics
    /// (px per world mm), so the user's chosen level must survive the switch in
    /// both directions.</summary>
    [AvaloniaFact]
    public async Task Toggling_iso_preserves_the_zoom()
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

        view.ZoomBy(2.0);
        for (var i = 0; i < 8; i++)
            await Pump();
        var zoomBefore = view.CompositionForTest[0].Zoom;

        var toggle = window.GetVisualDescendants().OfType<CheckBox>()
            .Single(c => c.Name == "IsoToggle");
        toggle.IsChecked = true;
        for (var i = 0; i < 8; i++)
            await Pump();
        Assert.True(vm.Isometric);

        Assert.Equal(zoomBefore, view.CompositionForTest[0].Zoom);

        toggle.IsChecked = false;
        for (var i = 0; i < 8; i++)
            await Pump();
        Assert.False(vm.Isometric);
        Assert.Equal(zoomBefore, view.CompositionForTest[0].Zoom);
    }

    /// <summary>Counts on the PLOT SURFACE only — the canvas interior, below the
    /// 24 px header band and inside the 1 px frame. Every whole-frame count
    /// here is a trap: the header is #e8e8e8 (21.6k pixels INSIDE the light
    /// grid band), the frame is #aaa (inside the grey path band), and the
    /// header's subpixel-antialiased TEXT fringes leak into several bands.
    /// Measured, not assumed, and the teal figure is the important one: a
    /// whole-frame <c>IsEnvelopeTeal</c> count reads 431 with the envelope OFF,
    /// against 22 for the old amber probe. Teal's r channel is 0, so dark
    /// antialiased glyph fringes satisfy <c>g &gt; r + 40 &amp;&amp; b &gt; g</c> far
    /// more readily than a mid-r hue can; the band-ratio form of the probe
    /// trades a little header sensitivity for the ability to follow the stroke
    /// across every background (see <c>IsEnvelopeTeal</c>). The <c>y &gt; 24</c>
    /// filter is what keeps those 431 out of every surface count, which is why
    /// the filter is not optional.</summary>
    private static int CountSurface(PlotterPixels.Frame frame, Func<byte, byte, byte, bool> match) =>
        PlotterPixels.Count(frame, match,
            xFilter: x => x > 0 && x < frame.Width - 1,
            yFilter: y => y > 24 && y < frame.Height - 1);

    /// <summary>The per-pixel predicates <see cref="TealFrameRectangles"/>
    /// is built on, handed back so a caller measuring a CONNECTOR can tell one
    /// from a frame edge that happens to graze the same spot.</summary>
    private static (Func<int, int, bool> Teal, Func<int, int, bool> OnFrame)
        TealFrameProbes(PlotterPixels.Frame frame)
    {
        const int Run = 20;
        bool Teal(int x, int y)
        {
            var p = PlotterPixels.At(frame, x, y);
            return IsEnvelopeTeal(p.R, p.G, p.B);
        }
        bool OnFrame(int x, int y)
        {
            if (!Teal(x, y))
                return false;
            var left = x - Run / 2;
            var right = x + Run / 2;
            if (left >= 1 && right < frame.Width - 1 && Teal(left, y) && Teal(right, y))
                return true;
            var top = y - Run / 2;
            var bottom = y + Run / 2;
            return top >= 25 && bottom < frame.Height - 1 && Teal(x, top) && Teal(x, bottom);
        }
        return (Teal, OnFrame);
    }

    /// <summary>The axis-aligned teal rectangles in <paramref name="frame"/>,
    /// as (Left, Top, Right, Bottom) — the tile frames, smallest ignored.
    /// <para>The frames and the box connectors are one teal and one gate, so a
    /// measurement over the combined render would measure the box too. They are
    /// separated by SHAPE instead, which is the real difference: the frames are
    /// axis-aligned rectangles whose edges are long straight runs, while the
    /// connectors are diagonals that belong to no long horizontal or vertical
    /// run.
    /// <para>The two frames are DISJOINT rectangles, and neither is separable by
    /// a midline: tile 0's frame is wider than half the canvas, so it straddles
    /// the midpoint and any left/right split cuts its own right edge into the
    /// other tile's box. They are therefore grouped by connectivity, which is
    /// what "the two tiles' rectangles" means.
    /// <para>The grouping mask is DILATED, because a connector leaves each
    /// frame corner and its antialiased foot eats the corner pixels themselves —
    /// the corner is the one place a rectangle's own two edges must touch, and
    /// they no longer do, so the raw mask fragments one frame into its four
    /// edges. One dilation of 6 px bridges that ~10 px nick and cannot bridge
    /// the gap between the two tiles (6 + 6 = 12 px). The gap is measured, not
    /// assumed: with both tiles sized from the one job envelope it is 19 px
    /// here, on a 920x560 window, so the two frames stay two blobs — the
    /// number to re-read if the window or the program ever shrinks it. Each
    /// group's extents are read from the UNDILATED pixels, which the nick does
    /// not shrink: every edge still reaches the corner it owns, so the union is
    /// the true rectangle. Blobs under 100 pixels are dropped as speckle.</summary>
    private static List<(int L, int T, int R, int B)> TealFrameRectangles(
        PlotterPixels.Frame frame)
    {
        const int Dilate = 6;
        const int MinPixels = 100;
        var (Teal, OnFrame) = TealFrameProbes(frame);

        var isFrame = new bool[frame.Width * frame.Height];
        for (var y = 25; y < frame.Height - 1; y++)
            for (var x = 1; x < frame.Width - 1; x++)
                if (Teal(x, y) && OnFrame(x, y))
                    isFrame[y * frame.Width + x] = true;

        var grown = new bool[frame.Width * frame.Height];
        for (var y = 25; y < frame.Height - 1; y++)
            for (var x = 1; x < frame.Width - 1; x++)
            {
                if (!isFrame[y * frame.Width + x])
                    continue;
                for (var dy = -Dilate; dy <= Dilate; dy++)
                    for (var dx = -Dilate; dx <= Dilate; dx++)
                        grown[(y + dy) * frame.Width + (x + dx)] = true;
            }

        var seen = new bool[frame.Width * frame.Height];
        var blobs = new List<(int L, int T, int R, int B)>();
        for (var y0 = 25; y0 < frame.Height - 1; y0++)
            for (var x0 = 1; x0 < frame.Width - 1; x0++)
            {
                if (!grown[y0 * frame.Width + x0] || seen[y0 * frame.Width + x0])
                    continue;
                var stack = new Stack<(int X, int Y)>();
                stack.Push((x0, y0));
                seen[y0 * frame.Width + x0] = true;
                var l = int.MaxValue;
                var t = int.MaxValue;
                var r = -1;
                var b = -1;
                var n = 0;
                while (stack.Count > 0)
                {
                    var (x, y) = stack.Pop();
                    // Counted and bounded on the ORIGINAL pixels only, so the
                    // dilation widens connectivity without widening the
                    // rectangle the caller reads.
                    if (isFrame[y * frame.Width + x])
                    {
                        n++;
                        if (x < l) l = x;
                        if (x > r) r = x;
                        if (y < t) t = y;
                        if (y > b) b = y;
                    }
                    for (var dy = -1; dy <= 1; dy++)
                        for (var dx = -1; dx <= 1; dx++)
                        {
                            var nx = x + dx;
                            var ny = y + dy;
                            if (nx < 1 || nx >= frame.Width - 1 || ny < 25 || ny >= frame.Height - 1)
                                continue;
                            if (!grown[ny * frame.Width + nx] || seen[ny * frame.Width + nx])
                                continue;
                            seen[ny * frame.Width + nx] = true;
                            stack.Push((nx, ny));
                        }
                }
                if (n > MinPixels)
                    blobs.Add((l, t, r, b));
            }
        return blobs;
    }

    /// <summary>The drawn path's on-screen bounding box: the one measurement
    /// that can say where a zoom anchored, because the path's WORLD bounds do
    /// not move when the view does. Excludes the header band and the 1px frame
    /// the same way <see cref="CountSurface"/> does.</summary>
    private static (int Left, int Top, int Right, int Bottom, double Cx, double Cy, double Width)
        PathBounds(WriteableBitmap bmp, int minY = 25) =>
        PathBounds(PlotterPixels.Snapshot(bmp), minY);

    /// <summary>Same measurement for a PNG that came off disk, so an export can
    /// be compared against the view it was taken from.
    /// <para><paramref name="minY"/> is 25 for a screen capture, which skips the
    /// 24px header band, but 1 for an export: the saved image has no header, and
    /// reusing 25 there would silently ignore the top 12 logical pixels of the
    /// scene — which is exactly the band a vertical offset moves content into or
    /// out of, so the very error being measured would hide from the measurement.</para></summary>
    /// <para>The scan starts ONE pixel in from each edge, and honours
    /// <paramref name="minY"/>. The inset is not cosmetic: the view's own
    /// <c>&lt;Border BorderBrush="#aaa" BorderThickness="1"&gt;</c> is
    /// #AAAAAA = 170, squarely inside this probe's 95..175 band, so the
    /// outermost column of a WINDOW capture is chrome and read as toolpath.
    /// It spans the full height, so it alone pinned the measured left edge at
    /// x=0 and made the on-screen path look 5px wider and shifted — which is
    /// how a correct export ended up "proven" misplaced. An export has no such
    /// border, so the same probe that caught the bug on one side is the reason
    /// the two sides were not comparable.</para></summary>
    private static (int Left, int Top, int Right, int Bottom, double Cx, double Cy, double Width)
        PathBounds(PlotterPixels.Frame frame, int minY = 25)
    {
        var left = frame.Width;
        var top = frame.Height;
        var right = -1;
        var bottom = -1;
        for (var y = minY; y < frame.Height - 1; y++)
            for (var x = 1; x < frame.Width - 1; x++)
            {
                var p = PlotterPixels.At(frame, x, y);
                if (!IsPathGrey(p.R, p.G, p.B))
                    continue;
                if (x < left) left = x;
                if (x > right) right = x;
                if (y < top) top = y;
                if (y > bottom) bottom = y;
            }
        if (right < 0)
            return (0, 0, -1, -1, 0, 0, 0);
        return (left, top, right, bottom, (left + right) / 2d, (top + bottom) / 2d, right - left);
    }

    /// <summary>Bounding box of everything that is not the background, ignoring
    /// WHICH colour it is.
    /// <para>This is the probe that can compare a screen capture with an export
    /// at all. <see cref="IsPathGrey"/> only admits neutral greys in 95..175, so
    /// it sees the toolpath on one side and, on the other, a different subset
    /// again — the screen's toolpath arrives through the bake raster, whose
    /// anti-aliased fringe and job rectangle sit inside the band, while the
    /// export draws clean vectors. The two boxes then disagree by tens of
    /// millimetres on content that is in fact identical, and a test built on it
    /// measures the renderer, not the bug.</para>
    /// <para>What a crop actually does is leave BACKGROUND where content should
    /// be, so asking where the ink is answers the question directly: a scene
    /// scaled past the surface leaves a blank band along the top and left, and
    /// that band's size is the error, whatever colour the content turned out
    /// to be.</para></summary>
    private static (int Left, int Top, int Right, int Bottom, int Width, int Height)
        InkBounds(PlotterPixels.Frame frame, int minY = 0, int inset = 0) =>
        InkBounds(frame, new Rect(inset, minY,
            frame.Width - inset * 2, frame.Height - minY - inset));

    /// <summary>The same measurement restricted to <paramref name="region"/>, so
    /// a window capture can be measured over the plot alone and compared with a
    /// file that has no chrome around it.
    /// <para>Coordinates stay in IMAGE space, not region-relative: the edges are
    /// compared against the export's own, which has no region to subtract.</para></summary>
    private static (int Left, int Top, int Right, int Bottom, int Width, int Height)
        InkBounds(PlotterPixels.Frame frame, Rect region)
    {
        var left = frame.Width;
        var top = frame.Height;
        var right = -1;
        var bottom = -1;
        var x0 = (int)Math.Max(0, Math.Floor(region.Left));
        var y0 = (int)Math.Max(0, Math.Floor(region.Top));
        var x1 = (int)Math.Min(frame.Width, Math.Ceiling(region.Right));
        var y1 = (int)Math.Min(frame.Height, Math.Ceiling(region.Bottom));
        for (var y = y0; y < y1; y++)
            for (var x = x0; x < x1; x++)
            {
                var p = PlotterPixels.At(frame, x, y);
                if (p.R > 250 && p.G > 250 && p.B > 250)
                    continue;
                if (x < left) left = x;
                if (x > right) right = x;
                if (y < top) top = y;
                if (y > bottom) bottom = y;
            }
        Assert.True(right >= 0, "the image has no ink at all to measure");
        return (left, top, right, bottom, right - left, bottom - top);
    }

    /// <summary>A dialog service that returns a fixed save path, so the export
    /// has somewhere to write and the test can prove it wrote there. Records
    /// the arguments the view passed, because "the dialog was even asked" and
    /// "the suggested name is a .png" are part of the contract.</summary>
    private sealed class StubFileDialog : CNC.Core.Abstractions.IFileDialogService
    {
        public string? PathToReturn { get; init; }
        public string? AskedTitle { get; private set; }
        public string? AskedSuggestedName { get; private set; }
        public string[]? AskedFilters { get; private set; }
        public List<string> Errors { get; } = [];

        public Task<string?> OpenFileAsync(string title, params string[] filters) =>
            Task.FromResult<string?>(null);

        public Task<string?> SaveFileAsync(string title, string suggestedFileName, params string[] filters)
        {
            AskedTitle = title;
            AskedSuggestedName = suggestedFileName;
            AskedFilters = filters;
            return Task.FromResult(PathToReturn);
        }

        public Task ShowErrorAsync(string title, string message)
        {
            Errors.Add($"{title}: {message}");
            return Task.CompletedTask;
        }
    }

    /// <summary>Builds a mill view showing <paramref name="segments"/> of toolpath,
    /// hosted and pumped like every other test here so the live canvas has
    /// actually fitted itself before the export reads its transform. The save
    /// path is returned rather than taken as an <c>out</c> parameter: an async
    /// method cannot have one (CS1988).</summary>
    private static async Task<(PlotterView View, Window Window, StubFileDialog Dialogs, string File)>
        ShowMillForExport(int segments)
    {
        var file = Path.Combine(Path.GetTempPath(), $"plot-{Guid.NewGuid():N}.png");
        var source = new StubProgramSource();
        var vm = MakeVm(source, MachineTypes.Mill);
        source.Load(SpiralProgram(segments));
        Assert.True(vm.HasGeometry);

        var dialogs = new StubFileDialog { PathToReturn = file };
        var view = new PlotterView(dialogs) { DataContext = vm };
        var window = new Window { Width = 920, Height = 560, Content = view };
        window.Show();
        for (var i = 0; i < 8; i++)
            await Pump();
        return (view, window, dialogs, file);
    }


    [AvaloniaFact]
    public async Task Export_writes_a_2x_image_with_chrome_and_leaves_the_view_alone()
    {
        var (view, window, dialogs, file) = await ShowMillForExport(600);
        try
        {
            var before = view.CompositionForTest;
            await view.ExportPngAsync();

            // Errors first: a failed export also writes no file, and "the export
            // must reach the chosen path" on its own hides the reason.
            Assert.True(dialogs.Errors.Count == 0, $"the export reported: {string.Join("; ", dialogs.Errors)}");
            Assert.True(File.Exists(file), "the export must reach the chosen path");
            // The size contract is the RATIO, not a pixel count: the view is the
            // window's content, so its exact size is Avalonia's business, but
            // "twice the view" is ours. Asserting 1840x1120 would pin the window
            // layout too and fail on an unrelated framework change.
            var frame = PlotterPixels.SnapshotPng(file);
            Assert.Equal((int)Math.Ceiling(view.Bounds.Width * 2), frame.Width);
            Assert.Equal((int)Math.Ceiling(view.Bounds.Height * 2), frame.Height);
            Assert.True(frame.Width > 1000, $"the export must be a real 2x image, got {frame.Width}px wide");
            Assert.True(PlotterPixels.Count(frame, IsChromeLight) > 300,
                "the exported image must include the chrome");

            var after = view.CompositionForTest;
            Assert.Equal(before[0].Zoom, after[0].Zoom, 9);
            Assert.Equal(before[0].Pan, after[0].Pan);
            Assert.Empty(dialogs.Errors);
        }
        finally
        {
            window.Close();
            File.Delete(file);
        }
    }

    /// <summary>The export must reproduce what the user is looking at, at twice
    /// the resolution. This is the test the plan does NOT have, and it is the one
    /// that matters: the chrome is drawn from SCREEN coordinates, so a version
    /// that forgot to map the toolpath's WORLD coordinates through the slot
    /// transform still exports a full frame of grid and axes and looks fine in
    /// every other assertion - it just puts the toolpath in a corner at 1 mm per
    /// pixel. Only a path-pixel count on the EXPORTED image can see that.
    /// <para>Also pins the zoom/pan relationship: the export copies the live
    /// transform, so a path drawn in the same place on screen must land in the
    /// same place in the file, scaled by exactly 2.</para></summary>
    [AvaloniaFact]
    public async Task Export_places_the_toolpath_where_the_view_shows_it_at_twice_the_size()
    {
        var (view, window, _, file) = await ShowMillForExport(600);
        try
        {
            var onScreen = PlotterPixels.Snapshot(CaptureFrame(window));
            var screenPath = PathBounds(CaptureFrame(window));
            Assert.True(screenPath.Width > 100,
                $"the on-screen path must be visible to compare against, got {screenPath.Width}px wide");
            var onScreenGrey = PlotterPixels.Count(onScreen, IsPathGrey);
            Assert.True(onScreenGrey > 100, $"expected the toolpath on screen, found {onScreenGrey}px");

            await view.ExportPngAsync();

            var frame = PlotterPixels.SnapshotPng(file);
            var exportedGrey = PlotterPixels.Count(frame, IsPathGrey);
            Assert.True(exportedGrey > 0,
                "the exported image must contain the toolpath, not just the chrome");
            // The scale factor, measured on WIDTH rather than on pixel count. A
            // 2x image of a hairline does not cover 4x the area: at 1x the line
            // is mostly anti-aliased pixels that fail the grey threshold, and at
            // 2x it has a solid core that passes, so the count roughly doubles
            // rather than quadruples (measured: 4542 -> 8911). Pinning the
            // ratio to 4x would be pinning Skia's anti-aliasing, not our
            // contract. Width, asserted below, is the honest measure of the
            // scale.
            //
            // The COUNT is still worth one assertion, but only as "the export
            // drew a comparable amount of path", not as a ratio to 2x. The old
            // bound here was `> onScreenGrey * 1.5` and it only passed because
            // the export was wrongly 4x: the surplus ink that cropped the scene
            // was exactly what carried the count over the line. Correcting the
            // DPI dropped the true ratio to 1.36x and the bound failed on a
            // correct export — a test inverted by the fix it was meant to
            // police. Comparing the two ink boxes is the same claim without
            // the anti-aliasing coupling; the width ratio below is what pins
            // the scale.
            Assert.True(exportedGrey > onScreenGrey * 0.75,
                $"a 2x export must draw a comparable path to the {onScreenGrey}px on screen, got {exportedGrey}px");
            // Same relative placement: the path's centre in the file must sit
            // where the view put it. A missing world->screen map sends the
            // centre off toward the origin corner instead.
            var exportedPath = PathBounds(frame);
            var widthRatio = exportedPath.Width / (double)screenPath.Width;
            Assert.True(widthRatio is > 1.6 and < 2.4,
                $"the exported path must be 2x the on-screen width: {exportedPath.Width}px vs {screenPath.Width}px is {widthRatio:F2}x");
            var expectedCx = screenPath.Cx * 2;
            Assert.True(Math.Abs(exportedPath.Cx - expectedCx) < screenPath.Width,
                $"the exported path must keep the view's horizontal placement: centre {exportedPath.Cx:F1}, expected near {expectedCx:F1}");
        }
        finally
        {
            window.Close();
            File.Delete(file);
        }
    }

    /// <summary>The export is a 2x snapshot of the VIEW: the whole scene, at
    /// twice the size, not a cropped corner of it. This is the test the user's
    /// report turns on.
    /// <para>It asserts the contract in INK, not in toolpath pixels, because
    /// that is what "shows only part of the scene" means and it is the one
    /// thing both renderers agree on. The view's toolpath reaches the screen
    /// through the bake raster while the export draws clean vectors, so a
    /// colour-specific probe measures two different sets of pixels and reports
    /// a disagreement of tens of millimetres on identical content. Ink does not
    /// care which renderer produced it.</para>
    /// <para>The scale assertion is 2x against the ON-SCREEN ink box, and the
    /// margins are asserted separately and tightly. A scene drawn at 4x — the
    /// failure this pins, where the export's own DPI scale doubled the explicit
    /// export scale — overflows the surface and leaves a blank band down the
    /// left and across the top while still hitting the far edges, so a
    /// width-ratio check alone passes it; the blank bands are the tell.</para></summary>
    [AvaloniaFact]
    public async Task Export_frames_the_scene_where_the_view_frames_it()
    {
        var (view, window, _, file) = await ShowMillForExport(600);
        try
        {
            // Chrome OFF, on BOTH sides. Grid, origin cross and job rectangle
            // are all ink that the view draws and are deliberately absent from a
            // comparison about the toolpath: they extend the on-screen box
            // vertically past anything the export draws, so a like-for-like
            // scale check reads 0.77x on a scene that is in fact perfect. The
            // export inherits these flags (ExportPngAsync hands the canvas the
            // same retained ChromeFlags), so one call turns them off on both.
            view.PushChromeFlags(new ChromeFlags(false, false, false, false));
            for (var i = 0; i < 4; i++)
                await Pump();

            // Hide the overlay: the zoom panel and the save button are view
            // CHROME, drawn over the canvas, and the export deliberately has
            // none of them ("the PNG has no buttons in it" — PlotterView.axaml).
            // An ink probe cannot tell a 16x16 button from a toolpath — both are
            // just not-white — so leaving them on measures a scene the file was
            // never meant to contain, and the 2x check fails on a good export.
            // The other capture tests in this file hide it for the same reason.
            view.FindControl<Grid>("OverlayHost")!.IsVisible = false;
            for (var i = 0; i < 2; i++)
                await Pump();

            // Measured over the live canvas rectangle, which is exactly the
            // region the export's PaintScene covers. That also drops the 24px
            // #e8e8e8 header and the 1px #aaa border, neither of which the
            // file carries, without either being special-cased.
            var live = Assert.Single(view.CanvasesForTest);
            var origin = live.TranslatePoint(new Point(0, 0), view) ?? new Point(0, 0);
            var plot = new Rect(
                origin.X, origin.Y, live.Bounds.Width, live.Bounds.Height);
            var onScreen = InkBounds(PlotterPixels.Snapshot(CaptureFrame(window)), plot);
            Assert.True(onScreen.Width > 100,
                $"the on-screen scene must be visible to compare against, got {onScreen.Width}px wide");

            await view.ExportPngAsync();
            var frame = PlotterPixels.SnapshotPng(file);
            var exported = InkBounds(frame);

            // 2x on BOTH axes, to within a pixel of rounding. The tolerance is
            // TIGHT on purpose: the bug being pinned is a whole extra factor of
            // two, which no jitter in a fit can hide.
            var wr = exported.Width / (double)onScreen.Width;
            Assert.True(wr is > 1.9 and < 2.1,
                $"the export must show the scene at 2x the view's width: {exported.Width}px vs {onScreen.Width}px is {wr:F3}x");
            var hr = exported.Height / (double)onScreen.Height;
            Assert.True(hr is > 1.9 and < 2.1,
                $"the export must show the scene at 2x the view's height: {exported.Height}px vs {onScreen.Height}px is {hr:F3}x");

            // Same box, same frame. Both are now measured in PLOT coordinates —
            // the screen over the live canvas, the file over its whole surface —
            // so the header offset that separated them is gone and each edge
            // lands exactly where 2x the view put it, with no constant to
            // subtract and no header height being asserted by accident.
            const int tol = 8; // a hairline's anti-aliased edge, either end of a stroke
            Assert.True(Math.Abs(exported.Left - onScreen.Left * 2) <= tol,
                $"the export's left edge must match the view's: {exported.Left}px, expected {onScreen.Left * 2}px (on-screen {onScreen.Left}px at 2x)");
            Assert.True(Math.Abs(exported.Top - onScreen.Top * 2) <= tol,
                $"the export's top edge must match the view's: {exported.Top}px, expected {onScreen.Top * 2}px");
            Assert.True(Math.Abs(exported.Right - onScreen.Right * 2) <= tol,
                $"the export's right edge must match the view's: {exported.Right}px, expected {onScreen.Right * 2}px");
            Assert.True(Math.Abs(exported.Bottom - onScreen.Bottom * 2) <= tol,
                $"the export's bottom edge must match the view's: {exported.Bottom}px, expected {onScreen.Bottom * 2}px");
        }
        finally
        {
            window.Close();
            File.Delete(file);
        }
    }

    [AvaloniaFact]
    public async Task Export_draws_vectors_and_never_bakes()
    {
        var (view, window, _, file) = await ShowMillForExport(600);
        try
        {
            var screen = Assert.Single(view.CanvasesForTest);
            var used = screen.BakePathUsesForTest;
            Assert.True(used > 0, "the on-screen path does bake");

            await view.ExportPngAsync();

            Assert.Equal(used, screen.BakePathUsesForTest);
            var exported = view.LastExportCanvasForTest;
            Assert.NotNull(exported);
            Assert.Equal(0, exported.BakePathUsesForTest);
            // Every slot's bake count, not an empty list: the export canvas sizes
            // its per-slot arrays like any other, so there is one entry per
            // slot. "Empty" would have passed on a canvas too broken to draw
            // anything at all, which is the exact failure this test exists to
            // rule out — assert the bakes did not HAPPEN instead.
            var counts = view.LastExportBakeCountsForTest;
            Assert.NotEmpty(counts);
            Assert.All(counts, c => Assert.Equal(0, c));
        }
        finally
        {
            window.Close();
            File.Delete(file);
        }
    }

    [AvaloniaFact]
    public async Task Export_without_a_chosen_path_writes_nothing()
    {
        var source = new StubProgramSource();
        var vm = MakeVm(source, MachineTypes.Mill);
        source.Load(SpiralProgram(600));
        Assert.True(vm.HasGeometry);

        var dialogs = new StubFileDialog { PathToReturn = null };
        var view = new PlotterView(dialogs) { DataContext = vm };
        var window = new Window { Width = 920, Height = 560, Content = view };
        window.Show();
        for (var i = 0; i < 8; i++)
            await Pump();

        try
        {
            await view.ExportPngAsync();
            Assert.Null(view.LastExportCanvasForTest);
            Assert.Empty(dialogs.Errors);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>A save failure must surface as a dialog, not as an unhandled
    /// exception and not as silence: the path the user just chose can be
    /// unwritable, and a silent no-op reads as "the app is broken".
    /// <para>The bad path is a file used as a DIRECTORY — the shape a stale
    /// "save to" location really takes. An earlier version pointed into a
    /// directory that did not exist, which reads better but proved nothing:
    /// Avalonia's string Save overload swallows the I/O error, so the test was
    /// asserting the ENCODER's behaviour rather than the view's error path. It
    /// passed in isolation and failed in full runs — the worst kind of flake.
    /// A file where a directory must be cannot be written on any platform, and
    /// the view opens the stream itself, so the failure is ours to report.</para></summary>
    [AvaloniaFact]
    public async Task Export_reports_a_failure_instead_of_throwing()
    {
        var blocker = Path.Combine(Path.GetTempPath(), $"plot-{Guid.NewGuid():N}.png");
        File.WriteAllText(blocker, "not a directory");
        var badPath = Path.Combine(blocker, "nested", "plot.png");
        var source = new StubProgramSource();
        var vm = MakeVm(source, MachineTypes.Mill);
        source.Load(SpiralProgram(600));
        Assert.True(vm.HasGeometry);

        var dialogs = new StubFileDialog { PathToReturn = badPath };
        var view = new PlotterView(dialogs) { DataContext = vm };
        var window = new Window { Width = 920, Height = 560, Content = view };
        window.Show();
        for (var i = 0; i < 8; i++)
            await Pump();

        try
        {
            await view.ExportPngAsync();
            Assert.NotNull(view.LastExportCanvasForTest); // it painted before the save failed
            Assert.NotEmpty(dialogs.Errors);
            Assert.False(File.Exists(badPath));
        }
        finally
        {
            window.Close();
            File.Delete(blocker);
        }
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

    /// <summary>Advances both clocks the rebake queue depends on, so a slot that
    /// was queued behind the limiter actually gets served.
    /// <para>The limiter reads <c>Environment.TickCount64</c> — real time — so it
    /// needs a real delay. The retry that brings back a pass is a
    /// <see cref="DispatcherTimer"/>, which runs on the headless STUB clock, and
    /// only <c>ForceRenderTimerTick</c> moves that. Doing just one of the two is
    /// how this test failed with "carriage 2 found 0 green pixels" while the
    /// production code was right: the bake was permitted and still never ran.
    /// 40 ticks x 16 ms = 640 ms, past <c>BakeMinIntervalMs</c>.</para></summary>
    private static async Task PumpPastBakeWindow()
    {
        await Task.Delay(560);
        for (var i = 0; i < 40; i++)
            await Pump();
    }

    /// <summary>Waits the rebake queue out, instead of guessing how many limiter
    /// windows it needs.
    /// <para>One round is NOT always enough, and the reason is worth recording:
    /// a slot queued behind the limiter is served by a
    /// <see cref="DispatcherTimer"/>, which (a) needs REAL time to elapse and
    /// (b) can only be dispatched by pumping the UI thread — a real delay alone
    /// does not pump, so the tick cannot be observed until the pumps that
    /// follow it. Each round therefore has to be its own delay+pump pair, and
    /// the retry that serves slot 1 lands close enough to slot 0's bake that it
    /// can be refused for "window not open yet" and re-armed. Measured on this
    /// suite: up to three rounds. The loop is bounded so a genuinely stuck
    /// queue fails the assertion that follows instead of hanging.</para></summary>
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

    /// <summary>The foam loft's COUPLING MESH, at 15% alpha over white: the
    /// stroke is #6a6a8a, so a fully covered pixel blends to (233, 233, 238) —
    /// a 5-level blue lean on an almost-white field.
    ///
    /// <para>The lean is the whole probe, and it is this tight ON PURPOSE. The
    /// mesh has no toggle of its own (<c>DrawCoupling</c> reads no
    /// <c>Chrome</c> flag), so it is measured with <c>Chrome.Grid</c> OFF,
    /// which removes the wire floor. That matters because the floor's
    /// ANTIALIASED EDGE pixels are the same near-white blue-grey as the mesh —
    /// a #c8c8da stroke fading into white passes straight through (233,233,238)
    /// — so with the grid on, this probe also scores the floor (measured: 1730
    /// extra pixels on the wing program, 5408 with the grid on against 3678
    /// with it off). With the grid off, the only thing left in this band is the
    /// mesh.
    ///
    /// <para>Specificity is measured, not assumed: the same probe, in the same
    /// grid-off state and on a MILL iso frame — same chrome, same window, no
    /// coupling mesh because <c>Loft</c> is false — scores 120, so the wing's
    /// 3678 sits ~30x above that control. (Both counts are re-measured: they
    /// were 14096 and 108 before the two foam tiles were sized from the one job
    /// envelope, which shrank the fit zoom and with it the mesh; see
    /// <see cref="Foam_iso_renders_the_four_axis_loft"/>.) Rejected by the
    /// clauses above: the white background (r = 255, above the ceiling), the
    /// grey toolpath and #888888 origin cross (neutral, so <c>b &gt; r</c>
    /// fails), the teal envelope (g far above r), the axis colours, the
    /// run-state greens.</para></summary>
    private static bool IsLoftMeshBlend(byte r, byte g, byte b) =>
        b > r && b - r <= 5 && r > 228 && r < 253 && Math.Abs(r - g) <= 2;

    /// <summary>The loft wire FLOOR (<c>PlotterPens.WireFloorBrush</c> = an
    /// OPAQUE #c8c8da), so its core pixels are that colour outright rather than
    /// a blend, and the antialiased edges fan out around it.
    ///
    /// <para>This used to be one probe — <c>IsWireBlend</c> — shared by the
    /// coupling mesh and the floor, which was a mistake the 35% mesh hid: the
    /// mesh blended to (203, 203, 214) against the floor's (200, 200, 218),
    /// three levels apart, so the probe could not tell them apart and
    /// <c>Foam_iso_renders_the_four_axis_loft</c>'s "the coupling mesh must
    /// draw" assertion was reading ~59% floor pixels (7067 total, 4155 of them
    /// the floor). It could not have failed if the mesh stopped drawing. The
    /// two elements now have their own probes — <see cref="IsLoftMeshBlend"/>
    /// for the mesh, this one for the floor.</para></summary>
    private static bool IsWireFloorBlend(byte r, byte g, byte b) =>
        b > r + 8 && b > 190 && g >= 180 && Math.Abs(r - g) <= 16;

    /// <summary>The run-state "done" green, <c>PlotterPens.DoneBrush</c> =
    /// #1b9e3e. The <c>b &gt; r + 15</c> is what makes this a probe for THAT colour
    /// rather than for green in general. The world Y axis arrow is green too
    /// (#33cc33), it is drawn unconditionally in the flat panes and the iso view
    /// alike, and "any green" therefore read ~20 arrow pixels as a finished run
    /// and failed <c>Run_state_off_paints_no_state_colors</c>. The two greens
    /// separate cleanly: done leans blue (b - r = 35), the arrow does not
    /// (b - r = 0), and the arrow is the one that is always there.</summary>
    private static bool IsDoneGreen(byte r, byte g, byte b) =>
        g > 100 && r < 90 && b < 110 && b > r + 15;

    /// <summary><c>IsDoneGreen</c> hardened against a CHROME stroke drawn over
    /// the Y axis arrow, which is the one place the two greens meet.
    ///
    /// <para>The recoloured envelope (teal <c>#00b2e6</c> at 50% alpha) crosses
    /// the arrow, and the blend FORGES the done-green signature: teal's r is 0,
    /// so covering the arrow's #33cc33 at a few tenths of alpha yields about
    /// (33, 195, 106) — a <c>b - r</c> of 73, well past the 35 that identifies
    /// the real brush, and it passes every clause of <c>IsDoneGreen</c>. Amber
    /// could not do this, because its r = 192 held the blend's r above 90; the
    /// old colour masked the problem rather than the probe avoiding it.
    ///
    /// <para>So the discriminator is tightened to what actually distinguishes
    /// the real brush: its b - r is about 35, not 73. Requiring
    /// <c>b &lt; r + 60</c> keeps the real done green (any antialiased edge is
    /// nearer the background, which shrinks the gap rather than growing it) and
    /// rejects the teal-over-arrow blend, which is the only producer of a
    /// large positive gap here. Chrome over chrome cannot fabricate this: the
    /// axis colours are opaque, so a covered arrow pixel is the arrow's own
    /// (50, 204, 50) with b - r = 0.</para></summary>
    private static bool IsRunStateGreen(byte r, byte g, byte b) =>
        IsDoneGreen(r, g, b) && b < r + 60;
}
