using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows.Input;
using Avalonia.Media;
using CNC.Core.Abstractions;
using CNC.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using SenderNG.Avalonia.Modules.Fixed;
using SenderNG.Avalonia.ViewModels;
using SenderNG.Core.Commands;
using SenderNG.Core.Modules;

namespace SenderNG.Avalonia.ViewModels;

public sealed class AxisSignal : ViewModelBase
{
    private bool _isActive;
    public AxisSignal(string letter) => Letter = letter;
    public string Letter { get; }
    public bool IsActive
    {
        get => _isActive;
        set => SetField(ref _isActive, value);
    }
}

public sealed record CenterTab(IModule Module, object? View);

public sealed record ModuleView(IModule Module, object? View);

public sealed record MenuEntry(string Header, ICommand Command, object? Parameter);

public class MainViewModel : ViewModelBase
{
    private readonly IServiceProvider _services;
    private readonly SettingsService _settingsService;
    private readonly IComPortService? _comPorts;
    private readonly IFileDialogService? _fileDialogs;
    private readonly IDialogService? _dialogs;
    private readonly Func<IModule, object>? _viewFactory;
    private bool _isDemo;
    private IGrblService _grbl;
    private string _status = string.Empty;
    private bool _isConnected;
    private string? _connectedTo;
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
    private bool _probeActive;
    private ProgramViewModel? _program;

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
        _isDemo = ResolveInner(grbl) is DemoGrblService;
        _isConnected = grbl.IsConnected;
        _connectedTo = _isDemo ? "demo" : null;

        // Tolerant resolve: unit tests construct MainViewModel with a minimal provider.
        var droVm = _services.GetService<Modules.Fixed.DroViewModel>();
        DroContent = new DroView { DataContext = droVm };

        foreach (var module in modules ?? Enumerable.Empty<IModule>())
            Modules.Add(module);

        Modules.CollectionChanged += OnModulesChanged;

        // Seed dynamic signals from initial state (DemoAxes) so UI shows X0 Y0 without connecting
        // Use settings DemoAxes as fallback; live State subscription will correct it immediately if BehaviourSubject replays
        var seedAxes = _settingsService.Load().Current.DemoAxes;
        seedAxes = Math.Min(seedAxes, CNC.Core.GrblInfo.AxisLetters.Length);
        seedAxes = Math.Max(1, seedAxes);
        for (var i = 0; i < seedAxes; i++)
            AxisSignals.Add(new AxisSignal(CNC.Core.GrblInfo.AxisLetters[i].ToString()));

        // Recent files persist across sessions via settings.json.
        foreach (var f in _settingsService.Load().RecentFiles)
            if (!string.IsNullOrWhiteSpace(f))
                RecentFiles.Add(f);

        DemoToggleCommand = new DelegateCommand(_ => ToggleDemo());
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
        SelectRecentCommand = new DelegateCommand(p => _ = LoadGCodeFileAsync(p as string));
        // Cycle bar: raw grbl realtime bytes (classic 0x7E/0x21/0x18, grblHAL compatible).
        // Start/Rewind need a loaded program; Stop only from Hold; Feed Hold only while Run.
        CycleStartCommand = new DelegateCommand(_ => SendRealtime(GrblRealtime.CycleStart, "Cycle Start"), _ => HasProgram);
        FeedHoldCommand = new DelegateCommand(_ => SendRealtime(GrblRealtime.FeedHold, "Feed Hold"), _ => IsMachineState("Run"));
        StopCommand = new DelegateCommand(_ => SendRealtime(GrblRealtime.Reset, "Stop"), _ => IsMachineState("Hold"));
        RewindCommand = new DelegateCommand(_ => Rewind(), _ => HasProgram && IsMachineState("Idle"));
        // Status bar buttons (ioSender semantics): Home -> $H, Unlock -> $X, Reset -> realtime 0x18.
        HomeCommand = new DelegateCommand(_ => SendLine("$H", "Home"));
        UnlockCommand = new DelegateCommand(_ => SendLine("$X", "Unlock"));
        ResetCommand = new DelegateCommand(_ => SendRealtime(GrblRealtime.Reset, "Reset"));
        MdiHistoryPrevCommand = new DelegateCommand(_ => MdiHistoryPrev());
        MdiHistoryNextCommand = new DelegateCommand(_ => MdiHistoryNext());
        // placeholder Help/Settings delegates keep Status
        MachineSettingsCommand = new DelegateCommand(_ => Status = "Machine settings (placeholder)");
        DemoMachineSettingsCommand = new DelegateCommand(_ => _ = ShowDemoMachineSettingsAsync());
        OpenMenuModuleCommand = new DelegateCommand(p => _ = OpenMenuModuleAsync(p as IModule));
        AboutCommand = new DelegateCommand(_ => Status = "SenderNG — About (placeholder)");
        LibrariesCommand = new DelegateCommand(_ => Status = "Libraries (placeholder)");
        _mdiHistoryIndex = MdiHistory.Count;

