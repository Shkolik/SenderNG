namespace CNC.Core.Services;

/// <summary>
/// Immutable snapshot of the connected controller's capabilities, populated from
/// the connect-time <c>$I</c> / <c>[$I+]</c> handshake and used to drive capability-
/// aware UI (e.g. hiding the Coolant Mist toggle when the firmware doesn't support
/// it). A <see cref="DemoGrblService"/> reports a "full" default so demo behavior
/// is unchanged; the real service fills it from actual firmware output.
/// </summary>
public sealed record ControllerCapabilities
{
    /// <summary>Unknown / not yet handshaken — UI treats it as full (safe default).</summary>
    public static readonly ControllerCapabilities Unknown = new() { IsKnown = false };

    /// <summary>Full capability set reported by the demo service. Demo shows both coolant modes.</summary>
    public static readonly ControllerCapabilities Full = new()
    {
        IsKnown = true,
        IsGrblHAL = true,
        NumAxes = 3,
        Version = "demo",
        Build = 0,
        Firmware = "grblHAL (demo)",
        CoolantFlood = true,
        CoolantMist = true,
        Spindle = true,
        Probing = true,
        PlanBufferSize = 16,
        SerialBufferSize = 128,
    };

    public bool IsKnown { get; init; }

    public bool IsGrblHAL { get; init; }
    public string Version { get; init; } = string.Empty;
    public int Build { get; init; }
    public string Firmware { get; init; } = string.Empty;

    public int NumAxes { get; init; } = 3;
    public int PlanBufferSize { get; init; } = 16;
    public int SerialBufferSize { get; init; } = 128;
    public int NumTools { get; init; }

    // Capability flags relevant to UI decisions.
    public bool CoolantFlood { get; init; } = true;
    public bool CoolantMist { get; init; }
    public bool Spindle { get; init; } = true;
    public bool Probing { get; init; } = true;
    public bool HomingEnabled { get; init; }
}