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

using System.Runtime.InteropServices;
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

        var frame = CaptureFrame(window);
        var grey = CountPixels(frame, IsPathGrey);
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

        var frame = CaptureFrame(window);
        var grey = CountPixels(frame, IsPathGrey);
        Assert.True(grey > 100,
            $"expected the grey iso toolpath in the frame, found {grey} path pixels (raster bake missing?)");

        // M4 layout fix: the iso canvas must stretch over the WHOLE panel, not
        // linger in the flat layout's left column — so the path must reach the
        // right part of the frame.
        var right = CountPixels(frame, IsPathGrey, x => x > frame.PixelSize.Width * 0.6);
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
        Assert.Equal(0, CountPixels(CaptureFrame(window), IsDoneGreen));

        // Ack the first half of the rows — exactly the live-run tick.
        source.AckRows(1, 301);
        for (var i = 0; i < 8; i++)
            await Pump();

        var green = CountPixels(CaptureFrame(window), IsDoneGreen);
        Assert.True(green > 200,
            $"acked segments must turn green from RowsChanged alone, found {green} green pixels");

        // And a later ack moves the green frontier forward without a rebuild.
        source.AckRows(301, 601);
        for (var i = 0; i < 8; i++)
            await Pump();
        var greenAll = CountPixels(CaptureFrame(window), IsDoneGreen);
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

        var grey = CountPixels(CaptureFrame(window), IsPathGrey);
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

        var grey = CountPixels(CaptureFrame(window), IsPathGrey);
        Assert.True(grey > 100,
            $"expected the foam 2D+loft in the iso frame, found {grey} path pixels");
        // The loft layout: plane 1 top-left, plane 2 bottom-right — grey path
        // must appear in BOTH halves of the iso frame.
        var top = CountPixels(CaptureFrame(window), IsPathGrey, y => y < 280);
        var bottom = CountPixels(CaptureFrame(window), IsPathGrey, y => y >= 280);
        Assert.True(top > 30, $"plane 1 must draw in the iso top-left, found {top} path pixels");
        Assert.True(bottom > 30, $"plane 2 must draw in the iso bottom-right, found {bottom} path pixels");
        // And the semi-transparent coupling mesh must be present (its blend is
        // a light blue-grey, distinct from the pure-grey path).
        var wire = CountPixels(CaptureFrame(window), IsWireBlend);
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
        Assert.Equal(0, CountPixels(CaptureFrame(window), IsDoneGreen));

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
        var green = CountPixels(CaptureFrame(window), IsDoneGreen);
        Assert.True(green > 200,
            $"live updates must survive a tab switch, found {green} green pixels");
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

    /// <summary>The idle path is grey (#808080).</summary>
    private static bool IsPathGrey(byte r, byte g, byte b) =>
        Math.Abs(r - g) <= 14 && Math.Abs(g - b) <= 14 && r is >= 95 and <= 175;

    /// <summary>The semi-transparent loft wire over white blends to a light
    /// blue-grey (~#cbcbd6) — distinctly bluer than the grey path.</summary>
    private static bool IsWireBlend(byte r, byte g, byte b) =>
        b > r + 8 && b > 190 && g >= 180 && Math.Abs(r - g) <= 16;

    private static bool IsDoneGreen(byte r, byte g, byte b) =>
        g > 100 && r < 90 && b < 110;

    private static int CountPixels(
        WriteableBitmap bmp, Func<byte, byte, byte, bool> match, Func<int, bool>? xFilter = null)
    {
        using var fb = bmp.Lock();
        var format = fb.Format.ToString();
        var bgra = format.Contains("Bgra") || format.Contains("Pbgra");
        var rgba = format.Contains("Rgba") || format.Contains("PArgb");
        if (!bgra && !rgba)
            throw new InvalidOperationException($"unexpected framebuffer format: {format}");
        var row = new byte[fb.RowBytes];
        var count = 0;
        for (var y = 0; y < fb.Size.Height; y++)
        {
            Marshal.Copy(fb.Address + y * fb.RowBytes, row, 0, fb.RowBytes);
            for (var x = 0; x < fb.Size.Width; x++)
            {
                if (xFilter is not null && !xFilter(x))
                    continue;
                var o = x * 4;
                var (r, g, b) = bgra
                    ? (row[o + 2], row[o + 1], row[o])
                    : (row[o], row[o + 1], row[o + 2]);
                if (match(r, g, b))
                    count++;
            }
        }
        return count;
    }
}