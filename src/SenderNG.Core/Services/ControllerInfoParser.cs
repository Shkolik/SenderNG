using System.Globalization;

namespace CNC.Core.Services;

/// <summary>
/// Pure parser for the connect-time <c>$I</c> / <c>$I+</c> handshake reply set.
/// Returns an immutable <see cref="ControllerCapabilities"/> populated from the
/// observed lines without mutating the global <see cref="CNC.Core.GrblInfo"/>
/// static (which is a ported ioSender blob best left untouched).
///
/// Handles the shapes SenderNG needs:
///   - Classic grbl identity:   <c>Grbl 1.1h ['$' for help]</c>
///   - Version line:            <c>[VER:1.2i.20260521:]</c> (mega5x fork, verified on a real board)
///   - Axis line:               <c>[AXS:5:XYZAB]</c> — the authoritative axis count
///   - Options line:            <c>[OPT:VNMGH,35,255,48]</c> → letters, plan, serial, tools
///   - Firmware line:           <c>[FIRMWARE:grblHAL]</c> — the ONLY reliable grblHAL signal
/// The option letters are scanned for feature hints the same way ioSender reads build
/// options.
///
/// grblHAL classification: a controller is grblHAL only when the reply contains the
/// "grblHAL" marker (<c>[FIRMWARE:grblHAL]</c> or a <c>grblHAL 1.1h</c> banner) or a
/// <c>[NAME:...]</c> line. Classic grbl 1.1 and 1.2i forks (mega5x) emit <c>[VER:]</c>,
/// <c>[OPT:]</c>, <c>[FIRMWARE:grbl]</c> and even <c>[AXS:]</c>, so those lines do NOT
/// imply grblHAL — the app must not "mix 1.2i with HAL" as it previously did.
/// </summary>
public static class ControllerInfoParser
{
    private const string GrblHALMarker = "grblHAL";

    /// <summary>
    /// Combine an observed <c>$I</c> reply set (each line trimmed) into capabilities.
    /// Returns <see cref="ControllerCapabilities.Unknown"/> when nothing recognized.
    /// </summary>
    public static ControllerCapabilities ParseInfo(IEnumerable<string> lines)
    {
        var ver = string.Empty;
        var firmware = string.Empty;
        var isHAL = false;
        var build = 0;
        var opt = string.Empty;
        var axesLine = string.Empty;
        var identity = string.Empty;
        var optionalSignals = CNC.Core.Signals.Off;

        foreach (var raw in lines)
        {
            var line = raw?.Trim();
            if (string.IsNullOrEmpty(line))
                continue;

            // The "grblHAL" marker string is the reliable HAL signal: grblHAL's $I
            // carries [FIRMWARE:grblHAL] and its boot banner is "grblHAL 1.1h...".
            // Classic grbl 1.1 and 1.2i forks (e.g. mega5x) emit [VER:...], [OPT:...],
            // [FIRMWARE:grbl] and even [AXS:...] (mega5x is 5-axis) — so a bare
            // [VER:]/[AXS:]/[FIRMWARE:...] line must NOT imply grblHAL.
            if (line.Contains(GrblHALMarker, StringComparison.OrdinalIgnoreCase))
            {
                isHAL = true;
                firmware = GrblHALMarker;
            }

            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                var kv = line[1..^1].Split(':', 2);
                switch (kv[0])
                {
                    case "VER":
                        // [VER:1.2i.20260521:] or [VER:1.1f.20230820:grblHAL]
                        if (kv.Length > 1)
                        {
                            var tail = kv[1].Split(':');
                            ver = tail[0];
                            if (tail.Length > 1 && tail[1].Length > 0)
                                firmware = tail[1];
                        }
                        break;
                    case "AXS":
                        // [AXS:5:XYZAB] — the number before the axis letters is axis count.
                        // Not a HAL signal: the mega5x 1.2i fork emits it too.
                        axesLine = kv.Length > 1 ? kv[1].Split(':')[0] : string.Empty;
                        break;
                    case "OPT":
                        if (kv.Length > 1)
                            opt = kv[1];
                        break;
                    case "NAME":
                        // [NAME:...] is grblHAL-specific; classic grbl never emits it.
                        isHAL = true;
                        break;
                    case "SIGNALS":
                        // grblHAL $I lists the signals the firmware is built with,
                        // one letter per signal, in SignalLetters order. ioSender
                        // maps each char to the matching Signals flag (Grbl.cs
                        // Process "SIGNALS" case). Classic grbl 1.1/1.2i have no
                        // such line, so they discover nothing here.
                        if (kv.Length > 1)
                            optionalSignals |= SignalLettersToFlags(kv[1]);
                        break;
                    case "NEWOPT":
                        // grblHAL $I extended options: BD=block delete, ES=e-stop,
                        // MW=motor warning, OS=optional stop (ioSender Grbl.cs
                        // Process "NEWOPT" case).
                        if (kv.Length > 1)
                            optionalSignals |= NewOptionsToFlags(kv[1]);
                        break;
                    // [FIRMWARE:grbl] (classic), [FW_BUILD:...] and other lines carry
                    // no HAL signal by themselves.
                }
            }
            else if (line.StartsWith("Grbl ", StringComparison.OrdinalIgnoreCase))
            {
                identity = line;
                ver = line[5..].Split(' ')[0];
            }
        }

