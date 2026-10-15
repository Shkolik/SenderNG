
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

using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using CNC.Core.Abstractions;
using CNC.Core.Services;
using CNC.GCode;
using Microsoft.Extensions.DependencyInjection;
using SenderNG.Localization;
using SenderNG.Modules.Fixed;
using SenderNG.Services;
using SenderNG.ViewModels;
using SenderNG.Core.Commands;
using SenderNG.Core.Modules;
using SenderNG.Core.Services;

namespace SenderNG.ViewModels;

/// <summary>
/// One LED in the signals strip: a colored dot above a letter, lit when the
/// controller's current status report raises the tracked <see cref="Flag"/>.
/// Mirrors ioSender's SignalControl (axis/limit LEDs on the left, machine
/// control signals — probe, safety door, e-stop, hold, cycle start, reset —
/// to the right). <see cref="ToolTip"/> names the signal for mouseover.
/// </summary>
public sealed class SignalIndicator : ViewModelBase
{
    private bool _isActive;
    public SignalIndicator(string letter, CNC.Core.Signals flag, string toolTip)
    {
        Letter = letter;
        Flag = flag;
        ToolTip = toolTip;
    }

    public string Letter { get; }
    public CNC.Core.Signals Flag { get; }
    public string ToolTip { get; }
    public bool IsActive
    {
        get => _isActive;
        set => SetField(ref _isActive, value);
    }
}

public sealed record CenterTab(IModule Module, object? View, string Name);

public sealed record ModuleView(IModule Module, object? View, string Name);

public sealed record NamedModule(IModule Module, string Name);

public sealed record MenuEntry(string Header, ICommand Command, object? Parameter);

public class MainViewModel : ViewModelBase, IDisposable
{
    private readonly IServiceProvider _services;
    private readonly SettingsService _settingsService;
    private readonly IComPortService? _comPorts;
    private readonly IFileDialogService? _fileDialogs;
    private readonly IDialogService? _dialogs;
    private readonly List<IGCodeFileHandler> _fileHandlers;
    private readonly Func<IModule, object>? _viewFactory;
    private readonly Dictionary<IModule, object> _moduleViews = new();
    /// <summary>Last sequence <see cref="CenterTabs"/> handed out. The getter
    /// rebuilds every read and returns this instance while the result still
    /// value-equals it (module ref, cached view ref, localized name), so a
    /// settings-save or locale-change notification with no visible effect does
    /// not give the TabControl a new sequence to rebind containers for — that
    /// re-parenting tore the plotter's GL scene down and rebuilt it against
    /// grid-default bounds (checkpoint-3 "zoom jump to default").</summary>
    private CenterTab[]? _centerTabsCache;
    private bool _isDemo;
    private IGrblService _grbl;
    private readonly CNC.Core.GrblInfoState _info;
    private string _status = string.Empty;
    private bool _isConnected;
    private RealGrblService? _networkService;
    private EventHandler? _connectionChangedHandler;
    private IModule? _selectedModule;
    private object? _currentModuleView;
    private string _mdiInput = string.Empty;
    private int _mdiHistoryIndex;
    private string _machineState = "Idle";
    private string? _currentFileName;
    private IBrush _stateBrush = new SolidColorBrush(Color.Parse("#ffe8a0"));
    private IBrush _stateForeground = Brushes.Black;
    private IDisposable? _stateSubscription;
    private IDisposable? _linesSubscription;
    private IDisposable? _workParametersSubscription;
    private WorkParameters? _latestWorkParameters;
    private int _signalAxisCount = -1;
    private CNC.Core.Signals _signalOptionalSignals = CNC.Core.Signals.Off;
    private readonly ProgramViewModel? _program;
    private readonly SenderNG.Core.Localization.LocalizationService? _localization;
    private readonly System.Diagnostics.Stopwatch _runStopwatch = new();
    private bool _isRunning;
    private bool _disposed;
    private readonly Func<WorkParameters?>? _workParametersProvider;
    private readonly StatusSink? _statusSink;
    private readonly Action<string>? _statusSinkHandler;
    /// <summary>The machine-kinematics seam (registered by <c>HostSetup</c>). Null in
    /// minimal test hosts, where <see cref="CurrentKinematics"/> resolves the active
    /// profile directly.</summary>
    private readonly IMachineKinematicsProvider? _kinematics;

    public MainViewModel(
        IGrblService grbl,
        SettingsService settings,
        IServiceProvider services,
        IEnumerable<IModule>? modules = null,
        IComPortService? comPorts = null,
        Func<IModule, object>? viewFactory = null,
        IFileDialogService? fileDialogs = null,
        IDialogService? dialogs = null,
        StatusSink? statusSink = null,
        CNC.Core.GrblInfoState? info = null)
    {
        ArgumentNullException.ThrowIfNull(grbl);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(services);

        _info = info ?? CNC.Core.GrblInfo.Current;
        _grbl = grbl;
        _settingsService = settings;
        SettingsService = settings;
        _services = services;
        _kinematics = services.GetService<IMachineKinematicsProvider>();
        _comPorts = comPorts;
        _fileDialogs = fileDialogs;
        _dialogs = dialogs;
        _viewFactory = viewFactory;
        // IErrorReporter.Status(text) → status bar (plugins report, the host
        // surfaces — see ModuleErrorReporter).
        if (statusSink is not null)
        {
            _statusSink = statusSink;
            _statusSinkHandler = message => UiThread.Post(() => Status = message);
            statusSink.Sink = _statusSinkHandler;
        }
        _localization = services.GetService<SenderNG.Core.Localization.LocalizationService>();
        if (_localization is not null)
            _localization.Changed += OnLocaleChanged;
        _isDemo = ResolveInner(grbl) is DemoGrblService;
        _isConnected = grbl.IsConnected;

        // Tolerant resolve: unit tests construct MainViewModel with a minimal provider.
        var droVm = _services.GetService<Modules.Fixed.DroViewModel>();
        DroContent = new DroView { DataContext = droVm };

        foreach (var module in modules ?? Enumerable.Empty<IModule>())
            Modules.Add(module);
        // File-format handlers are modules implementing IGCodeFileHandler — the
        // app knows only the contract; no handler module, no extra filters.
        _fileHandlers = Modules.OfType<IGCodeFileHandler>().ToList();

        Modules.CollectionChanged += OnModulesChanged;

        // Seed dynamic signals from initial state (DemoAxes) so UI shows X0 Y0 without connecting
        // Use settings DemoAxes as fallback; live State subscription will correct it immediately if BehaviourSubject replays
        var seedAxes = _settingsService.Load().DemoAxes;
        seedAxes = Math.Clamp(seedAxes, 1, _info.AxisLetters.Length);
        RebuildSignalStrip(seedAxes, _grbl.Capabilities.OptionalSignals);

        // Recent files persist across sessions via global.json.
        foreach (var f in _settingsService.Global.RecentFiles)
            if (!string.IsNullOrWhiteSpace(f))
                RecentFiles.Add(f);

        ConnectCommand = new DelegateCommand(_ => _ = ShowConnectDialogAsync(), _ => !IsConnected && !IsDemo);
        DisconnectCommand = new DelegateCommand(_ => Disconnect(), _ => IsConnected || IsDemo);
        SelectModuleCommand = new DelegateCommand(p =>
        {
            if (p is IModule m && SelectedModule == m)
                SelectedModule = null; // clicking the open flyout button toggles it closed
            else
                SelectedModule = p as IModule;
        });
        SendMdiCommand = new DelegateCommand(_ => _ = SendMdiAsync(), _ => !string.IsNullOrWhiteSpace(MdiInput));
        OpenFileCommand = new DelegateCommand(_ => _ = OpenFileAsync());
        SaveFileCommand = new DelegateCommand(_ => _ = SaveFileAsync(), _ => HasProgram && !SessionActive);
        CloseFileCommand = new DelegateCommand(_ => CloseFile(), _ => HasProgram && !SessionActive);
        RotateProgramCommand = new DelegateCommand(_ => _ = RotateProgramAsync(), _ => HasProgram && !SessionActive);
        ArcsToLinesProgramCommand = new DelegateCommand(_ => _ = ArcsToLinesProgramAsync(), _ => HasProgram && !SessionActive);
        CompressProgramCommand = new DelegateCommand(_ => _ = CompressProgramAsync(), _ => HasProgram && !SessionActive);
        WrapProgramCommand = new DelegateCommand(_ => _ = WrapProgramAsync(CNC.Core.ViewModels.WrapDirection.Wrap), _ => HasProgram && !SessionActive);
        UnwrapProgramCommand = new DelegateCommand(_ => _ = WrapProgramAsync(CNC.Core.ViewModels.WrapDirection.Unwrap), _ => HasProgram && !SessionActive);
        SelectRecentCommand = new DelegateCommand(p => _ = LoadGCodeFileAsync(p as string));
        // Cycle bar — the only run controls (the program pane shows no transport
        // bar). The machine runs at most one streaming session at a time, so the
        // buttons are gated on the session state, not a parallel machine-level
        // state machine: Start = run (idle program) / resume (paused session),
        // Hold = pause the session, Stop = controlled stop while a run is active
        // (Run or Hold) — grblHAL 0x19, classic reset 0x18 — ending the session so
        // it never resumes. Rewind = local rewind. Without a loaded program they fall back to raw realtime bytes
        // (classic 0x7E/0x21/0x18, grblHAL compatible) for MDI runs; the remaining
        // machine-state checks only cover that no-program fallback. Buttons stay
        // visible — enablement (CanExecute) is what changes.
        CycleStartCommand = new DelegateCommand(_ => _ = CycleStartAsync(),
            _ => IsConnected && (SessionPaused || (HasProgram && !SessionActive)
                || (!HasProgram && (IsMachineState("Run") || IsMachineState("Hold")))));
        FeedHoldCommand = new DelegateCommand(_ => FeedHold(),
            _ => (SessionActive && !SessionPaused) || (_program?.IsRunning != true && IsMachineState("Run")));
        StopCommand = new DelegateCommand(_ => StopMachine(),
            _ => IsMachineState("Hold") || IsMachineState("Run") || SessionActive);
        RewindCommand = new DelegateCommand(_ => Rewind(), _ => HasProgram && !SessionActive);
        // Status bar buttons (ioSender semantics): Home -> $H, Unlock -> $X, Reset -> realtime 0x18.
        HomeCommand = new DelegateCommand(_ => _ = SendLineAsync("$H", "Home"));
        UnlockCommand = new DelegateCommand(_ => _ = SendLineAsync("$X", "Unlock"));
        // Check toggles grbl check mode ($C): a dry run that verifies g-code and
        // simulates motion without moving the machine. Enabled only when connected.
        CheckCommand = new DelegateCommand(_ => _ = SendLineAsync("$C", "Check mode toggle"), _ => IsConnected);
        ResetCommand = new DelegateCommand(_ => SendRealtime(GrblRealtime.Reset, "Reset"));
        MdiHistoryPrevCommand = new DelegateCommand(_ => MdiHistoryPrev());
        MdiHistoryNextCommand = new DelegateCommand(_ => MdiHistoryNext());
        // The Machine settings menu entry opens App Settings on the Profiles leaf.
        MachineSettingsCommand = new DelegateCommand(_ => _ = ShowAppSettingsAsync(profiles: true));
        OpenMenuModuleCommand = new DelegateCommand(p => _ = OpenMenuModuleAsync(p as IModule));
        AboutCommand = new DelegateCommand(_ => _ = ShowAboutAsync());
        _mdiHistoryIndex = MdiHistory.Count;

        // Cycle bar Start/Rewind enablement follows the loaded program rows; the
        // status bar block counter follows live row statuses, and run messages
        // surface on the status bar.
        _program = _services.GetService<ProgramViewModel>();
        if (_program is not null)
        {
            _program.ProgramRows.CollectionChanged += OnProgramRowsCollectionChanged;
            _program.RowsChanged += OnProgramRowsChanged;
            _program.PropertyChanged += OnProgramPropertyChanged;
        }

        // State indicator: subscribe to grbl State and expose MachineState + StateBrush (RUN green #c8e8c8, IDLE yellow #ffe8a0, ALARM red #e04545, Height 28)
        // Also drives dynamic Signals panel (AxisSignals + ProbeActive) from MachineState.NumAxes / Signals
        _stateSubscription = _grbl.State.Subscribe(s => UiThread.Post(() => ApplyState(s)));

        // Command replies: surface a bare "error:<n>" line (which also shows in the
        // console) as an actionable status-bar message, e.g. error:79 on grblHAL
        // means a critical alarm (e-stop/hard limit) is blocking unlock/motion.
        _linesSubscription = _grbl.ReceivedLines.Subscribe(line =>
        {
            if (TryDescribeError(line, out var message))
                UiThread.Post(() => Status = message);
        });

        // Keep the latest work-parameters snapshot and feed it to the emulator
        // (Machine.WorkParametersProvider): G-code limits / bounding-box math then
        // starts from the controller's real coordinate systems, G28/G30, tools and
        // probe data instead of an empty table. The snapshot is immutable by
        // convention, so the closure always sees a consistent value.
        _workParametersSubscription = _grbl.WorkParametersChanges.Subscribe(wp => UiThread.Post(() => _latestWorkParameters = wp));
        _workParametersProvider = () => _latestWorkParameters;
        CNC.Core.Machine.WorkParametersProvider = _workParametersProvider;

        // Connection state drives the "NOT CONNECTED" indicator and status bar.
        _connectionChangedHandler = OnConnectionChanged;
        _grbl.ConnectionChanged += _connectionChangedHandler;

        // Seeded defaults: the app starts disconnected and makes that obvious.
        Status = LocText.Resolve("app.msg.chooseController", "Not connected — choose a controller via Connect (Serial, Network or Demo)");

        // Status-bar values track live settings (jog step) and program state changes;
        // live-apply visibility/mode changes, since the AppSettings dialog saves and
        // then raises Changed. Subscribed once, here: Dispose removes exactly one.
        _settingsService.Changed += OnSettingsChanged;
        // A machine swap invalidates the loaded program (different geometry ⇒
        // limits/jog semantics differ), so MainViewModel clears it on switch.
        _settingsService.ProfileSwitched += OnProfileSwitched;
        // A restore rewrites every settings file while this instance keeps the
        // pre-restore graph and stops writing, so the restored files only take
        // effect on restart — the operator needs to be told that.
        _settingsService.RestorePendingChanged += OnRestorePendingChanged;
        OnPropertyChanged(nameof(StatusBarJogStep));
        Hotkeys = _services.GetService<HotkeyService>();
        RegisterHotkeys();
    }