        // Cycle bar Start/Rewind enablement follows the loaded program rows.
        _program = _services.GetService<ProgramViewModel>();
        if (_program is not null)
            _program.ProgramRows.CollectionChanged += (_, _) => RaiseProgramCommands();

        // State indicator: subscribe to grbl State and expose MachineState + StateBrush (RUN green #c8e8c8, IDLE yellow #ffe8a0, ALARM red #e04545, Height 28)
        // Also drives dynamic Signals panel (AxisSignals + ProbeActive) from MachineState.NumAxes / Signals
        _stateSubscription = _grbl.State.Subscribe(s => UiThread.Post(() => ApplyState(s)));

        // Connection state drives the "NOT CONNECTED" indicator and status bar.
        _connectionChangedHandler = OnConnectionChanged;
        _grbl.ConnectionChanged += _connectionChangedHandler;

        // Seeded defaults: the app starts disconnected and makes that obvious.
        Status = "Not connected — choose a controller via Connect (Serial, Network or Demo)";
    }

    public SettingsService SettingsService { get; }

    /// <summary>Hosted DRO module view (per-axis position rows + zero buttons).</summary>
    public object DroContent { get; }

    public string DroHeader => "DRO";

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
            {
                OnPropertyChanged(nameof(Title));
                OnPropertyChanged(nameof(ConnectionStatus));
            }
        }
    }

    /// <summary>Whether the active controller connection is live (drives the state indicator).</summary>
    public bool IsConnected => _isConnected;

    /// <summary>Status-bar connection summary: which controller (if any) is active.</summary>
    public string ConnectionStatus =>
        !IsConnected ? "Not connected"
        : IsDemo ? "Demo mode — controller simulated"
        : string.IsNullOrEmpty(ConnectedTo) ? "Connected"
        : $"Connected: {ConnectedTo}";

    private string? ConnectedTo
    {
        get => _connectedTo;
        set
        {
            if (SetField(ref _connectedTo, value))
                OnPropertyChanged(nameof(ConnectionStatus));
        }
    }

    /// <summary>Window title: demo flag, connection state and loaded file name.</summary>
    public string Title =>
        (IsDemo ? BaseTitleWithDemo : BaseTitleWithoutDemo) + ConnectionTitleSuffix + FileTitleSuffix;

    private string ConnectionTitleSuffix => IsConnected || IsDemo ? "" : " - Not connected";

    private static string BaseTitleWithDemo => "SenderNG (DEMO mode)";
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
        private set => SetField(ref _status, value);
    }

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
        IsConnected ? _machineState.ToUpperInvariant() : "NOT CONNECTED";

    public IBrush StateBrush => IsConnected ? _stateBrush : DisconnectedBrush;
    public IBrush StateForeground => IsConnected ? _stateForeground : Brushes.Black;

    private static readonly IBrush DisconnectedBrush = new SolidColorBrush(Color.Parse("#d8d8d8"));

    public ObservableCollection<IModule> Modules { get; } = new();

    public bool IsModuleVisible(IModule m) =>
        _settingsService.Load().Current.ModulesVisibility.TryGetValue(m.Id, out var v) ? v : m.IsVisibleByDefault;

    public IEnumerable<IModule> LeftModules =>
        Modules.Where(IsModuleVisible).Where(m => m.Panel == PanelHint.Left).OrderBy(m => m.Order);

    public IEnumerable<IModule> CenterModules =>
        Modules.Where(IsModuleVisible).Where(m => m.Panel == PanelHint.Center).OrderBy(m => m.Order);

    public IEnumerable<IModule> RightModules =>
        Modules.Where(IsModuleVisible).Where(m => m.Panel == PanelHint.Right).OrderBy(m => m.Order);

    /// <summary>Right-panel modules paired with their built views (Expander content).</summary>
    public IEnumerable<ModuleView> RightModuleViews =>
        RightModules.Select(m => new ModuleView(m, _viewFactory?.Invoke(m)));

    public IEnumerable<IModule> FlyoutModules =>
        Modules.Where(IsModuleVisible).Where(m => m.Panel == PanelHint.Flyout).OrderBy(m => m.Order);

    public IEnumerable<IModule> MenuModules =>
        Modules.Where(IsModuleVisible).Where(m => m.Panel == PanelHint.Menu).OrderBy(m => m.Order);

    /// <summary>Settings menu entries: fixed items plus one per Menu-claiming module.</summary>
    public IReadOnlyList<MenuEntry> SettingsMenu =>
        [
            new MenuEntry("Machine", MachineSettingsCommand, null),
            new MenuEntry("Demo machine", DemoMachineSettingsCommand, null),
            ..MenuModules.Select(m => new MenuEntry(m.Name, OpenMenuModuleCommand, m)),
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

    public ObservableCollection<AxisSignal> AxisSignals { get; } = new();

    public bool ProbeActive
    {
        get => _probeActive;
        private set => SetField(ref _probeActive, value);
    }

    public IEnumerable<CenterTab> CenterTabs =>
        CenterModules.Select(m => new CenterTab(m, _viewFactory?.Invoke(m)));

    public IModule? SelectedModule
    {
        get => _selectedModule;
        set
        {
            if (SetField(ref _selectedModule, value))
                CurrentModuleView = value is not null ? _viewFactory?.Invoke(value) : null;
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

    public DelegateCommand DemoToggleCommand { get; }

    public ICommand ConnectCommand { get; }

    public ICommand DisconnectCommand { get; }

    public ICommand SendMdiCommand { get; }

    public ICommand OpenFileCommand { get; }

    public ICommand CycleStartCommand { get; }

    public ICommand FeedHoldCommand { get; }

    public ICommand StopCommand { get; }

    public ICommand RewindCommand { get; }

    public ICommand HomeCommand { get; }

    public ICommand UnlockCommand { get; }

    public ICommand ResetCommand { get; }

    public ICommand MdiHistoryPrevCommand { get; }

    public ICommand MdiHistoryNextCommand { get; }

    public ICommand MachineSettingsCommand { get; }

    public ICommand DemoMachineSettingsCommand { get; }

    public DelegateCommand OpenMenuModuleCommand { get; }

    public ICommand AboutCommand { get; }

    public ICommand LibrariesCommand { get; }

    public DelegateCommand SelectModuleCommand { get; }

    public ICommand SelectRecentCommand { get; }

    private void ToggleDemo()
    {
        if (IsDemo)
        {
            // Leaving demo: back to the (disconnected) serial controller service.
            SwitchToSerial();
            ConnectedTo = null;
        }
        else
        {
            _ = ActivateDemoAsync();
        }
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
        SwapInner(demo);
        IsDemo = true;
        ConnectedTo = "demo";
        if (!demo.IsConnected)
            await Grbl.ConnectAsync("demo");
        Status = "Demo mode — controller simulated in software";
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
            Status = $"MDI: {line}";
        }
        catch (Exception ex)
        {
            Status = $"MDI failed: {ex.Message}";
        }
    }

    private async Task OpenFileAsync()
    {
        if (_fileDialogs is null)
        {
            Status = "Open file: no dialog service available";
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
            var errors = GCodeValidator.Validate(text);
            if (errors.Count > 0)
            {
                Status = $"Validation failed: {Path.GetFileName(path)} ({errors.Count}+ error(s))";
                if (_fileDialogs is not null)
                    await _fileDialogs.ShowErrorAsync("G-code validation failed", string.Join(Environment.NewLine, errors));
                return;
            }

            var blocks = text.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries).Length;
            var program = _services.GetRequiredService<ProgramViewModel>();
            program.LoadGCode(text);
            HasProgramLimits = program.HasLimits;
            CurrentFileName = Path.GetFileName(path);

            RecentFiles.Remove(path);
            RecentFiles.Insert(0, path);
            const int MaxRecentFiles = 10;
            while (RecentFiles.Count > MaxRecentFiles)
                RecentFiles.RemoveAt(RecentFiles.Count - 1);
            var appSettings = _settingsService.Load();
            appSettings.RecentFiles = [.. RecentFiles];
            _settingsService.Save(appSettings);
            Status = $"Loaded {Path.GetFileName(path)} ({blocks} blocks)";
        }
        catch (Exception ex)
        {
            Status = $"Open failed: {ex.Message}";
            if (_fileDialogs is not null)
                await _fileDialogs.ShowErrorAsync("Open failed", ex.Message);
        }
    }

    private async Task ShowDemoMachineSettingsAsync()
    {
        if (_dialogs is null)
        {
            Status = "Demo machine settings: no dialog service available";
            return;
        }

        var vm = new CNC.Core.ViewModels.DemoMachineSettingsViewModel(
            _settingsService.Load(),
            _services.GetRequiredService<DemoGrblService>());
        var result = await _dialogs.ShowDialogAsync("Demo machine", vm);
        if (result == true)
            Status = "Demo machine settings updated";
    }

    // Menu-claiming modules (PanelHint.Menu) open as modal dialogs.
    private async Task OpenMenuModuleAsync(IModule? module)
    {
        if (module is null)
            return;
        if (_dialogs is null)
        {
            Status = $"{module.Name}: no dialog service available";
            return;
        }
        try
        {
            await _dialogs.ShowDialogAsync(module.Name, module.CreateViewModel(_services), module.DialogPreferredSize);
        }
        catch (Exception ex)
        {
            Status = $"{module.Name} failed: {ex.Message}";
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
            ConnectedTo = null;
            Status = "Disconnected";
        }
        catch (Exception ex) { Status = $"Disconnect failed: {ex.Message}"; }
    }

    private void SendRealtime(byte code, string label)
    {
        try { Grbl.SendRealtime(code); Status = $"{label} sent"; }
        catch (Exception ex) { Status = $"{label} failed: {ex.Message}"; }
    }

    private void SendLine(string line, string label)
    {
        try { Grbl.SendAsync(line); Status = $"{label} sent"; }
        catch (Exception ex) { Status = $"{label} failed: {ex.Message}"; }
    }

    /// <summary>Rewind the loaded program to the start (local reset, no byte sent - mirrors ioSender RewindFile).</summary>
    private void Rewind()
    {
        var program = _services.GetService<ProgramViewModel>();
        if (program is null || program.ProgramRows.Count == 0)
        {
            Status = "Rewind: no program loaded";
            return;
        }

        program.Rewind();
        Status = "Program rewound";
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
    }

    /// <summary>Cycle bar Start/Rewind only make sense once a program is loaded.</summary>
    private bool HasProgram => _program is { ProgramRows.Count: > 0 };

    private bool IsMachineState(string value) =>
        _machineState.Contains(value, StringComparison.OrdinalIgnoreCase);

    private void RaiseProgramCommands()
    {
        (CycleStartCommand as DelegateCommand)?.RaiseCanExecuteChanged();
        (RewindCommand as DelegateCommand)?.RaiseCanExecuteChanged();
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
        // Clamp to letters length
        num = Math.Min(num, letters.Length);

        while (AxisSignals.Count > num)
            AxisSignals.RemoveAt(AxisSignals.Count - 1);
        while (AxisSignals.Count < num)
            AxisSignals.Add(new AxisSignal(letters[AxisSignals.Count].ToString()));

        for (var i = 0; i < num; i++)
        {
            var flag = (CNC.Core.Signals)(1 << i);
            AxisSignals[i].IsActive = (state.Signals & flag) != 0;
        }

        ProbeActive = (state.Signals & CNC.Core.Signals.Probe) != 0;
    }

    private void ApplyMachineState(string status)
    {
        _machineState = string.IsNullOrWhiteSpace(status) ? "Idle" : status;
        _stateBrush = ResolveStateBrush(_machineState);
        _stateForeground = _machineState.Contains("alarm", StringComparison.OrdinalIgnoreCase)
            ? Brushes.White : Brushes.Black;
        OnPropertyChanged(nameof(MachineState));
        OnPropertyChanged(nameof(MachineStateDisplay));
        OnPropertyChanged(nameof(StateBrush));
        OnPropertyChanged(nameof(StateForeground));

        // Cycle bar enablement follows the machine state: Stop in Hold, Feed Hold in Run,
        // Rewind only when Idle.
        (FeedHoldCommand as DelegateCommand)?.RaiseCanExecuteChanged();
        (StopCommand as DelegateCommand)?.RaiseCanExecuteChanged();
        (RewindCommand as DelegateCommand)?.RaiseCanExecuteChanged();
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
            Status = $"Connection failed: {ex.Message}";
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
            await ConnectToAsync(_settingsService.Load().Current.Port);
            return;
        }

        var settings = _settingsService.Load();
        var vm = new CNC.Core.ViewModels.ConnectViewModel(settings, _comPorts, UiThread.Post);
        // No fixed size: the dialog grows/shrinks with its tab content.
        var result = await _dialogs.ShowDialogAsync("Connect", vm);
        if (result != true)
        {
            Status = "Connect cancelled";
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
            Status = "Invalid network endpoint";
            return;
        }

        Status = $"Connecting to {endpoint}...";
        var network = new RealGrblService(new CNC.Core.Services.TcpGrblTransport());
        _networkService = network;
        SwapInner(network);
        await ConnectToAsync(endpoint, null);
    }

    private async Task ConnectToAsync(string port, int? baud = null, ConnectResetMode resetMode = ConnectResetMode.None)
    {
        if (string.IsNullOrWhiteSpace(port))
        {
            Status = "No port selected";
            return;
        }

        Status = $"Connecting to {port}...";
        // Apply the chosen (or profile default) serial baud and the On-connect reset
        // action to the real service before it opens the port (network ignores reset).
        if (ResolveInner(Grbl) is RealGrblService real)
        {
            if (baud is not null)
                real.BaudRate = baud.Value;
            real.SerialResetMode = resetMode;
        }
        await Grbl.ConnectAsync(port);
        ConnectedTo = port;
        IsDemo = ResolveInner(Grbl) is DemoGrblService;
        Status = $"Connected: {port}";

        // Only a *successful* connection is persisted (global, per connect mode)
        // so the connect dialog re-opens pre-filled with values that worked.
        try
        {
            var app = _settingsService.Load();
            if (ReferenceEquals(ResolveInner(Grbl), _networkService))
            {
                app.LastNetworkEndpoint = port;
            }
            else
            {
                app.LastSerialPort = port;
                if (baud is not null)
                    app.LastSerialBaudRate = baud.Value;
                app.SerialResetMode = resetMode;
            }
            _settingsService.Save(app);
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
        OnPropertyChanged(nameof(StateBrush));
        OnPropertyChanged(nameof(StateForeground));
        OnPropertyChanged(nameof(ConnectionStatus));
        OnPropertyChanged(nameof(Title));
        if (!connected)
            Status = "Controller disconnected — check the cable and reconnect";
    }
}
