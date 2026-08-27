using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using CNC.Core.Abstractions;
using CNC.Core.Services;
using SenderNG.Core.Commands;

namespace CNC.Core.ViewModels;

/// <summary>How the app connects to a controller, chosen in the connect dialog.</summary>
public enum ConnectMode
{
    Serial,
    Network,
    Demo,
}

/// <summary>What the user picked in the connect dialog (valid after Connect closes true).</summary>
public sealed record ConnectResult(ConnectMode Mode, string? Port, int BaudRate, string? Endpoint);

/// <summary>
/// Connect dialog: three tabs — Serial (port list + refresh + baud), Network
/// (TCP endpoint for grblHAL Ethernet boards) and Demo (simulated controller).
/// Serial is grayed out when no ports are enumerated, Network only when the
/// settings enable network connectivity, Demo is always available and switches
/// the app into demo mode. Port enumeration goes through
/// <see cref="IComPortService"/> so the dialog is cross-platform; the network
/// transport is <see cref="TcpGrblTransport"/> behind <see cref="RealGrblService"/>.
/// </summary>
public class ConnectViewModel : ViewModelBase, IClosableDialog
{
    private readonly AppSettings _settings;
    private readonly IComPortService? _ports;
    private string _selectedPort = string.Empty;
    private int _baudRate = 115200;
    private string _networkEndpoint = string.Empty;
    private int _selectedIndex;

    public event EventHandler<bool>? CloseRequested;

    public ConnectViewModel(AppSettings settings, IComPortService? comPorts = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _settings = settings;
        _ports = comPorts;

        // Persisted baud otherwise defaults to the standard grbl rate (115200).
        var savedBaud = settings.Current.BaudRate;
        _baudRate = BaudRates.Contains(savedBaud) ? savedBaud : 115200;
        _networkEndpoint = string.IsNullOrWhiteSpace(settings.NetworkEndpoint)
            ? "192.168.1.100:23"
            : settings.NetworkEndpoint;

        ConnectCommand = new DelegateCommand(_ => Connect(), _ => CanConnect);
        CancelCommand = new DelegateCommand(_ => RequestClose(false));
        RefreshCommand = new DelegateCommand(_ => RefreshPorts());

        RefreshPorts();

        // Default to the first usable tab: Serial when ports exist, otherwise
        // Network when enabled, otherwise Demo (always available).
        SelectedIndex = HasPorts ? 0 : NetworkEnabled ? 1 : 2;
    }

    /// <summary>Standard grbl / grblHAL baud choices (cross-platform friendly).</summary>
    public IReadOnlyList<int> BaudRates { get; } = [9600, 19200, 38400, 57600, 115200, 230400, 250000];

    public ObservableCollection<string> Ports { get; } = new();

    public DelegateCommand ConnectCommand { get; }

    public DelegateCommand CancelCommand { get; }

    public DelegateCommand RefreshCommand { get; }

    public string SelectedPort
    {
        get => _selectedPort;
        set
        {
            if (SetField(ref _selectedPort, value ?? string.Empty))
                RaiseCanConnectChanged();
        }
    }

    public int BaudRate
    {
        get => _baudRate;
        set => SetField(ref _baudRate, value);
    }

