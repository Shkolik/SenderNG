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

    // Legacy flat fields kept ONLY for settings.json migration; not used after Load.
    public int DemoAxes { get; set; } = 3;
    public string Port { get; set; } = "auto";
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
    public Dictionary<string, bool> ModulesVisibility { get; set; } = new();
}
