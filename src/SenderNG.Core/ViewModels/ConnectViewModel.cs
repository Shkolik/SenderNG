
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

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Windows.Input;
using CNC.Core.Abstractions;
using CNC.Core.Services;
using SenderNG.Core.Commands;
using SenderNG.Core.Localization;

namespace CNC.Core.ViewModels;

/// <summary>How the app connects to a controller, chosen in the connect dialog.</summary>
public enum ConnectMode
{
    Serial,
    Network,
    Demo,
}

/// <summary>What the user picked in the connect dialog (valid after Connect closes true).</summary>
public sealed record ConnectResult(ConnectMode Mode, string? Port, int BaudRate, string? Endpoint,
    ConnectResetMode SerialResetMode = ConnectResetMode.None, string? ProfileId = null, int DemoAxes = 3);

/// <summary>A named "On connect" reset option for the Serial tab (ioSender dropdown).</summary>
public sealed record ResetModeOption(ConnectResetMode Mode, string Name);

/// <summary>
/// Connect dialog: three tabs — Serial (live auto-refreshing port list + baud),
/// Network (TCP endpoint for grblHAL Ethernet boards) and Demo (simulated controller).
/// Serial is grayed out when no ports are enumerated, Network only when the
/// settings enable network connectivity, Demo is always available and switches
/// the app into demo mode. Port enumeration goes through
/// <see cref="IComPortService"/> so the dialog is cross-platform; the network
/// transport is <see cref="TcpGrblTransport"/> behind <see cref="RealGrblService"/>.
/// </summary>
public class ConnectViewModel : ViewModelBase, IClosableDialog, IDisposable
{
    private const int AutoRefreshIntervalMs = 1000;

    private readonly GlobalSettings _global;
    private readonly IComPortService? _ports;
    private readonly System.Action<System.Action>? _ui;
    private readonly LocalizationService? _localization;
    private MachineProfile _profile;
    private IReadOnlyList<MachineProfile> _allProfiles;
    private ProfileItem? _selectedProfile;
    private Timer? _autoRefreshTimer;
    private string _selectedPort = string.Empty;
    private int _baudRate = 115200;
    private ConnectResetMode _resetMode = ConnectResetMode.None;
    private string _networkEndpoint = string.Empty;
    private int _demoAxes = 3;
    private int _selectedIndex;

    public event EventHandler<bool>? CloseRequested;

    /// <param name="ui">
    /// Optional marshaller that runs an action on the UI thread. When absent
    /// (e.g. unit tests, headless) the auto-refresh applies directly.
    /// </param>
    public ConnectViewModel(MachineProfile profile, GlobalSettings global, IComPortService? comPorts = null,
        System.Action<System.Action>? ui = null, LocalizationService? localization = null,
        IEnumerable<MachineProfile>? profiles = null)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(global);
        _profile = profile;
        _global = global;
        _ports = comPorts;
        _ui = ui;
        _localization = localization ?? LocalizationService.Current;

        ResetModeOptions =
        [
            new(ConnectResetMode.None, Resolve("connect.reset.none", "No action")),
            new(ConnectResetMode.ToggleDtr, Resolve("connect.reset.dtr", "Toggle DTR")),
            new(ConnectResetMode.ToggleRts, Resolve("connect.reset.rts", "Toggle RTS")),
        ];
        var list = profiles?.ToList();
        _allProfiles = list is null || list.Count == 0 ? [profile] : list;
        Profiles = _allProfiles.Select(p => new ProfileItem(p.Id, p.Name, p.MachineTypeId)).ToList();
        _selectedProfile = Profiles.FirstOrDefault(p => p.Id == profile.Id) ?? Profiles[0];

        ConnectCommand = new DelegateCommand(_ => Connect(), _ => CanConnect);
        CancelCommand = new DelegateCommand(_ => RequestClose(false));

        ApplyPrefill(_profile);

        // Default to the first usable tab: Serial when ports exist, otherwise
        // Network when enabled, otherwise Demo (always available).
        SelectedIndex = HasPorts ? 0 : NetworkEnabled ? 1 : 2;