    /// <summary>Registers built-in run/MDI actions, then every module-provided
    /// <see cref="IHotkeyActionProvider"/>, linking each action's default gesture
    /// from its module's <see cref="IModule.HotKeys"/> declaration by ActionId.</summary>
    private void RegisterHotkeys()
    {
        var hotkeys = Hotkeys;
        if (hotkeys is null)
            return;

        void Builtin(string id, string label, ICommand command, string gesture)
            => hotkeys.RegisterAction(new CommandHotkeyAction(
                id,
                Resolve($"app.hotkeys.{id}", label),
                $"app.hotkeys.{id}",
                () => command.Execute(null),
                () => command.CanExecute(null)), gesture);

        Builtin("cycle.start", "Cycle start", CycleStartCommand, "Alt+R");
        Builtin("cycle.hold", "Feed hold", FeedHoldCommand, "Space");
        Builtin("cycle.stop", "Stop", StopCommand, "Alt+S");
        Builtin("cycle.rewind", "Rewind", RewindCommand, "");
        Builtin("home", "Home", HomeCommand, "Ctrl+H");
        Builtin("unlock", "Unlock", UnlockCommand, "Ctrl+U");
        Builtin("reset", "Reset", ResetCommand, "Ctrl+Shift+R");
        Builtin("check", "Check mode", CheckCommand, "");

        hotkeys.RegisterAction(new CommandHotkeyAction(
            "mdi.focus",
            Resolve("app.hotkeys.mdi.focus", "MDI focus"),
            "app.hotkeys.mdi.focus",
            () => FocusMdiRequested?.Invoke(this, EventArgs.Empty)), "Ctrl+M");

        foreach (var module in Modules)
        {
            if (module is not IHotkeyActionProvider provider)
                continue;
            try
            {
                foreach (var action in provider.CreateHotkeyActions(_services))
                {
                    var defaultGesture = module.HotKeys
                        .FirstOrDefault(h => h.ActionId == action.ActionId)?.Gesture ?? string.Empty;
                    hotkeys.RegisterAction(action, defaultGesture);
                }
            }
            catch (Exception ex)
            {
                Status = LocText.Format("app.msg.hotkeyModuleFailed", "{0} hotkeys failed: {1}", module.Name, ex.Message);
            }
        }
    }

    private void OnProfileSwitched(string profileId)
    {
        UiThread.Post(() =>
        {
            UnloadProgram();
            Status = LocText.Format("app.msg.profileSwitched", "Profile switched: {0}", profileId);
            OnSettingsChanged();
        });
    }

    /// <summary>Raised once by the settings service after an app-settings restore
    /// wrote the archive over the live files: the restored settings can only be
    /// applied by restarting, so say so instead of leaving frozen saves unexplained.</summary>
    private void OnRestorePendingChanged()
    {
        Status = LocText.Resolve("appSettings.backup.restartRequired",
            "Settings were restored — restart SenderNG to apply them.");
    }

    /// <summary>
    /// Saves the loaded program's text (File → Save / toolbar Save, Ctrl+S).
    /// Save As is the default action: with no editor there is no dirty document
    /// to persist, so overwriting the source file is never implied — a
    /// converter or transform must not silently rewrite the `.drl`/.nc it came
    /// from. The picker pre-fills the current file name, so overwriting in
    /// place is one accept-click away; a new name/location is an explicit
    /// choice. A successfully picked path becomes the program's home (window
    /// title, next prefill). When an editor lands (dirty flag), quick Save —
    /// direct overwrite of the home file — can return.
    /// </summary>
    private async Task SaveFileAsync()
    {
        if (!HasProgram || SessionActive)
            return;
        if (_fileDialogs is null)
        {
            Status = LocText.Resolve("app.msg.saveNoDialog", "Save: no dialog service available");
            return;
        }

        var text = _program?.LoadedText;
        if (text is null)
            return;

        var path = await _fileDialogs.SaveFileAsync(
            "Save G-code file",
            CurrentFileName ?? "program.nc",
            "G-code (*.nc;*.ngc;*.gcode;*.gco;*.tap)|*.nc;*.ngc;*.gcode;*.gco;*.tap",
            "All files|*.*");
        if (string.IsNullOrWhiteSpace(path))
            return; // cancelled

        try
        {
            await File.WriteAllTextAsync(path, text);
            CurrentFileName = Path.GetFileName(path);
            Status = LocText.Format("app.msg.saved", "Saved {0}", Path.GetFileName(path));
        }
        catch (Exception ex)
        {
            Status = LocText.Format("app.msg.saveFailed", "Save failed: {0}", ex.Message);
            if (_fileDialogs is not null)
                await _fileDialogs.ShowErrorAsync("Save failed", ex.Message);
        }
    }

    /// <summary>Unloads the loaded program (rows, limits panel, title suffix) —
    /// shared by File → Close and the machine-profile switch (a different
    /// geometry invalidates the loaded program's limits and jog semantics).</summary>
    private void UnloadProgram()
    {
        _program?.ClearProgram();
        HasProgramLimits = false;
        CurrentFileName = null;
    }

    private void CloseFile()
    {
        if (!HasProgram || SessionActive)
            return;
        var fileName = CurrentFileName;
        UnloadProgram();
        Status = LocText.Format("app.msg.closed", "Closed {0}", fileName ?? string.Empty);
    }

