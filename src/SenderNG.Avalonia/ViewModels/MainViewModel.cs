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
    private bool _isDemo;
    private IGrblService _grbl;
    private string _status = string.Empty;

    public MainViewModel(
        IGrblService grbl,
        SettingsService settings,
        IServiceProvider services,
        GCodeConsoleViewModel? console = null,
        IEnumerable<IModule>? modules = null)
    {
        ArgumentNullException.ThrowIfNull(grbl);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(services);

        _grbl = grbl;
        _settingsService = settings;
        SettingsService = settings;
        _services = services;
        _isDemo = grbl is DemoGrblService;
        Console = console;

        foreach (var module in modules ?? Enumerable.Empty<IModule>())
            Modules.Add(module);

        DemoToggleCommand = new DelegateCommand(_ => ToggleDemo());
        ConnectCommand = new DelegateCommand(_ => _ = ConnectAsync());
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

    public GCodeConsoleViewModel? Console { get; }

    public DelegateCommand DemoToggleCommand { get; }

    public ICommand ConnectCommand { get; }

    private void ToggleDemo()
    {
        IsDemo = !IsDemo;
        Grbl = IsDemo
            ? _services.GetRequiredService<DemoGrblService>()
            : _services.GetRequiredService<RealGrblService>();
    }

    private async Task ConnectAsync()
    {
        try
        {
            var port = _settingsService.Load().Port;
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
