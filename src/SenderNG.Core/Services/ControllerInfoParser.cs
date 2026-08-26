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
///   - grblHAL version line:    <c>[VER:1.2i.20260521:]</c> (verified on a real board)
///   - grblHAL axis line:       <c>[AXS:5:XYZAB]</c> — the authoritative axis count
///   - grblHAL options line:    <c>[OPT:VNMGH,35,255,48]</c> → letters, plan, serial, tools
/// The option letters are scanned for feature hints the same way ioSender reads build
/// options.
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

        foreach (var raw in lines)
        {
            var line = raw?.Trim();
            if (string.IsNullOrEmpty(line))
                continue;

            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                var kv = line[1..^1].Split(':', 2);
                switch (kv[0])
                {
                    case "VER":
                        // [VER:1.2i.20260521:] or [VER:1.1f.20230820:name]
                        if (kv.Length > 1)
                            ver = kv[1].Split(':')[0];
                        if (kv.Length > 2 && kv[2].Length > 0)
                            firmware = kv[2];
                        isHAL = true;
                        break;
                    case "AXS":
                        // [AXS:5:XYZAB] — the number before the axis letters is axis count.
                        axesLine = kv.Length > 1 ? kv[1].Split(':')[0] : string.Empty;
                        isHAL = true;
                        break;
                    case "OPT":
                        if (kv.Length > 1)
                            opt = kv[1];
                        break;
                    default:
                        isHAL = true; // any [NAME:...] under $I implies grblHAL extended
                        break;
                }
            }
            else if (line.StartsWith("Grbl ", StringComparison.OrdinalIgnoreCase))
            {
                identity = line;
                ver = line[5..].Split(' ')[0];
            }
            else if (line.Contains(GrblHALMarker, StringComparison.OrdinalIgnoreCase))
            {
                isHAL = true;
                firmware = GrblHALMarker;
            }
        }

        if (string.IsNullOrEmpty(ver) && string.IsNullOrEmpty(opt) && !isHAL && string.IsNullOrEmpty(identity))
            return ControllerCapabilities.Unknown;

        // grblHAL embeds a build number in the version tail: 1.1f.20230820 -> 20230820.
        var dot = ver.LastIndexOf('.');
        if (isHAL && dot >= 0 && int.TryParse(ver[(dot + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var b))
        {
            build = b;
            ver = ver[..dot];
        }

        var planBuffers = 16;
        var serialBuffers = 128;
        var axes = 3;
        var tools = 0;
        var optLetters = string.Empty;

        // [OPT:letters,plan,serial,tools] — grblHAL 1.2i verified layout: the 4th field
        // is the tool count, NOT axes. Axes come from [AXS:...] when present.
        if (opt.Length > 0)
        {
            var parts = opt.TrimEnd(']').Split(',');
            if (parts.Length > 0)
                optLetters = parts[0];
            if (parts.Length > 1 && int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var p))
                planBuffers = p;
            if (parts.Length > 2 && int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var s))
                serialBuffers = s;
            if (parts.Length > 3 && int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var t))
                tools = t;
        }

        // [AXS:n:letters] is authoritative for the axis count when present.
        if (int.TryParse(axesLine, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) && n > 0)
            axes = n;

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
        };
    }
}