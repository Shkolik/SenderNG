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

        DemoToggleCommand = new DelegateCommand(_ => ToggleDemo());
        ConnectCommand = new DelegateCommand(_ => _ = ConnectAsync());
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
        // Cycle bar delegates (grblHAL realtime)
        // TODO: Cycle commands currently use SendAsync(string) placeholder; future: use realtime byte API (e.g., SendRealtime) for "~", "!", 0x18
        CycleStartCommand = new DelegateCommand(_ => _ = CycleAsync("~", "Cycle Start"));
        FeedHoldCommand = new DelegateCommand(_ => _ = CycleAsync("!", "Feed Hold"));
        StopCommand = new DelegateCommand(_ => _ = CycleAsync("\x18", "Stop"));
        RewindCommand = new DelegateCommand(_ => Rewind());
        MdiHistoryPrevCommand = new DelegateCommand(_ => MdiHistoryPrev());
        MdiHistoryNextCommand = new DelegateCommand(_ => MdiHistoryNext());
        // placeholder Help/Settings delegates keep Status
        MachineSettingsCommand = new DelegateCommand(_ => Status = "Machine settings (placeholder)");
        DemoMachineSettingsCommand = new DelegateCommand(_ => _ = ShowDemoMachineSettingsAsync());
        OpenMenuModuleCommand = new DelegateCommand(p => _ = OpenMenuModuleAsync(p as IModule));
        AboutCommand = new DelegateCommand(_ => Status = "SenderNG — About (placeholder)");
        LibrariesCommand = new DelegateCommand(_ => Status = "Libraries (placeholder)");
        _mdiHistoryIndex = MdiHistory.Count;

        // State indicator: subscribe to grbl State and expose MachineState + StateBrush (RUN green #c8e8c8, IDLE yellow #ffe8a0, ALARM red #e04545, Height 28)
        // Also drives dynamic Signals panel (AxisSignals + ProbeActive) from MachineState.NumAxes / Signals
        _stateSubscription = _grbl.State.Subscribe(s => ApplyState(s));

        // Demo mode connects automatically so commands work without manual Connect.
        if (_isDemo)
            _ = AutoConnectDemoAsync();
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
            if (SetField(ref _grbl, value))
            {
                _stateSubscription?.Dispose();
        _stateSubscription = _grbl.State.Subscribe(s => ApplyState(s));

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

    /// <summary>Window title: demo flag and loaded file name.</summary>
    public string Title =>
        IsDemo ? BaseTitleWithDemo + FileTitleSuffix : BaseTitleWithoutDemo + FileTitleSuffix;

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

    public IBrush StateBrush => _stateBrush;
    public IBrush StateForeground => _stateForeground;

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
        IsDemo = !IsDemo;
        IGrblService target = IsDemo
            ? _services.GetRequiredService<DemoGrblService>()
            : _services.GetRequiredService<RealGrblService>();

        if (Grbl is GrblServiceProxy proxy)
        {
            // Swap the inner service in place so existing subscribers keep working.
            proxy.Inner = target;
        }
        else
        {
            Grbl = target;
        }

        if (IsDemo)
            _ = AutoConnectDemoAsync();
    }

    /// <summary>Demo mode needs no port - connect automatically so commands just work.</summary>
    private async Task AutoConnectDemoAsync()
    {
        var inner = ResolveInner(Grbl);
        if (inner is DemoGrblService demo && !demo.IsConnected)
        {
            try
            {
                await Grbl.ConnectAsync("demo");
            }
            catch
            {
                // Demo connect cannot realistically fail; ignore to keep ctor/toggle path safe.
            }
        }
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
            await _dialogs.ShowDialogAsync(module.Name, module.CreateViewModel(_services));
        }
        catch (Exception ex)
        {
            Status = $"{module.Name} failed: {ex.Message}";
        }
    }

    private void Disconnect()
    {
        try { Grbl.Disconnect(); Status = "Disconnected"; }
        catch (Exception ex) { Status = $"Disconnect failed: {ex.Message}"; }
    }

    // TODO: Cycle commands via SendAsync(string) placeholder; future: use realtime byte API (e.g., SendRealtime) for grblHAL realtime commands
    private async Task CycleAsync(string payload, string label)
    {
        // TODO: replace SendAsync(string) with SendRealtime(byte) for "~" (Cycle Start), "!" (Feed Hold), 0x18 (Reset/Stop)
        try { await Grbl.SendAsync(payload); Status = label; }
        catch (Exception ex) { Status = $"{label} failed: {ex.Message}"; }
    }

    // TODO: Rewind should use realtime/control API if applicable; placeholder Status for now
    private void Rewind()
    {
        Status = "Rewind (placeholder)";
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
        OnPropertyChanged(nameof(StateBrush));
        OnPropertyChanged(nameof(StateForeground));
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

    private async Task ConnectAsync()
    {
        try
        {
            var port = _settingsService.Load().Current.Port;
            if (port == "auto" && _comPorts is not null)
            {
                var ports = _comPorts?.GetPorts() ?? [];
                if (ports.Length == 0)
                {
                    Status = "No ports found";
                    return;
                }
                port = ports[0];
            }
            Status = $"Connecting to {port}...";
            await Grbl.ConnectAsync(port);
            Status = $"Connected: {port}";
        }
        catch (Exception ex)
        {
            Status = $"Connection failed: {ex.Message}";
        }
    }
}
