using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using CNC.Core.Abstractions;
using CNC.Core.Services;
using SenderNG.Core.Commands;

namespace CNC.Core.ViewModels;

/// <summary>
/// Connect dialog: pick a serial port + baud rate and connect. Port enumeration
/// goes through <see cref="IComPortService"/> so the dialog is cross-platform
/// (Windows COM / Linux /dev/ttyUSB*|ttyACM*); nothing here touches the OS ports
/// directly. Network connect is a deliberate, deferred option — the UI exposes a
/// permanently-disabled section and <see cref="NetworkEnabled"/> stays false until a
/// TCP/Telnet/WebSocket transport is added (see docs/p0-controller-plumbing-plan.md).
/// </summary>
public class ConnectViewModel : ViewModelBase, IClosableDialog
{
    private readonly AppSettings _settings;
    private readonly IComPortService? _ports;
    private string _selectedPort = string.Empty;
    private int _baudRate = 115200;

    public event EventHandler<bool>? CloseRequested;

    public ConnectViewModel(AppSettings settings, IComPortService? comPorts = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _settings = settings;
        _ports = comPorts;

        // Persisted baud otherwise defaults to the standard grbl rate (115200).
        var savedBaud = settings.Current.BaudRate;
        _baudRate = BaudRates.Contains(savedBaud) ? savedBaud : 115200;

        ConnectCommand = new DelegateCommand(_ => Connect(), _ => CanConnect);
        CancelCommand = new DelegateCommand(_ => RequestClose(false));
        RefreshCommand = new DelegateCommand(_ => RefreshPorts());

        RefreshPorts();
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

    // Network connect is deferred (documented). Selections stay false so the UI
    // renders a disabled section explaining the limitation.
    public bool NetworkEnabled => false;
    public string NetworkNote => "TCP / Telnet / WebSocket connect is deferred — see the P0 plan.";

    private bool CanConnect => !string.IsNullOrEmpty(SelectedPort);

    private void RaiseCanConnectChanged() =>
        (ConnectCommand as DelegateCommand)?.RaiseCanExecuteChanged();

    private void Connect()
    {
        if (!CanConnect)
            return;

        // Persist the chosen port + baud so the dialog re-opens pre-filled.
        _settings.Current.Port = SelectedPort;
        _settings.Current.BaudRate = BaudRate;
        RequestClose(true);
    }

    /// <summary>Which port the user selected (valid only after Connect closes true).</summary>
    public string ResultPort => _selectedPort;

    /// <summary>Which baud the user selected (valid only after Connect closes true).</summary>
    public int ResultBaudRate => _baudRate;

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
    }

    protected void RequestClose(bool result) =>
        CloseRequested?.Invoke(this, result);
}