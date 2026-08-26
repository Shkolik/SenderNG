using System.Globalization;

namespace CNC.Core.Services;

/// <summary>
/// Pure, serial-port-free parser for grbl / grblHAL status-report lines. Kept
/// out of <see cref="RealGrblService"/> so every parse path is unit-testable
/// without hardware.
///
/// Handles the report shapes SenderNG currently needs:
///   - Probe completion:  <c>[PRB:x,y,z:1]</c> and <c>[PRB:x,y,z:0]</c>
///   - Status report:     <c>&lt;Idle|WPos:1,2,3|FS:120,8000|Ov:100,100,100|...&gt;</c>
///     with the <c>WPos:</c> / <c>MPos:</c> / <c>FS:</c> / <c>Ov:</c> / <c>Pn:</c> /
///     <c>A:</c> / <c>WCS:</c> fields.
///
/// grblHAL accessory state (<c>A:</c>) drives Flood/Mist/spindle — see
/// ioSender GrblViewModel.cs (~964) for the upstream semantics. Classic grbl 1.1
/// has no <c>A:</c> field; when absent the parser leaves those flags untouched.
/// </summary>
public static class StatusReportParser
{
    /// <summary>
    /// Parse one complete controller line and apply it to <paramref name="state"/>.
    /// Lines that match no known report shape are ignored (no-op). Matched report
    /// fields mutate <paramref name="state"/> in place so callers can keep a single
    /// live <see cref="MachineState"/> and emit it after each parse.
    /// Returns true when the line was a recognized report; false when ignored.
    /// </summary>
    public static bool Parse(string line, MachineState state)
    {
        if (string.IsNullOrEmpty(line) || state is null)
            return false;

        if (TryParseProbeReport(line, state))
            return true;

        return TryParseStatusReport(line, state);
    }

    private static bool TryParseProbeReport(string line, MachineState state)
    {
        if (!line.StartsWith("[PRB:", StringComparison.Ordinal) || !line.EndsWith(']'))
            return false;

        var body = line[5..^1];
        var colon = body.LastIndexOf(':');
        // grblHAL ends [PRB:...] with 1 (success) or 0 (fail) — an integer,
        // not a .NET bool literal, so bool.TryParse would silently treat both
        // as failure. Parse it explicitly.
        var ok = false;
        if (colon > 0 && body[(colon + 1)..].Trim() is "1")
            ok = true;

        var pos = (colon > 0 ? body[..colon] : body).Split(',');
        if (state.ProbePosition is null || state.ProbePosition.Length != state.Positions.Length)
            state.ProbePosition = new double[state.Positions.Length];
        for (var i = 0; i < pos.Length && i < state.ProbePosition.Length; i++)
        {
            if (double.TryParse(pos[i], NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                state.ProbePosition[i] = value;
        }
        state.ProbeSuccess = ok;
        return true;
    }

    private static bool TryParseStatusReport(string line, MachineState state)
    {
        if (!line.StartsWith('<') || !line.EndsWith('>'))
            return false;

        // Report format: <Status|WPos:x,y,z|MPos:x,y,z|FS:feed,speed|Ov:f,r,s|Pn:flags|A:accessory|WCS:g54|...>
        var fields = line[1..^1].Split('|');
        state.Status = fields[0];

        foreach (var field in fields.AsSpan(1))
        {
            if (field.StartsWith("WPos:", StringComparison.Ordinal))
            {
                SetPositions(state.Positions, field[5..]);
            }
            else if (field.StartsWith("MPos:", StringComparison.Ordinal))
            {
                if (state.MachinePositions is null || state.MachinePositions.Length != state.Positions.Length)
                    state.MachinePositions = new double[state.Positions.Length];
                SetPositions(state.MachinePositions, field[5..]);
            }
            else if (field.StartsWith("FS:", StringComparison.Ordinal))
            {
                var fs = field[3..].Split(',');
                if (fs.Length >= 1 && double.TryParse(fs[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var feed))
                    state.Feed = feed;
                if (fs.Length >= 2 && double.TryParse(fs[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var speed))
                    state.Speed = speed;
            }
            else if (field.StartsWith("Ov:", StringComparison.Ordinal))
            {
                var ov = field[3..].Split(',');
                if (ov.Length >= 1 && int.TryParse(ov[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var feedOvr))
                    state.FeedOvr = feedOvr;
                if (ov.Length >= 2 && int.TryParse(ov[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var rapidOvr))
                    state.RapidOvr = rapidOvr;
                if (ov.Length >= 3 && int.TryParse(ov[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var spindleOvr))
                    state.SpindleOvr = spindleOvr;
            }
            else if (field.StartsWith("Pn:", StringComparison.Ordinal))
            {
                // Probe pin state: 'P' asserted, 'O' probe disconnected. GPIO
                // limit bits (X/Y/Z/...) are intentionally left unparsed for now —
                // signals/limits are marked untested-on-hardware in the P0 plan.
                var flags = field[3..];
                if (flags.Contains('P'))
                    state.Signals |= CNC.Core.Signals.Probe;
                if (flags.Contains('O'))
                    state.Signals |= CNC.Core.Signals.ProbeDisconnected;
            }
            else if (field.StartsWith("A:", StringComparison.Ordinal))
            {
                ApplyAccessoryField(field[2..], state);
            }
        }

        return true;
    }

    private static void SetPositions(double[] target, string csv)
    {
        var pos = csv.Split(',');
        for (var i = 0; i < pos.Length && i < target.Length; i++)
        {
            if (double.TryParse(pos[i], NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                target[i] = value;
        }
    }

    /// <summary>
    /// grblHAL accessory field semantics (ioSender GrblViewModel.cs ~964):
    ///   'M' → Mist on, 'F' → Flood on, 'T' → tool changing, 'S' → spindle CW,
    ///   'C' → spindle CCW. An empty field clears Flood/Mist and puts the spindle off.
    /// </summary>
    private static void ApplyAccessoryField(string flags, MachineState state)
    {
        if (flags.Length == 0)
        {
            state.Flood = false;
            state.Mist = false;
            state.IsToolChanging = false;
            state.SpindleState = SpindleDirection.Off;
            return;
        }

        state.Mist = flags.Contains('M');
        state.Flood = flags.Contains('F');
        state.IsToolChanging = flags.Contains('T');
        state.SpindleState = flags.Contains('S') ? SpindleDirection.Forward
            : flags.Contains('C') ? SpindleDirection.Reverse
            : SpindleDirection.Off;
    }
}