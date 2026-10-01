/*
 * Excellon2GCode.cs - part of CNC Converters library
 *
 * v0.41 / 2022-07-19 / Io Engineering (Terje Io)
 *
 * Ported to SenderNG (text-in/text-out; the module plugin owns the UI).
 */

/*

Copyright (c) 2020-2022, Io Engineering (Terje Io)
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
using System.Text;
using CNC.Core;
using SenderNG.Modules.Shared;

namespace SenderNG.Modules.Excellon;

/// <summary>
/// Excellon (PCB drill) `.drl`/`.xln` → g-code. Faithful port of ioSender's
/// Excellon2GCode (v0.41): same header handling (M48/METRIC/INCH/TZ/LZ, scale
/// factor, T..C.. tools), same drill/slot (G85) expansion, same output shape
/// (per-tool M6 tool change, G81 drills, helical G2 when the tool exceeds the
/// configured diameter, G85 slots as a chain of G81 at 1/3-tool steps).
/// Quirks kept verbatim for output parity: junk lines are skipped silently
/// (per-line try/catch) and a file without any X commands still yields the
/// preamble.
/// </summary>
public class Excellon2GCode
{
    private struct ExcellonCommand
    {
        public string Command;
        public int Tool;
        public Point3D Start;
        public Point3D End;
    }

    private enum ExcellonState
    {
        M48,
        M71,
        M72,
        M95,
        G05
    }

    private bool _isDrillMode = true, _isMetric = true;
    private bool? _isHeader;
    private readonly List<(int Id, double Diameter)> _tools = [];
    private readonly List<ExcellonCommand> _commands = [];
    private Point3D _lastPos;

    // Modal Excellon position, in output units (mm). Coordinates are modal, so
    // a line may carry only X or only Y and the other axis carries over.
    private double _curX, _curY;

    // Input lines that were dropped; reported in the output so a corrupt file
    // cannot silently lose holes (audit §1.7).
    private int _skippedLines;

    public string FileType => "Excellon files";

    public string? FileTypeKey => "converter.excellon.fileType";
    public string FileExtensions => "drl,xln";
    public string Name => "Excellon";

    // M48 Start header
    // M71 metric
    // M72 Inch
    // G04Xn Dwell
    // INCH,LZ - LZ = LeadingZeros
    // M95 or % end header
    // G05 Drill Mode (modal)

    public string? Convert(string sourceText, ConverterParameters parameters)
    {
        _tools.Clear();
        _commands.Clear();
        _isDrillMode = true;
        _isMetric = true;
        _isHeader = null;
        _lastPos = default;
        _curX = _curY = 0d;
        _skippedLines = 0;

        bool leadingZeros = false;
        double scaleFactor = 0d;
        (int Id, double Diameter) tool = (0, 0d);

        using var reader = new StringReader(sourceText);
        string? s = reader.ReadLine();

        while (s != null)
        {
            try
            {
                s = s.Trim();

                if (_isHeader == null)
                    _isHeader = s == "M48";

                else if (_isHeader == true)
                {
                    var hdr = s.Split(',');

                    switch (hdr[0])
                    {
                        case "METRIC":
                            _isMetric = true;
                            leadingZeros = hdr.Length > 1 && hdr[1] == "TZ";
                            if (hdr.Length > 2)
                            {
                                var scale = hdr[2].Split('.');
                                if (scale.Length == 2)
                                    scaleFactor = ParseD("1" + scale[1]);
                            }
                            break;

                        case "INCH":
                            _isMetric = false;
                            leadingZeros = hdr.Length > 1 && hdr[1] == "TZ";
                            if (hdr.Length > 2)
                            {
                                var scale = hdr[2].Split('.');
                                if (scale.Length == 2)
                                    scaleFactor = ParseD("1" + scale[1]);
                            }
                            break;

                        case "%":
                        case "M95":
                            _isHeader = false;
                            break;
                    }

                    if (s[0] == 'T')
                    {
                        int cpos = s.IndexOf('C');
                        _tools.Add((Id: int.Parse(s.Substring(1, cpos - 1)), Diameter: ParseD(s.Substring(cpos + 1))));
                    }
                }
                else
                {
                    switch (s[0])
                    {
                        case 'G':
                            switch (s)
                            {
                                case "G00":
                                case "G01":
                                case "G02":
                                case "G03":
                                    _isDrillMode = false;
                                    break;

                                case "G05":
                                    _isDrillMode = true;
                                    break;
                            }
                            break;

                        case 'X':
                        case 'Y':
                            if (_isDrillMode)
                            {
                                int g85pos = s.IndexOf("G85");
                                string args = g85pos >= 0 ? s.Substring(0, g85pos) : s;
                                string? xWord = AxisWord(args, 'X');
                                string? yWord = AxisWord(args, 'Y');
                                if (xWord is null && yWord is null)
                                    throw new FormatException(s);

                                // Faithful port of the original text-position scale
                                // heuristic: a 7-character X field meant a 3-decimal
                                // fixed format, anything else 4 decimals.
                                double factor = scaleFactor != 0d ? scaleFactor
                                    : leadingZeros ? (xWord is { Length: 7 } ? 1000d : 10000d)
                                    : 1.0;
                                double units = _isMetric ? 1d : 25.4d;

                                // Parse whichever axis is present; the absent one keeps
                                // the previous hole's coordinate (Excellon is modal).
                                if (xWord is not null)
                                    _curX = ParseD(xWord) / factor / units;
                                if (yWord is not null)
                                    _curY = ParseD(yWord) / factor / units;

                                var cmd = new ExcellonCommand();
                                cmd.Command = g85pos >= 0 ? "Slot" : "Drill";
                                cmd.Tool = tool.Id;
                                cmd.Start.X = _curX;
                                cmd.Start.Y = _curY;
                                if (g85pos >= 0)
                                {
                                    _isDrillMode = false;
                                    string end = s.Substring(g85pos + 3);
                                    string? endX = AxisWord(end, 'X');
                                    string? endY = AxisWord(end, 'Y');
                                    // Verbatim quirk: the original divided the start by
                                    // `factor` but the G85 end only by the unit scale. An
                                    // omitted end axis carries the start's value.
                                    cmd.End.X = endX is not null ? ParseD(endX) / units : _curX;
                                    cmd.End.Y = endY is not null ? ParseD(endY) / units : _curY;
                                    _curX = cmd.End.X;
                                    _curY = cmd.End.Y;
                                }
                                _commands.Add(cmd);
                            }
                            break;

                        case 'T':
                            tool = _tools.FirstOrDefault(x => x.Id == int.Parse(s.Substring(1)));
                            break;
                    }
                }
            }
            catch (Exception)
            {
                // Bad line — skip, like ioSender, but count it: a silently dropped
                // line means a silently missing hole (audit §1.7). Blank lines and
                // `;` comments are not input, so they are not counted.
                if (!string.IsNullOrWhiteSpace(s) && !s.StartsWith(';'))
                    _skippedLines++;
            }
            s = reader.ReadLine();
        }

        var sb = new StringBuilder();

        sb.AppendLine("(Translated by Excellon to GCode converter)");
        if (_skippedLines > 0)
            sb.AppendLine(string.Format("(WARNING: {0} input line(s) could not be parsed and were skipped - holes may be missing)", _skippedLines));
        sb.AppendLine("G90G17G21G50");

        if (parameters.ScaleX != 1d || parameters.ScaleY != 1d)
            sb.AppendLine(string.Format("G51X{0}Y{1}", Inv(parameters.ScaleX), Inv(parameters.ScaleY)));

        sb.AppendLine("G0Z" + Inv(parameters.ZRapids));
        sb.AppendLine("X0Y0");

        var target = new Point3D(0d, 0d, parameters.ZRapids);

        foreach (var t in _tools)
        {
            sb.AppendLine("M5");
            target.X = 0d;
            target.Y = 0d;
            target.Z = parameters.ZHome;
            sb.AppendLine("G0" + PosToParams(target));
            sb.AppendLine(string.Format("M6 (MSG, {0} mm {1})",
                Inv(t.Diameter < parameters.ToolDiameter ? t.Diameter : parameters.ToolDiameter),
                t.Diameter < parameters.ToolDiameter ? "drill" : "mill"));
            sb.AppendLine("M3S" + Inv(parameters.RPM));
            sb.AppendLine("G4P1");
            target.Z = parameters.ZRapids;
            sb.AppendLine("G0" + PosToParams(target));
            sb.AppendLine("F" + Inv(parameters.PlungeRate));

            foreach (var cmd in _commands)
            {
                if (cmd.Tool != t.Id)
                    continue;

                switch (cmd.Command)
                {
                    case "Drill":
                        if (t.Diameter > parameters.ToolDiameter)
                        {
                            double r = (t.Diameter - parameters.ToolDiameter) / 2d;
                            target.X = cmd.Start.X + r;
                            target.Y = cmd.Start.Y;
                            string p = PosToParams(target);
                            if (p.Length > 0)
                                sb.AppendLine("G0" + p);
                            target.Z = parameters.ZMin;
                            sb.AppendLine("G1" + PosToParams(target));
                            sb.AppendLine(string.Format("G{0}X{1}I-{2}", "2", Inv(target.X), Inv(r)));
                            target.Z = parameters.ZRapids;
                            sb.AppendLine("G0" + PosToParams(target));
                        }
                        else
                            OutputG81(sb, parameters, new Point3D(cmd.Start.X, cmd.Start.Y, parameters.ZMin));
                        break;

                    case "Slot":
                        OutputSlot(sb, parameters, cmd, t.Diameter);
                        break;
                }
            }
        }

        sb.AppendLine("G0X0Y0Z" + Inv(parameters.ZHome));
        sb.AppendLine("M30");

        return sb.ToString();
    }

    private string PosToParams(Point3D pos)
    {
        string gcode = string.Empty;

        if (pos.X != _lastPos.X)
            gcode += "X" + Inv(Math.Round(pos.X, 3));

        if (pos.Y != _lastPos.Y)
            gcode += "Y" + Inv(Math.Round(pos.Y, 3));

        if (pos.Z != _lastPos.Z)
            gcode += "Z" + Inv(Math.Round(pos.Z, 3));

        _lastPos = pos;

        return gcode;
    }

    private void OutputSlot(StringBuilder sb, ConverterParameters parameters, ExcellonCommand cmd, double tsize)
    {
        double x = cmd.End.X - cmd.Start.X;
        double y = cmd.End.Y - cmd.Start.Y;
        double dist = Math.Sqrt(x * x + y * y);
        int holes = (int)Math.Round(dist / (tsize / 3d) + 0.5d, 0);
        double factor = dist / (holes - 1);
        x = x / dist * factor;
        y = y / dist * factor;

        sb.AppendLine(string.Format("(Slot {0};{1} - {2};{3})",
            Inv(cmd.Start.X), Inv(cmd.Start.Y), Inv(cmd.End.X), Inv(cmd.End.Y)));

        Point3D target = new Point3D(cmd.Start.X, cmd.Start.Y, parameters.ZMin);

        OutputG81(sb, parameters, target);

        while (--holes > 0)
        {
            target.X += x;
            target.Y += y;
            OutputG81(sb, parameters, target);
        }

        _lastPos = target;
    }

    private void OutputG81(StringBuilder sb, ConverterParameters parameters, Point3D pos)
    {
        string p = PosToParams(pos);

        if (p.Length > 0)
            sb.AppendLine("G81" + p + "R" + Inv(parameters.ZSafe));
    }

    /// <summary>The number text of the <c>&lt;axis&gt;&lt;number&gt;</c> word in a
    /// coordinate segment, or null when the axis is absent or has no digits —
    /// in which case the caller carries over the modal coordinate.</summary>
    private static string? AxisWord(string segment, char axis)
    {
        int at = segment.IndexOf(axis);
        if (at < 0)
            return null;
        int end = at + 1;
        if (end < segment.Length && (segment[end] == '-' || segment[end] == '+'))
            end++;
        while (end < segment.Length && (char.IsAsciiDigit(segment[end]) || segment[end] == '.'))
            end++;
        return end > at + 1 ? segment[(at + 1)..end] : null;
    }

    private static double ParseD(string s) => double.Parse(s, CultureInfo.InvariantCulture);

    private static string Inv(double v) => v.ToString(CultureInfo.InvariantCulture);
}