        if (string.IsNullOrEmpty(ver) && string.IsNullOrEmpty(opt) && !isHAL && string.IsNullOrEmpty(identity))
            return ControllerCapabilities.Unknown;

        // grbl embeds a build date in the version tail: 1.1f.20230820 -> 20230820.
        // Classic grbl 1.1h, grblHAL and the mega5x 1.2i fork all do this, so the
        // strip is NOT gated on the HAL flag.
        var dot = ver.LastIndexOf('.');
        if (dot >= 0 && int.TryParse(ver[(dot + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var b))
        {
            build = b;
            ver = ver[..dot];
        }

        var planBuffers = 16;
        var serialBuffers = 128;
        var axes = 3;
        var tools = 0;
        var optLetters = string.Empty;

        // [OPT:...] numeric layout varies by firmware:
        //   classic grbl 1.1: [letters, plan, serial]                -> no tools
        //   mega5x 1.2i fork: [letters, plan, serial, tools]         -> 4th field = tools
        //   grblHAL 1.1f+:    [letters, plan, serial, axes, tools]   -> 5th field = tools, 4th = axes
        // [AXS:...] is authoritative for the axis count; read tools from the
        // firmware's tool slot (last numeric field).
        if (opt.Length > 0)
        {
            var parts = opt.TrimEnd(']').Split(',');
            if (parts.Length > 0)
                optLetters = parts[0];
            if (parts.Length > 1 && int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var p))
                planBuffers = p;
            if (parts.Length > 2 && int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var s))
                serialBuffers = s;
            if (parts.Length > 4 && int.TryParse(parts[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out var t))
                tools = t;
            else if (parts.Length == 4 && int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var t4))
                tools = t4;
        }

        // [AXS:n:letters] is authoritative for the axis count when present.
        if (int.TryParse(axesLine, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) && n > 0)
            axes = n;

        // Classic grbl reports the safety door via a '+' in the first [OPT:] letter
        // field (ioSender: `if (s[0].Contains('+')) OptionalSignals |= SafetyDoor`).
        if (optLetters.Contains('+'))
            optionalSignals |= CNC.Core.Signals.SafetyDoor;

        return new ControllerCapabilities
        {
            IsKnown = true,
            IsGrblHAL = isHAL,
            Version = ver,
            Build = build,
            Firmware = string.IsNullOrEmpty(firmware) ? (isHAL ? GrblHALMarker : "grbl") : firmware,
            NumAxes = axes,
            PlanBufferSize = planBuffers,
            SerialBufferSize = serialBuffers,
            NumTools = tools,
            // Classic grbl 1.1 has no way to signal ENABLE_M7 via $I; grblHAL exposes
            // it via an explicit 'M' option. Default: flood+spindle on, mist only when
            // the firmware is grblHAL (the reliable signal we have).
            CoolantFlood = true,
            CoolantMist = isHAL || optLetters.Contains('M'),
            Spindle = true,
            Probing = true,
            HomingEnabled = optLetters.Contains('H'),
            OptionalSignals = optionalSignals,
        };
    }

    // grblHAL's [SIGNALS:] letters are ordered by ioSender's SignalLetters constant
    // ("XYZABCUVWEPRDHSLTOMF", kept in sync with the Signals enum): index i -> 1<<i.
    private static readonly string SignalLetters = "XYZABCUVWEPRDHSLTOMF";

    /// <summary>Map a grblHAL [SIGNALS:] string to Signals flags (ioSender semantics).</summary>
    private static CNC.Core.Signals SignalLettersToFlags(string chars)
    {
        var flags = CNC.Core.Signals.Off;
        foreach (var c in chars)
        {
            var i = SignalLetters.IndexOf(c);
            if (i >= 0)
                flags |= (CNC.Core.Signals)(1 << i);
        }
        return flags;
    }

    /// <summary>Map grblHAL [NEWOPT:] tokens to Signals flags (ioSender semantics).</summary>
    private static CNC.Core.Signals NewOptionsToFlags(string options)
    {
        var flags = CNC.Core.Signals.Off;
        foreach (var opt in options.Split(','))
        {
            flags |= opt switch
            {
                "BD" => CNC.Core.Signals.BlockDelete,
                "ES" => CNC.Core.Signals.EStop,
                "MW" => CNC.Core.Signals.MotorWarning,
                "OS" => CNC.Core.Signals.OptionalStop,
                _ => CNC.Core.Signals.Off,
            };
        }
        return flags;
    }
}