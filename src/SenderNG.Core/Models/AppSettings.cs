using CNC.Core.Abstractions;

namespace CNC.Core.Services;

public class AppSettings
{
    public const string MillMode = "Mill";
    public const string FoamMode = "Foam";
    public static string[] KnownModes { get; } = [MillMode, FoamMode];

    public string Mode { get; set; } = MillMode;
    public Dictionary<string, ModeSettings> Modes { get; set; } = new();
    // Recent G-code files, most recent first; persists across app restarts.
    public List<string> RecentFiles { get; set; } = new();
    // Global (not mode-aware) plugin data sections.
    public Dictionary<string, object> Modules { get; set; } = new();

    // Global (not mode-aware) connection settings. Network connect (grblHAL
    // over TCP) is available when NetworkAvailable is true; the connect dialog's
    // Network tab is enabled only then. These are intentionally global — a
    // controller/network is a property of the machine setup, not of the mode.
    public bool NetworkAvailable { get; set; } = true;
    public string NetworkEndpoint { get; set; } = "192.168.1.100:23";

    // Last *successful* connection per connect mode (serial / network), written
    // only after a connect actually succeeds so the dialog re-opens pre-filled
    // with values that are known to work. Distinct from the mode-scoped Port /
    // BaudRate legacy fields above.
    public string LastSerialPort { get; set; } = string.Empty;
    public int LastSerialBaudRate { get; set; }
    public string LastNetworkEndpoint { get; set; } = string.Empty;

    // Serial "On connect" reset action (ioSender Port-dialog dropdown): an 8-bit
    // Arduino usually needs Toggle DTR to reset its bootloader when the port opens;
    // ESP32-style boards use Toggle RTS. Global, like the other connection settings.
    public ConnectResetMode SerialResetMode { get; set; }

    // Legacy flat fields kept ONLY for settings.json migration; not used after Load.
    public int DemoAxes { get; set; } = 3;
    public string Port { get; set; } = "auto";
    public int BaudRate { get; set; } = 115200;
    public Dictionary<string, bool> ModulesVisibility { get; set; } = new();

    public ModeSettings Current
    {
        get
        {
            if (!Modes.TryGetValue(Mode, out var m))
                Modes[Mode] = m = new ModeSettings();
            return m;
        }
    }
}

public class ModeSettings
{
    public int DemoAxes { get; set; } = 3;
    public string Port { get; set; } = "auto";
    public int BaudRate { get; set; } = 115200;
    public Dictionary<string, bool> ModulesVisibility { get; set; } = new();
}
