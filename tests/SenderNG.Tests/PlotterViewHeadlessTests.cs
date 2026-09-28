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
        // Single(): the plot overlay's own chrome toggles are checkboxes too,
        // so a bare Single() here is a coin flip on how many the view hosts.
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
        var offEmpty = CountSurface(PlotterPixels.Snapshot(CaptureFrame(window)), IsDoneGreen);
        view.PushChromeFlags(ChromeFlags.Default);
        for (var i = 0; i < 8; i++)
            await Pump();
        await DrainRebakeQueue(view);
        var carriage1 = CountSurface(PlotterPixels.Snapshot(CaptureFrame(window)), IsDoneGreen);
        Assert.True(offEmpty == 0, $"run-state off must paint no green at all, found {offEmpty}");
        Assert.True(carriage1 > 400,
            $"carriage 1's own rows must paint green with the toggle on, found {carriage1} green pixels");

        // Carriage 2 as well: the same two flips again. Carriage 1's raster
        // already paints green and is NOT touched by the rebake (its rows and
        // its Chrome are unchanged), so the whole difference between the two
        // readings is carriage 2's raster.
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
        var both = CountSurface(PlotterPixels.Snapshot(CaptureFrame(window)), IsDoneGreen);
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
        PlotterPixels.Count(frame, IsDoneGreen,
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

    /// <summary>The wheel has to feed the readout too, not just the buttons: the
    /// wheel is how a user zooms most of the time, and a percentage that only
    /// moves for the button cluster reads as a broken indicator. At exactly
    /// 1:1 the readout is hidden — the number is noise when the drawing is at
    /// its natural size.</summary>
    [AvaloniaFact]
    public async Task The_zoom_readout_hides_at_one_to_one()
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

        view.ZoomTo(1.0);
        for (var i = 0; i < 8; i++)
            await Pump();
        Assert.False(readout.IsVisible, "at 1:1 the readout is noise");

        view.ZoomTo(1.25);
        for (var i = 0; i < 8; i++)
            await Pump();
        Assert.True(readout.IsVisible);
        Assert.Contains("125", readout.Text);

        // The wheel reaches the same indicator: one notch is 1.15x, so the
        // number on screen follows the pointer. Driven from the FITTED view
        // (where the readout is already showing) rather than from 1:1: at
        // exactly 1:1 the headless platform routes the wheel past the canvas
        // and the notch never lands, which is a test-harness artefact, not
        // something the indicator can see. What is asserted here is the rule
        // that matters - the readout is a multiple of 1:1 and it tracks the
        // canvas - and the 1:1 case is already pinned by the two lines above.
        view.Reframe();
        for (var i = 0; i < 8; i++)
            await Pump();
        Assert.True(readout.IsVisible, "the fitted zoom is not 1:1, so the readout is showing");
        var before = readout.Text;
        window.MouseWheel(new Point(460, 280), new Vector(0, 1), RawInputModifiers.None);
        for (var i = 0; i < 8; i++)
            await Pump();
        Assert.NotEqual(before, readout.Text);
        Assert.Contains(
            ((int)Math.Round(view.CurrentZoom * 100)).ToString(CultureInfo.InvariantCulture),
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
        // control by design and the 24px row has no room for four more.
        var chromeInHeader = view.GetVisualDescendants().OfType<CheckBox>()
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

            // The model reached the overlay's checkboxes...
            var gridToggle = window.GetVisualDescendants()
                .OfType<CheckBox>().Single(c => c.Name == "GridToggle");
            Assert.True(gridToggle.IsChecked == true);

            // The lattice the shared model's default asked for is on the canvas.
            var on = CountSurface(PlotterPixels.Snapshot(CaptureFrame(window)), IsChromeLight);
            Assert.True(on > 0, $"the default grid must paint, found {on} light pixels");

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

        // The overlay is hidden for the measurement: these two assertions are
        // about the CANVAS painters, and the chrome's own glyphs antialias into
        // the same amber the envelope uses, so a visible toolbar would show up
        // as eight "envelope" pixels after the envelope was switched off. The
        // chrome has its own tests; this one stays about the drawing.
        view.FindControl<Grid>("OverlayHost")!.IsVisible = false;
        for (var i = 0; i < 8; i++)
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
        // Hidden for the same reason as in the envelope test above: the chrome
        // is a different subject, and its glyph edges land in the amber/blue
        // bands this test asserts are empty.
        view.FindControl<Grid>("OverlayHost")!.IsVisible = false;
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

    /// <summary>The drawn path's on-screen bounding box: the one measurement
    /// that can say where a zoom anchored, because the path's WORLD bounds do
    /// not move when the view does. Excludes the header band and the 1px frame
    /// the same way <see cref="CountSurface"/> does.</summary>
    private static (int Left, int Top, int Right, int Bottom, double Cx, double Cy, double Width)
        PathBounds(WriteableBitmap bmp)
    {
        var frame = PlotterPixels.Snapshot(bmp);
        var left = frame.Width;
        var top = frame.Height;
        var right = -1;
        var bottom = -1;
        for (var y = 25; y < frame.Height - 1; y++)
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

    /// <summary>The semi-transparent loft wire over white blends to a light
    /// blue-grey (~#cbcbd6) — distinctly bluer than the grey path.</summary>
    private static bool IsWireBlend(byte r, byte g, byte b) =>
        b > r + 8 && b > 190 && g >= 180 && Math.Abs(r - g) <= 16;

    private static bool IsDoneGreen(byte r, byte g, byte b) =>
        g > 100 && r < 90 && b < 110;
}