        // Live-enumerate while the dialog is open: plugging in a controller
        // revives the (otherwise grayed-out) Serial tab without a manual refresh.
        _autoRefreshTimer = new Timer(_ => AutoRefreshPorts(), null, AutoRefreshIntervalMs, AutoRefreshIntervalMs);
    }

    /// <summary>Standard grbl / grblHAL baud choices (cross-platform friendly).</summary>
    public IReadOnlyList<int> BaudRates { get; } = [9600, 19200, 38400, 57600, 115200, 230400, 250000];

    /// <summary>Serial "On connect" reset options (ioSender dropdown): No action / Toggle DTR / Toggle RTS.
    /// Names resolve through the active locale when a localization service is set.</summary>
    public IReadOnlyList<ResetModeOption> ResetModeOptions { get; }

    /// <summary>Selected "On connect" reset action (persisted; applied when the serial port opens).</summary>
    public ConnectResetMode SelectedResetMode
    {
        get => _resetMode;
        set { if (SetField(ref _resetMode, value)) OnPropertyChanged(nameof(SelectedResetModeOption)); }
    }

    /// <summary>The matching named option, for binding the ComboBox selection.</summary>
    public ResetModeOption SelectedResetModeOption
    {
        get => ResetModeOptions.First(r => r.Mode == _resetMode);
        set
        {
            if (value is not null)
                SelectedResetMode = value.Mode;
        }
    }

    /// <summary>Ports as displayed in the Serial tab ("COM7 - USB Serial Device" when known).</summary>
    public ObservableCollection<SerialPortEntry> Ports { get; } = new();

    public DelegateCommand ConnectCommand { get; }

    public DelegateCommand CancelCommand { get; }

    public string SelectedPort
    {
        get => _selectedPort;
        set
        {
            if (SetField(ref _selectedPort, value ?? string.Empty))
            {
                OnPropertyChanged(nameof(SelectedPortEntry));
                RaiseCanConnectChanged();
            }
        }
    }

    /// <summary>The selected <see cref="Ports"/> entry (the ComboBox binds this;
    /// <see cref="SelectedPort"/> stays the plain name used to open the port).</summary>
    public SerialPortEntry? SelectedPortEntry
    {
        get => Ports.FirstOrDefault(p => p.Name == _selectedPort);
        set
        {
            if (value is not null)
                SelectedPort = value.Name;
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

    public IReadOnlyList<ProfileItem> Profiles { get; }

    public bool ProfileSelectorVisible => Profiles.Count > 1;

    public ProfileItem SelectedProfile
    {
        get => _selectedProfile!;
        set
        {
            if (value is null || value.Id == _selectedProfile?.Id) return;
            var profile = _allProfiles.FirstOrDefault(p => p.Id == value.Id);
            if (profile is null) return;
            _selectedProfile = value;
            OnPropertyChanged();
            ApplyPrefill(profile);
        }
    }

    /// <summary>Serial tab usable: at least one port was enumerated.</summary>
    public bool HasPorts => Ports.Count > 0;

    /// <summary>Serial tab content is grayed out when no ports are found.</summary>
    public bool SerialEnabled => HasPorts;

    /// <summary>Network tab is only available when the global settings say network connectivity is available.</summary>
    public bool NetworkEnabled => _global.NetworkAvailable;

    private string Resolve(string key, string fallback) =>
        _localization is null ? fallback : _localization.Resolve(key, fallback);

    public string NoPortsHint => HasPorts
        ? string.Empty
        : Resolve("connect.noPortsHint", "No serial ports found — plug in a controller and it will be detected automatically.");

    public string NetworkNote => NetworkEnabled
        ? Resolve("connect.networkNote", "Connect to a grblHAL controller with Ethernet/WiFi (TCP). Example: 192.168.1.100:23")
        : Resolve("connect.networkNoteDisabled", "Network connectivity is not enabled in settings (settings.json → NetworkAvailable).");

    /// <summary>Describes demo mode inside the Demo tab (the user must opt in knowingly).</summary>
    public string DemoDisclaimer => Resolve("connect.demoDisclaimer",
        "Demo mode simulates a controller locally so you can evaluate the app without hardware. Machine movement is not real.");

    /// <summary>Axis counts the demo controller can simulate (the Connect dialog offers only these).</summary>
    public IReadOnlyList<int> DemoAxisOptions { get; } = [3, 4, 5];

    /// <summary>Axes chosen for the next demo session (persisted by the caller on connect).</summary>
    public int DemoAxes
    {
        get => _demoAxes;
        set => SetField(ref _demoAxes, value);
    }

    public string ConnectButtonLabel => SelectedTab == ConnectMode.Demo
        ? Resolve("connect.startDemo", "Start demo")
        : Resolve("connect.connect", "Connect");

    /// <summary>Dialog window title, resolved through the active locale (the
    /// About dialog's <c>about.title</c> pattern); English fallback.</summary>
    public string Title => Resolve("connect.title", "Connect");

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
                Result = new ConnectResult(ConnectMode.Serial, SelectedPort, BaudRate, null, SelectedResetMode, _profile.Id);
                break;
            case ConnectMode.Network:
                Result = new ConnectResult(ConnectMode.Network, null, 0, NetworkEndpoint?.Trim(), ProfileId: _profile.Id);
                break;
            default:
                Result = new ConnectResult(ConnectMode.Demo, null, 0, null, ProfileId: _profile.Id, DemoAxes: DemoAxes);
                break;
        }

        // Persistence of the last successful connection happens in the caller
        // (MainViewModel) only after the connect actually succeeds.
        RequestClose(true);
    }

    private void ApplyPrefill(MachineProfile profile)
    {
        _profile = profile;
        var savedBaud = profile.Connection.LastSerialBaudRate > 0
            ? profile.Connection.LastSerialBaudRate
            : profile.Connection.SerialBaudRate;
        _baudRate = BaudRates.Contains(savedBaud) ? savedBaud : 115200;
        _networkEndpoint = !string.IsNullOrWhiteSpace(profile.Connection.LastNetworkEndpoint)
            ? profile.Connection.LastNetworkEndpoint
            : "192.168.1.100:23";
        _resetMode = ResetModeOptions.FirstOrDefault(
            r => r.Mode == profile.Connection.SerialResetMode, ResetModeOptions[0]).Mode;
        _demoAxes = Math.Clamp(profile.DemoAxes, DemoAxisOptions[0], DemoAxisOptions[^1]);
        OnPropertyChanged(nameof(BaudRate));
        OnPropertyChanged(nameof(NetworkEndpoint));
        OnPropertyChanged(nameof(SelectedResetMode));
        OnPropertyChanged(nameof(SelectedResetModeOption));
        OnPropertyChanged(nameof(DemoAxes));
        _selectedPort = string.Empty;
        RefreshPorts();
    }

    public void RefreshPorts()
    {
        var ports = _ports?.GetPortEntries() ?? Array.Empty<SerialPortEntry>();
        var previous = _selectedPort;

        Ports.Clear();
        foreach (var p in ports)
            Ports.Add(p);

        var names = ports.Select(p => p.Name).ToArray();
        // Prefer the current selection, then the last successful port, then the
        // profile's preferred port. With nothing persisted, pick the first real
        // (USB/PCI) port over phantom/Bluetooth entries so first-run doesn't
        // default to a virtual port, falling back to any enumerated port.
        SelectedPort = names.Contains(previous) ? previous
            : names.Contains(_profile.Connection.LastSerialPort) ? _profile.Connection.LastSerialPort
            : names.Contains(_profile.Connection.SerialPort) ? _profile.Connection.SerialPort
            : (ports.FirstOrDefault(p => p.Preferred)?.Name ?? names.FirstOrDefault()) ?? string.Empty;

        OnPropertyChanged(nameof(HasPorts));
        OnPropertyChanged(nameof(SerialEnabled));
        OnPropertyChanged(nameof(NoPortsHint));
        OnPropertyChanged(nameof(SelectedPortEntry));
        RaiseCanConnectChanged();
    }

    /// <summary>
    /// Periodically re-enumerate ports. Only when the enumerated set actually
    /// changes do we refresh the UI — steady state stays untouched so the user's
    /// selection/scroll position is never disturbed by a no-op timer tick.
    /// </summary>
    private void AutoRefreshPorts()
    {
        // Enumerate on the timer (threadpool) thread; the comparison is pure
        // reads, and the UI mutation is marshalled so ObservableCollection changes
        // stay on the dispatcher thread in the Avalonia app. The cheap name-only
        // pass detects changes; the device-name lookup (registry walk on Windows)
        // only runs when the port set actually changed.
        var current = _ports?.GetPorts() ?? Array.Empty<string>();
        if (current.Length == Ports.Count
            && current.All(name => Ports.Any(p => p.Name == name))
            && Ports.All(p => current.Contains(p.Name)))
            return;

        if (_ui is not null)
            _ui(() => RefreshPorts());
        else
            RefreshPorts();
    }

    protected void RequestClose(bool result)
    {
        // The dialog is closing; stop background port enumeration.
        _autoRefreshTimer?.Dispose();
        _autoRefreshTimer = null;
        CloseRequested?.Invoke(this, result);
    }

    public void Dispose()
    {
        _autoRefreshTimer?.Dispose();
        _autoRefreshTimer = null;
        GC.SuppressFinalize(this);
    }
}
