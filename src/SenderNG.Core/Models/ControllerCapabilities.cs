
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
        // Demo emulates a fully-equipped grblHAL board: settings database via
        // $ES/$EG/$SED and every optional signal is reported.
        HasSettingEnums = true,
        HasSettingDescriptions = true,
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

    /// <summary>Raw <c>[OPT:]</c> value from <c>$I</c> (letters + plan/serial/axes/tools
    /// numbers, e.g. "VNMSL+,100,1024,4,32"). Retained verbatim for the
    /// controller fingerprint — the parsed flags lose the exact letter layout
    /// (order, '-' exclusions), which is what distinguishes firmware builds.</summary>
    public string RawOptions { get; init; } = string.Empty;

    /// <summary>Raw <c>[AXS:]</c> value from <c>$I</c> (count + letters, e.g.
    /// "4:XYZA"). Classic grbl 1.1 emits no <c>[AXS:]</c> line → empty. The exact
    /// letter set distinguishes e.g. a 4-axis mill (XYZA) from a 4-axis lathe
    /// (XYZC).</summary>
    public string RawAxes { get; init; } = string.Empty;

    /// <summary>
    /// Stable cross-session identity for scoping editor-local metadata (tool
    /// names): the <c>$I</c> firmware fingerprint only — deliberately WITHOUT the
    /// connection endpoint, so moving a machine to another COM port or IP keeps
    /// its names, and two byte-identical boards (same firmware build, options
    /// and axes — effectively clones) share one set of names, which matches the
    /// tooling a user would set up on them. Different firmware → different key.
    /// An unidentified controller (handshake failed / not yet done) is "unknown".
    /// </summary>
    public string ControllerKey => IsKnown
        ? $"{Firmware}|{Version}.{Build}|{RawOptions}|{RawAxes}"
        : "unknown";

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
    /// grblHAL only: the firmware carries its own settings database and serves it via
    /// <c>$ES</c> (setting metadata) and <c>$EG</c> (group tree). Signalled by the
    /// <c>ENUMS</c> token in <c>[NEWOPT:]</c>. Classic grbl 1.1 / 1.2i forks have no
    /// such dump — they fall back to the embedded catalog.
    /// </summary>
    public bool HasSettingEnums { get; init; }

    /// <summary>
    /// grblHAL only: per-setting long descriptions are available via <c>$SED=&lt;id&gt;</c>.
    /// Signalled by the <c>SED</c> token in <c>[NEWOPT:]</c>.
    /// </summary>
    public bool HasSettingDescriptions { get; init; }

    /// <summary>
    /// Signals the controller reports as available, discovered from <c>$I</c>
    /// (<c>[SIGNALS:]</c> chars, <c>[OPT:]</c> '+' for the safety door, <c>[NEWOPT:]</c>
    /// BD/ES/MW/OS). Mirrors ioSender's <c>OptionalSignals</c>: the signals panel
    /// only shows LEDs for these (plus the always-on axis/H/S/R/P set).
    /// </summary>
    public CNC.Core.Signals OptionalSignals { get; init; } = CNC.Core.Signals.Off;
}