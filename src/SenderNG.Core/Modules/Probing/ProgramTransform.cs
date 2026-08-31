
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
using System.Text;

namespace SenderNG.Core.Modules.Probing;

/// <summary>
/// Line-based transforms applied to a loaded gcode program: coordinate-system
/// rotation (probed by the Rotation tool) and height-map Z correction. A
/// simplified, text-level port of ioSender's GCodeTransform (which splits lines
/// into motion segments on the token model); arcs are handled for rotation via
/// their I/J center words, and height-map Z is injected on X/Y moves.
/// </summary>
public static class ProgramTransform
{
    /// <summary>Rotates the X/Y coordinates (and arc I/J centers) of motion lines by <paramref name="angleRadians"/> around (ox, oy).</summary>
    public static string Rotate(string gcode, double angleRadians, double ox, double oy)
    {
        var cos = Math.Cos(angleRadians);
        var sin = Math.Sin(angleRadians);

        return TransformLines(gcode, (line, words) =>
        {
            if (!IsMotion(line))
                return line;

            var x = FindWord(words, 'X');
            var y = FindWord(words, 'Y');
            var i = FindWord(words, 'I');
            var j = FindWord(words, 'J');

            if (x is not null && y is not null)
            {
                var rx = cos * (x.Value - ox) - sin * (y.Value - oy) + ox;
                var ry = sin * (x.Value - ox) + cos * (y.Value - oy) + oy;
                line = ReplaceWord(line, x.Value, rx, 'X', words);
                return ReplaceWord(line, y.Value, ry, 'Y', words);
            }

            if (i is not null && j is not null)
            {
                var ri = cos * i.Value - sin * j.Value;
                var rj = sin * i.Value + cos * j.Value;
                line = ReplaceWord(line, i.Value, ri, 'I', words);
                return ReplaceWord(line, j.Value, rj, 'J', words);
            }

            return line;
        });
    }

    /// <summary>Adds the height map's Z correction to absolute X/Y motion lines (G0/G1; G91 not supported).</summary>
    public static string ApplyHeightMap(string gcode, HeightMap map)
    {
        return TransformLines(gcode, (line, words) =>
        {
            if (!IsMotion(line))
                return line;

            var x = FindWord(words, 'X');
            var y = FindWord(words, 'Y');
            if (x is null || y is null)
                return line;

            var z = map.InterpolateZ(x.Value, y.Value);
            return line + " Z" + z.ToString("0.###", CultureInfo.InvariantCulture);
        });
    }

    private static bool IsMotion(string line)
    {
        var trimmed = line.TrimStart('(', ';', ' ', '\t');
        if (trimmed.Length == 0 || trimmed[0] == '(' || trimmed[0] == ';')
            return false;
        // Only G0/G1/G2/G3 (motion) lines are transformed.
        return trimmed.StartsWith("G0 ", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("G1 ", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("G2 ", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("G3 ", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("G00", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("G01", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("G02", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("G03", StringComparison.OrdinalIgnoreCase);
    }

    private static List<(char Letter, double Value)> Words(string line)
    {
        var words = new List<(char, double)>();
        for (var i = 0; i < line.Length; i++)
        {
            if (!char.IsLetter(line[i]) || line[i] is '#' or '%')
                continue;
            var start = i;
            i++;
            while (i < line.Length && (char.IsDigit(line[i]) || line[i] is '.' or '-' or '+'))
                i++;
            if (i > start + 1 && double.TryParse(line[(start + 1)..i], NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                words.Add((char.ToUpperInvariant(line[start]), v));
        }
        return words;
    }

    private static double? FindWord(List<(char Letter, double Value)> words, char letter)
    {
        foreach (var (l, v) in words)
            if (l == letter)
                return v;
        return null;
    }

    private static string ReplaceWord(string line, double oldValue, double newValue, char letter, List<(char Letter, double Value)> words)
    {
        var formatted = newValue.ToString("0.###", CultureInfo.InvariantCulture);
        var search = letter + oldValue.ToString("0.###", CultureInfo.InvariantCulture);
        var index = line.IndexOf(search, StringComparison.OrdinalIgnoreCase);
        return index >= 0 ? line[..index] + letter + formatted + line[(index + search.Length)..] : line;
    }

    private static string TransformLines(string gcode, Func<string, List<(char, double)>, string> transform)
    {
        var sb = new StringBuilder();
        foreach (var line in gcode.Replace("\r\n", "\n").Split('\n'))
        {
            sb.Append(transform(line, Words(line)));
            sb.Append('\n');
        }
        return sb.ToString().TrimEnd('\n');
    }

}
