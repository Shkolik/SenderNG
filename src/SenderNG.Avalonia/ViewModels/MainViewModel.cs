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
        SelectModuleCommand = new DelegateCommand(p => SelectedModule = p as IModule);
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

    public DelegateCommand DemoToggleCommand { get; }

    public ICommand ConnectCommand { get; }

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
