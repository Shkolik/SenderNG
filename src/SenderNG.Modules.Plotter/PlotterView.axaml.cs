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

using System.ComponentModel;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Rendering;
using Avalonia.Threading;
using CNC.Core.Abstractions;
using CNC.Core.Services;
using SenderNG.Modules.Plotter.Settings;

namespace SenderNG.Modules.Plotter;

/// <summary>
/// Classic 2:1 isometric projection (Z up): the screen X axis runs along the
/// x−y diagonal, screen Y mixes the floor diagonal with height so +Z raises
/// the projected Y — the canvases' Y-flip then shows it upward on screen
/// (M4 orientation fix). Pure math — headlessly testable; the canvases apply
/// zoom, pan and the Y flip on top of the projected coordinates.
/// </summary>
public static class PlotterProjection
{
    public const double Cos30 = 0.86602540378443865;

    /// <summary>Mill-style iso: floor = (x, y), height = z.</summary>
    public static (double X, double Y) Project(double x, double y, double z) =>
        ((x - y) * Cos30, (x + y) * 0.5 + z);
}

/// <summary>Shared pens/brushes for the flat and isometric canvases (frontier
/// coloring is row-state driven: done green, error red, in-flight amber).
/// The path is grey by default (the user's palette request — blue was too
/// dark against the grid); the idle grey and in-flight amber cut at the base
/// width so a dense toolpath reads as lines, while the done/error frontiers
/// are 1.5x heavier so the cut frontier stands out inside the grey. Brushes + widths
/// are exposed separately so the cached-raster bake can re-create pens scaled
/// to the cache's world→pixel scale (Avalonia's CTM scales stroke width).</summary>
internal static class PlotterPens
{
    public static readonly IBrush FeedBrush = new SolidColorBrush(Color.FromRgb(0x80, 0x80, 0x80));
    public const double FeedW = 1.1;
    public static readonly IBrush RapidBrush = new SolidColorBrush(Color.FromRgb(0xc8, 0xc8, 0xc8));
    public const double RapidW = 0.8;
    public static readonly IBrush DoneBrush = new SolidColorBrush(Color.FromRgb(0x1b, 0x9e, 0x3e));
    public const double DoneW = 1.65;
    public static readonly IBrush ErrorBrush = new SolidColorBrush(Color.FromRgb(0xc0, 0x30, 0x30));
    public const double ErrorW = 1.65;
    public static readonly IBrush InFlightBrush = new SolidColorBrush(Color.FromRgb(0xe0, 0x96, 0x20));
    public const double InFlightW = 1.1;
    public static readonly IBrush OriginBrush = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88));
    public const double OriginW = 1;
    public static readonly IBrush ToolBrush = new SolidColorBrush(Color.FromRgb(0xd0, 0x20, 0x20));
    public const double ToolW = 1.6;
    // Semi-transparent loft wires — the foam iso's coupling mesh.
    public static readonly IBrush WireBrush = new SolidColorBrush(Color.FromArgb(0x59, 0x6a, 0x6a, 0x8a));
    public const double WireW = 0.7;
    public static readonly IBrush EmptyBrush = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88));

    public static readonly IPen Feed = new Pen(FeedBrush, FeedW);
    public static readonly IPen Rapid = new Pen(RapidBrush, RapidW, dashStyle: DashStyle.Dash);
    public static readonly IPen Done = new Pen(DoneBrush, DoneW);
    public static readonly IPen Error = new Pen(ErrorBrush, ErrorW);
    public static readonly IPen InFlight = new Pen(InFlightBrush, InFlightW);
    public static readonly IPen Origin = new Pen(OriginBrush, OriginW);
    public static readonly IPen Tool = new Pen(ToolBrush, ToolW);
    public static readonly IPen Wire = new Pen(WireBrush, WireW);
}

/// <summary>Pen set scaled for one raster bake: the bake draws in world
/// coordinates through a world→pixel transform, and Avalonia's CTM scales
/// stroke width with it — pens must be pre-divided by the bake scale so a
/// baked line hits the same DEVICE pixels as the direct draw. (M8: the old
/// code multiplied here — with a ~15x fit zoom the baked path rendered 10–15x
/// thicker than specified and dense toolpaths turned into grey blobs.)</summary>
internal sealed class BakePens
{
    public readonly IPen Feed, Rapid, Done, Error, InFlight;

    public BakePens(double scale)
    {
        Feed = new Pen(PlotterPens.FeedBrush, PlotterPens.FeedW / scale);
        Rapid = new Pen(PlotterPens.RapidBrush, PlotterPens.RapidW / scale, dashStyle: DashStyle.Dash);
        Done = new Pen(PlotterPens.DoneBrush, PlotterPens.DoneW / scale);
        Error = new Pen(PlotterPens.ErrorBrush, PlotterPens.ErrorW / scale);
        InFlight = new Pen(PlotterPens.InFlightBrush, PlotterPens.InFlightW / scale);
    }

    public IPen For(bool rapid, ProgramRowState state) => state switch
    {
        ProgramRowState.Ok => Done,
        ProgramRowState.Error => Error,
        ProgramRowState.Sent => InFlight,
        _ => rapid ? Rapid : Feed,
    };
}

public partial class PlotterView : UserControl
{
    private PlotterViewModel? _vm;
    private readonly List<Control> _canvases = [];
    private bool _suppressIso;
    private bool _attached;
    private int _lastGeometryVersion;
    // Header text cache: UpdateHeader runs on EVERY view-model change tick
    // (frontier/position storms during a run); setting TextBlock.Text on each
    // tick forces layout work, so identical values must not re-assign.
    private string _lastHeaderLetters = string.Empty;
    private string _lastProgress = string.Empty;
    private string _lastStatus = string.Empty;

    /// <summary>Throttle for live-run redraws: state ticks arrive at hundreds
    /// per second while the eye reads ~30fps — a 33ms minimum interval
    /// collapses the burst into one render without perceptible latency, and
    /// keeps the UI thread free for input.</summary>
    private const int RedrawIntervalMs = 33;

    /// <summary>The PNG save dialog service. Resolved from the app's DI by
    /// <c>PlotterModule.CreateView</c>, so it is non-null in the only production
    /// path; null only in the headless tests, which use the parameterless ctor.
    /// That is why <see cref="OnSavePng"/> reads it before doing anything — an
    /// assigned but unread field is CS0414 and the build is 0-warning by
    /// contract. The export itself is Task 12; until then the button is inert.</summary>
    private readonly IFileDialogService? _dialogs;

    /// <summary>Parameterless, and NOT just a convenience: the Avalonia XAML
    /// compiler warns (AVLN3001) when a compiled view has no public one, because
    /// the runtime loader needs it — with only the optional-argument ctor the
    /// build went to 1 warning, and this project is 0-warning by contract.
    /// No dialog service means the PNG button is inert, which is what the
    /// headless tests get.</summary>
    public PlotterView() : this(null)
    {
    }