    /// <summary>
    /// Applies a token-level transform to the loaded program — the shared
    /// File → Transform path: parse the loaded text, run the transform, re-parse
    /// the result through the SAME engine gate that refuses bad files on Open
    /// (a transformed program that would fail while sending is never loaded),
    /// then swap it in. Limits and the status-bar counters rebuild from the new
    /// program. The source file on disk is never touched — the Save picker is
    /// the only write path (see <see cref="SaveFileAsync"/>).
    /// </summary>
    /// <param name="transform">Token transform (see <see cref="CNC.GCode.GCodeTransformers"/>).</param>
    /// <param name="successMessage">Status text; receives the result program and
    /// the pre-transform row count (for before/after summaries).</param>
    private async Task<bool> ApplyTransformAsync(
        Func<GCodeProgram, List<string>> transform,
        Func<GCodeProgram, int, string> successMessage)
    {
        var program = _program;
        var text = program?.LoadedText;
        if (program is null || string.IsNullOrEmpty(text))
            return false;

        var before = program.ProgramRows.Count;
        try
        {
            var parsed = GCodeProgram.Parse(text);
            if (parsed.HasErrors)
                throw new InvalidOperationException(string.Join(Environment.NewLine, parsed.Errors));

            var resultText = string.Join("\n", transform(parsed));
            var result = GCodeProgram.Parse(resultText);
            if (result.HasErrors)
                throw new InvalidOperationException(string.Join(Environment.NewLine, result.Errors));

            program.LoadProgram(result, resultText);
            HasProgramLimits = program.HasLimits;
            Status = successMessage(result, before);
            return true;
        }
        catch (Exception ex)
        {
            Status = LocText.Format("app.msg.transformFailed", "Transform failed: {0}", ex.Message);
            if (_fileDialogs is not null)
                await _fileDialogs.ShowErrorAsync(
                    Resolve("app.transform.failedTitle", "Transform failed"), ex.Message);
            return false;
        }
    }

    /// <summary>File → Transform → Rotate…: angle in degrees (CCW-positive) +
    /// rotation origin. The origin defaults to the current DRO (work) position
    /// when connected — rotate about where the tool actually is.</summary>
    private async Task RotateProgramAsync()
    {
        if (!HasProgram || SessionActive)
            return;
        if (_dialogs is null)
        {
            Status = LocText.Resolve("app.msg.transformNoDialog", "Transform: no dialog service available");
            return;
        }

        var originX = 0d;
        var originY = 0d;
        var dro = _services.GetService<Modules.Fixed.DroViewModel>();
        if (dro is not null)
        {
            var x = dro.Axes.FirstOrDefault(a => a.Letter == "X");
            var y = dro.Axes.FirstOrDefault(a => a.Letter == "Y");
            originX = x?.Position ?? 0d;
            originY = y?.Position ?? 0d;
        }

        var vm = new CNC.Core.ViewModels.RotateProgramViewModel(originX, originY);
        var result = await _dialogs.ShowDialogAsync(
            Resolve("app.transform.rotate.title", "Rotate program"), vm);
        if (result != true)
            return;

        var angle = vm.Angle;
        var angleText = angle.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        await ApplyTransformAsync(
            p => GCodeTransformers.Rotate(p.Tokens, angle * Math.PI / 180d, vm.OriginX, vm.OriginY, p.Decimals),
            (_, _) => LocText.Format("app.msg.rotated", "Rotated by {0}°", angleText));
    }

    /// <summary>File → Transform → Arcs to lines…: chord tolerance for the
    /// G1 polyline approximation of arcs and splines.</summary>
    private async Task ArcsToLinesProgramAsync()
    {
        if (!HasProgram || SessionActive)
            return;
        if (_dialogs is null)
        {
            Status = LocText.Resolve("app.msg.transformNoDialog", "Transform: no dialog service available");
            return;
        }

        var vm = new CNC.Core.ViewModels.ArcsToLinesProgramViewModel();
        var result = await _dialogs.ShowDialogAsync(
            Resolve("app.transform.arcsToLines.title", "Arcs to lines"), vm);
        if (result != true)
            return;

        var tolerance = vm.Tolerance;
        await ApplyTransformAsync(
            p => GCodeTransformers.ArcsToLines(p.Tokens, tolerance, p.Decimals),
            (r, before) => LocText.Format("app.msg.arcsToLines", "Arcs to lines: {0} → {1} lines", before, r.Entries.Count));
    }

    /// <summary>File → Transform → Compress: canonical re-serialization. Comment
    /// handling comes from the active profile's Program settings (keep all /
    /// strip ';' remarks / strip all); modal motion words merge per the
    /// serializer's compress mode.</summary>
    private async Task CompressProgramAsync()
    {
        if (!HasProgram || SessionActive)
            return;
        var mode = ProgramConfigStore.Load(_settingsService.Load()).CompressCommentMode;
        await ApplyTransformAsync(
            p => GCodeTransformers.Compress(p.Tokens, mode),
            (r, before) => LocText.Format("app.msg.compressed", "Compressed: {0} → {1} lines", before, r.Entries.Count));
    }

    /// <summary>
    /// File → Transform → Wrap / Unwrap. Wrap remaps a linear axis (X/Y) onto a
    /// rotary one (A/B); Unwrap does the reverse. Both go through the shared
    /// <see cref="ApplyTransformAsync"/> gate; a localized precondition rejects
    /// programs with arcs/splines/drills before the engine throws, and a G95
    /// (units/rev) program is reported through the gate's own error path.
    /// </summary>
    private async Task WrapProgramAsync(CNC.Core.ViewModels.WrapDirection direction)
    {
        if (!HasProgram || SessionActive)
            return;
        if (_dialogs is null)
        {
            Status = LocText.Resolve("app.msg.transformNoDialog", "Transform: no dialog service available");
            return;
        }
        var kinematics = CurrentKinematics();
        if (kinematics.RotaryAxes.Count == 0)
        {
            var message = LocText.Resolve("app.transform.wrap.noRotaryAxis",
                "Wrap/unwrap needs a rotary axis (A/B) declared in the machine profile.");
            Status = message;
            if (_fileDialogs is not null)
                await _fileDialogs.ShowErrorAsync(Resolve("app.transform.failedTitle", "Transform failed"), message);
            return;
        }

        var vm = new CNC.Core.ViewModels.WrapProgramViewModel(direction, kinematics,
            letter => LocText.Format("app.transform.wrap.axisLabel", "{0}-axis", letter));
        var title = Resolve(
            direction == CNC.Core.ViewModels.WrapDirection.Wrap ? "app.transform.wrap.title" : "app.transform.unwrap.title",
            "Wrap / Unwrap");
        if (await _dialogs.ShowDialogAsync(title, vm) != true)
            return;
        // Defensive: the dialog disables OK for a non-positive diameter and only
        // offers axes that exist, so the guards below only fire on a forced close.
        if (vm.SourceAxis is null || vm.TargetAxis is null || vm.Diameter <= 0d)
            return;

        var program = _program;
        if (program is not null)
        {
            var parsed = GCodeProgram.Parse(program.LoadedText ?? string.Empty);
            if (!parsed.HasErrors && parsed.Tokens.Any(UnsupportedByWrap))
            {
                var message = LocText.Resolve("app.transform.wrap.arcsUnsupported",
                    "Wrap/unwrap does not support arcs, splines or canned cycles. Run Arcs to lines first.");
                Status = message;
                if (_fileDialogs is not null)
                    await _fileDialogs.ShowErrorAsync(Resolve("app.transform.failedTitle", "Transform failed"), message);
                return;
            }
        }

        var diameter = vm.Diameter;
        var mmPerDegree = diameter * Math.PI / 360d;
        var fromAxis = vm.SourceAxis.Index;
        var toAxis = vm.TargetAxis.Index;
        var wrap = direction == CNC.Core.ViewModels.WrapDirection.Wrap;
        var zShift = vm.Z0AtCenter ? (wrap ? diameter / 2d : -diameter / 2d) : 0d;
        var diameterText = diameter.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        var header = string.Format(System.Globalization.CultureInfo.InvariantCulture,
            "{0} applied, diameter: {1}, {2}-axis to {3}-axis",
            wrap ? "Wrap" : "Unwrap",
            diameterText,
            kinematics.LetterFor(fromAxis),
            kinematics.LetterFor(toAxis));

        await ApplyTransformAsync(
            p => GCodeTransformers.AxisRemap(p.Tokens, fromAxis, toAxis, mmPerDegree, wrap, p.Decimals, zShift, header),
            (r, before) => wrap
                ? LocText.Format("app.msg.wrapped", "Wrapped: {0} → {1} lines", before, r.Entries.Count)
                : LocText.Format("app.msg.unwrapped", "Unwrapped: {0} → {1} lines", before, r.Entries.Count));
    }

    private static bool UnsupportedByWrap(GCodeToken token) =>
        token.Command is Commands.G2 or Commands.G3 or Commands.G5 or Commands.G5_1
            or Commands.G73 or Commands.G81 or Commands.G82 or Commands.G83
            or Commands.G85 or Commands.G86 or Commands.G89;

    /// <summary>The resolved machine for the wrap gate: the shared provider's snapshot
    /// when one is registered, else a direct resolve of the active profile (minimal
    /// test hosts). Either way "which axis is rotary" is an axis-kind fact, so a
    /// 4-axis foam profile with no rotary axis is correctly refused.</summary>
    private MachineKinematics CurrentKinematics() =>
        _kinematics?.Current
        ?? MachineKinematicsResolver.Resolve(_settingsService.Load(), _info.AxisLetters, _info.NumAxes);

    public SettingsService SettingsService { get; }

    /// <summary>Hosted DRO module view (per-axis position rows + zero buttons).</summary>
    public object DroContent { get; }

    public string DroHeader => Resolve("module.dro.name", "DRO");