    /// <summary>Selected tab as index (0 Serial, 1 Network, 2 Demo) for TabControl.SelectedIndex.</summary>
    public int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            if (SetField(ref _selectedIndex, Math.Clamp(value, 0, 2)))
            {
                OnPropertyChanged(nameof(SelectedTab));
                OnPropertyChanged(nameof(ConnectButtonLabel));
                RaiseCanConnectChanged();
            }
        }
    }

    public ConnectMode SelectedTab => (ConnectMode)SelectedIndex;

    /// <summary>Serial tab usable: at least one port was enumerated.</summary>
    public bool HasPorts => Ports.Count > 0;

    /// <summary>Serial tab content is grayed out when no ports are found.</summary>
    public bool SerialEnabled => HasPorts;

    /// <summary>Network tab is only available when the global settings say network connectivity is available.</summary>
    public bool NetworkEnabled => _settings.NetworkAvailable;

    public string NoPortsHint => HasPorts
        ? string.Empty
        : "No serial ports found — plug in a controller and press ⟳ to refresh.";

    public string NetworkNote => NetworkEnabled
        ? "Connect to a grblHAL controller with Ethernet/WiFi (TCP). Example: 192.168.1.100:23"
        : "Network connectivity is not enabled in settings (settings.json → NetworkAvailable).";

    /// <summary>Describes demo mode inside the Demo tab (the user must opt in knowingly).</summary>
    public string DemoDisclaimer =>
        "Demo mode runs a simulated grbl controller entirely inside the app — " +
        "no serial or network connection is made. Jogging, MDI, probing, coolant and " +
        "cycle commands are emulated against a virtual machine, so nothing is sent to " +
        "real hardware and positions/settings are not real. Use it to explore SenderNG " +
        "without a controller.";

    public string ConnectButtonLabel => SelectedTab == ConnectMode.Demo ? "Start demo" : "Connect";

    public string NetworkEndpoint
    {
        get => _networkEndpoint;
        set
        {
            if (SetField(ref _networkEndpoint, value ?? string.Empty))
                RaiseCanConnectChanged();
        }
    }

    /// <summary>The user's choice (valid only after Connect closes true).</summary>
    public ConnectResult? Result { get; private set; }

    /// <summary>Which serial port the user selected (valid only after Connect closes true).</summary>
    public string ResultPort => Result?.Port ?? string.Empty;

    /// <summary>Which baud the user selected (valid only after Connect closes true).</summary>
    public int ResultBaudRate => Result?.BaudRate ?? 0;

    /// <summary>Which network endpoint the user selected (valid only after Connect closes true).</summary>
    public string ResultEndpoint => Result?.Endpoint ?? string.Empty;

    private bool CanConnect => SelectedTab switch
    {
        ConnectMode.Serial => !string.IsNullOrEmpty(SelectedPort),
        ConnectMode.Network => TcpGrblTransport.TryParseEndpoint(NetworkEndpoint, out _, out _),
        _ => true,
    };

    private void RaiseCanConnectChanged() =>
        (ConnectCommand as DelegateCommand)?.RaiseCanExecuteChanged();

    private void Connect()
    {
        if (!CanConnect)
            return;

        switch (SelectedTab)
        {
            case ConnectMode.Serial:
                // Persist the chosen port + baud so the dialog re-opens pre-filled.
                _settings.Current.Port = SelectedPort;
                _settings.Current.BaudRate = BaudRate;
                Result = new ConnectResult(ConnectMode.Serial, SelectedPort, BaudRate, null);
                break;
            case ConnectMode.Network:
                _settings.NetworkEndpoint = NetworkEndpoint?.Trim() ?? string.Empty;
                Result = new ConnectResult(ConnectMode.Network, null, 0, NetworkEndpoint?.Trim());
                break;
            default:
                Result = new ConnectResult(ConnectMode.Demo, null, 0, null);
                break;
        }

        RequestClose(true);
    }

    public void RefreshPorts()
    {
        var ports = _ports?.GetPorts() ?? Array.Empty<string>();
        var previous = _selectedPort;

        Ports.Clear();
        foreach (var p in ports)
            Ports.Add(p);

        // Prefer the persisted port, then the first enumerated one.
        SelectedPort = ports.Contains(previous) ? previous
            : ports.Contains(_settings.Current.Port) ? _settings.Current.Port
            : ports.FirstOrDefault() ?? string.Empty;

        OnPropertyChanged(nameof(HasPorts));
        OnPropertyChanged(nameof(SerialEnabled));
        OnPropertyChanged(nameof(NoPortsHint));
        RaiseCanConnectChanged();
    }

    protected void RequestClose(bool result) =>
        CloseRequested?.Invoke(this, result);
}
