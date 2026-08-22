using System.Collections.ObjectModel;
using System.Windows.Input;
using CNC.Core.Abstractions;
using CNC.Core.Services;
using SenderNG.Core.Commands;
using SenderNG.Avalonia.ViewModels;
using SenderNG.Core.Modules;

namespace SenderNG.Avalonia.ViewModels;

public class MainViewModel : ViewModelBase
{
    private readonly Func<IGrblService> _demoFactory;
    private readonly Func<IGrblService> _realFactory;
    private bool _isDemo;
    private IGrblService _grbl;

    public MainViewModel(IGrblService grbl, SettingsService settings, Func<IGrblService>? realFactory = null)
    {
        ArgumentNullException.ThrowIfNull(grbl);
        ArgumentNullException.ThrowIfNull(settings);

        _grbl = grbl;
        SettingsService = settings;
        _isDemo = grbl is DemoGrblService;
        _demoFactory = () => new DemoGrblService(settings.Load());
        _realFactory = realFactory ?? (() => new RealGrblService());

        DemoToggleCommand = new DelegateCommand(_ => ToggleDemo());
        ConnectCommand = new DelegateCommand(async _ => await Grbl.ConnectAsync("auto"));
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

    public ObservableCollection<IModule> Modules { get; } = new();

    public ObservableCollection<string> GCodeLines { get; } = new();

    public ObservableCollection<string> ConsoleLines { get; } = new();

    public DelegateCommand DemoToggleCommand { get; }

    public ICommand ConnectCommand { get; }

    private void ToggleDemo()
    {
        IsDemo = !IsDemo;
        Grbl = IsDemo ? _demoFactory() : _realFactory();
    }
}