    /// <param name="dialogs">The app's file-dialog service. Supplied by
    /// <c>PlotterModule.CreateView</c> in production; null only in the headless
    /// tests, which leave the PNG button inert.</param>
    public PlotterView(IFileDialogService? dialogs)
    {
        _dialogs = dialogs;
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        // Avalonia has no Checked/Unchecked CLR events — observe IsChecked.
        IsoToggle.PropertyChanged += (_, e) =>
        {
            if (e.Property.Name == nameof(CheckBox.IsChecked) && !_suppressIso)
                OnIsoToggled(IsoToggle.IsChecked == true);
        };
        // Tab switches detach the view from the visual tree; the PropertyChanged
        // subscription must survive a return trip (the old code unsubscribed on
        // detach and never re-subscribed — after ONE tab switch the plot froze
        // on frontier updates forever).
        AttachedToVisualTree += (_, _) =>
        {
            _attached = true;
            if (_vm is null)
                return;
            _vm.PropertyChanged += OnVmPropertyChanged;
            _lastGeometryVersion = _vm.GeometryVersion;
            RebuildCanvases();
            UpdateHeader();
        };
        DetachedFromVisualTree += (_, _) =>
        {
            _attached = false;
            if (_vm is not null)
                _vm.PropertyChanged -= OnVmPropertyChanged;
        };
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_vm is not null)
            _vm.PropertyChanged -= OnVmPropertyChanged;
        _vm = DataContext as PlotterViewModel;
        if (_vm is null)
            return;
        if (_attached)
            _vm.PropertyChanged += OnVmPropertyChanged;
        _suppressIso = true;
        IsoToggle.IsChecked = _vm.Isometric;
        IsoToggle.Content = _vm.IsometricLabel;
        _suppressIso = false;
        _lastGeometryVersion = _vm.GeometryVersion;
        RebuildCanvases();
        UpdateHeader();
    }

    private void OnIsoToggled(bool value)
    {
        if (_suppressIso || _vm is null || _vm.Isometric == value)
            return;
        _vm.Isometric = value;
        RebuildCanvases();
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_vm is null)
            return;

        if (e.PropertyName is nameof(PlotterViewModel.PlaneCount) or nameof(PlotterViewModel.PlaneLetters))
            RebuildCanvases();

        // A new geometry (load/transform/clear) re-fits the canvases; frontier
        // and live-position updates never reset the user's zoom/pan.
        if (e.PropertyName == nameof(PlotterViewModel.GeometryVersion) &&
            _vm.GeometryVersion != _lastGeometryVersion)
        {
            _lastGeometryVersion = _vm.GeometryVersion;
            foreach (var c in _canvases)
                if (c is PlotCanvas pc)
                    pc.OnGeometryReplaced();
        }

        UpdateHeader();

        // Live-run ticks (frontier rows, tool position) invalidate at a
        // throttled cadence — a burst of acks renders ~30fps, not per ack.
        // Everything else (geometry, layout, status) invalidates directly.
        if (e.PropertyName is nameof(PlotterViewModel.FrontierVersion))
        {
            foreach (var c in _canvases)
                if (c is PlotCanvas pc)
                    pc.ThrottledInvalidate();
        }
        else
        {
            InvalidateAll();
        }
    }

    /// <summary>Rebuilds the canvas set: one isometric canvas when the toggle
    /// is on (stretched over the whole panel), otherwise ONE canvas — the two
    /// foam planes draw in diagonal quadrants of the same surface (plane 1
    /// top-left, plane 2 bottom-right), single-plane machines just fill it.</summary>
    private void RebuildCanvases()
    {
        PlanesGrid.Children.Clear();
        // No definitions = one star cell: the single canvas (or the iso view)
        // stretches over the WHOLE panel (the old two-column layout leaked
        // into the iso rebuild and shrank it to the left half).
        PlanesGrid.ColumnDefinitions = new ColumnDefinitions();
        PlanesGrid.RowDefinitions = new RowDefinitions();
        _canvases.Clear();
        if (_vm is null)
            return;

        if (_vm.Isometric)
        {
            var iso = new IsoCanvas(_vm);
            PlanesGrid.Children.Add(iso);
            _canvases.Add(iso);
            iso.ZoomChanged = UpdateZoomReadout;
            // A fresh PlotCanvas starts at ChromeFlags.Default, so the rebuild
            // has to hand it the flags the user already chose — see ApplyChrome.
            ApplyChrome();
            return;
        }

        var canvas = new PlaneCanvas(_vm);
        PlanesGrid.Children.Add(canvas);
        _canvases.Add(canvas);
        canvas.ZoomChanged = UpdateZoomReadout;
        ApplyChrome();
    }

    /// <summary>The shared settings model — the SAME instance the App Settings
    /// leaf binds, handed in by the module. Setting it binds the overlay chrome
    /// to it and re-applies the toggles into the canvases, so a flip on either
    /// surface is visible on both.
    /// <para>The <c>Changed</c> subscription is what makes the OTHER direction
    /// work: the App Settings leaf writes to the model, not to this view, so
    /// without it a leaf-side flip would move the leaf's checkbox and leave the
    /// plot's lattice on screen. The same shape as
    /// <c>PlotterNexView.OverlaySettings</c>. The module pushes on the same
    /// event as well; this view keeps its own so one built outside the module
    /// still follows the model instead of depending on its host to forward
    /// it. Both paths are idempotent — they set the same flags.</para></summary>
    public PlotterSettingsViewModel? OverlaySettings
    {
        get => _overlaySettings;
        set
        {
            if (_overlaySettings is not null)
                _overlaySettings.Changed -= OnOverlaySettingsChanged;
            _overlaySettings = value;
            if (OverlayHost is not null)
                OverlayHost.DataContext = value;
            if (_overlaySettings is not null)
                _overlaySettings.Changed += OnOverlaySettingsChanged;
            // Bare call = re-apply, NOT a reset: with nothing pushed before, the
            // attached model's flags are the first thing in effect, and on a
            // later assignment the user's toggles must survive.
            PushChromeFlags();
        }
    }

    /// <summary>A settings edit reached the model — from the overlay's own
    /// checkbox or from the App Settings leaf, which are the same object. The
    /// model has already raised PropertyChanged for the individual flags by now
    /// (<c>Persist</c> notifies first, then raises this), so
    /// <see cref="Flags"/> reads the new set.</summary>
    private void OnOverlaySettingsChanged() => PushChromeFlags();

    private PlotterSettingsViewModel? _overlaySettings;

    /// <summary>How far out the view may be zoomed before the button cluster
    /// gives up. NOT the wheel's floor: <c>OnPointerWheelChanged</c> applies no
    /// minimum of its own and only <c>ZoomSlot</c> clamps, using whatever
    /// minimum it is handed — which is this constant on the button path and
    /// nothing at all on the wheel path.</summary>
    internal const double MinZoom = 0.02;

    /// <summary>Scales every canvas about its own center, so the composition's
    /// midpoint stays put. The wheel keeps its cursor-anchored behavior — this is
    /// the button cluster only.</summary>
    public void ZoomBy(double factor)
    {
        foreach (var canvas in _canvases.OfType<PlotCanvas>())
            canvas.ZoomSlot(0, factor, MinZoom);
        UpdateZoomReadout();
        InvalidateAll();
    }

    /// <summary>Zooms to an exact multiple of 1:1 about the composition's center.
    /// A no-op before the first fit (<see cref="CurrentZoom"/> is 0 then) and for
    /// a non-positive target, so a bad caller cannot invert the view.</summary>
    public void ZoomTo(double zoom)
    {
        var current = CurrentZoom;
        if (current <= 0 || zoom <= 0)
            return;
        ZoomBy(zoom / current);
    }

    /// <summary>Back to the fitted view.</summary>
    public void Reframe()
    {
        foreach (var canvas in _canvases.OfType<PlotCanvas>())
            canvas.Reframe();
        UpdateZoomReadout();
        InvalidateAll();
    }

    /// <summary>The composition's current zoom as a multiple of 1:1, or 0 before
    /// the first fit — a canvas that has not been fitted yet holds NaN, and the
    /// filter drops it, so an empty match yields <c>default(double)</c>.</summary>
    internal double CurrentZoom =>
        _canvases.OfType<PlotCanvas>()
            .Select(c => c.SlotTransformForTest(0).Zoom)
            .FirstOrDefault(z => !double.IsNaN(z) && z > 0);

    /// <summary>Refreshes the overlay's zoom readout. The canvas posts its
    /// render-pass announcements (see <c>PlotCanvas.AnnounceZoom</c>), so this
    /// runs either inline from the wheel/button path or from the dispatcher
    /// queue afterwards.</summary>
    private void UpdateZoomReadout()
    {
        if (ZoomPctText is null)
            return;
        var zoom = CurrentZoom;
        // `zoom > 0`, not merely "not NaN": CurrentZoom reports 0 for a view
        // that has never been fitted, and 0 is not NaN, so a view with no
        // geometry rendered a permanent "0%" claiming it was infinitely far
        // out. Reframe() hit the same guard on the first frame of a view whose
        // fit has not happened yet.
        //
        // The floor is applied to the ROUNDED value, not the raw one. The
        // stated invariant is that a multiple of 1:1 is shown and exactly 1:1
        // is not, but rounding first breaks it: 1.004 is not 1:1, so the
        // readout shows — and `(1.004 * 100).ToString("0")` is "100", so it
        // claims exactly 1:1 while being visible. Flooring the rounded number
        // instead turns 1.004 into 99%, which is honest at the precision the
        // readout actually displays.
        if (zoom <= 0)
        {
            ZoomPctText.Text = string.Empty;
            ZoomPctText.IsVisible = false;
            return;
        }
        var show = Math.Abs(zoom - 1.0) > 1e-6;
        ZoomPctText.Text = show
            ? DisplayPercent(zoom)
            : string.Empty;
        ZoomPctText.IsVisible = show;
    }

    /// <summary>The percentage as the readout shows it: whole percent,
    /// floored, and never 100.
    /// <para>Floored because the readout must not round UP into a claim it is
    /// not making. And 100 is barred outright, because 100% IS the 1:1 claim:
    /// a visible readout is only ever shown when the zoom is not 1:1, so
    /// printing 100% there says the one thing the visibility rule exists to
    /// avoid. 1.004 is the case that forced this — it is not 1:1, so the
    /// readout shows, and both <c>Math.Round</c> and <c>Math.Floor</c> of
    /// 100.4 give 100. It reads 99%, which understates by less than a percent
    /// and is the only honest option at the precision on screen.</para></summary>
    private static string DisplayPercent(double zoom)
    {
        var percent = Math.Floor(zoom * 100);
        // Only the exact 100, NOT everything above it: 150% and 1279% are
        // perfectly good claims and must survive untouched.
        if (Math.Abs(percent - 100) < 1e-9)
            percent = 99;
        return percent.ToString(CultureInfo.InvariantCulture) + "%";
    }

    private void OnZoomIn(object? sender, RoutedEventArgs e) => ZoomBy(1.25);

    private void OnZoomOut(object? sender, RoutedEventArgs e) => ZoomBy(1 / 1.25);

    private void OnZoomFit(object? sender, RoutedEventArgs e) => Reframe();

    /// <summary>PNG export is Task 12. The stub reads <see cref="_dialogs"/> on
    /// purpose: there is nowhere to save to without a dialog service, and an
    /// assigned-but-unread field would be CS0414. It is deliberately NOT
    /// <c>async</c> — there is nothing to await, and an async body with no
    /// await is CS1998.</summary>
    private void OnSavePng(object? sender, RoutedEventArgs e)
    {
        if (_dialogs is null)
            return; // no dialog service wired: the save path does not exist yet
    }

    /// <summary>The last set of flags <see cref="PushChromeFlags"/> was given, kept
    /// so <see cref="RebuildCanvases"/> can hand them to the canvases it creates.
    /// Null means "nothing was ever pushed" and reads as
    /// <see cref="ChromeFlags.Default"/>, which is also what a fresh
    /// <c>PlotCanvas</c> starts at — so with nothing pushed this field only
    /// re-states the default.</summary>
    private ChromeFlags? _chromeFlags;

    /// <summary>Pushes the view toggles into every canvas, remembering them so a
    /// later rebuild re-applies the same set.
    /// <para>Passing nothing is a pure re-apply: it does NOT clear the memory. That
    /// matters because the rebuild path and the overlay-settings setter both call
    /// this with no argument to mean "re-apply what is already in effect", so
    /// treating a null argument as "reset to default" would discard the user's
    /// toggles on exactly the calls that are supposed to preserve them. To go back
    /// to the defaults, pass <see cref="ChromeFlags.Default"/> explicitly.</para></summary>
    internal void PushChromeFlags(ChromeFlags? flags = null)
    {
        // Only a real value updates the memory. A null argument is the re-apply
        // call, not a reset — see the doc above and RebuildCanvases.
        if (flags is not null)
            _chromeFlags = flags;
        ApplyChrome();
    }

    /// <summary>The flags the view would hand the next canvas it creates: the last
    /// value pushed, then an attached overlay model, then
    /// <see cref="ChromeFlags.Default"/> when neither exists. This is the same
    /// expression <see cref="ApplyChrome"/> uses, read back through one property
    /// so a test can assert the retained state without duplicating the fallback —
    /// and without painting pixels to do it.</summary>
    internal ChromeFlags? DebugStoredChromeFlags =>
        _chromeFlags ?? _overlaySettings?.Flags ?? ChromeFlags.Default;

    /// <summary>Writes the remembered flags into every canvas. Split out of
    /// <see cref="PushChromeFlags"/> so <see cref="RebuildCanvases"/> can re-apply
    /// them to the canvases it just created: the iso toggle rebuilds the whole set,
    /// and before this existed a rebuild handed every new canvas
    /// <see cref="ChromeFlags.Default"/>, silently dropping every toggle the user
    /// had set — the reported "set the toggles, toggle iso, watch them disappear".</summary>
    private void ApplyChrome()
    {
        // The RETAINED state wins, not the attached model: `_chromeFlags` is
        // checked first. It was previously documented the other way round, and
        // in the shipped app it is not true — the module calls ApplySettings()
        // (and so PushChromeFlags) AFTER the view's initializer, which both
        // populates `_chromeFlags` and pushes the flags explicitly, so from
        // that moment on the retained value shadows the model.
        //
        // The overlay's flips still work, but not through this line: they land
        // because the model raises Changed and the view re-pushes on it, which
        // updates `_chromeFlags` first and only then reaches here. A model
        // assigned with no push after it therefore has no effect until
        // something else pushes — the ordering dependency worth knowing before
        // reordering those two calls.
        var flags = _chromeFlags ?? _overlaySettings?.Flags ?? ChromeFlags.Default;
        foreach (var canvas in _canvases.OfType<PlotCanvas>())
        {
            canvas.Chrome = flags;
            // Setting Chrome is a plain property write, so nothing repaints on its
            // own. Without this invalidate the toggle tests measure the SAME frame
            // twice and the "grid went away" assertion fails on a screen that never
            // changed — and in the real app the switch would appear dead.
            canvas.InvalidateVisual();
        }
    }

    /// <summary>The header's plane-letter string. The generated <c>HeaderLetters</c>
    /// field is internal to the view's own assembly, so tests read this.</summary>
    internal string HeaderLettersForTest => HeaderLetters.Text ?? string.Empty;

    /// <summary>The injected dialog service, for headless tests.
    /// <para>A seam rather than a public property because the field is only
    /// meaningful as a question of FACT — "did the host hand this view the
    /// app's dialog service, or a null one it can never save through?" — and
    /// there is no behaviour to observe until PNG export lands. Reflection
    /// would answer the same question without the production surface growing a
    /// member that exists only for tests.</para></summary>
    internal IFileDialogService? DialogsForTest => _dialogs;

    /// <summary>One transform per canvas, for headless tests.</summary>
    internal IReadOnlyList<(double Zoom, Vector Pan)> CompositionForTest =>
        _canvases.OfType<PlotCanvas>().Select(c => c.SlotTransformForTest(0)).ToArray();

    /// <summary>Raster bakes run per canvas, for headless tests. The chain
    /// limiter is invisible from pixels — a refused bake and a bake of identical
    /// pixels look the same on the surface — so "the limiter limits" has to be
    /// asserted on a count.</summary>
    internal IReadOnlyList<int> BakeCountsForTest =>
        _canvases.OfType<PlotCanvas>().SelectMany(c => c.SlotBakeCounts).ToArray();

    internal bool HasPendingRebake => _canvases.OfType<PlotCanvas>().Any(c => c.HasPendingRebake);

    /// <summary>Slots the flat canvas currently considers drawable. A plane whose
    /// mapping does not feed world Y has no 2D extent, so it is skipped.</summary>
    internal IReadOnlyList<int> DrawableSlotsForTest =>
        _canvases.OfType<PlaneCanvas>()
            .SelectMany(c => Enumerable.Range(0, c.SlotCountForTest).Where(c.IsSlotDrawableForTest))
            .ToArray();

    private void UpdateHeader()
    {
        if (_vm is null)
            return;
        var letters = string.Join(" | ", _vm.PlaneLetters);
        if (letters != _lastHeaderLetters)
        {
            _lastHeaderLetters = letters;
            HeaderLetters.Text = letters;
        }
        var progress = _vm.ProgressText;
        if (progress != _lastProgress)
        {
            _lastProgress = progress;
            ProgressText.Text = progress;
        }
        var status = _vm.StatusText ?? string.Empty;
        if (status != _lastStatus)
        {
            _lastStatus = status;
            StatusTextBlock.Text = status;
        }
    }

    private void InvalidateAll()
    {
        foreach (var c in _canvases)
            c.InvalidateVisual();
    }

    /// <summary>Throttle for live-run redraws: collapses invalidation bursts
    /// into at most one render per <see cref="RedrawIntervalMs"/>, with a
    /// trailing render so the last state always lands. UI-thread only.</summary>
    private sealed class RedrawThrottle
    {
        private long _lastTicks = long.MinValue;
        private bool _pending;

        public void Tick(Action render)
        {
            var now = Environment.TickCount64;
            if (_lastTicks != long.MinValue && now - _lastTicks < RedrawIntervalMs)
            {
                if (_pending)
                    return;
                _pending = true;
                _ = Dispatcher.UIThread.InvokeAsync(() =>
                {
                    _pending = false;
                    _lastTicks = Environment.TickCount64;
                    render();
                }, DispatcherPriority.Background);
                return;
            }
            _lastTicks = now;
            render();
        }
    }

    /// <summary>
    /// Shared machinery of the flat and isometric canvases: one shared
    /// fit/pan/zoom for the whole composition (the two foam panes move together
    /// as a single canvas), a per-slot cached raster of the toolpath, and the
    /// live tool glyph.
    /// <para>
    /// RENDER PATH (M7): the full path bakes ONCE into a world-sized raster
    /// per slot (on geometry replace / after a zoom gesture settles — never
    /// during one), and every frame is then a bitmap blit plus a small vector
    /// recolor window (the rows acked since the bake, capped) plus the grid
    /// and tool glyph. A 100k-segment file therefore costs ~1 blit per frame instead of 100k DrawLine calls — the M5/M6 full-path-per-frame render
    /// saturated the UI thread during runs (the reported cycle-start freeze),
    /// and the M4 viewport-sized raster clipped panned content. The world-
    /// sized cache can never expose blank edges (panning is a pure translate)
    /// and the recolor window keeps frontier coloring real-time.
    /// </para>
    /// <para>
    /// INPUT: <see cref="ICustomHitTest.HitTest"/> below is load-bearing, not
    /// decoration. Avalonia hit-tests a custom-drawn control over what it PAINTS,
    /// and this class paints into a cached raster, so most of the surface — every
    /// pixel the toolpath does not cover — was not a hit target. Wheel-zoom and
    /// drag-pan then fell through to the white wrapping <c>Border</c> in
    /// <c>PlotterView.axaml</c>, which carries no handlers, and died over most of
    /// the plot. Measured: a 5x5 probe grid returned the canvas at zoom 2.0 and
    /// the Border at 1.25 and below, at every point tried.
    /// <c>Background = Brushes.Transparent</c> — the one-liner
    /// <c>PlotterNexView.axaml:86</c> uses on its input panel — does NOT port: that
    /// works on a <c>Panel</c>, which paints the brush itself, while
    /// <c>Background</c> on a <c>TemplatedControl</c> is template-bound and these
    /// canvases have no template (measured: still falling through). So the canvas
    /// stays a <c>Control</c> and declares the hit area directly. The four pointer
    /// handlers are on the subclasses, and this makes all of them reachable
    /// everywhere on the surface.
    /// </para>
    /// </summary>
    private abstract class PlotCanvas : Control, ICustomHitTest
    {
        /// <summary>Recolor window cap per frame: the delta since the last
        /// bake draws as vectors on top of the raster; a bigger delta (demo
        /// flood) requests a rebake instead of drawing tens of thousands of
        /// segments per frame.</summary>
        private const int OverlayCap = 2048;

        /// <summary>Minimum interval between raster bakes — bakes of huge
        /// paths take tens to hundreds of ms, so they must never chain.</summary>
        private const long BakeMinIntervalMs = 500;

        /// <summary>Cache size clamp per side: a deeply zoomed world would
        /// otherwise allocate gigabytes. Beyond the clamp the blit scales up
        /// (soft) — grid, recolor window and glyph stay crisp.</summary>
        private const int CacheMaxPx = 2048;

        /// <summary>Zoom tolerance below which the cached raster is reused
        /// (a small drift blits 1:1 and needs no rebake).</summary>
        private const double ZoomTolerance = 0.02;

        /// <summary>How long after the last input event interaction is
        /// considered "active" (decimation window, no bakes).</summary>
        private const int InteractionWindowMs = 400;

        /// <summary>How long after the last frontier advance a run counts as
        /// active (coupling-wire decimation returns to full detail 3s after
        /// the last ack — runs end without an explicit signal).</summary>
        private const int RunActiveWindowMs = 3000;

        /// <summary>Gap between the two diagonal foam tiles, in machine units —
        /// keeps the to-scale panes visually separated in the shared world
        /// (both panes share one unit = one mm).</summary>
        protected const double FoamTileGap = 8;

        /// <summary>Fitting margin kept on every side of the composed foam
        /// canvas (px), same as the single-plane fit.</summary>
        protected const double FoamFitMargin = 24;

        protected readonly PlotterViewModel Vm;

        /// <summary>Which chrome this canvas draws. Pushed from the shared settings
        /// view model once the view is attached (Task 10).
        /// <para><see cref="ChromeFlags.RunState"/> is the ONE flag the raster
        /// bake reads: <c>RowStateFor</c> collapses to Idle without it, and the
        /// row states it resolves are baked into the cached raster. So flipping
        /// it has to invalidate that raster — an <c>InvalidateVisual</c> alone
        /// re-blits the old green and the switch looks dead, which is the trap
        /// the grid/axes/envelope flags do not have, because those are drawn
        /// per frame in <c>DrawSlotChrome</c> and never reach the bake.</para></summary>
        internal ChromeFlags Chrome
        {
            get => _chrome;
            set
            {
                if (value.RunState != _chrome.RunState)
                {
                    // Two requests, and the second one is the load-bearing part.
                    // A plain request alone is NOT enough: a bake is rate-limited
                    // to one per BakeMinIntervalMs, the flag push invalidates
                    // exactly once, and with no run in flight nothing invalidates
                    // again — so the render that could have rebaked is refused by
                    // the limiter and the switch stays dead until the user pans or
                    // zooms. A toggle is one discrete user event, not a chain of
                    // frames, so the FIRST slot is allowed to pay for its own bake
                    // (see BlitOrBake) and the rest queue behind it.
                    //
                    // RequestRebake sizes the arrays: this is the one caller that
                    // can run before the first render has done it.
                    for (var slot = 0; slot < SlotCount; slot++)
                        RequestRebake(slot, urgent: true);
                }
                _chrome = value;
            }
        }

        private ChromeFlags _chrome = ChromeFlags.Default;

        private Point _dragOrigin;
        private Vector _dragBasePan;
        private bool _dragging;
        private int _gestureSlot;
        private long _lastInputTicks = long.MinValue;
        private readonly RedrawThrottle _throttle = new();
        private readonly DispatcherTimer _settleTimer;

        /// <summary>One-shot: brings back a render that the bake limiter refused.
        /// <para>A refused rebake keeps its per-slot pending bit, but a pending
        /// bit only repaints when a render happens, and with nothing else
        /// driving one the queue would sit unapplied — the settle timer runs
        /// only after a gesture, and the VM's throttled ticks stop when the run
        /// does. Firing one interval after the refusal is what makes "refused
        /// now, repainted later" true rather than "refused until the user pans".
        /// Re-entrant starts are harmless: the tick stops the timer first, and
        /// a refusal that is still pending restarts it.</para></summary>
        private readonly DispatcherTimer _rebakeRetry;

        /// <summary>Scales the slot about its own center, so the composition's
        /// midpoint stays put. The wheel keeps its cursor-anchored behavior — this is
        /// the button cluster only.
        /// <para>Internal, and it takes the work rather than exposing the transform:
        /// <c>protected</c> on a NESTED type is reachable from that type and its
        /// derived types, NOT from the class that encloses it, so the view cannot
        /// read <see cref="SlotZoom"/>/<see cref="SlotPan"/>/<see cref="SetSlotTransform"/>
        /// however much the plan says otherwise. Same reason
        /// <see cref="SlotTransformForTest"/> exists.</para></summary>
        internal void ZoomSlot(int slot, double factor, double minZoom)
        {
            var zoom = SlotZoom(slot);
            if (double.IsNaN(zoom) || zoom <= 0)
                return;
            var area = new Rect(Bounds.Size);
            if (area.Width < 1 || area.Height < 1)
                return;
            var pan = SlotPan(slot);

            // The world point under the canvas center, so it can be put back.
            var world = ScreenToWorld(area.Center, zoom, pan);
            var next = Math.Max(zoom * factor, minZoom);
            SetSlotTransform(slot, next,
                new Vector(area.Center.X - world.X * next, area.Center.Y + world.Y * next));
            InvalidateVisual();
        }

        /// <summary>Drops the fitted transform so the next render re-fits — the
        /// "Fit" button, and the seam for the same reason as
        /// <see cref="ZoomSlot"/>. Internal wrapper rather than widening
        /// <see cref="ResetTransforms"/> itself: nothing outside this type should be
        /// able to wipe the viewport.</summary>
        internal void Reframe() => ResetTransforms();

        /// <summary>Notifies the view that the zoom changed, from either caller, but
        /// the two need different timing. The wheel is an input event: the view can
        /// update the readout inline. The fit runs INSIDE a render pass, and
        /// <c>TextBlock.Text</c> is a measure invalidation — so that one is posted
        /// and lands after the render, instead of mutating layout from inside it.
        /// The canvas is the only side that knows which caller it is, so the choice
        /// lives here and the view just gets told.</summary>
        private void AnnounceZoom(bool insideRender)
        {
            if (insideRender)
                Dispatcher.UIThread.Post(() => ZoomChanged?.Invoke(), DispatcherPriority.Normal);
            else
                ZoomChanged?.Invoke();
        }

        /// <summary>Set by the view when it creates this canvas: the zoom changed
        /// (a wheel notch, or the initial fit), so the readout has to follow.
        /// The canvas cannot reach the overlay's TextBlock itself.</summary>
        internal Action? ZoomChanged { get; set; }


        /// <summary>Internal, not the protected <see cref="SlotZoom"/>/<see cref="SlotPan"/>:
        /// a containing type may not read a nested type's protected members, so the
        /// view reads the transform through this seam (as it already does for
        /// <see cref="OnGeometryReplaced"/>).</summary>
        internal (double Zoom, Vector Pan) SlotTransformForTest(int slot) =>
            (SlotZoom(slot), SlotPan(slot));

        // Per-slot raster caches: the path drawn ONCE per (geometry, settled
        // zoom) into a world-sized bitmap; every frame blits it through the
        // current transform and overlays the recolor window.
        private RenderTargetBitmap?[] _cache = [];
        private double[] _cacheZoom = [];
        private int[] _cacheGeoVersion = [];
        private long _lastBakeTicks = long.MinValue;
        /// <summary>Bakes run since construction. A test seam for the chain
        /// limiter, which is otherwise invisible: a refused bake and a bake of
        /// identical pixels look exactly the same on screen.</summary>
        /// <summary>Bakes per slot, for tests only. Zeroed, never reset: a
        /// clearing count cannot express "stopped rebaking", which is half of
        /// what the overflow regression has to prove.</summary>
        private int[] _bakeCounts = [];

        // Rebake requests, PER SLOT. These were two canvas-wide booleans lifted
        // and cleared once per render pass, which is what let the first slot to
        // ask spend the limiter window and strand every slot after it.
        /// <summary>This slot owes a rebake: anything that makes its cached
        /// raster wrong (a recolor-window overflow, a rewind, a chrome flip).
        /// Cleared ONLY by this slot's own bake, so a slot the limiter refuses
        /// re-arms itself and a refusal can never cancel a sibling's
        /// request.</summary>
        private bool[] _rebakePending = [];
        /// <summary>This slot's pending rebake came from a direct user request
        /// (a chrome flag flip), so it may open the limiter window early instead
        /// of waiting out <see cref="BakeMinIntervalMs"/>. See the
        /// <see cref="Chrome"/> setter for why the plain request is not enough.
        /// At most ONE slot per pass spends it — see <see cref="BlitOrBake"/>.</summary>
        private bool[] _rebakeUrgent = [];

        /// <summary>The current <see cref="BakeMinIntervalMs"/> window has
        /// already been opened early by an urgent request, so it cannot be opened
        /// early again until the window elapses.
        /// <para>Per WINDOW, not per pass: the old code let every slot bypass the
        /// limiter on every pass, so one chrome toggle chained SlotCount
        /// full-world bakes. Scoping it to a pass was not enough either — the
        /// next pass re-armed it and the queue behind slot 0 drained one slot per
        /// pass, which is still a chain.</para></summary>
        private bool _urgentSpentInWindow;

        // Run-frontier tracking, PER SLOT for the same reason: rows ≤
        // _bakedFrontierRow[slot] are reflected in that slot's cached colors;
        // rows above recolor as vectors until a rebake. Canvas-wide it was, so
        // slot 0's bake closed slot 1's recolor window too and nothing ever
        // repainted the second carriage.
        private int[] _bakedFrontierRow = [];
        private int _lastSeenDone = -1;
        private int _lastTouched = -1;
        private long _lastRunTicks = long.MinValue;
        private bool _runActive;
        private bool _completionResyncDone;

        // Incremental tool-glyph tracking: the end of the last acked segment,
        // advanced per frontier delta instead of the old O(n) full walk.
        private int _glyphRow = -1;
        private double[]? _ackedEnd;

        /// <summary>The whole surface is a wheel/drag target, not just the pixels
        /// the toolpath covers — see the INPUT note on the class. Coordinates are
        /// local, so the bounds check is what keeps a point outside the canvas
        /// from being claimed.</summary>
        bool ICustomHitTest.HitTest(Point p)
            => p.X >= 0 && p.Y >= 0 && p.X <= Bounds.Width && p.Y <= Bounds.Height;

        protected PlotCanvas(PlotterViewModel vm)
        {
            Vm = vm;
            ClipToBounds = true;
            _settleTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(InteractionWindowMs + 60) };
            _settleTimer.Tick += (_, _) =>
            {
                _settleTimer.Stop();
                InvalidateVisual(); // interaction over → the render can rebake
            };
            _rebakeRetry = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(BakeMinIntervalMs) };
            _rebakeRetry.Tick += (_, _) =>
            {
                _rebakeRetry.Stop();
                InvalidateVisual(); // the limiter window has opened → serve the queue
            };
        }

        /// <summary>The view model replaced the geometry — drop the fitted
        /// zoom/pan so the next render re-fits the new toolpath (a second
        /// program must not inherit the previous one's viewport) and discard
        /// the cached rasters (stale geometry).</summary>
        internal void OnGeometryReplaced()
        {
            ResetTransforms();
            InvalidateCaches();
        }

        protected abstract int SlotCount { get; }

        /// <summary>The slot's viewport rectangle inside the canvas.</summary>
        protected abstract Rect SlotViewport(int slot, Rect area);

        /// <summary>Current zoom of slot <paramref name="slot"/> (NaN = fit on
        /// next render).</summary>
        protected abstract double SlotZoom(int slot);

        /// <summary>Current pan of slot <paramref name="slot"/>.</summary>
        protected abstract Vector SlotPan(int slot);

        /// <summary>Persist the slot transform after a gesture step.</summary>
        protected abstract void SetSlotTransform(int slot, double zoom, Vector pan);

        /// <summary>Fit slot <paramref name="slot"/> into its viewport (first
        /// render / geometry replacement); bounds come from
        /// <see cref="SlotWorldBounds"/>.</summary>
        protected abstract void FitSlot(int slot, Rect viewport);

        /// <summary>World bounding box of the slot's content (the cache's
        /// coverage and the fit's span source).</summary>
        protected abstract (double MinX, double MinY, double MaxX, double MaxY) SlotWorldBounds(int slot);

        /// <summary>Segments of the slot in WORLD coordinates.</summary>
        protected abstract int SlotSegmentCount(int slot);

        protected abstract WorldSeg SlotSegmentAt(int slot, int index);

        /// <summary>Draw the slot's full toolpath in world coordinates with
        /// per-state pens scaled for the raster's world→pixel scale. Runs only
        /// inside a bake (rate-limited) or as the pre-bake fallback.</summary>
        protected abstract void DrawSlotPathWorld(DrawingContext ctx, int slot, BakePens pens);

        /// <summary>Draw the slot's chrome overlay in world coordinates (per
        /// frame, crisp vectors — it must not blur under the baked raster): the
        /// world XY lattice, the machine-origin cross, the world axis arrows and
        /// the job rectangle, each gated on <see cref="Chrome"/>.</summary>
        protected abstract void DrawSlotChrome(DrawingContext ctx, int slot);

        /// <summary>Coupling/loft wires spanning several slots (foam iso),
        /// drawn once per frame after the per-slot passes. Default: none.</summary>
        protected virtual void DrawCoupling(DrawingContext ctx, Rect area) { }

        protected abstract void DrawToolGlyph(DrawingContext ctx, Rect area, double[]? frontierEnd);

        /// <summary>Path loop step for coupling wires: 1 at rest, decimated
        /// during interaction and while a run is advancing.</summary>
        protected int WireStep(int count) =>
            count > 1500 && (Interacting || _runActive) ? (count + 1500 - 1) / 1500 : 1;

        private bool Interacting =>
            _lastInputTicks != long.MinValue &&
            Environment.TickCount64 - _lastInputTicks < InteractionWindowMs;

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            _dragging = true;
            _gestureSlot = SlotUnder(e.GetPosition(this));
            _dragOrigin = e.GetPosition(this);
            _dragBasePan = SlotPan(_gestureSlot);
            _lastInputTicks = Environment.TickCount64;
            _settleTimer.Stop();
            e.Pointer.Capture(this);
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            if (!_dragging)
                return;
            _lastInputTicks = Environment.TickCount64;
            var pan = _dragBasePan + (e.GetPosition(this) - _dragOrigin);
            SetSlotTransform(_gestureSlot, SlotZoom(_gestureSlot), pan);
            InvalidateVisual();
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            _dragging = false;
            _lastInputTicks = Environment.TickCount64;
            _settleTimer.Stop();
            _settleTimer.Start(); // rebake the raster at the settled transform
            e.Pointer.Capture(null);
            InvalidateVisual();
        }

        protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
        {
            var factor = e.Delta.Y > 0 ? 1.15 : 1 / 1.15;
            _lastInputTicks = Environment.TickCount64;
            _settleTimer.Stop();
            _settleTimer.Start();
            var area = new Rect(Bounds.Size);
            var slot = SlotUnder(e.GetPosition(this));
            var zoom = SlotZoom(slot);
            var pan = SlotPan(slot);
            // Anchor: a SHARED multi-slot composition (foam 2D/loft) zooms
            // around the canvas center so both panes grow in place — a cursor
            // anchor makes the OTHER pane slide per notch (the "zooming shrinks
            // / clips the path" report, and the wheel felt like a pan). A
            // single canvas zooms to the cursor like the classic plotter.
            var anchor = SlotCount > 1
                ? new Point(area.Center.X, area.Center.Y)
                : e.GetPosition(this);
            // Keep the anchor's world point fixed while zooming.
            var world = ScreenToWorld(anchor, zoom, pan);
            zoom = (double.IsNaN(zoom) ? 1 : zoom) * factor;
            var anchored = WorldToScreen(world, zoom, pan);
            pan += new Vector(anchor.X - anchored.X, anchor.Y - anchored.Y);
            SetSlotTransform(slot, zoom, pan);
            InvalidateVisual();
            AnnounceZoom(insideRender: false);
        }

        /// <summary>Slot whose viewport contains <paramref name="point"/>
        /// (fallback 0).</summary>
        protected int SlotUnder(Point point)
        {
            var area = new Rect(Bounds.Size);
            for (var s = 0; s < SlotCount; s++)
                if (SlotViewport(s, area).Contains(point))
                    return s;
            return 0;
        }

        protected static Point WorldToScreen(double wx, double wy, double zoom, Vector pan) =>
            PlotterChrome.ToScreen(wx, wy, zoom, pan);

        protected static Point WorldToScreen(Point world, double zoom, Vector pan) =>
            PlotterChrome.ToScreen(world.X, world.Y, zoom, pan);

        protected static Point ScreenToWorld(Point s, double zoom, Vector pan) =>
            new((s.X - pan.X) / zoom, (pan.Y - s.Y) / zoom);

        /// <summary>Row-state pen for a segment row (frontier coloring).</summary>
        protected static IPen PenForRow(ProgramRowState state, IPen neutral) => state switch
        {
            ProgramRowState.Ok => PlotterPens.Done,
            ProgramRowState.Error => PlotterPens.Error,
            ProgramRowState.Sent => PlotterPens.InFlight,
            _ => neutral,
        };

        public override void Render(DrawingContext context)
        {
            var area = new Rect(Bounds.Size);
            if (area.Width < 1 || area.Height < 1 || !Vm.HasGeometry)
            {
                DrawEmpty(context, area);
                return;
            }

            EnsureFit(area);
            EnsureCacheArrays();
            DetectFrontierChanges();

            // The rebake request is PER SLOT, and this pass tracks only WHICH
            // SLOTS have already spent the limiter window.
            //
            // The request used to be a pair of canvas-wide booleans that this
            // loop lifted and cleared ONCE, which made the request effectively
            // single-slot in the opposite direction: slot 0 baked, the flags
            // went false, and slot 1 recomputed needBake as "nothing to do" and
            // blitted its stale raster (a chrome flip half-applied on foam).
            // Lifting the flags per pass fixed that and created two worse bugs,
            // because the flags are still canvas-wide:
            //
            //  - a request raised by a NON-immediate path (DrawSlotFrontier's
            //    overflow branch) was picked up by the NEXT pass, whose slot 0
            //    always found the window open, stamped _lastBakeTicks, and baked
            //    a full world-sized raster; slot 1, microseconds later, was
            //    refused and re-armed, so carriage 2 could never repaint while
            //    carriage 0 rebaked every 500 ms for as long as the flag stood;
            //  - `immediate` short-circuited the limiter for EVERY slot, so one
            //    chrome toggle on foam paid for SlotCount back-to-back bakes
            //    against BakeMinIntervalMs's stated purpose, and skipped the
            //    stamp, leaving the window blind to the work just done.
            //
            // Both are per-slot state now: the flags live in the arrays, only
            // the slot that actually baked clears its own, and a slot may open
            // the limiter window early at most ONCE per window. That last rule
            // needs no per-pass bookkeeping — every bake stamps _lastBakeTicks,
            // so the next slot's CanBakeNow() in the same pass is refused, and
            // one pass cannot chain bakes.
            //
            // The slot viewports are the planes' HOME positions, not bounds:
            // the true-scale composition shares ONE transform, so panning /
            // zooming moves a plane's raster across the screen-center line and
            // it must NOT be trimmed at its quadrant edge (M9). The framework
            // already clips the drawing to the control bounds.
            for (var slot = 0; slot < SlotCount; slot++)
            {
                if (!BlitOrBake(context, slot))
                    DrawSlotPathWorld(context, slot, new BakePens(1)); // pre-first-bake fallback
                DrawSlotFrontier(context, slot);
            }
            // The chrome overlay (world lattice, origin crosses, axis arrows,
            // job rectangles) is a crisp shared overlay drawn last so it never
            // sits under a raster edge.
            for (var slot = 0; slot < SlotCount; slot++)
                DrawSlotChrome(context, slot);
            DrawCoupling(context, area);
            DrawToolGlyph(context, area, ToolGlyphPosition());
        }

        /// <summary>Tool-glyph position source (M9 priority flip): OUTSIDE check
        /// mode the live machine position wins (the controller really moves);
        /// in check mode it never does, so the position comes from the last
        /// acked segment end. The fallback covers the pre-first-ack /
        /// pre-first-report window in either direction.</summary>
        private double[]? ToolGlyphPosition()
        {
            var live = Vm.LivePosition;
            if (Vm.CheckMode)
                return AckedEnd() ?? live;
            return live ?? AckedEnd();
        }

        /// <summary>Status notice for the empty/degenerate cases (centered —
        /// the single canvas may host several quadrants).</summary>
        protected void DrawEmpty(DrawingContext context, Rect area)
        {
            ResetTransforms(); // re-fit when geometry arrives
            InvalidateCaches();
            if (area.Width < 1 || area.Height < 1)
                return;
            var text = new FormattedText(
                Vm.StatusText ?? string.Empty, System.Globalization.CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, Typeface.Default, 11, PlotterPens.EmptyBrush);
            var origin = new Point(
                Math.Max(8, area.Center.X - text.Width / 2),
                Math.Max(8, area.Center.Y - text.Height / 2));
            context.DrawText(text, origin);
        }

        /// <summary>Throttled invalidate for VM change ticks (row/position
        /// storms): at most ~30 invalidations per second reach Render.</summary>
        internal void ThrottledInvalidate() => _throttle.Tick(InvalidateVisual);

        private void EnsureFit(Rect area)
        {
            for (var slot = 0; slot < SlotCount; slot++)
            {
                if (!double.IsNaN(SlotZoom(slot)))
                    continue;
                var viewport = SlotViewport(slot, area);
                FitSlot(slot, viewport);
                // The FIRST fit is a zoom change too: without this the readout
                // stays blank until the user happens to zoom, on a view that is
                // already off 1:1.
                AnnounceZoom(insideRender: true);
            }
        }

        private void ResetTransforms()
        {
            for (var slot = 0; slot < SlotCount; slot++)
                SetSlotTransform(slot, double.NaN, default);
        }

        private void InvalidateCaches()
        {
            for (var i = 0; i < _cacheGeoVersion.Length; i++)
                _cacheGeoVersion[i] = -1;
            for (var i = 0; i < _rebakePending.Length; i++)
            {
                _rebakePending[i] = false;
                _rebakeUrgent[i] = false;
                _bakedFrontierRow[i] = -1;
            }
        }

        private void EnsureCacheArrays()
        {
            if (_cache.Length >= SlotCount)
                return;
            var n = SlotCount;
            Array.Resize(ref _cache, n);
            Array.Resize(ref _cacheZoom, n);
            Array.Resize(ref _cacheGeoVersion, n);
            Array.Resize(ref _rebakePending, n);
            Array.Resize(ref _rebakeUrgent, n);
            Array.Resize(ref _bakedFrontierRow, n);
            Array.Resize(ref _bakeCounts, n);
            for (var i = 0; i < n; i++)
                _cacheGeoVersion[i] = -1;
        }

        /// <summary>Rewind detection (done fell) and run-activity tracking —
        /// cheap O(1) reads of the VM's counters, per frame.</summary>
        private void DetectFrontierChanges()
        {
            var done = Vm.DoneCount;
            if (_lastSeenDone >= 0 && done < _lastSeenDone)
            {
                // Run rewound/reset: the cached colors (done green) are stale —
                // force a full rebake; the glyph returns to the live position.
                InvalidateCaches();
                _glyphRow = -1;
                _ackedEnd = null;
                _completionResyncDone = false;
            }
            _lastSeenDone = done;

            var touched = Vm.TouchedRow;
            if (touched != _lastTouched)
            {
                _lastTouched = touched;
                _runActive = true;
                _lastRunTicks = Environment.TickCount64;
            }
            else if (_runActive && Environment.TickCount64 - _lastRunTicks >= RunActiveWindowMs)
            {
                _runActive = false;
            }

            // Run completed: the controller's planner drains a couple of blocks
            // AFTER the final ack, so the acked end can sit 1–2 segments short
            // of the program's true end. Once the last row has landed, resync
            // the glyph to a full rescan AND resync the drawn frontier (the
            // baked raster's recolor window and the acked scan both follow the
            // touched frontier).
            var total = Vm.TotalCount;
            if (!_completionResyncDone && done > 0 && done == total && touched >= total - 1)
            {
                _completionResyncDone = true;
                _glyphRow = -1;
                _ackedEnd = null;
            }
        }

        /// <summary>The end of the last ACKED segment, advanced incrementally
        /// along the touched frontier (the old per-frame O(n) walk is gone —
        /// it was a full program scan at render cadence). Serves as the glyph
        /// source in check mode (the machine never moves there — see
        /// <see cref="ToolGlyphPosition"/>) and as the fallback to the live
        /// controller position otherwise. Null before the first ack.</summary>
        private double[]? AckedEnd()
        {
            var touched = Vm.TouchedRow;
            if (touched == _glyphRow)
                return _ackedEnd;
            if (touched < _glyphRow)
            {
                _glyphRow = touched;
                _ackedEnd = null;
                return _ackedEnd;
            }

            // Advance through the newly-touched rows only.
            var segments = Vm.SourceSegments;
            var i0 = LowerBoundRow(segments, _glyphRow + 1);
            var i1 = LowerBoundRow(segments, touched + 1);
            for (var i = i0; i < i1 && i < segments.Count; i++)
            {
                var s = segments[i];
                var row = Vm.RowForLine(s.LineNumber);
                if (row >= 0 && Vm.RowState(row) == ProgramRowState.Ok)
                    _ackedEnd = s.To;
            }
            _glyphRow = touched;
            return _ackedEnd;
        }

        /// <summary>First index in <paramref name="segments"/> whose row (via
        /// <see cref="PlotterViewModel.RowForLine"/>) is ≥ <paramref name="target"/>.
        /// Rows are monotonic along segments (file lines and program entries
        /// both ascend), so a binary search is exact.</summary>
        private int LowerBoundRow(IReadOnlyList<ToolpathSegment> segments, int target)
        {
            int lo = 0, hi = segments.Count;
            while (lo < hi)
            {
                var mid = (lo + hi) / 2;
                if (Vm.RowForLine(segments[mid].LineNumber) < target)
                    lo = mid + 1;
                else
                    hi = mid;
            }
            return lo;
        }

        /// <summary>Records that <paramref name="slot"/> owes a rebake, and
        /// guarantees a render will come to take it.
        /// <para>The second half is not bookkeeping, it is the whole reason a
        /// request can be lost. A request raised INSIDE a render — the overflow
        /// branch of <see cref="DrawSlotFrontier"/>, which runs after this
        /// slot's <see cref="BlitOrBake"/> — cannot be served by that pass, and
        /// nothing else is obliged to produce another one: the settle timer runs
        /// only after a gesture, and the VM's throttled ticks stop when the run
        /// does. The same applies to a bake the limiter refused. Both used to
        /// leave the request pending on a raster that never repainted.</para></summary>
        private void RequestRebake(int slot, bool urgent)
        {
            // Sizing lives HERE, at the only place these arrays are written,
            // rather than in the first render. The Chrome setter reaches this
            // before any render has run — ApplyChrome pushes the flags while the
            // canvases are still being built — and a length check is far cheaper
            // than rediscovering that as an IndexOutOfRange on the first push.
            EnsureCacheArrays();

            _rebakePending[slot] = true;
            if (urgent)
                _rebakeUrgent[slot] = true;
            _rebakeRetry.Start();
        }

        /// <summary>Bakes the slot's raster when needed (geometry replaced,
        /// settled zoom changed, recolor window overflow, rebake requested) and
        /// blits it through the current transform. Returns true when the path is
        /// on screen.
        /// <para>The rebake request is read from the PER-SLOT array, not from a
        /// pass-scoped snapshot. Both halves of that decision used to be
        /// canvas-wide and each was wrong in a different direction: one shared
        /// flag pair lifted once per pass made a slot the first bake could leave
        /// stale forever, and a per-pass "urgent" flag let every slot bypass the
        /// chain limiter. Per-slot state fixes both, and
        /// <paramref name="bakedThisPass"/> is what keeps one pass from chaining
        /// several full-world bakes.</para></summary>
        private bool BlitOrBake(DrawingContext ctx, int slot)
        {
            var zoom = SlotZoom(slot);
            var cache = _cache[slot];
            var needBake = cache is null
                || _cacheGeoVersion[slot] != Vm.GeometryVersion
                || _rebakePending[slot]
                || (!Interacting && Math.Abs(zoom - _cacheZoom[slot]) / Math.Max(_cacheZoom[slot], 1e-9) > ZoomTolerance);

            if (needBake)
            {
                // The user asked for this one (a chrome flag flip), so it may
                // open the limiter window early — but ONCE per window. Every
                // other slot falls through to the limiter and keeps its pending
                // bit, so the queue behind the first one drains across passes
                // instead of chaining several full-world bakes back to back.
                var urgent = _rebakeUrgent[slot] && !_urgentSpentInWindow;
                if (urgent || CanBakeNow())
                {
                    if (urgent)
                    {
                        // Stamp on the urgent path too: the old short-circuit
                        // skipped CanBakeNow's side effect, so _lastBakeTicks
                        // was never written for the work just done and the
                        // limiter's window was blind to it. The stamp is also
                        // what caps a pass at one bake — the next slot's
                        // CanBakeNow() sees a window that just opened.
                        _urgentSpentInWindow = true;
                        _lastBakeTicks = Environment.TickCount64;
                    }
                    // Clear ONLY this slot's request. A bake used to be able to
                    // cancel the same pass's remaining slots, which is how a
                    // chrome flip half-applied on foam.
                    _rebakePending[slot] = false;
                    _rebakeUrgent[slot] = false;
                    BakeSlot(slot);
                }
                else
                {
                    // Refused. RequestRebake deliberately does not clear either
                    // bit — that IS the re-arm, because the request is per-slot
                    // and only this slot can clear it. It also books the retry
                    // pass, without which the pending bit would sit on a raster
                    // that never repaints.
                    RequestRebake(slot, urgent: false);
                }
            }
            cache = _cache[slot];
            if (cache is null)
                return false; // first frames before the first bake → direct draw

            var (minX, minY, maxX, maxY) = SlotWorldBounds(slot);
            var tl = WorldToScreen(minX, maxY, zoom, SlotPan(slot));
            var br = WorldToScreen(maxX, minY, zoom, SlotPan(slot));
            ctx.DrawImage(cache,
                new Rect(0, 0, cache.PixelSize.Width, cache.PixelSize.Height),
                new Rect(tl, new Size(br.X - tl.X, br.Y - tl.Y)));
            return true;
        }

        private bool CanBakeNow()
        {
            var now = Environment.TickCount64;
            if (_lastBakeTicks != long.MinValue && now - _lastBakeTicks < BakeMinIntervalMs)
                return false;
            _lastBakeTicks = now;
            // A fresh window, so whatever early-open the previous one bought is
            // spent and the next urgent request may take one again.
            _urgentSpentInWindow = false;
            return true;
        }

        /// <summary>Renders the slot's path (colored by CURRENT row states)
        /// into a world-sized raster at the slot's zoom (clamped), then marks
        /// THAT SLOT's recolor window clean up to the current touched frontier.
        /// <para>It deliberately does NOT touch the rebake request: that belongs
        /// to the slot, and the caller (<see cref="BlitOrBake"/>) clears it after
        /// this returns, so one slot's bake cannot cancel another's.</para></summary>
        private void BakeSlot(int slot)
        {
            var (minX, minY, maxX, maxY) = SlotWorldBounds(slot);
            var spanX = Math.Max(maxX - minX, 1e-6);
            var spanY = Math.Max(maxY - minY, 1e-6);
            var zoom = SlotZoom(slot);
            var zc = Math.Min(Math.Max(zoom, 1e-9), Math.Min(CacheMaxPx / spanX, CacheMaxPx / spanY));
            var w = Math.Max(8, (int)Math.Ceiling(spanX * zc));
            var h = Math.Max(8, (int)Math.Ceiling(spanY * zc));

            var bmp = new RenderTargetBitmap(new PixelSize(w, h), new Vector(96, 96));
            using (var bake = bmp.CreateDrawingContext())
            {
                // world (x, y) → cache px: u = (x - minX)·zc, v = (maxY - y)·zc
                using (bake.PushTransform(Matrix.CreateScale(zc, -zc) *
                                          Matrix.CreateTranslation(-minX * zc, maxY * zc)))
                    DrawSlotPathWorld(bake, slot, new BakePens(zc));
            }

            _cache[slot] = bmp;
            // The VIEWPORT zoom, not zc. These are the same number until a cap
            // binds, and that is exactly when they must not be confused:
            //
            //   zc  = the scale the raster was PHYSICALLY drawn at
            //   zoom = the scale the user is LOOKING at
            //
            // The blit maps the raster's pixel rect onto the slot's screen rect
            // regardless of zc, so zc never affects how the result is displayed
            // — it only bounds raster memory. Storing zc here made the drift
            // check in BlitOrBake compare the viewport zoom against a clamped
            // physical scale, two quantities that are equal by accident. Once
            // CacheMaxPx bound (a zoom deep enough that the world is wider on
            // screen than 2048 px) zc < zoom permanently, every bake re-stored
            // the same clamped value, the ratio stayed above ZoomTolerance
            // forever, and needBake was permanently true: the slot rebaked once
            // per limiter window, forever, with nothing changed. That is the
            // chaining BakeMinIntervalMs exists to prevent — the limiter was
            // rationing the damage to one bake per window instead of preventing
            // it. A program does not have to be large to hit this; the fit zoom
            // scales inversely with the span, so only ZOOM brings the product
            // span x zoom past the cap.
            _cacheZoom[slot] = zoom;
            _cacheGeoVersion[slot] = Vm.GeometryVersion;
            // Per slot, or slot 0's bake closed slot 1's recolor window as a side
            // effect and nothing repainted the second carriage.
            _bakedFrontierRow[slot] = Vm.TouchedRow; // cache colors reflect ≤ this
            _bakeCounts[slot]++;
        }

        /// <summary>Number of bakes this canvas has run. Test seam: the chain
        /// limiter is invisible from pixels, because a refused bake and a bake of
        /// identical pixels look the same on the surface.</summary>
        internal IReadOnlyList<int> SlotBakeCounts => _bakeCounts.Take(SlotCount).ToArray();

        /// <summary>True while any slot still owes a bake, for tests that have to
        /// wait the queue out rather than guess how many limiter windows it
        /// takes.</summary>
        internal bool HasPendingRebake => _rebakePending.Take(SlotCount).Any(p => p);

        /// <summary>Per-frame recolor: the rows acked/sent since the bake draw
        /// as vectors on top of the raster (with a small margin below the bake
        /// frontier for the sent→ok transition). Bounded by
        /// <see cref="OverlayCap"/>; a bigger delta requests a rebake instead
        /// of drawing tens of thousands of segments per frame.</summary>
        private void DrawSlotFrontier(DrawingContext ctx, int slot)
        {
            var touched = Vm.TouchedRow;
            if (touched <= _bakedFrontierRow[slot])
                return;
            var n = SlotSegmentCount(slot);
            if (n == 0)
                return;

            var i0 = LowerBoundSlotRow(slot, Math.Max(0, _bakedFrontierRow[slot] - 3));
            var i1 = LowerBoundSlotRow(slot, touched + 1);
            if (i1 - i0 > OverlayCap)
            {
                // Window too big → refresh the raster. Marked on THIS slot: the
                // request used to be canvas-wide, so slot 0's bake satisfied
                // slot 1's overflow request too and the second carriage could
                // never repaint. Not urgent either — this is a steady-state
                // overflow during a run, not a user gesture, so it waits for the
                // limiter rather than bypassing it. RequestRebake books the
                // retry pass, which this branch genuinely needs: the request is
                // raised after this slot's BlitOrBake, so this pass cannot
                // serve it and no tick is coming.
                RequestRebake(slot, urgent: false);
                return;
            }

            var zoom = SlotZoom(slot);
            var pan = SlotPan(slot);
            var drawn = 0;
            for (var i = i0; i < i1; i++)
            {
                var seg = SlotSegmentAt(slot, i);
                var row = Vm.RowForLine(seg.Line);
                if (row < 0)
                    continue;
                var state = Vm.RowState(row);
                if (state == ProgramRowState.Idle)
                    continue; // comment rows etc. never recolor
                ctx.DrawLine(PenForRow(state, seg.Rapid ? PlotterPens.Rapid : PlotterPens.Feed),
                    WorldToScreen(seg.X0, seg.Y0, zoom, pan),
                    WorldToScreen(seg.X1, seg.Y1, zoom, pan));
                if (++drawn >= OverlayCap)
                {
                    // Ran out of budget mid-window: same overflow, same
                    // non-urgent rebake request as the pre-loop check.
                    RequestRebake(slot, urgent: false);
                    break;
                }
            }
        }

        /// <summary>First slot-segment index whose row is ≥ <paramref name="target"/>
        /// (rows are monotonic along the slot's segments).</summary>
        private int LowerBoundSlotRow(int slot, int target)
        {
            int lo = 0, hi = SlotSegmentCount(slot);
            while (lo < hi)
            {
                var mid = (lo + hi) / 2;
                if (Vm.RowForLine(SlotSegmentAt(slot, mid).Line) < target)
                    lo = mid + 1;
                else
                    hi = mid;
            }
            return lo;
        }

        /// <summary>Row-state pen for a bake (scaled pens).</summary>
        protected static IPen BakePenFor(BakePens pens, bool rapid, ProgramRowState state) =>
            pens.For(rapid, state);
    }

    /// <summary>One straight world-space piece of a slot's path (plane axes or
    /// projected coordinates) plus its source line for frontier coloring.</summary>
    protected readonly record struct WorldSeg(double X0, double Y0, double X1, double Y1, uint Line, bool Rapid);

    /// <summary>Flat canvas: single-plane machines fill the whole surface
    /// with their raw coordinates; foam machines draw both carriage planes as
    /// TRUE-SCALE diagonal tiles of the same canvas — plane 1 top-left, plane
    /// 2 bottom-right, one shared mm scale (no quadrant stretching), each
    /// tile's bounds unioned with the machine origin so its cross stays
    /// in-frame. ONE fit/pan/zoom drives the whole composition, so panning
    /// or zooming never separates the two panes.</summary>
    private sealed class PlaneCanvas : PlotCanvas
    {
        // ONE transform for the whole composed canvas.
        private double _zoom = double.NaN;
        private Vector _pan;

        // Foam: the diagonal tiles' shared world extents (true machine units —
        // both panes share ONE scale so proportions are real) and the per-plane
        // raw→world maps.
        private double _compoW = 1, _compoH = 1;
        private readonly PlaneMap[] _maps = new PlaneMap[2];

        public PlaneCanvas(PlotterViewModel vm) : base(vm) { }

        private bool Foam => Vm.PlaneCount > 1;

        protected override int SlotCount => Foam ? 2 : 1;

        /// <summary>Diagonal quadrant layout: slot 0 top-left, slot 1
        /// bottom-right, slot 2 top-right, slot 3 bottom-left, cycling — both
        /// foam planes stay visible while the shared transform pans/zooms.</summary>
        protected override Rect SlotViewport(int slot, Rect area)
        {
            if (!Foam)
                return area;
            var halfW = area.Width / 2;
            var halfH = area.Height / 2;
            var (col, row) = (slot % 4) switch
            {
                0 => (0, 0), // top-left
                1 => (1, 1), // bottom-right
                2 => (1, 0), // top-right
                _ => (0, 1), // bottom-left
            };
            return new Rect(area.X + col * halfW, area.Y + row * halfH, halfW, halfH);
        }

        protected override double SlotZoom(int slot) => _zoom;

        protected override Vector SlotPan(int slot) => _pan;

        protected override void SetSlotTransform(int slot, double zoom, Vector pan)
        {
            _zoom = zoom;
            _pan = pan;
        }

        private bool PlaneDrawable(int slot)
        {
            if (slot >= Vm.PlaneCount || Vm.Planes[slot].Count == 0 || Vm.Bounds.Length == 0)
                return false;
            var indices = Vm.PlaneAxisIndices(slot);
            if (indices.Length < 2 || Vm.Bounds.Length <= indices[^1])
                return false;
            var (sx, sy) = PaneFeeds(slot);
            return sx >= 0 && sy >= 0;
        }

        /// <summary>The plane slots feeding world X and world Y. This is the ONLY
        /// place the flat view turns a plane mapping into slots — bounds, the baked
        /// segments, the vector overlay, the glyph and the chrome painters all read
        /// these two numbers, so they cannot drift apart.</summary>
        private (int X, int Y) PaneFeeds(int slot) =>
            PlotterPlaneMapper.Feeds(Vm.GeometryFor(slot));

        private (double MinX, double MinY, double MaxX, double MaxY) PlaneBounds(int slot)
        {
            if (!PlaneDrawable(slot))
                return (0, 0, 1, 1);
            var indices = Vm.PlaneAxisIndices(slot);
            var (sx, sy) = PaneFeeds(slot);
            var minX = Vm.Bounds[indices[sx]][0];
            var spanX = Math.Max(Vm.Bounds[indices[sx]][1] - minX, 1e-6);
            var minY = Vm.Bounds[indices[sy]][0];
            var spanY = Math.Max(Vm.Bounds[indices[sy]][1] - minY, 1e-6);
            return (minX, minY, minX + spanX, minY + spanY);
        }

        /// <summary>Builds the shared composition. Foam: each plane becomes a
        /// TRUE-SCALE tile in the shared world — one mm in plane 0 equals one
        /// mm in plane 1 (the ZA band is no longer stretched to fill its
        /// quadrant), and each tile's bounds are UNIONED with the machine origin
        /// so the origin cross always sits in-frame. Tiles sit diagonally (plane
        /// 0 top-left, plane 1 bottom-right) exactly like the old quadrants.
        /// Single-plane: the identity map (raw coordinates ARE the world).</summary>
        private void EnsureLayout(Rect area)
        {
            if (Foam)
            {
                var t0 = PlaneTile(0);
                var t1 = PlaneTile(1);
                // Screen Y flips world Y, so plane 0 (top-left) rides the HIGH
                // world y and plane 1 (bottom-right) the LOW world y — the
                // diagonal matches the normalized layout the user is used to.
                _maps[0] = new PlaneMap(t0.MinX, t0.SpanX, t0.MinY, t0.SpanY, 0, 0, 1, 1, 0, t1.SpanY + FoamTileGap);
                _maps[1] = new PlaneMap(t1.MinX, t1.SpanX, t1.MinY, t1.SpanY, 0, 0, 1, 1, t0.SpanX + FoamTileGap, 0);
                _compoW = t0.SpanX + FoamTileGap + t1.SpanX;
                _compoH = t0.SpanY + FoamTileGap + t1.SpanY;
            }
            else
            {
                // Identity: raw coordinates ARE the world.
                _maps[0] = new PlaneMap(0, 1, 0, 1, 0, 0, 1, 1, 0, 0);
            }
        }

        /// <summary>One plane's true tile in machine units — its fitted bounds
        /// unioned with the machine origin, so the fit always keeps the origin
        /// cross on the visible composition.</summary>
        private (double MinX, double MinY, double SpanX, double SpanY) PlaneTile(int slot)
        {
            var (minX, minY, maxX, maxY) = PlaneBounds(slot);
            var minX0 = Math.Min(minX, 0);
            var maxX0 = Math.Max(maxX, 0);
            var minY0 = Math.Min(minY, 0);
            var maxY0 = Math.Max(maxY, 0);
            return (minX0, minY0, Math.Max(maxX0 - minX0, 1e-6), Math.Max(maxY0 - minY0, 1e-6));
        }

        protected override (double MinX, double MinY, double MaxX, double MaxY) SlotWorldBounds(int slot)
        {
            if (Foam)
            {
                var m = _maps[slot];
                return (m.Ox, m.Oy, m.Ox + m.SpanX, m.Oy + m.SpanY);
            }
            return PlaneBounds(0);
        }

        protected override void FitSlot(int slot, Rect viewport)
        {
            var area = new Rect(Bounds.Size);
            EnsureLayout(area);
            if (Foam)
            {
                // Fit the whole diagonal 2-tile composition into the canvas.
                _zoom = Math.Min((area.Width - FoamFitMargin) / _compoW, (area.Height - FoamFitMargin) / _compoH);
                _pan = new Vector(area.Center.X - _zoom * _compoW / 2, area.Center.Y + _zoom * _compoH / 2);
            }
            else
            {
                var (minX, minY, maxX, maxY) = PlaneBounds(0);
                var spanX = Math.Max(maxX - minX, 1e-6);
                var spanY = Math.Max(maxY - minY, 1e-6);
                _zoom = Math.Min((area.Width - 24) / spanX, (area.Height - 24) / spanY);
                _pan = new Vector(
                    area.Center.X - _zoom * (minX + spanX / 2),
                    area.Center.Y + _zoom * (minY + spanY / 2));
            }
        }

        protected override int SlotSegmentCount(int slot) =>
            PlaneDrawable(slot) ? Vm.Planes[slot].Count : 0;

        protected override WorldSeg SlotSegmentAt(int slot, int index)
        {
            var s = Vm.Planes[slot][index];
            var m = _maps[Foam ? slot : 0];
            var (sx, sy) = PaneFeeds(slot);
            return new WorldSeg(m.Wx(s.From[sx]), m.Wy(s.From[sy]),
                m.Wx(s.To[sx]), m.Wy(s.To[sy]), s.Line, s.Rapid);
        }

        protected override void DrawSlotPathWorld(DrawingContext ctx, int slot, BakePens pens)
        {
            if (!PlaneDrawable(slot))
                return;
            var segments = Vm.Planes[slot];
            var m = _maps[Foam ? slot : 0];
            var (sx, sy) = PaneFeeds(slot);
            for (var i = 0; i < segments.Count; i++)
            {
                var s = segments[i];
                var pen = BakePenFor(pens, s.Rapid, RowStateFor(s.Line));
                ctx.DrawLine(pen,
                    new Point(m.Wx(s.From[sx]), m.Wy(s.From[sy])),
                    new Point(m.Wx(s.To[sx]), m.Wy(s.To[sy])));
            }
        }

        private ProgramRowState RowStateFor(uint line)
        {
            if (!Chrome.RunState)
                return ProgramRowState.Idle;
            var row = Vm.RowForLine(line);
            return row >= 0 ? Vm.RowState(row) : ProgramRowState.Idle;
        }

        protected override void DrawSlotChrome(DrawingContext ctx, int slot)
        {
            if (!PlaneDrawable(slot))
                return;
            var m = _maps[Foam ? slot : 0];
            var bounds = PlaneBounds(slot);
            // The chrome clips to the whole canvas, NOT to SlotViewport. A
            // foam slot's viewport is its quadrant, but the fit centres the
            // whole diagonal two-tile composition, so whenever plane 0's Y
            // extent exceeds plane 1's by more than FoamTileGap — the normal
            // XY-vs-XZ case — plane 0's world area reaches past its quadrant
            // into the other tile's half of the canvas. Clipping the lattice to
            // the quadrant sliced it with a hard horizontal edge at the canvas
            // midline while the same tile's envelope and cross, which take no
            // clip, kept drawing past it. This is the same trimming the file's
            // own rule at BlitOrBake's call site forbids for the raster; a
            // non-foam slot's viewport is the whole control, so for a mill the
            // two rects are identical and nothing here changes.
            var clip = new Rect(Bounds.Size);

            if (Chrome.Grid)
                PlotterFlatChrome.DrawGrid(ctx, m, bounds, _zoom, _pan, clip);
            PlotterFlatChrome.DrawAxes(ctx, m, bounds, _zoom, _pan, clip);
            if (Chrome.Envelope)
                PlotterFlatChrome.DrawEnvelope(ctx, m, bounds, _zoom, _pan);
            // The origin cross paints LAST, on top of everything. This order is
            // right but it is NOT what makes the cross visible: G-code starts at
            // the machine origin, so the cross sits exactly on the job
            // envelope's corner and under the axis shafts, and the first draft
            // of this plan drew Origin second, so the new chrome painted over
            // it. Reordering recovered 0 of the missing pixels, because a cross
            // blended against amber is still tinted whatever order it is in. The
            // fix that works is the 2 px stroke in
            // PlotterFlatChrome.DrawOrigin. Keeping the cross on top is still
            // correct (nothing painted after it before this task), so leave the
            // order alone.
            if (Chrome.Origin)
                PlotterFlatChrome.DrawOrigin(ctx, m, _zoom, _pan);
        }

        protected override void DrawToolGlyph(DrawingContext ctx, Rect area, double[]? pos)
        {
            if (pos is null || pos.Length < 2)
                return;
            if (Foam)
            {
                // Foam: the wire glyph spans the two carriage quadrants; each
                // carriage point maps through its own plane's axis indices into
                // the shared composition.
                if (!PlaneDrawable(0))
                    return;
                var idx0 = Vm.PlaneAxisIndices(0);
                var (x0, y0) = PaneFeeds(0);
                if (pos.Length <= idx0[^1])
                    return;
                var m0 = _maps[0];
                var p0 = WorldToScreen(m0.Wx(pos[idx0[x0]]), m0.Wy(pos[idx0[y0]]), _zoom, _pan);
                if (!PlaneDrawable(1))
                {
                    ctx.DrawEllipse(PlotterPens.ToolBrush, PlotterPens.Tool, p0, 4, 4);
                    return;
                }
                var idx1 = Vm.PlaneAxisIndices(1);
                var (x1, y1) = PaneFeeds(1);
                if (pos.Length <= idx1[^1])
                    return;
                var m1 = _maps[1];
                var p1 = WorldToScreen(m1.Wx(pos[idx1[x1]]), m1.Wy(pos[idx1[y1]]), _zoom, _pan);
                ctx.DrawLine(PlotterPens.Tool, p0, p1);
                ctx.DrawEllipse(PlotterPens.ToolBrush, null, p0, 3, 3);
                ctx.DrawEllipse(PlotterPens.ToolBrush, null, p1, 3, 3);
            }
            else
            {
                if (!PlaneDrawable(0))
                    return;
                var indices = Vm.PlaneAxisIndices(0);
                var (sx, sy) = PaneFeeds(0);
                if (pos.Length <= indices[^1])
                    return;
                var m = _maps[0];
                var p = WorldToScreen(m.Wx(pos[indices[sx]]), m.Wy(pos[indices[sy]]), _zoom, _pan);
                ctx.DrawEllipse(PlotterPens.ToolBrush, PlotterPens.Tool, p, 4, 4);
            }
        }

        /// <summary>Tile count of the flat composition (2 for foam, 1 otherwise).</summary>
        internal int SlotCountForTest => SlotCount;

        /// <summary>Whether the flat pane for <paramref name="slot"/> has a usable
        /// 2D extent — it must feed both world X and world Y.</summary>
        internal bool IsSlotDrawableForTest(int slot) => PlaneDrawable(slot);
    }

    /// <summary>
    /// Isometric canvas. Mill: the classic Z-up iso projection of the whole
    /// toolpath in one slot. Foam: the user's preferred "2D + loft" — the two
    /// carriage planes draw FLAT as TRUE-SCALE diagonal tiles (one shared mm
    /// scale, origin cross unioned into each tile's bounds), driven by a
    /// single fit/pan/zoom, with a semi-transparent coupling wire between the
    /// two carriages per segment — the two panes resolve into one pane with a
    /// diagonal offset, instead of the confusing stacked loft.
    /// </summary>
    private sealed class IsoCanvas : PlotCanvas
    {
        // ONE transform for the whole composed canvas (mill: the projected
        // world; foam loft: the TRUE-SCALE diagonal tile composition).
        private double _zoom = double.NaN;
        private Vector _pan;
        private double _compoW = 1, _compoH = 1;
        private readonly PlaneMap[] _maps = new PlaneMap[2];

        public IsoCanvas(PlotterViewModel vm) : base(vm) { }

        /// <summary>True for the two-carriage view: foam machine with a
        /// program that moves all four axes (both carriages present).</summary>
        private bool Loft => Vm.PlaneCount > 1 && Vm.SourceSegments.Count > 0
            && Vm.SourceSegments[0].From.Length >= 4;

        protected override int SlotCount => Loft ? 2 : 1;

        protected override Rect SlotViewport(int slot, Rect area)
        {
            if (!Loft)
                return area;
            var halfW = area.Width / 2;
            var halfH = area.Height / 2;
            return slot == 0
                ? new Rect(area.X, area.Y, halfW, halfH)            // top-left
                : new Rect(area.X + halfW, area.Y + halfH, halfW, halfH); // bottom-right
        }

        protected override double SlotZoom(int slot) => _zoom;

        protected override Vector SlotPan(int slot) => _pan;

        protected override void SetSlotTransform(int slot, double zoom, Vector pan)
        {
            _zoom = zoom;
            _pan = pan;
        }

        private bool PlaneDrawable(int slot) =>
            Loft && slot < Vm.Planes.Count && Vm.Planes[slot].Count > 0
            && Vm.Bounds.Length > Vm.PlaneAxisIndices(slot)[^1];

        private (double MinX, double MinY, double MaxX, double MaxY) PlaneBounds(int slot)
        {
            if (!PlaneDrawable(slot))
                return (0, 0, 1, 1);
            var indices = Vm.PlaneAxisIndices(slot);
            var minX = Vm.Bounds[indices[0]][0];
            var spanX = Math.Max(Vm.Bounds[indices[0]][1] - minX, 1e-6);
            var minY = Vm.Bounds[indices[1]][0];
            var spanY = Math.Max(Vm.Bounds[indices[1]][1] - minY, 1e-6);
            return (minX, minY, minX + spanX, minY + spanY);
        }

        private (double MinX, double MinY, double MaxX, double MaxY) ProjectedBounds()
        {
            double minX = 0, maxX = 0, minY = 0, maxY = 0;
            var first = true;
            void Add((double X, double Y) p)
            {
                if (first)
                {
                    minX = maxX = p.X;
                    minY = maxY = p.Y;
                    first = false;
                    return;
                }
                if (p.X < minX) minX = p.X;
                if (p.X > maxX) maxX = p.X;
                if (p.Y < minY) minY = p.Y;
                if (p.Y > maxY) maxY = p.Y;
            }

            foreach (var s in Vm.SourceSegments)
            {
                var zFrom = s.From.Length > 2 ? s.From[2] : 0;
                var zTo = s.To.Length > 2 ? s.To[2] : 0;
                Add(PlotterProjection.Project(s.From[0], s.From[1], zFrom));
                Add(PlotterProjection.Project(s.To[0], s.To[1], zTo));
            }
            return (minX, minY, maxX, maxY);
        }

        /// <summary>Builds the shared composition. Loft: each plane becomes a
        /// TRUE-SCALE tile in the shared world — one mm in plane 0 equals one
        /// mm in plane 1 (the ZA band is no longer stretched to fill its
        /// quadrant), each tile's bounds UNIONED with the machine origin so the
        /// origin cross always sits in-frame. Tiles sit diagonally (plane 0
        /// top-left, plane 1 bottom-right). Mill: the identity map over
        /// projected coordinates.</summary>
        private void EnsureLayout(Rect area)
        {
            if (Loft)
            {
                var t0 = PlaneTile(0);
                var t1 = PlaneTile(1);
                // Screen Y flips world Y, so plane 0 (top-left) rides the HIGH
                // world y and plane 1 (bottom-right) the LOW world y — the
                // diagonal matches the normalized layout the user is used to.
                _maps[0] = new PlaneMap(t0.MinX, t0.SpanX, t0.MinY, t0.SpanY, 0, 0, 1, 1, 0, t1.SpanY + FoamTileGap);
                _maps[1] = new PlaneMap(t1.MinX, t1.SpanX, t1.MinY, t1.SpanY, 0, 0, 1, 1, t0.SpanX + FoamTileGap, 0);
                _compoW = t0.SpanX + FoamTileGap + t1.SpanX;
                _compoH = t0.SpanY + FoamTileGap + t1.SpanY;
            }
            else
            {
                // Identity: projected coordinates ARE the world.
                _maps[0] = new PlaneMap(0, 1, 0, 1, 0, 0, 1, 1, 0, 0);
            }
        }

        /// <summary>One plane's true tile in machine units — its fitted bounds
        /// unioned with the machine origin, so the fit always keeps the origin
        /// cross on the visible composition.</summary>
        private (double MinX, double MinY, double SpanX, double SpanY) PlaneTile(int slot)
        {
            var (minX, minY, maxX, maxY) = PlaneBounds(slot);
            var minX0 = Math.Min(minX, 0);
            var maxX0 = Math.Max(maxX, 0);
            var minY0 = Math.Min(minY, 0);
            var maxY0 = Math.Max(maxY, 0);
            return (minX0, minY0, Math.Max(maxX0 - minX0, 1e-6), Math.Max(maxY0 - minY0, 1e-6));
        }

        protected override (double MinX, double MinY, double MaxX, double MaxY) SlotWorldBounds(int slot)
        {
            if (Loft)
            {
                var m = _maps[slot];
                return (m.Ox, m.Oy, m.Ox + m.SpanX, m.Oy + m.SpanY);
            }
            return ProjectedBounds();
        }

        protected override void FitSlot(int slot, Rect viewport)
        {
            var area = new Rect(Bounds.Size);
            EnsureLayout(area);
            if (Loft)
            {
                // Fit the whole diagonal 2-tile composition into the canvas.
                _zoom = Math.Min((area.Width - FoamFitMargin) / _compoW, (area.Height - FoamFitMargin) / _compoH);
                _pan = new Vector(area.Center.X - _zoom * _compoW / 2, area.Center.Y + _zoom * _compoH / 2);
            }
            else
            {
                var (minX, minY, maxX, maxY) = ProjectedBounds();
                var spanX = Math.Max(maxX - minX, 1e-6);
                var spanY = Math.Max(maxY - minY, 1e-6);
                _zoom = Math.Min((area.Width - 24) / spanX, (area.Height - 24) / spanY);
                _pan = new Vector(
                    area.Center.X - _zoom * (minX + spanX / 2),
                    area.Center.Y + _zoom * (minY + spanY / 2));
            }
        }

        protected override int SlotSegmentCount(int slot)
        {
            if (!Loft)
                return Vm.SourceSegments.Count;
            return PlaneDrawable(slot) ? Vm.Planes[slot].Count : 0;
        }

        protected override WorldSeg SlotSegmentAt(int slot, int index)
        {
            if (!Loft)
            {
                var s = Vm.SourceSegments[index];
                var zFrom = s.From.Length > 2 ? s.From[2] : 0;
                var zTo = s.To.Length > 2 ? s.To[2] : 0;
                var a = PlotterProjection.Project(s.From[0], s.From[1], zFrom);
                var b = PlotterProjection.Project(s.To[0], s.To[1], zTo);
                return new WorldSeg(a.X, a.Y, b.X, b.Y, s.LineNumber, s.Rapid);
            }
            var p = Vm.Planes[slot][index];
            var m = _maps[slot];
            return new WorldSeg(m.Wx(p.From[0]), m.Wy(p.From[1]),
                m.Wx(p.To[0]), m.Wy(p.To[1]), p.Line, p.Rapid);
        }

        protected override void DrawSlotPathWorld(DrawingContext ctx, int slot, BakePens pens)
        {
            if (!Loft)
            {
                var segments = Vm.SourceSegments;
                for (var i = 0; i < segments.Count; i++)
                {
                    var s = segments[i];
                    var zFrom = s.From.Length > 2 ? s.From[2] : 0;
                    var zTo = s.To.Length > 2 ? s.To[2] : 0;
                    var a = PlotterProjection.Project(s.From[0], s.From[1], zFrom);
                    var b = PlotterProjection.Project(s.To[0], s.To[1], zTo);
                    var pen = BakePenFor(pens, s.Rapid, RowStateFor(s.LineNumber));
                    ctx.DrawLine(pen, new Point(a.X, a.Y), new Point(b.X, b.Y));
                }
                return;
            }

            if (!PlaneDrawable(slot))
                return;
            var segments2 = Vm.Planes[slot];
            var m = _maps[slot];
            for (var i = 0; i < segments2.Count; i++)
            {
                var s = segments2[i];
                var pen = BakePenFor(pens, s.Rapid, RowStateFor(s.Line));
                ctx.DrawLine(pen,
                    new Point(m.Wx(s.From[0]), m.Wy(s.From[1])),
                    new Point(m.Wx(s.To[0]), m.Wy(s.To[1])));
            }
        }

        private ProgramRowState RowStateFor(uint line)
        {
            if (!Chrome.RunState)
                return ProgramRowState.Idle;
            var row = Vm.RowForLine(line);
            return row >= 0 ? Vm.RowState(row) : ProgramRowState.Idle;
        }

        /// <summary>The iso view's chrome: the world lattice, the origin cross,
        /// the three world axis arrows and the job box, drawn last over the
        /// projected composition.
        /// <para>The mill's composition IS the projected world, so the world XY
        /// floor, the arrows and the box are drawn THROUGH
        /// <c>PlotterProjection.Project</c> — a world grid line is straight in
        /// 3D, so it is one projected segment and the lattice recedes correctly
        /// when the view zooms. The M9 note that stood here said the grid framed
        /// each pane into a "box" and stacked a faint lattice over the canvas;
        /// that objection was about the LOFT, whose panes are not a projection
        /// at all (see the branch below), and for the mill the flat view now
        /// draws the same world lattice on by default, so the two views agree
        /// and switching between them no longer makes the chrome appear and
        /// vanish. The lattice is opt-out through <see cref="Chrome"/>.</para></summary>
        protected override void DrawSlotChrome(DrawingContext ctx, int slot)
        {
            var zoom = _zoom;
            var pan = _pan;
            if (double.IsNaN(zoom))
                return;

            if (Loft)
            {
                // The loft's two panes are NOT a projection: they are FLAT
                // true-scale tiles sharing one composition, drawn in each
                // plane's own role order. A projected lattice over them would be
                // a foreshortened 30-degree grid whose mm are NOT the tiles' mm —
                // a wrong measurement, not a style question — and the
                // extent-derived chrome for exactly these two tiles is already
                // drawn by the flat view, which lays out the same diagonal
                // composition through the flat painter. So the loft keeps what
                // it has always drawn here: one machine-origin cross per
                // carriage plane, through that plane's own map. Adding the
                // lattice to these panes is a flat-painter question (it also has
                // to settle the role-vs-world feed difference the two canvases
                // still have), not this task's.
                if (!PlaneDrawable(slot))
                    return;
                if (Chrome.Origin)
                {
                    var m = _maps[slot];
                    var o = WorldToScreen(m.Wx(0), m.Wy(0), zoom, pan);
                    ctx.DrawLine(PlotterPens.Origin, new Point(o.X - 6, o.Y), new Point(o.X + 6, o.Y));
                    ctx.DrawLine(PlotterPens.Origin, new Point(o.X, o.Y - 6), new Point(o.X, o.Y + 6));
                }
                return;
            }

            // The chrome clips to the whole canvas, NOT to SlotViewport — same
            // rule as the flat pane, and for the same reason: the iso view's
            // slot viewports are a pane's HOME position, not its bounds, and the
            // framework already clips the drawing to the control. (The mill's
            // single slot makes the two rects the same thing today; the clip is
            // the canvas area so a future multi-slot iso pane cannot slice the
            // lattice at a quadrant edge.)
            var clip = new Rect(Bounds.Size);
            // Everything the lattice, the arrows and the box have in common is
            // the drawn plane's world XY extent, so it is resolved ONCE here. A
            // plane that feeds no world X or no world Y has no extent to
            // measure: the extent-derived chrome is skipped rather than wrapped
            // around a default. The mill's built-in mapping is a permutation of
            // world X/Y/Z and so always has a footprint; this guard is for a
            // mapping that does not, which is a legal (and user-settable) plane
            // mapping elsewhere in this file.
            var hasFootprint = PlotterPlaneMapper.TryWorldFootprint(
                Vm.GeometryFor(0), Vm.PlaneAxisIndices(0), Vm.Bounds, out var footprint);

            if (hasFootprint && Chrome.Grid)
                PlotterIsoChrome.DrawGrid(ctx, footprint, zoom, pan, clip);
            // The cross is the machine ORIGIN, not the extent, so it paints
            // even where there is no footprint — and at the pre-existing 1 px
            // PlotterPens.Origin, unlike the flat panes' deliberate 2 px.
            if (Chrome.Origin)
                PlotterIsoChrome.DrawOrigin(ctx, zoom, pan);
            if (!hasFootprint)
                return;

            // A quarter of the pane's own span, as in the flat painter: long
            // enough to name the direction, short enough to stay off the path.
            var reach = Math.Max(Math.Max(footprint.MaxX - footprint.MinX,
                footprint.MaxY - footprint.MinY), 1e-6) * 0.25;
            PlotterIsoChrome.DrawAxes(ctx, zoom, pan, clip, reach);
            if (Chrome.Envelope)
            {
                var (minZ, maxZ) = JobZRange();
                PlotterIsoChrome.DrawEnvelope(ctx, footprint, minZ, maxZ, zoom, pan, clip);
            }
        }

        /// <summary>The world Z span of the job box: the machine axis that feeds
        /// world Z, unioned with the origin. A plane that does not feed world Z
        /// gets a flat box on Z = 0, because that is the extent it really has.
        /// One rule, no special case, and it is what the mill (which does feed
        /// world Z) wants.</summary>
        private (double Min, double Max) JobZRange()
        {
            if (!PlotterPlaneMapper.TryWorldSpan(
                Vm.GeometryFor(0), Vm.PlaneAxisIndices(0), Vm.Bounds, 2, out var z))
                return (0, 0);
            return (Math.Min(z.Min, 0), Math.Max(z.Max, 0));
        }

        /// <summary>The foam loft's coupling mesh: for each source segment, a
        /// semi-transparent wire from the carriage-1 point (through plane 0's
        /// map into the shared composition) to the carriage-2 point (through
        /// plane 1's map). Decimated during interaction and while a run
        /// advances.</summary>
        protected override void DrawCoupling(DrawingContext ctx, Rect area)
        {
            if (!Loft)
                return;
            var segments = Vm.SourceSegments;
            var idx0 = Vm.PlaneAxisIndices(0);
            var idx1 = Vm.PlaneAxisIndices(1);
            if (segments.Count == 0 || !PlaneDrawable(0) || !PlaneDrawable(1)
                || segments[0].From.Length <= idx1[^1])
                return;
            var step = WireStep(segments.Count);
            var m0 = _maps[0];
            var m1 = _maps[1];
            for (var i = 0; i < segments.Count; i += step)
            {
                var s = segments[i];
                var p0 = WorldToScreen(m0.Wx(s.From[idx0[0]]), m0.Wy(s.From[idx0[1]]), _zoom, _pan);
                var p1 = WorldToScreen(m1.Wx(s.From[idx1[0]]), m1.Wy(s.From[idx1[1]]), _zoom, _pan);
                ctx.DrawLine(PlotterPens.Wire, p0, p1);
            }
        }

        protected override void DrawToolGlyph(DrawingContext ctx, Rect area, double[]? pos)
        {
            if (pos is null || pos.Length < 2)
                return;
            if (Loft && pos.Length >= 4 && PlaneDrawable(0) && PlaneDrawable(1))
            {
                var idx0 = Vm.PlaneAxisIndices(0);
                var idx1 = Vm.PlaneAxisIndices(1);
                var m0 = _maps[0];
                var m1 = _maps[1];
                // The live coupling wire between the two carriages.
                var p0 = WorldToScreen(m0.Wx(pos[idx0[0]]), m0.Wy(pos[idx0[1]]), _zoom, _pan);
                var p1 = WorldToScreen(m1.Wx(pos[idx1[0]]), m1.Wy(pos[idx1[1]]), _zoom, _pan);
                ctx.DrawLine(PlotterPens.Tool, p0, p1);
                ctx.DrawEllipse(PlotterPens.ToolBrush, null, p0, 3, 3);
                ctx.DrawEllipse(PlotterPens.ToolBrush, null, p1, 3, 3);
            }
            else
            {
                var z = pos.Length > 2 ? pos[2] : 0;
                var (px, py) = PlotterProjection.Project(pos[0], pos[1], z);
                var p = WorldToScreen(px, py, _zoom, _pan);
                ctx.DrawEllipse(PlotterPens.ToolBrush, PlotterPens.Tool, p, 4, 4);
            }
        }
    }
}