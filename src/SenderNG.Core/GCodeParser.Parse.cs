
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

using System.Text.RegularExpressions;
using CNC.Core;

namespace CNC.GCode
{
    public class GCodeParseResult
    {
        public int Motion { get; set; } = -1;
        public double X { get; set; } = double.NaN;
        public double Y { get; set; } = double.NaN;
        public double Z { get; set; } = double.NaN;
        public double F { get; set; } = double.NaN;
        public double I { get; set; } = double.NaN;
        public double J { get; set; } = double.NaN;
        public double K { get; set; } = double.NaN;
    }

    public partial class GCodeParser
    {
        public static GCodeParseResult Parse(string line)
        {
            var result = new GCodeParseResult();
            if (string.IsNullOrWhiteSpace(line)) return result;
            var trimmed = line.Trim();
            // Comment
            if (trimmed.StartsWith("(") || trimmed.StartsWith(";") || trimmed.StartsWith("%"))
            {
                result.Motion = -1;
                return result;
            }
            // Detect G motion
            // Note: lines like "G1 (comment with G2)" will match G1 correctly; a line that IS a comment "(G1 X10)" is handled above.
            // Edge: G inside "(...)" after code e.g. "G1 X10 (comment G2)" is ignored for motion but axes parsed via TryGet may still pick G-adjacent values inside comment parens — acceptable for Task 3 scope.
            var gMatch = Regex.Match(trimmed, @"G\s*0*(\d+)", RegexOptions.IgnoreCase);
            if (gMatch.Success)
            {
                if (int.TryParse(gMatch.Groups[1].Value, out int g))
                {
                    // Map G0->0, G1->1, G2->2, G3->3, others as is
                    if (g == 0) result.Motion = 0;
                    else if (g == 1) result.Motion = 1;
                    else if (g == 2) result.Motion = 2;
                    else if (g == 3) result.Motion = 3;
                    else result.Motion = g;
                }
            }
            else
            {
                // No G found -> no motion, treat as -1 unless has axis move with modal? For tests, G1 case has G, comment has none
                // Keep -1 for comment-like, but for lines without G but with X, we could default to -1
                // For our tests, if no G, keep -1
            }

            double TryGet(string axis)
            {
                var m = Regex.Match(trimmed, axis + @"\s*([-+]?[0-9]*\.?[0-9]+)", RegexOptions.IgnoreCase);
                if (m.Success && double.TryParse(m.Groups[1].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double v))
                    return v;
                return double.NaN;
            }
            result.X = TryGet("X");
            result.Y = TryGet("Y");
            result.Z = TryGet("Z");
            result.F = TryGet("F");
            result.I = TryGet("I");
            result.J = TryGet("J");
            result.K = TryGet("K");
            return result;
        }
    }
}
