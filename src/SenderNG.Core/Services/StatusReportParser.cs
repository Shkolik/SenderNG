
/*

Copyright (c) 2018-2022 Io Engineering (Terje Io)

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

        // Report format: <Status|WPos:x,y,z|MPos:x,y,z|WCO:x,y,z|FS:feed,speed|Ov:f,r,s|Pn:flags|A:accessory|WCS:g54|...>
        var fields = line[1..^1].Split('|');
        state.Status = fields[0];

        // An alarm always means the machine needs (re-)homing: the reported
        // homed state is lost regardless of whether the alarm is then cleared
        // with $X (unlock without homing). Classic grbl's status report has no
        // explicit homed flag, so the alarm transition is the honest signal.
        if (fields[0].Equals("Alarm", StringComparison.OrdinalIgnoreCase))
            state.IsHomed = false;

        // Each status report fully describes the current pin/switch state, so the
        // accumulated signal flags are rebuilt from this report (Pn:/A:) rather than
        // OR-ed onto whatever tripped earlier. This keeps the signal LEDs live: a
        // limit that clears stops showing as active. Line-by-line runs keep the
        // semantic that a report always sets the current state.
        state.Signals = 0;

        // Exactly one position field arrives per report ($10 bit 0 picks MPos or
        // WPos); whichever is missing is derived from WCO after the field loop.
        var sawWPos = false;
        var sawMPos = false;

        foreach (var field in fields.AsSpan(1))
        {
            if (field.StartsWith("WPos:", StringComparison.Ordinal))
            {
                SetPositions(state.Positions, field[5..]);
                sawWPos = true;
            }
            else if (field.StartsWith("MPos:", StringComparison.Ordinal))
            {
                if (state.MachinePositions is null || state.MachinePositions.Length != state.Positions.Length)
                    state.MachinePositions = new double[state.Positions.Length];
                SetPositions(state.MachinePositions, field[5..]);
                sawMPos = true;
            }
            else if (field.StartsWith("WCO:", StringComparison.Ordinal))
            {
                if (state.WorkOffsets is null || state.WorkOffsets.Length != state.Positions.Length)
                    state.WorkOffsets = new double[state.Positions.Length];
                SetPositions(state.WorkOffsets, field[4..]);
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
            else if (field.StartsWith("Ln:", StringComparison.Ordinal))
            {
                // Executing block number (grblHAL / newer grbl, only while a run is active).
                if (int.TryParse(field[3..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var lineNumber))
                    state.LineNumber = lineNumber;
            }
            else if (field.StartsWith("Pn:", StringComparison.Ordinal))
            {
                ApplyPinField(field[3..], state);
            }
            else if (field.StartsWith("A:", StringComparison.Ordinal))
            {
                ApplyAccessoryField(field[2..], state);
            }
            else if (field.StartsWith("WCS:", StringComparison.Ordinal))
            {
                // grblHAL reports the ACTIVE work coordinate system (g54..g59.3)
                // in every status report — the live truth for which offset the
                // DRO zero (G10 L20 P0) targets; may be empty when reporting is
                // limited, in which case keep the previous value.
                var wcs = field[4..].Trim().ToUpperInvariant();
                if (wcs.Length > 0)
                    state.ActiveCoordinateSystem = wcs;
            }
        }

        // grbl and grblHAL report exactly ONE position field per status report and
        // the $10 mask's bit 0 picks which: machine (MPos) or work (WPos) position.
        // Both read-outs must therefore be derived from whichever one arrived plus
        // WCO — the same symmetric derivation ioSender's GrblViewModel.ParseStatus
        // does. Offsets are reported periodically rather than in every report, so
        // the last known WCO carried in state is used when this report omits it;
        // when no WCO was ever seen there is no offset as far as the sender knows,
        // so the two read-outs agree (ioSender: Position = MachinePosition when
        // there is no WCO). An explicit field always wins over a derived one.
        if (!sawWPos && sawMPos && state.MachinePositions is not null)
        {
            DeriveFrom(state.Positions, state.MachinePositions, state.WorkOffsets, subtractOffsets: true);
        }
        else if (sawWPos && !sawMPos)
        {
            if (state.MachinePositions is null || state.MachinePositions.Length != state.Positions.Length)
                state.MachinePositions = new double[state.Positions.Length];
            DeriveFrom(state.MachinePositions, state.Positions, state.WorkOffsets, subtractOffsets: false);
        }

        return true;
    }

    /// <summary>
    /// Fills <paramref name="target"/> from <paramref name="source"/> plus the work
    /// offset: <c>work = machine - WCO</c> (subtract) or <c>machine = work + WCO</c>
    /// (add). Without a known offset the source is copied unchanged.
    /// </summary>
    private static void DeriveFrom(double[] target, double[] source, double[]? workOffsets, bool subtractOffsets)
    {
        for (var i = 0; i < target.Length && i < source.Length; i++)
            target[i] = workOffsets is not null && i < workOffsets.Length
                ? (subtractOffsets ? source[i] - workOffsets[i] : source[i] + workOffsets[i])
                : source[i];
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
    /// Parse the <c>Pn:</c> field — the controller's live limit/control pin state.
    /// Both grbl 1.1 (report.c REPORT_FIELD_PIN_STATE) and grblHAL (report.c
    /// axis/control_signals_tostring) emit the same convention: one axis letter per
    /// active limit (X/Y/Z/A/B/C/U/V/W) plus control-state chars for probe and the
    /// machine switches. The chars below map 1:1 onto <see cref="Signals"/> flags:
    ///   P=probe, O=probe disconnected, R=reset, H=feed hold, S=cycle start, D=safety
    ///   door, E=e-stop. Unknown/grblHAL-only chars are ignored. Axis letters map to the
    ///   limit flag for that letter (X→LimitX, Y→LimitY, … mirroring axis order).
    /// </summary>
    private static void ApplyPinField(string flags, MachineState state)
    {
        foreach (var ch in flags)
        {
            switch (ch)
            {
                case 'X': state.Signals |= CNC.Core.Signals.LimitX; break;
                case 'Y': state.Signals |= CNC.Core.Signals.LimitY; break;
                case 'Z': state.Signals |= CNC.Core.Signals.LimitZ; break;
                case 'A': state.Signals |= CNC.Core.Signals.LimitA; break;
                case 'B': state.Signals |= CNC.Core.Signals.LimitB; break;
                case 'C': state.Signals |= CNC.Core.Signals.LimitC; break;
                case 'U': state.Signals |= CNC.Core.Signals.LimitU; break;
                case 'V': state.Signals |= CNC.Core.Signals.LimitV; break;
                case 'W': state.Signals |= CNC.Core.Signals.LimitW; break;
                case 'P': state.Signals |= CNC.Core.Signals.Probe; break;
                case 'O': state.Signals |= CNC.Core.Signals.ProbeDisconnected; break;
                case 'D': state.Signals |= CNC.Core.Signals.SafetyDoor; break;
                case 'R': state.Signals |= CNC.Core.Signals.Reset; break;
                case 'H': state.Signals |= CNC.Core.Signals.Hold; break;
                case 'S': state.Signals |= CNC.Core.Signals.CycleStart; break;
                case 'E':
                    state.Signals |= CNC.Core.Signals.EStop;
                    // Dynamic discovery: a controller that trips e-stop but never
                    // reported it in $I (e.g. classic grbl, which has no [SIGNALS:])
                    // gets its e-stop LED revealed by observation, exactly like
                    // ioSender's Set("Pn", ...) adds EStop to OptionalSignals.
                    state.OptionalSignals |= CNC.Core.Signals.EStop;
                    break;
            }
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