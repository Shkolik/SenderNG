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
        // Demo emulates a fully-equipped grblHAL board: 4 tool-table slots so the
        // Offsets tool table is shown and savable in demo mode.
        NumTools = 4,
        // Demo emulates a fully-equipped grblHAL board: every optional signal is
        // reported, so the signals panel shows the complete set (real boards only
        // show what their $I reports).
        OptionalSignals = CNC.Core.Signals.SafetyDoor | CNC.Core.Signals.EStop |
            CNC.Core.Signals.MotorWarning | CNC.Core.Signals.MotorFault |
            CNC.Core.Signals.BlockDelete | CNC.Core.Signals.OptionalStop,
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

    /// <summary>
    /// Signals the controller reports as available, discovered from <c>$I</c>
    /// (<c>[SIGNALS:]</c> chars, <c>[OPT:]</c> '+' for the safety door, <c>[NEWOPT:]</c>
    /// BD/ES/MW/OS). Mirrors ioSender's <c>OptionalSignals</c>: the signals panel
    /// only shows LEDs for these (plus the always-on axis/H/S/R/P set).
    /// </summary>
    public CNC.Core.Signals OptionalSignals { get; init; } = CNC.Core.Signals.Off;
}