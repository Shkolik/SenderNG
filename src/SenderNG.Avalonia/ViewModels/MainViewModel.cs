using System.Collections.ObjectModel;
using System.Windows.Input;
using CNC.Core.Abstractions;
using CNC.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using SenderNG.Avalonia.Modules.Fixed;
using SenderNG.Avalonia.ViewModels;
using SenderNG.Core.Commands;
using SenderNG.Core.Modules;

namespace SenderNG.Avalonia.ViewModels;

public class MainViewModel : ViewModelBase
{
    private readonly IServiceProvider _services;
    private readonly SettingsService _settingsService;
    private readonly IComPortService? _comPorts;
    private readonly Func<IModule, object>? _viewFactory;
    private bool _isDemo;
    private IGrblService _grbl;
    private string _status = string.Empty;
    private IModule? _selectedModule;
    private object? _currentModuleView;
    private string _mdiInput = string.Empty;
    private int _mdiHistoryIndex;

    public MainViewModel(
        IGrblService grbl,
        SettingsService settings,
        IServiceProvider services,
        GCodeConsoleViewModel? console = null,
        IEnumerable<IModule>? modules = null,
        IComPortService? comPorts = null,
        Func<IModule, object>? viewFactory = null)
    {
        ArgumentNullException.ThrowIfNull(grbl);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(services);

        _grbl = grbl;
        _settingsService = settings;
        SettingsService = settings;
        _services = services;
        _comPorts = comPorts;
        _viewFactory = viewFactory;
        _isDemo = ResolveInner(grbl) is DemoGrblService;
        Console = console;

        foreach (var module in modules ?? Enumerable.Empty<IModule>())
            Modules.Add(module);

        DemoToggleCommand = new DelegateCommand(_ => ToggleDemo());
        ConnectCommand = new DelegateCommand(_ => _ = ConnectAsync());
        DisconnectCommand = new DelegateCommand(_ => Disconnect());
        SelectModuleCommand = new DelegateCommand(p => SelectedModule = p as IModule);
        SendMdiCommand = new DelegateCommand(_ => _ = SendMdiAsync(), _ => !string.IsNullOrWhiteSpace(MdiInput));
        OpenFileCommand = new DelegateCommand(_ => OpenFile());
        // Cycle bar delegates (grblHAL realtime)
        CycleStartCommand = new DelegateCommand(_ => _ = CycleAsync("~", "Cycle Start"));
        FeedHoldCommand = new DelegateCommand(_ => _ = CycleAsync("!", "Feed Hold"));
        StopCommand = new DelegateCommand(_ => _ = CycleAsync("\x18", "Stop"));
        RewindCommand = new DelegateCommand(_ => Rewind());
        MdiHistoryPrevCommand = new DelegateCommand(_ => MdiHistoryPrev());
        MdiHistoryNextCommand = new DelegateCommand(_ => MdiHistoryNext());
        // placeholder Help/Settings delegates keep Status
        MachineSettingsCommand = new DelegateCommand(_ => Status = "Machine settings (placeholder)");
        AppSettingsCommand = new DelegateCommand(_ => Status = "App settings (placeholder)");
        AboutCommand = new DelegateCommand(_ => Status = "SenderNG — About (placeholder)");
        LibrariesCommand = new DelegateCommand(_ => Status = "Libraries (placeholder)");
        _mdiHistoryIndex = MdiHistory.Count;
    }

    public SettingsService SettingsService { get; }

    public IGrblService Grbl
    {
        get => _grbl;
        private set => SetField(ref _grbl, value);
    }

    public bool IsDemo
    {
        get => _isDemo;
        private set => SetField(ref _isDemo, value);
    }

    public string Status
    {
        get => _status;
        private set => SetField(ref _status, value);
    }

    public ObservableCollection<IModule> Modules { get; } = new();

    public bool IsModuleVisible(IModule m) =>
        _settingsService.Load().ModulesVisibility.TryGetValue(m.Id, out var v) ? v : m.IsVisibleByDefault;

    public IEnumerable<IModule> LeftModules =>
        Modules.Where(IsModuleVisible).Where(m => m.Panel == PanelHint.Left).OrderBy(m => m.Order);

    public IEnumerable<IModule> CenterModules =>
        Modules.Where(IsModuleVisible).Where(m => m.Panel == PanelHint.Center).OrderBy(m => m.Order);

    public IEnumerable<IModule> RightModules =>
        Modules.Where(IsModuleVisible).Where(m => m.Panel == PanelHint.Right).OrderBy(m => m.Order);

    public IEnumerable<IModule> FlyoutModules =>
        Modules.Where(IsModuleVisible).Where(m => m.Panel == PanelHint.Flyout).OrderBy(m => m.Order);

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

    public GCodeConsoleViewModel? Console { get; }

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

    public ICommand AppSettingsCommand { get; }

    public ICommand AboutCommand { get; }

    public ICommand LibrariesCommand { get; }

    public DelegateCommand SelectModuleCommand { get; }

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
            Console?.Log($"> {line}");
            Status = $"MDI: {line}";
        }
        catch (Exception ex)
        {
            Status = $"MDI failed: {ex.Message}";
        }
    }

    private void OpenFile()
    {
        Status = "Open file... (placeholder)";
        // keep RecentFiles extensible
        if (!string.IsNullOrWhiteSpace(MdiInput) && !RecentFiles.Contains(MdiInput))
            RecentFiles.Add(MdiInput);
    }

    private void Disconnect()
    {
        try { Grbl.Disconnect(); Status = "Disconnected"; }
        catch (Exception ex) { Status = $"Disconnect failed: {ex.Message}"; }
    }

    private async Task CycleAsync(string payload, string label)
    {
        try { await Grbl.SendAsync(payload); Status = label; }
        catch (Exception ex) { Status = $"{label} failed: {ex.Message}"; }
    }

    private void Rewind()
    {
        Status = "Rewind (placeholder)";
        Console?.Log("Rewind");
    }

    private static IGrblService ResolveInner(IGrblService service) =>
        service is GrblServiceProxy proxy ? proxy.Inner : service;

    private async Task ConnectAsync()
    {
        try
        {
            var port = _settingsService.Load().Port;
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