    private string LocalizedModuleName(IModule m) =>
        _localization is null ? m.Name : ModuleNames.Localized(m, _localization);

    private string Resolve(string key, string fallback) =>
        _localization is null ? fallback : _localization.Resolve(key, fallback);

    public IGrblService Grbl
    {
        get => _grbl;
        private set
        {
            var previous = _grbl;
            if (SetField(ref _grbl, value))
            {
                _stateSubscription?.Dispose();
                _stateSubscription = _grbl.State.Subscribe(s => UiThread.Post(() => ApplyState(s)));

                _linesSubscription?.Dispose();
                _linesSubscription = _grbl.ReceivedLines.Subscribe(line =>
                {
                    if (TryDescribeError(line, out var message))
                        UiThread.Post(() => Status = message);
                });

                _workParametersSubscription?.Dispose();
                _workParametersSubscription = _grbl.WorkParametersChanges.Subscribe(wp => UiThread.Post(() => _latestWorkParameters = wp));

                if (_connectionChangedHandler is not null)
                {
                    previous.ConnectionChanged -= _connectionChangedHandler;
                    _grbl.ConnectionChanged += _connectionChangedHandler;
                }
            }
        }
    }

    public bool IsDemo
    {
        get => _isDemo;
        private set
        {
            if (SetField(ref _isDemo, value))
            {
                OnPropertyChanged(nameof(Title));
                OnPropertyChanged(nameof(CanConnect));
                OnPropertyChanged(nameof(ShowDisconnect));
                (ConnectCommand as DelegateCommand)?.RaiseCanExecuteChanged();
                (DisconnectCommand as DelegateCommand)?.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>Whether the active controller connection is live (drives the state indicator).</summary>
    public bool IsConnected => _isConnected;

    /// <summary>Connect is available only when no controller is live: the toolbar plug disabled while running/demo.</summary>
    public bool CanConnect => !IsConnected && !IsDemo;

    /// <summary>Disconnect shows exactly when there is a live controller (or demo) to tear down.</summary>
    public bool ShowDisconnect => IsConnected || IsDemo;

    /// <summary>Status-bar block progress: executed/total sendable blocks of the
    /// loaded program (comments are not blocks — they stay in the grid but never
    /// enter the run counter, so a finished run reads N/N).</summary>
    public string StatusBarBlocks
    {
        get
        {
            if (_program is null || _program.SendableCount == 0)
                return LocText.Format("app.status.block", "Block: {0}/{1}", "--", "--");
            var exec = _program.ProgramRows.Count(r => r.Status == "\u2713");
            return LocText.Format("app.status.block", "Block: {0}/{1}", exec, _program.SendableCount);
        }
    }

    /// <summary>Status-bar run time — time elapsed since the current Run started.</summary>
    public string RunTime =>
        LocText.Format("app.status.runTime", "Run time: {0}", FormatElapsed(_runStopwatch.Elapsed));

    private static string FormatElapsed(TimeSpan t)
    {
        var s = (long)t.TotalSeconds;
        var h = s / 3600;
        var m = (s % 3600) / 60;
        var sec = s % 60;
        return h > 0 ? $"{h}:{m:D2}:{sec:D2}" : $"{m}:{sec:D2}";
    }

    /// <summary>Status-bar jog step — the active jog distance from the Jog module settings.</summary>
    public string StatusBarJogStep
    {
        get
        {
            var cfg = CNC.Core.Services.JogConfigStore.Load(_settingsService.Load());
            var step = cfg.Distances.Length == 0 ? 0.0
                : cfg.Distances[Math.Clamp(cfg.DistanceIndex, 0, cfg.Distances.Length - 1)];
            return LocText.Format("app.status.jogStep", "Jog step: {0}",
                step.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
        }
    }

    /// <summary>Window title: demo flag, connection state and loaded file name.</summary>
    public string Title =>
        (IsDemo ? BaseTitleWithDemo : BaseTitleWithoutDemo) + ConnectionTitleSuffix + FileTitleSuffix;

    private string ConnectionTitleSuffix => IsConnected || IsDemo ? "" : Resolve("app.title.notConnectedSuffix", " - Not connected");

    private string BaseTitleWithDemo => Resolve("app.title.demo", "SenderNG (DEMO mode)");
    private static string BaseTitleWithoutDemo => "SenderNG";

    private string FileTitleSuffix => string.IsNullOrEmpty(CurrentFileName) ? "" : $" - {CurrentFileName}";

    public string? CurrentFileName
    {
        get => _currentFileName;
        private set
        {
            if (SetField(ref _currentFileName, value))
                OnPropertyChanged(nameof(Title));
        }
    }

    public string Status
    {
        get => _status;
        private set
        {
            if (SetField(ref _status, value))
                OnPropertyChanged(nameof(Message));
        }
    }

    /// <summary>Status-bar message — the live transient status/alarm line.</summary>
    public string Message => Status;

    public string MachineState
    {
        get => _machineState;
    }

    /// <summary>State indicator text: machine state in all caps, or NOT CONNECTED.</summary>
    public string MachineStateDisplay =>
        IsConnected ? _machineState.ToUpperInvariant() : Resolve("app.state.notConnected", "NOT CONNECTED");

    public IBrush StateBrush => IsConnected ? _stateBrush : DisconnectedBrush;
    public IBrush StateForeground => IsConnected ? _stateForeground : Brushes.Black;

    private static readonly IBrush DisconnectedBrush = new SolidColorBrush(Color.Parse("#d8d8d8"));

    public ObservableCollection<IModule> Modules { get; } = new();

    public bool IsModuleVisible(IModule m) =>
        m.CanHide
            ? _settingsService.Load().Modules.TryGetValue(m.Id, out var v) ? v : m.IsVisibleByDefault
            : m.IsVisibleByDefault;

    public IEnumerable<NamedModule> LeftModules =>
        Modules.Where(IsModuleVisible).Where(m => m.Panel == PanelHint.Left).OrderBy(m => m.Order)
            .Select(m => new NamedModule(m, LocalizedModuleName(m)));

    public IEnumerable<IModule> CenterModules =>
        Modules.Where(IsModuleVisible).Where(m => m.Panel == PanelHint.Center).OrderBy(m => m.Order);

    public IEnumerable<IModule> RightModules =>
        Modules.Where(IsModuleVisible).Where(m => m.Panel == PanelHint.Right).OrderBy(m => m.Order);

    /// <summary>Right-panel modules paired with their built views (Expander content).</summary>
    private object? ViewFor(IModule m)
    {
        if (_moduleViews.TryGetValue(m, out var v))
            return v;
        v = _viewFactory?.Invoke(m);
        _moduleViews[m] = v!;
        return v;
    }

    public IEnumerable<ModuleView> RightModuleViews =>
        RightModules.Select(m => new ModuleView(m, ViewFor(m), LocalizedModuleName(m)));

    public IEnumerable<NamedModule> FlyoutModules =>
        Modules.Where(IsModuleVisible).Where(m => m.Panel == PanelHint.Flyout).OrderBy(m => m.Order)
            .Select(m => new NamedModule(m, LocalizedModuleName(m)));

    public IEnumerable<IModule> MenuModules =>
        Modules.Where(IsModuleVisible).Where(m => m.Panel == PanelHint.Menu).OrderBy(m => m.Order);

    /// <summary>Settings menu entries: fixed items plus one per Menu-claiming module.</summary>
    public IReadOnlyList<MenuEntry> SettingsMenu =>
        [
            ..MenuModules.Select(m => new MenuEntry(LocalizedModuleName(m), OpenMenuModuleCommand, m)),
        ];

    private bool _hasProgramLimits;

    /// <summary>Program limits panel is only shown when a program with motion is loaded.</summary>
    public bool HasProgramLimits
    {
        get => _hasProgramLimits;
        private set
        {
            if (SetField(ref _hasProgramLimits, value))
                OnPropertyChanged(nameof(ProgramLimitRows));
        }
    }

    public IEnumerable<ProgramLimitRow>? ProgramLimitRows =>
        _services.GetService<ProgramViewModel>()?.LimitRows;

    /// <summary>Signal LEDs shown in the left panel — axis limits first, then the
    /// machine control signals. Axis entries follow the controller's axis count;
    /// H/S/R/P (feed hold, cycle start, reset, probe) are always shown like
    /// ioSender, and the rest are gated on <see cref="MachineState.OptionalSignals"/>
    /// — the signals the controller actually reports in $I — so a board without a
    /// safety door / e-stop / motor fault wiring doesn't show LEDs it can't drive.</summary>
    /// <remarks>All three collections hold the same <see cref="SignalIndicator"/>
    /// instances; <see cref="SignalIndicators"/> is the flattened render order
    /// (used for state updates and tests), while the two grouped collections feed
    /// the axis-left / control-right layout mirroring ioSender's SignalsControl
    /// (DockPanel Left axis limits, Right machine controls).</remarks>
    public ObservableCollection<SignalIndicator> SignalIndicators { get; } = new();

    /// <summary>Axis/limit LEDs only (ioSender left panel).</summary>
    public ObservableCollection<SignalIndicator> AxisSignalIndicators { get; } = new();

    /// <summary>Machine control LEDs only — always-on H/S/R/P plus reported optional
    /// signals (ioSender right panel).</summary>
    public ObservableCollection<SignalIndicator> ControlSignalIndicators { get; } = new();

    /// <summary>Always-visible machine control signals (ioSender ungated set).</summary>
    private static readonly (CNC.Core.Signals Flag, string Label, string Tip)[] AlwaysControlSignals =
    [
        (CNC.Core.Signals.Hold, "H", "Feed hold"),
        (CNC.Core.Signals.CycleStart, "S", "Cycle start"),
        (CNC.Core.Signals.Reset, "R", "Reset"),
        (CNC.Core.Signals.Probe, "P", "Probe"),
    ];

    /// <summary>Optional signals, shown only when the controller reports them
    /// (ioSender gates these on OptionalSignals in SignalsControl.xaml).</summary>
    private static readonly (CNC.Core.Signals Flag, string Label, string Tip)[] OptionalControlSignals =
    [
        (CNC.Core.Signals.MotorWarning, "W", "Motor warning"),
        (CNC.Core.Signals.MotorFault, "F", "Motor fault"),
        (CNC.Core.Signals.BlockDelete, "B", "Block delete"),
        (CNC.Core.Signals.OptionalStop, "O", "Optional stop"),
        (CNC.Core.Signals.SafetyDoor, "D", "Safety door"),
        (CNC.Core.Signals.EStop, "E", "E-stop"),
    ];


    /// <summary>Center-module tabs. Rebuilding, but returning the cached
    /// sequence when nothing visible changed (see <see cref="_centerTabsCache"/>).</summary>
    public IEnumerable<CenterTab> CenterTabs
    {
        get
        {
            var built = CenterModules
                .Select(m => new CenterTab(m, ViewFor(m), LocalizedModuleName(m)))
                .ToArray();
            var cached = _centerTabsCache;
            if (cached is not null && cached.SequenceEqual(built))
                return cached;
            _centerTabsCache = built;
            return built;
        }
    }

    public string SelectedModuleName =>
        SelectedModule is null ? string.Empty : LocalizedModuleName(SelectedModule);

    public IModule? SelectedModule
    {
        get => _selectedModule;
        set
        {
            if (SetField(ref _selectedModule, value))
            {
                CurrentModuleView = value is not null ? ViewFor(value) : null;
                OnPropertyChanged(nameof(SelectedModuleName));
            }
        }
    }

    /// <summary>View (control) for the selected module, built by the Avalonia view factory.</summary>
    public object? CurrentModuleView
    {
        get => _currentModuleView;
        private set => SetField(ref _currentModuleView, value);
    }

    public ObservableCollection<string> MdiHistory { get; } = new();

    public ObservableCollection<string> RecentFiles { get; } = new();

    public string MdiInput
    {
        get => _mdiInput;
        set
        {
            if (SetField(ref _mdiInput, value))
                (SendMdiCommand as DelegateCommand)?.RaiseCanExecuteChanged();
        }
    }

    public ICommand ConnectCommand { get; }

    public ICommand DisconnectCommand { get; }

    public ICommand SendMdiCommand { get; }

    public ICommand OpenFileCommand { get; }

    /// <summary>Saves the loaded program's text via the save picker, pre-filled
    /// with the current file name (Save As semantics — the source is never
    /// overwritten implicitly; see <see cref="SaveFileAsync"/>).</summary>
    public ICommand SaveFileCommand { get; }

    /// <summary>Unloads the loaded program (File → Close / toolbar Close). Run
    /// control lives on the cycle bar, so there is no toolbar Run — ioSender's
    /// Load/Save/Run/Close strip is deliberately scoped to Open + Save + Close
    /// (Save always goes through the picker — see <see cref="SaveFileAsync"/>).</summary>
    public ICommand CloseFileCommand { get; }

    /// <summary>File → Transform → Rotate…: angle + origin, defaulting to the
    /// current DRO (work) position when connected. Gated like Save/Close — a
    /// loaded program, no active session.</summary>
    public ICommand RotateProgramCommand { get; }

    /// <summary>File → Transform → Arcs to lines…: arcs and splines become G1
    /// chains at a chord tolerance.</summary>
    public ICommand ArcsToLinesProgramCommand { get; }

    /// <summary>File → Transform → Compress: canonical re-serialization (modal
    /// motion words merge; comments per the profile's Program setting).</summary>
    public ICommand CompressProgramCommand { get; }

    /// <summary>File → Transform → Wrap to rotary…: linear X/Y toolpaths wrapped
    /// onto a rotary A/B axis around a cylinder.</summary>
    public ICommand WrapProgramCommand { get; }

    /// <summary>File → Transform → Unwrap from rotary…: rotary A/B toolpaths
    /// flattened back onto a linear X/Y axis.</summary>
    public ICommand UnwrapProgramCommand { get; }

    public ICommand CycleStartCommand { get; }

    public ICommand FeedHoldCommand { get; }

    public ICommand StopCommand { get; }

    public ICommand RewindCommand { get; }

    public ICommand HomeCommand { get; }

    public ICommand UnlockCommand { get; }

    public ICommand CheckCommand { get; }

    public ICommand ResetCommand { get; }

    /// <summary>True while grbl check mode is active (controller state "Check").
    /// Drives the [x] Check checkbox next to the state indicator.</summary>
    public bool IsCheckMode =>
        _machineState.Contains("check", StringComparison.OrdinalIgnoreCase);

    public ICommand MdiHistoryPrevCommand { get; }

    public ICommand MdiHistoryNextCommand { get; }

    public ICommand MachineSettingsCommand { get; }

    public DelegateCommand OpenMenuModuleCommand { get; }

    public ICommand AboutCommand { get; }

    public DelegateCommand SelectModuleCommand { get; }

    public ICommand SelectRecentCommand { get; }

    /// <summary>The shared hotkey catalog (null when the host did not register one, e.g. minimal tests).</summary>
    public HotkeyService? Hotkeys { get; }

    /// <summary>Raised by the <c>mdi.focus</c> hotkey; MainWindow focuses the MDI box.</summary>
    public event EventHandler? FocusMdiRequested;

    /// <summary>Maps a realized module view (or a focused descendant) to its module id,
    /// for hotkey scope resolution.</summary>
    public string? ModuleIdForView(object? view)
    {
        if (view is null)
            return null;
        foreach (var kv in _moduleViews)
            if (ReferenceEquals(kv.Value, view))
                return kv.Key.Id;
        return null;
    }

    /// <summary>Opens the App Settings dialog, optionally on the Profiles leaf
    /// (Machine settings menu entry). Profile switching is gated while the
    /// controller is connected or a session is running.</summary>
    private async Task ShowAppSettingsAsync(bool profiles)
    {
        if (_dialogs is null)
        {
            Status = LocText.Resolve("app.msg.settingsNoDialog", "App settings: no dialog service available");
            return;
        }
        var module = Modules.FirstOrDefault(m => m.Id == "app-settings");
        if (module is null)
        {
            Status = LocText.Resolve("app.msg.settingsMissing", "App settings module not found");
            return;
        }
        var viewModel = module.CreateViewModel(_services);
        ApplySettingsDialogGuards(viewModel);
        if (viewModel is CNC.Core.ViewModels.AppSettingsViewModel settingsVm)
            settingsVm.PreselectProfiles = profiles;
        try
        {
            await _dialogs.ShowDialogAsync(module.Name, viewModel, module.DialogPreferredSize);
        }
        finally
        {
            (viewModel as IDisposable)?.Dispose();
        }
    }

    /// <summary>Profile switching is nonsense mid-session: the dialog's switch
    /// guard blocks it while connected or running.</summary>
    private void ApplySettingsDialogGuards(object viewModel)
    {
        if (viewModel is CNC.Core.ViewModels.AppSettingsViewModel settingsVm)
            settingsVm.ProfileSwitchAllowed = () => !IsConnected && !SessionActive;
    }

    private async Task ShowAboutAsync()
    {
        var window = new Views.AboutView { DataContext = new AboutViewModel(Modules) };
        var owner = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        if (owner is null)
            window.Show();
        else
            await window.ShowDialog(owner);
    }

    /// <summary>
    /// Swap the active service (through the proxy when present) so subscribers
    /// keep working across demo/real/network transitions. A service replaced by
    /// this call is disconnected/disposed so its reader and poller never leak.
    /// </summary>
    private void SwapInner(IGrblService target)
    {
        var previous = ResolveInner(Grbl);
        if (Grbl is GrblServiceProxy proxy)
            proxy.Inner = target;
        else
            Grbl = target;

        if (ReferenceEquals(previous, _networkService))
            _networkService.Dispose();
        if (!ReferenceEquals(target, _networkService))
            _networkService = null;
    }

    /// <summary>Make the DI singleton serial service the active one (no-op when already active).</summary>
    private void SwitchToSerial()
    {
        var inner = ResolveInner(Grbl);
        if (inner is RealGrblService && !ReferenceEquals(inner, _networkService))
            return;
        SwapInner(_services.GetRequiredService<RealGrblService>());
        IsDemo = false;
    }

    /// <summary>Switch the app into demo mode: simulated controller, no port needed.</summary>
    private async Task ActivateDemoAsync()
    {
        var demo = _services.GetRequiredService<DemoGrblService>();
        // The demo singleton captured the profile at construction; re-read the
        // axes of the now-active profile on every activation (a machine swap
        // must not leave the demo simulating the old one).
        demo.SetNumAxes(_settingsService.Load().DemoAxes);
        SwapInner(demo);
        IsDemo = true;
        if (!demo.IsConnected)
            await Grbl.ConnectAsync("demo");
        Status = LocText.Resolve("app.msg.demoMode", "Demo mode — controller simulated in software");
    }

    private void MdiHistoryPrev()
    {
        if (MdiHistory.Count == 0) return;
        if (_mdiHistoryIndex == MdiHistory.Count)
            _mdiHistoryIndex = MdiHistory.Count - 1;
        else if (_mdiHistoryIndex > 0)
            _mdiHistoryIndex--;
        // else stay at 0
        MdiInput = MdiHistory[_mdiHistoryIndex];
    }

    private void MdiHistoryNext()
    {
        if (MdiHistory.Count == 0) return;
        if (_mdiHistoryIndex < MdiHistory.Count - 1)
        {
            _mdiHistoryIndex++;
            MdiInput = MdiHistory[_mdiHistoryIndex];
        }
        else if (_mdiHistoryIndex == MdiHistory.Count - 1)
        {
            _mdiHistoryIndex = MdiHistory.Count;
            MdiInput = string.Empty;
        }
    }

    private async Task SendMdiAsync()
    {
        var line = MdiInput?.Trim();
        if (string.IsNullOrWhiteSpace(line)) return;
        // push to session history first (dedup tail), even if send fails or not connected
        if (MdiHistory.Count == 0 || MdiHistory[^1] != line)
            MdiHistory.Add(line);
        _mdiHistoryIndex = MdiHistory.Count;
        MdiInput = string.Empty;
        try
        {
            await Grbl.SendAsync(line);
            Status = LocText.Format("app.msg.mdi", "MDI: {0}", line);
        }
        catch (Exception ex)
        {
            Status = LocText.Format("app.msg.mdiFailed", "MDI failed: {0}", ex.Message);
        }
    }

    private async Task OpenFileAsync()
    {
        if (_fileDialogs is null)
        {
            Status = LocText.Resolve("app.msg.openFileNoDialog", "Open file: no dialog service available");
            return;
        }
        var path = await _fileDialogs.OpenFileAsync("Open G-code file", OpenFileFilters());
        if (string.IsNullOrWhiteSpace(path))
            return;

        await LoadGCodeFileAsync(path);
    }

    /// <summary>Open dialog filter: g-code + one entry per file-format handler
    /// module (no separate File menu entry — conversion IS the native open,
    /// dispatched by extension in <see cref="LoadGCodeFileAsync"/>). The app
    /// knows only the handler contract: no handler modules, no extra filters.</summary>
    private string[] OpenFileFilters()
    {
        var filters = new List<string>
        {
            "G-code (*.nc;*.ngc;*.gcode;*.gco;*.tap)|*.nc;*.ngc;*.gcode;*.gco;*.tap",
        };
        foreach (var handler in _fileHandlers)
        {
            var ext = string.Join(";", handler.FileExtensions.Split(',').Select(e => "*." + e.Trim()));
            // Filter label resolves through the locale when the handler opts in
            // (FileTypeKey); handlers without a key keep their literal label.
            var label = string.IsNullOrEmpty(handler.FileTypeKey)
                ? handler.FileType
                : Resolve(handler.FileTypeKey, handler.FileType);
            filters.Add($"{label} ({ext})|{ext}");
        }
        filters.Add("All files|*.*");
        return [.. filters];
    }

    private async Task LoadGCodeFileAsync(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        try
        {
            var text = await File.ReadAllTextAsync(path);

            // Open file → check if a handler module owns the extension → the
            // module converts (its own parameters dialog + per-profile settings)
            // and hands back g-code, loaded through the same engine gate as
            // plain g-code. Everything else takes the plain g-code path.
            var handler = FindFileHandler(Path.GetExtension(path));
            if (handler is not null)
            {
                var converted = await handler.ConvertAsync(_services, Path.GetFileName(path), text);
                if (converted is null)
                {
                    Status = LocText.Format("app.msg.convertFailed", "Conversion failed: {0}", Path.GetFileName(path));
                    return;
                }
                await LoadParsedProgramAsync(path, converted);
                return;
            }

            await LoadParsedProgramAsync(path, text);
        }
        catch (Exception ex)
        {
            Status = LocText.Format("app.msg.openFailed", "Open failed: {0}", ex.Message);
            if (_fileDialogs is not null)
                await _fileDialogs.ShowErrorAsync("Open failed", ex.Message);
        }
    }

    /// <summary>Finds the handler module that owns an extension (case-insensitive,
    /// no leading dot). Null when no module claims it.</summary>
    private IGCodeFileHandler? FindFileHandler(string extension)
    {
        var ext = (extension ?? string.Empty).TrimStart('.').ToLowerInvariant();
        if (ext.Length == 0)
            return null;

        return _fileHandlers.FirstOrDefault(h =>
            h.FileExtensions.Split(',', StringSplitOptions.TrimEntries)
                .Any(e => e.Equals(ext, StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>
    /// The load gate IS the engine: GCodeProgram parses the file once with
    /// the same GCodeParser the streamer consumes, so a file (or converted
    /// output) that would fail while sending can never be loaded. Errors
    /// carry file line numbers and refuse the load outright (no skip-and-load
    /// — a partial program is a different program than the user opened).
    /// </summary>
    private async Task LoadParsedProgramAsync(string path, string text)
    {
        var program = _services.GetRequiredService<ProgramViewModel>();
        // Parse and the limits emulator pass run off the UI thread: on big
        // files both take seconds, and the app must stay responsive (the
        // program view shows a loading overlay while IsBusy). The load gate
        // still refuses bad files — just not by freezing the UI to decide.
        program.SetBusy(true);
        Status = LocText.Format("app.msg.loading", "Loading {0}…", Path.GetFileName(path));
        var parsed = await Task.Run(() => GCodeProgram.Parse(text));
        if (parsed.HasErrors)
        {
            program.SetBusy(false);
            Status = LocText.Format("app.msg.validationFailed", "Validation failed: {0} ({1} error(s))", Path.GetFileName(path), parsed.Errors.Count);
            if (_fileDialogs is not null)
                await _fileDialogs.ShowErrorAsync("G-code validation failed", string.Join(Environment.NewLine, parsed.Errors));
            return;
        }

        var limits = await Task.Run(() => GCodeLimitsCalculator.Compute(parsed.Tokens));
        program.SetBusy(false);
        program.LoadProgram(parsed, text, limits);
        HasProgramLimits = program.HasLimits;
        CurrentFileName = Path.GetFileName(path);

        RecentFiles.Remove(path);
        RecentFiles.Insert(0, path);
        var maxRecent = Math.Max(1, _settingsService.Global.MaxRecentFiles);
        while (RecentFiles.Count > maxRecent)
            RecentFiles.RemoveAt(RecentFiles.Count - 1);
        var global = _settingsService.Global;
        global.RecentFiles = [.. RecentFiles];
        try
        {
            _settingsService.SaveGlobal(global);
        }
        catch
        {
            // Best-effort persistence; the recent list still shows for the session.
        }

        var isConverted = FindFileHandler(Path.GetExtension(path)) is not null;
        // A letter neither the board's letters nor the profile's aliases resolve means
        // the program is for another machine: its value landed on the axis's
        // conventional index, so one axis would move wrongly. That is worth saying at
        // load time on the status line (the app's load-diagnostic channel), because the
        // failure would otherwise only show up as a wrong axis mid-run.
        Status = parsed.UnknownAxisLetters.Count > 0
            ? LocText.Format("app.msg.unknownAxisLetters",
                "Loaded {0} ({1} lines), but it uses axis letters this machine does not know: {2}",
                Path.GetFileName(path), program.ProgramRows.Count,
                string.Join(", ", parsed.UnknownAxisLetters))
            : isConverted
                ? LocText.Format("app.msg.converted", "Converted {0}: {1} lines", Path.GetFileName(path), program.ProgramRows.Count)
                : LocText.Format("app.msg.loaded", "Loaded {0} ({1} lines)", Path.GetFileName(path), program.ProgramRows.Count);
    }

    // Menu-claiming modules (PanelHint.Menu) open as modal dialogs.
    private async Task OpenMenuModuleAsync(IModule? module)
    {
        if (module is null)
            return;
        if (_dialogs is null)
        {
            Status = LocText.Format("app.msg.moduleNoDialog", "{0}: no dialog service available", module.Name);
            return;
        }
        object? viewModel = null;
        try
        {
            viewModel = module.CreateViewModel(_services);
            ApplySettingsDialogGuards(viewModel);
            await _dialogs.ShowDialogAsync(module.Name, viewModel, module.DialogPreferredSize);
        }
        catch (Exception ex)
        {
            Status = LocText.Format("app.msg.moduleFailed", "{0} failed: {1}", module.Name, ex.Message);
        }
        finally
        {
            // The wizard may have left modal G-code on the controller; allow the
            // module to undo dialog-only side effects now that it is closing.
            if (viewModel is IAsyncDialogClosing closing)
            {
                try { await closing.OnDialogClosedAsync(); }
                catch { /* window is gone — nothing meaningful to report */ }
            }
            // Modal loops can swallow change-driven layout updates; re-raise
            // everything once the dialog closes so visibility applies immediately.
            OnSettingsChanged();
        }
    }

    private void Disconnect()
    {
        try
        {
            if (IsDemo) SwitchToSerial();
            Grbl.Disconnect();
            Status = LocText.Resolve("app.msg.disconnected", "Disconnected");
        }
        catch (Exception ex) { Status = LocText.Format("app.msg.disconnectFailed", "Disconnect failed: {0}", ex.Message); }
    }

    private void SendRealtime(byte code, string label)
    {
        try { Grbl.SendRealtime(code); Status = LocText.Format("app.msg.sent", "{0} sent", label); }
        catch (Exception ex) { Status = LocText.Format("app.msg.failed", "{0} failed: {1}", label, ex.Message); }
    }

    private async Task SendLineAsync(string line, string label)
    {
        try
        {
            await Grbl.SendAsync(line);
            Status = LocText.Format("app.msg.sent", "{0} sent", label);
        }
        catch (Exception ex)
        {
            Status = LocText.Format("app.msg.failed", "{0} failed: {1}", label, ex.Message);
        }
    }

    /// <summary>
    /// Interpret an <c>error:&lt;n&gt;</c> controller reply into a status-bar message
    /// (the raw line still reaches the console). The explanation resolves through the
    /// active locale when available (fallback: the built-in English dictionary).
    /// Returns false for any other line.
    /// </summary>
    private bool TryDescribeError(string line, out string message)
    {
        message = string.Empty;
        const string prefix = "error:";
        if (!line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return false;
        if (!int.TryParse(line[prefix.Length..], out var code))
            return false;
        var description = _localization?.TError(code) ?? CNC.Core.Services.GrblErrorCodes.Describe(code);
        if (description is null)
            return false;
        var hint = _localization?.TErrorHint(code) ?? CNC.Core.Services.GrblErrorCodes.Hint(code);
        message = hint is null
            ? $"error:{code} — {description}"
            : $"error:{code} — {description} {hint}";
        return true;
    }

    /// <summary>
    /// Cycle-bar Start: with a loaded program it starts the streaming run (or
    /// resumes a paused one); without, it falls back to the raw cycle-start byte.
    /// </summary>
    private async Task CycleStartAsync()
    {
        if (_program is { IsRunning: true })
        {
            // A paused run resumes on cycle start; a running machine ignores it.
            if (IsMachineState("Hold"))
                _program.Resume();
            else
                SendRealtime(GrblRealtime.CycleStart, "Cycle Start");
            return;
        }
        if (_program is null || !HasProgram)
        {
            // Raw passthrough without a loaded program (harmless no-op byte).
            SendRealtime(GrblRealtime.CycleStart, "Cycle Start");
            return;
        }
        // Streaming start; RunAsync itself no-ops when not connected.
        await _program.RunAsync();
    }

    /// <summary>Cycle-bar Feed Hold: pauses the streaming run if one is active, else sends the raw byte.</summary>
    private void FeedHold()
    {
        if (_program is { IsRunning: true })
            _program.Pause();
        else
            SendRealtime(GrblRealtime.FeedHold, "Feed Hold");
    }

    /// <summary>Cycle-bar Stop: controlled stop byte (grblHAL 0x19 / classic 0x18),
    /// ending the streaming session so it never resumes.</summary>
    private void StopMachine()
    {
        _program?.OnHardReset();
        var hal = Grbl.Capabilities.IsGrblHAL;
        SendRealtime(GrblRealtime.StopByte(hal), "Stop");
    }

    /// <summary>Rewind the loaded program to the start (local reset, no byte sent - mirrors ioSender RewindFile).</summary>
    private void Rewind()
    {
        var program = _services.GetService<ProgramViewModel>();
        if (program is null || program.ProgramRows.Count == 0)
        {
            Status = LocText.Resolve("app.msg.rewindNoProgram", "Rewind: no program loaded");
            return;
        }

        program.Rewind();
        Status = LocText.Resolve("app.msg.programRewound", "Program rewound");
    }

    private void OnLocaleChanged(object? sender, EventArgs e) =>
        UiThread.Post(() =>
        {
            OnPropertyChanged(nameof(CenterTabs));
            OnPropertyChanged(nameof(RightModuleViews));
            OnPropertyChanged(nameof(SettingsMenu));
            OnPropertyChanged(nameof(LeftModules));
            OnPropertyChanged(nameof(FlyoutModules));
            OnPropertyChanged(nameof(SelectedModuleName));
            OnPropertyChanged(nameof(DroHeader));
            OnPropertyChanged(nameof(Title));
            OnPropertyChanged(nameof(MachineStateDisplay));
            OnPropertyChanged(nameof(Status));
            OnPropertyChanged(nameof(RunTime));
            OnPropertyChanged(nameof(StatusBarJogStep));
            OnPropertyChanged(nameof(StatusBarBlocks));
        });

    public void Dispose()
    {
        // Idempotent: the app may tear down twice, and every step below is only
        // safe to skip after the first pass has run.
        if (_disposed)
            return;
        _disposed = true;

        if (_localization is not null)
            _localization.Changed -= OnLocaleChanged;
        _settingsService.Changed -= OnSettingsChanged;
        _settingsService.ProfileSwitched -= OnProfileSwitched;
        _settingsService.RestorePendingChanged -= OnRestorePendingChanged;
        Modules.CollectionChanged -= OnModulesChanged;
        // Nulling the field stops the Grbl setter re-homing the handler onto the
        // next controller: without it a swap after teardown re-subscribes.
        if (_connectionChangedHandler is not null)
        {
            _grbl.ConnectionChanged -= _connectionChangedHandler;
            _connectionChangedHandler = null;
        }

        if (_program is not null)
        {
            _program.ProgramRows.CollectionChanged -= OnProgramRowsCollectionChanged;
            _program.RowsChanged -= OnProgramRowsChanged;
            _program.PropertyChanged -= OnProgramPropertyChanged;
        }

        _stateSubscription?.Dispose();
        _stateSubscription = null;
        _linesSubscription?.Dispose();
        _linesSubscription = null;
        _workParametersSubscription?.Dispose();
        _workParametersSubscription = null;

        // Only release the static if this instance still owns it; a newer view
        // model must keep its provider.
        if (CNC.Core.Machine.WorkParametersProvider == _workParametersProvider)
            CNC.Core.Machine.WorkParametersProvider = null;

        // Same ownership rule for the status sink. ReferenceEquals, not the
        // delegate == the provider uses: lambdas are never equal to each other,
        // so the identity of the instance we installed is the only reliable test.
        if (_statusSink is not null && ReferenceEquals(_statusSink.Sink, _statusSinkHandler))
            _statusSink.Sink = null;
    }

    private void OnModulesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(LeftModules));
        OnPropertyChanged(nameof(CenterModules));
        OnPropertyChanged(nameof(RightModules));
        OnPropertyChanged(nameof(RightModuleViews));
        OnPropertyChanged(nameof(FlyoutModules));
        OnPropertyChanged(nameof(MenuModules));
        OnPropertyChanged(nameof(SettingsMenu));
        OnPropertyChanged(nameof(CenterTabs));
    }

    // Raised when any consumer saves settings: visibility toggles and mode
    // switches from the App Settings dialog apply live.
    private void OnSettingsChanged()
    {
        OnPropertyChanged(nameof(LeftModules));
        OnPropertyChanged(nameof(RightModules));
        OnPropertyChanged(nameof(RightModuleViews));
        OnPropertyChanged(nameof(FlyoutModules));
        OnPropertyChanged(nameof(MenuModules));
        OnPropertyChanged(nameof(SettingsMenu));
        OnPropertyChanged(nameof(CenterTabs));
        OnPropertyChanged(nameof(StatusBarJogStep));
        OnPropertyChanged(nameof(StatusBarBlocks));
    }

    /// <summary>Cycle bar Start/Rewind only make sense once a program is loaded.</summary>
    private bool HasProgram => _program is { ProgramRows.Count: > 0 };

    /// <summary>A streaming session is active — its state is the machine run
    /// (the machine runs at most one session at a time).</summary>
    private bool SessionActive => _program is { IsRunning: true };

    /// <summary>The active session is feed-held (paused).</summary>
    private bool SessionPaused => _program?.IsPaused == true;

    private bool IsMachineState(string value) =>
        _machineState.Contains(value, StringComparison.OrdinalIgnoreCase);

    private void OnProgramRowsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => RaiseProgramCommands();

    private void OnProgramRowsChanged()
        => OnPropertyChanged(nameof(StatusBarBlocks));

    private void OnProgramPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ProgramViewModel.RunMessage) && _program?.RunMessage is { } m)
            Status = m;
        // Session transitions (start / pause / finish) re-gate the cycle bar.
        if (e.PropertyName is nameof(ProgramViewModel.IsRunning) or nameof(ProgramViewModel.IsPaused))
            RaiseProgramCommands();
    }

    // Session/program state transitions re-gate the whole cycle bar; the
    // machine-state fallback for the no-program path re-raises from
    // ApplyMachineState.
    private void RaiseProgramCommands()
    {
        (CycleStartCommand as DelegateCommand)?.RaiseCanExecuteChanged();
        (FeedHoldCommand as DelegateCommand)?.RaiseCanExecuteChanged();
        (StopCommand as DelegateCommand)?.RaiseCanExecuteChanged();
        (RewindCommand as DelegateCommand)?.RaiseCanExecuteChanged();
        (SaveFileCommand as DelegateCommand)?.RaiseCanExecuteChanged();
        (CloseFileCommand as DelegateCommand)?.RaiseCanExecuteChanged();
        (RotateProgramCommand as DelegateCommand)?.RaiseCanExecuteChanged();
        (ArcsToLinesProgramCommand as DelegateCommand)?.RaiseCanExecuteChanged();
        (CompressProgramCommand as DelegateCommand)?.RaiseCanExecuteChanged();
        (WrapProgramCommand as DelegateCommand)?.RaiseCanExecuteChanged();
        (UnwrapProgramCommand as DelegateCommand)?.RaiseCanExecuteChanged();
        OnPropertyChanged(nameof(StatusBarBlocks));
    }

    private void ApplyState(MachineState state)
    {
        if (state is null) return;
        ApplyMachineState(state.Status);
        UpdateSignals(state);
    }

    private void UpdateSignals(MachineState state)
    {
        var letters = _info.AxisLetters;
        var num = state.NumAxes;
        num = Math.Clamp(num, 0, letters.Length);
        var optional = state.OptionalSignals;

        if (_signalAxisCount != num || _signalOptionalSignals != optional)
            RebuildSignalStrip(num, optional);

        var sig = state.Signals;
        for (var i = 0; i < SignalIndicators.Count; i++)
            SignalIndicators[i].IsActive = (sig & SignalIndicators[i].Flag) != 0;
    }

    /// <summary>Rebuild the signal strip for a given axis count and optional-signal
    /// set — axis/limit LEDs first, then the always-on machine controls (H/S/R/P),
    /// then the optional signals the controller reports (ioSender's gating). Called
    /// on connect (axis count / $I signals known) and on startup seeding; kept
    /// idempotent so it only runs when the count or the signal set changes.</summary>
    private void RebuildSignalStrip(int numAxes, CNC.Core.Signals optionalSignals)
    {
        SignalIndicators.Clear();
        AxisSignalIndicators.Clear();
        ControlSignalIndicators.Clear();
        var letters = _info.AxisLetters;
        for (var i = 0; i < numAxes; i++)
        {
            var letter = letters[i].ToString();
            var indicator = new SignalIndicator(
                letter, (CNC.Core.Signals)(1 << i), $"Limit {letter}");
            SignalIndicators.Add(indicator);
            AxisSignalIndicators.Add(indicator);
        }
        foreach (var (flag, label, tip) in AlwaysControlSignals)
        {
            var indicator = new SignalIndicator(label, flag, tip);
            SignalIndicators.Add(indicator);
            ControlSignalIndicators.Add(indicator);
        }
        foreach (var (flag, label, tip) in OptionalControlSignals)
        {
            if ((optionalSignals & flag) == 0)
                continue;
            var indicator = new SignalIndicator(label, flag, tip);
            SignalIndicators.Add(indicator);
            ControlSignalIndicators.Add(indicator);
        }
        _signalAxisCount = numAxes;
        _signalOptionalSignals = optionalSignals;
    }

    private void ApplyMachineState(string status)
    {
        // Re-entering Run from Hold/Door is a RESUME, not a new cycle: the
        // run-time clock continues from where it froze instead of restarting.
        var wasPaused = _machineState.StartsWith("Hold", StringComparison.OrdinalIgnoreCase) ||
                        _machineState.StartsWith("Door", StringComparison.OrdinalIgnoreCase);
        _machineState = string.IsNullOrWhiteSpace(status) ? "Idle" : status;
        _stateBrush = ResolveStateBrush(_machineState);
        _stateForeground = _machineState.Contains("alarm", StringComparison.OrdinalIgnoreCase)
            ? Brushes.White : Brushes.Black;
        OnPropertyChanged(nameof(MachineState));

        // Status-bar run-time field: start the clock on the Run transition and
        // freeze it when the cycle leaves Run (Hold/Idle/Alarm). The periodic
        // status reports refresh the displayed value while running.
        if (_machineState.Equals("run", StringComparison.OrdinalIgnoreCase))
        {
            if (!_isRunning)
            {
                if (wasPaused)
                    _runStopwatch.Start();   // resume from hold: keep counting
                else
                    _runStopwatch.Restart(); // fresh cycle: start from zero
                _isRunning = true;
            }
            OnPropertyChanged(nameof(RunTime));
        }
        else if (_isRunning)
        {
            // Freeze (Hold/Door/Alarm/Idle). Only a fresh Run entry (above)
            // resets the clock; a resume after a pause just starts it again.
            _runStopwatch.Stop();
            _isRunning = false;
            OnPropertyChanged(nameof(RunTime));
        }
        OnPropertyChanged(nameof(MachineStateDisplay));
        OnPropertyChanged(nameof(IsCheckMode));
        OnPropertyChanged(nameof(StateBrush));
        OnPropertyChanged(nameof(StateForeground));

        // Cycle bar enablement: the session state gates run controls (Start/Hold/
        // Rewind), and Stop still requires machine Hold (the only state a hard
        // reset makes sense from). The no-program fallback also re-gates here.
        RaiseProgramCommands();
    }

    private static readonly IBrush RunBrush = new SolidColorBrush(Color.Parse("#c8e8c8"));
    private static readonly IBrush AlarmBrush = new SolidColorBrush(Color.Parse("#e04545"));
    private static readonly IBrush IdleBrush = new SolidColorBrush(Color.Parse("#ffe8a0"));
    private static readonly IBrush DefaultBrush = new SolidColorBrush(Color.Parse("#e0e0e0"));

    internal static IBrush ResolveStateBrush(string status)
    {
        if (status.Contains("run", StringComparison.OrdinalIgnoreCase)) return RunBrush;
        if (status.Contains("alarm", StringComparison.OrdinalIgnoreCase)) return AlarmBrush;
        if (status.Contains("idle", StringComparison.OrdinalIgnoreCase)) return IdleBrush;
        if (status.Contains("hold", StringComparison.OrdinalIgnoreCase)) return IdleBrush;
        if (status.Contains("check", StringComparison.OrdinalIgnoreCase)) return IdleBrush;
        return DefaultBrush;
    }

    private static IGrblService ResolveInner(IGrblService service) =>
        service is GrblServiceProxy proxy ? proxy.Inner : service;

    /// <summary>Opens the connect dialog. Public so the app can show it right after startup.</summary>
    public async Task ShowConnectDialogAsync()
    {
        try
        {
            await ConnectWithDialogAsync();
        }
        catch (Exception ex)
        {
            Status = LocText.Format("app.msg.connectFailed", "Connection failed: {0}", ex.Message);
        }
    }

    /// <summary>
    /// Opens the connect dialog (Serial / Network / Demo tabs) and, on confirm,
    /// switches the app to the chosen controller mode. When dialog infra is
    /// unavailable (unit tests) it falls back to the persisted port directly.
    /// </summary>
    private async Task ConnectWithDialogAsync()
    {
        // Unit tests build MainViewModel without a dialog service: connect straight
        // to the persisted port (no auto/first-port magic - that moved into ConnectViewModel).
        if (_dialogs is null)
        {
            await ConnectToAsync(_settingsService.Load().Connection.SerialPort);
            return;
        }

        var profile = _settingsService.Load();
        var vm = new CNC.Core.ViewModels.ConnectViewModel(profile, _settingsService.Global, _comPorts, UiThread.Post,
            localization: _localization, profiles: _settingsService.AllProfiles());
        // No fixed size: the dialog grows/shrinks with its tab content.
        var result = await _dialogs.ShowDialogAsync(vm.Title, vm);
        if (result != true)
        {
            Status = LocText.Resolve("app.msg.connectCancelled", "Connect cancelled");
            return;
        }

        var choice = vm.Result
            ?? new CNC.Core.ViewModels.ConnectResult(vm.ResultPort is { Length: > 0 }
                    ? CNC.Core.ViewModels.ConnectMode.Serial
                    : CNC.Core.ViewModels.ConnectMode.Demo,
                vm.ResultPort, vm.ResultBaudRate, null);
        if (choice.ProfileId is { Length: > 0 } pid && pid != profile.Id)
            _settingsService.Activate(pid);
        switch (choice.Mode)
        {
            case CNC.Core.ViewModels.ConnectMode.Network:
                await ConnectNetworkAsync(choice.Endpoint);
                break;
            case CNC.Core.ViewModels.ConnectMode.Demo:
                var active = _settingsService.Load();
                if (active.DemoAxes != choice.DemoAxes)
                {
                    active.DemoAxes = choice.DemoAxes;
                    try
                    {
                        _settingsService.Save(active);
                    }
                    catch
                    {
                        // Best-effort persistence; the demo choice still applies for the session.
                    }
                }
                await ActivateDemoAsync();
                break;
            default:
                // Serial: make sure the DI serial service is active (a previous
                // demo/network session may have swapped the proxy inner).
                SwitchToSerial();
                await ConnectToAsync(choice.Port ?? string.Empty, choice.BaudRate, choice.SerialResetMode);
                break;
        }
    }

    /// <summary>Connect the network service to a grblHAL TCP endpoint (host[:port]).</summary>
    private async Task ConnectNetworkAsync(string? endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint)
            || !CNC.Core.Services.TcpGrblTransport.TryParseEndpoint(endpoint, out _, out _))
        {
            Status = LocText.Resolve("app.msg.invalidEndpoint", "Invalid network endpoint");
            return;
        }

        Status = LocText.Format("app.msg.connectingTo", "Connecting to {0}...", endpoint);
        var network = new RealGrblService(new CNC.Core.Services.TcpGrblTransport());
        _networkService = network;
        SwapInner(network);
        await ConnectToAsync(endpoint, null);
    }

    private async Task ConnectToAsync(string port, int? baud = null, ConnectResetMode resetMode = ConnectResetMode.None)
    {
        if (string.IsNullOrWhiteSpace(port))
        {
            Status = LocText.Resolve("app.msg.noPortSelected", "No port selected");
            return;
        }

        Status = LocText.Format("app.msg.connectingTo", "Connecting to {0}...", port);
        // Apply the chosen (or profile default) serial baud and the On-connect reset
        // action to the real service before it opens the port (network ignores reset).
        if (ResolveInner(Grbl) is RealGrblService real)
        {
            if (baud is not null)
                real.BaudRate = baud.Value;
            real.SerialResetMode = resetMode;
        }
        await Grbl.ConnectAsync(port);
        IsDemo = ResolveInner(Grbl) is DemoGrblService;
        Status = LocText.Format("app.msg.connectedToPort", "Connected: {0}", port);

        // Only a *successful* connection is persisted (profile tier, per connect
        // mode) so the connect dialog re-opens pre-filled with values that worked.
        try
        {
            var profile = _settingsService.Load();
            if (ReferenceEquals(ResolveInner(Grbl), _networkService))
            {
                profile.Connection.LastNetworkEndpoint = port;
            }
            else
            {
                profile.Connection.LastSerialPort = port;
                if (baud is not null)
                    profile.Connection.LastSerialBaudRate = baud.Value;
                profile.Connection.SerialResetMode = resetMode;
            }
            _settingsService.Save(profile);
        }
        catch
        {
            // Best-effort persistence; the connection itself already succeeded.
        }
    }

    /// <summary>Connection events (connect / disconnect / inner swap) refresh the UI state.</summary>
    private void OnConnectionChanged(object? sender, EventArgs e) =>
        UiThread.Post(UpdateConnectionState);

    private void UpdateConnectionState()
    {
        var connected = Grbl.IsConnected;
        if (!SetField(ref _isConnected, connected))
            return;

        OnPropertyChanged(nameof(MachineStateDisplay));
        OnPropertyChanged(nameof(IsCheckMode));
        OnPropertyChanged(nameof(StateBrush));
        OnPropertyChanged(nameof(StateForeground));
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(CanConnect));
        OnPropertyChanged(nameof(ShowDisconnect));
        (ConnectCommand as DelegateCommand)?.RaiseCanExecuteChanged();
        (DisconnectCommand as DelegateCommand)?.RaiseCanExecuteChanged();
        (CheckCommand as DelegateCommand)?.RaiseCanExecuteChanged();
        RaiseProgramCommands(); // Start is gated on IsConnected
        if (!connected)
            Status = LocText.Resolve("app.msg.connectionLost", "Controller disconnected — check the cable and reconnect");
    }
}
