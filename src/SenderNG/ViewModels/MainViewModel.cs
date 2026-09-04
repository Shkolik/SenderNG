
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
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using CNC.Core.Abstractions;
using CNC.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using SenderNG.Localization;
using SenderNG.Modules.Fixed;
using SenderNG.ViewModels;
using SenderNG.Core.Commands;
using SenderNG.Core.Modules;

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
    private readonly Func<IModule, object>? _viewFactory;
    private readonly Dictionary<IModule, object> _moduleViews = new();
    private bool _isDemo;
    private IGrblService _grbl;
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
    private ProgramViewModel? _program;
    private readonly SenderNG.Core.Localization.LocalizationService? _localization;
    private readonly System.Diagnostics.Stopwatch _runStopwatch = new();
    private bool _isRunning;

    public MainViewModel(
        IGrblService grbl,
        SettingsService settings,
        IServiceProvider services,
        IEnumerable<IModule>? modules = null,
        IComPortService? comPorts = null,
        Func<IModule, object>? viewFactory = null,
        IFileDialogService? fileDialogs = null,
        IDialogService? dialogs = null)
    {
        ArgumentNullException.ThrowIfNull(grbl);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(services);

        _grbl = grbl;
        _settingsService = settings;
        SettingsService = settings;
        _services = services;
        _comPorts = comPorts;
        _fileDialogs = fileDialogs;
        _dialogs = dialogs;
        _viewFactory = viewFactory;
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

        Modules.CollectionChanged += OnModulesChanged;

        // Seed dynamic signals from initial state (DemoAxes) so UI shows X0 Y0 without connecting
        // Use settings DemoAxes as fallback; live State subscription will correct it immediately if BehaviourSubject replays
        var seedAxes = _settingsService.Load().DemoAxes;
        seedAxes = Math.Clamp(seedAxes, 1, CNC.Core.GrblInfo.AxisLetters.Length);
        RebuildSignalStrip(seedAxes, _grbl.Capabilities.OptionalSignals);

        // Recent files persist across sessions via global.json.
        foreach (var f in _settingsService.Global.RecentFiles)
            if (!string.IsNullOrWhiteSpace(f))
                RecentFiles.Add(f);

        ConnectCommand = new DelegateCommand(_ => _ = ShowConnectDialogAsync());
        DisconnectCommand = new DelegateCommand(_ => Disconnect());
        SelectModuleCommand = new DelegateCommand(p =>
        {
            if (p is IModule m && SelectedModule == m)
                SelectedModule = null; // clicking the open flyout button toggles it closed
            else
                SelectedModule = p as IModule;
        });
        SendMdiCommand = new DelegateCommand(_ => _ = SendMdiAsync(), _ => !string.IsNullOrWhiteSpace(MdiInput));
        OpenFileCommand = new DelegateCommand(_ => _ = OpenFileAsync());
        CloseFileCommand = new DelegateCommand(_ => CloseFile(), _ => HasProgram && !SessionActive);
        SelectRecentCommand = new DelegateCommand(p => _ = LoadGCodeFileAsync(p as string));
        // Cycle bar — the only run controls (the program pane shows no transport
        // bar). The machine runs at most one streaming session at a time, so the
        // buttons are gated on the session state, not a parallel machine-level
        // state machine: Start = run (idle program) / resume (paused session),
        // Hold = pause the session, Stop = hard reset while Held, Rewind = local
        // rewind. Without a loaded program they fall back to raw realtime bytes
        // (classic 0x7E/0x21/0x18, grblHAL compatible) for MDI runs; the remaining
        // machine-state checks only cover that no-program fallback. Buttons stay
        // visible — enablement (CanExecute) is what changes.
        CycleStartCommand = new DelegateCommand(_ => _ = CycleStartAsync(),
            _ => IsConnected && (SessionPaused || (HasProgram && !SessionActive)
                || (!HasProgram && (IsMachineState("Run") || IsMachineState("Hold")))));
        FeedHoldCommand = new DelegateCommand(_ => FeedHold(),
            _ => (SessionActive && !SessionPaused) || (_program?.IsRunning != true && IsMachineState("Run")));
        StopCommand = new DelegateCommand(_ => StopMachine(), _ => IsMachineState("Hold"));
        RewindCommand = new DelegateCommand(_ => Rewind(), _ => HasProgram && !SessionActive);
        // Status bar buttons (ioSender semantics): Home -> $H, Unlock -> $X, Reset -> realtime 0x18.
        HomeCommand = new DelegateCommand(_ => SendLine("$H", "Home"));
        UnlockCommand = new DelegateCommand(_ => SendLine("$X", "Unlock"));
        // Check toggles grbl check mode ($C): a dry run that verifies g-code and
        // simulates motion without moving the machine. Enabled only when connected.
        CheckCommand = new DelegateCommand(_ => SendLine("$C", "Check mode toggle"), _ => IsConnected);
        ResetCommand = new DelegateCommand(_ => SendRealtime(GrblRealtime.Reset, "Reset"));
        MdiHistoryPrevCommand = new DelegateCommand(_ => MdiHistoryPrev());
        MdiHistoryNextCommand = new DelegateCommand(_ => MdiHistoryNext());
        // The Machine settings menu entry opens App Settings on the Profiles leaf.
        MachineSettingsCommand = new DelegateCommand(_ => _ = ShowAppSettingsAsync(profiles: true));
        DemoMachineSettingsCommand = new DelegateCommand(_ => _ = ShowDemoMachineSettingsAsync());
        OpenMenuModuleCommand = new DelegateCommand(p => _ = OpenMenuModuleAsync(p as IModule));
        AboutCommand = new DelegateCommand(_ => _ = ShowAboutAsync());
        _mdiHistoryIndex = MdiHistory.Count;

        // Cycle bar Start/Rewind enablement follows the loaded program rows; the
        // status bar block counter follows live row statuses, and run messages
        // surface on the status bar.
        _program = _services.GetService<ProgramViewModel>();
        if (_program is not null)
        {
            _program.ProgramRows.CollectionChanged += (_, _) => RaiseProgramCommands();
            _program.RowsChanged += () => OnPropertyChanged(nameof(StatusBarBlocks));
            _program.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(ProgramViewModel.RunMessage) && _program.RunMessage is { } m)
                    Status = m;
                // Session transitions (start / pause / finish) re-gate the cycle bar.
                if (e.PropertyName is nameof(ProgramViewModel.IsRunning) or nameof(ProgramViewModel.IsPaused))
                    RaiseProgramCommands();
            };
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
        CNC.Core.Machine.WorkParametersProvider = () => _latestWorkParameters;

        // Connection state drives the "NOT CONNECTED" indicator and status bar.
        _connectionChangedHandler = OnConnectionChanged;
        _grbl.ConnectionChanged += _connectionChangedHandler;

        // Seeded defaults: the app starts disconnected and makes that obvious.
        Status = LocText.Resolve("app.msg.chooseController", "Not connected — choose a controller via Connect (Serial, Network or Demo)");

        // Status-bar values track live settings (jog step) and program state changes.
        _settingsService.Changed += OnSettingsChanged;
        // A machine swap invalidates the loaded program (different geometry ⇒
        // limits/jog semantics differ), so MainViewModel clears it on switch.
        _settingsService.ProfileSwitched += OnProfileSwitched;
        OnPropertyChanged(nameof(StatusBarJogStep));
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

                // Live-apply visibility/mode changes: AppSettings dialog saves -> Changed fires.
                _settingsService.Changed += OnSettingsChanged;
            }
        }
    }

    public bool IsDemo
    {
        get => _isDemo;
        private set
        {
            if (SetField(ref _isDemo, value))
                OnPropertyChanged(nameof(Title));
        }
    }

    /// <summary>Whether the active controller connection is live (drives the state indicator).</summary>
    public bool IsConnected => _isConnected;

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
        private set
        {
            if (SetField(ref _machineState, value))
                OnPropertyChanged(nameof(StateBrush));
        }
    }

    /// <summary>State indicator text: machine state in all caps, or NOT CONNECTED.</summary>
    public string MachineStateDisplay =>
        IsConnected ? _machineState.ToUpperInvariant() : Resolve("app.state.notConnected", "NOT CONNECTED");

    public IBrush StateBrush => IsConnected ? _stateBrush : DisconnectedBrush;
    public IBrush StateForeground => IsConnected ? _stateForeground : Brushes.Black;

    private static readonly IBrush DisconnectedBrush = new SolidColorBrush(Color.Parse("#d8d8d8"));

    public ObservableCollection<IModule> Modules { get; } = new();

    public bool IsModuleVisible(IModule m) =>
        _settingsService.Load().Modules.TryGetValue(m.Id, out var v) ? v : m.IsVisibleByDefault;

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
            new MenuEntry(Resolve("app.settings.machine", "Machine"), MachineSettingsCommand, null),
            new MenuEntry(Resolve("app.settings.demoMachine", "Demo machine"), DemoMachineSettingsCommand, null),
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


    public IEnumerable<CenterTab> CenterTabs =>
        CenterModules.Select(m => new CenterTab(m, ViewFor(m), LocalizedModuleName(m)));

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

    /// <summary>Unloads the loaded program (File → Close / toolbar Close). Run
    /// control lives on the cycle bar, so there is no toolbar Run — ioSender's
    /// Load/Save/Run/Close strip is deliberately scoped to Open + Close; Save
    /// waits for converters/transforms that produce output worth persisting.</summary>
    public ICommand CloseFileCommand { get; }

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

    public ICommand DemoMachineSettingsCommand { get; }

    public DelegateCommand OpenMenuModuleCommand { get; }

    public ICommand AboutCommand { get; }

    public DelegateCommand SelectModuleCommand { get; }

    public ICommand SelectRecentCommand { get; }

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
        await _dialogs.ShowDialogAsync(module.Name, viewModel, module.DialogPreferredSize);
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
        var window = new Views.AboutView { DataContext = new AboutViewModel() };
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
        var path = await _fileDialogs.OpenFileAsync(
            "Open G-code file",
            "G-code (*.nc;*.ngc;*.gcode;*.gco;*.tap)|*.nc;*.ngc;*.gcode;*.gco;*.tap",
            "All files|*.*");
        if (string.IsNullOrWhiteSpace(path))
            return;

        await LoadGCodeFileAsync(path);
    }

    private async Task LoadGCodeFileAsync(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        try
        {
            var text = await File.ReadAllTextAsync(path);
            // The load gate IS the engine: GCodeProgram parses the file once with
            // the same GCodeParser the streamer consumes, so a file that would
            // fail while sending can never be loaded. Errors carry file line
            // numbers and refuse the load outright (no skip-and-load — a partial
            // program is a different program than the user opened).
            var parsed = GCodeProgram.Parse(text);
            if (parsed.HasErrors)
            {
                Status = LocText.Format("app.msg.validationFailed", "Validation failed: {0} ({1} error(s))", Path.GetFileName(path), parsed.Errors.Count);
                if (_fileDialogs is not null)
                    await _fileDialogs.ShowErrorAsync("G-code validation failed", string.Join(Environment.NewLine, parsed.Errors));
                return;
            }

            var program = _services.GetRequiredService<ProgramViewModel>();
            program.LoadProgram(parsed, text);
            HasProgramLimits = program.HasLimits;
            CurrentFileName = Path.GetFileName(path);

            RecentFiles.Remove(path);
            RecentFiles.Insert(0, path);
            var maxRecent = Math.Max(1, _settingsService.Global.MaxRecentFiles);
            while (RecentFiles.Count > maxRecent)
                RecentFiles.RemoveAt(RecentFiles.Count - 1);
            var global = _settingsService.Global;
            global.RecentFiles = [.. RecentFiles];
            _settingsService.SaveGlobal(global);
            Status = LocText.Format("app.msg.loaded", "Loaded {0} ({1} lines)", Path.GetFileName(path), program.ProgramRows.Count);
        }
        catch (Exception ex)
        {
            Status = LocText.Format("app.msg.openFailed", "Open failed: {0}", ex.Message);
            if (_fileDialogs is not null)
                await _fileDialogs.ShowErrorAsync("Open failed", ex.Message);
        }
    }

    private async Task ShowDemoMachineSettingsAsync()
    {
        if (_dialogs is null)
        {
            Status = LocText.Resolve("app.msg.demoSettingsNoDialog", "Demo machine settings: no dialog service available");
            return;
        }

        var vm = new CNC.Core.ViewModels.DemoMachineSettingsViewModel(
            _settingsService.Load(),
            _services.GetRequiredService<DemoGrblService>());
        var result = await _dialogs.ShowDialogAsync("Demo machine", vm);
        if (result == true)
            Status = LocText.Resolve("app.msg.demoSettingsUpdated", "Demo machine settings updated");
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
        try
        {
            var viewModel = module.CreateViewModel(_services);
            ApplySettingsDialogGuards(viewModel);
            await _dialogs.ShowDialogAsync(module.Name, viewModel, module.DialogPreferredSize);
        }
        catch (Exception ex)
        {
            Status = LocText.Format("app.msg.moduleFailed", "{0} failed: {1}", module.Name, ex.Message);
        }
        finally
        {
            // Modal loops can swallow change-driven layout updates; re-raise
            // everything once the dialog closes so visibility applies immediately.
            OnSettingsChanged();
        }
    }

    private void Disconnect()
    {
        try
        {
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

    private void SendLine(string line, string label)
    {
        try { Grbl.SendAsync(line); Status = LocText.Format("app.msg.sent", "{0} sent", label); }
        catch (Exception ex) { Status = LocText.Format("app.msg.failed", "{0} failed: {1}", label, ex.Message); }
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

    /// <summary>Cycle-bar Stop (enabled while Held): hard reset; ends the streaming session so it never resumes.</summary>
    private void StopMachine()
    {
        _program?.OnHardReset();
        SendRealtime(GrblRealtime.Reset, "Stop");
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
        if (_localization is not null)
            _localization.Changed -= OnLocaleChanged;
        _settingsService.Changed -= OnSettingsChanged;
        _settingsService.ProfileSwitched -= OnProfileSwitched;
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

    // Session/program state transitions re-gate the whole cycle bar; the
    // machine-state fallback for the no-program path re-raises from
    // ApplyMachineState.
    private void RaiseProgramCommands()
    {
        (CycleStartCommand as DelegateCommand)?.RaiseCanExecuteChanged();
        (FeedHoldCommand as DelegateCommand)?.RaiseCanExecuteChanged();
        (StopCommand as DelegateCommand)?.RaiseCanExecuteChanged();
        (RewindCommand as DelegateCommand)?.RaiseCanExecuteChanged();
        (CloseFileCommand as DelegateCommand)?.RaiseCanExecuteChanged();
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
        var letters = CNC.Core.GrblInfo.AxisLetters;
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
        var letters = CNC.Core.GrblInfo.AxisLetters;
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

    private static IBrush ResolveStateBrush(string status)
    {
        if (status.Contains("run", StringComparison.OrdinalIgnoreCase))
            return new SolidColorBrush(Color.Parse("#c8e8c8"));
        if (status.Contains("alarm", StringComparison.OrdinalIgnoreCase))
            return new SolidColorBrush(Color.Parse("#e04545"));
        if (status.Contains("idle", StringComparison.OrdinalIgnoreCase))
            return new SolidColorBrush(Color.Parse("#ffe8a0"));
        if (status.Contains("hold", StringComparison.OrdinalIgnoreCase))
            return new SolidColorBrush(Color.Parse("#ffe8a0"));
        if (status.Contains("check", StringComparison.OrdinalIgnoreCase))
            return new SolidColorBrush(Color.Parse("#ffe8a0"));
        return new SolidColorBrush(Color.Parse("#e0e0e0"));
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
        var vm = new CNC.Core.ViewModels.ConnectViewModel(profile, _settingsService.Global, _comPorts, UiThread.Post);
        // No fixed size: the dialog grows/shrinks with its tab content.
        var result = await _dialogs.ShowDialogAsync("Connect", vm);
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
        switch (choice.Mode)
        {
            case CNC.Core.ViewModels.ConnectMode.Network:
                await ConnectNetworkAsync(choice.Endpoint);
                break;
            case CNC.Core.ViewModels.ConnectMode.Demo:
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
        (CheckCommand as DelegateCommand)?.RaiseCanExecuteChanged();
        RaiseProgramCommands(); // Start is gated on IsConnected
        if (!connected)
            Status = LocText.Resolve("app.msg.connectionLost", "Controller disconnected — check the cable and reconnect");
    }
}
