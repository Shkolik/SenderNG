
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

using CNC.GCode;

namespace CNC.Core.Services;

/// <summary>
/// Validates G-code text line by line using <see cref="GCodeParser"/>,
/// collecting errors as "Line {n}: {message}" entries (empty list = valid).
/// </summary>
public static class GCodeValidator
{
    private const int MaxErrors = 10;

    public static IReadOnlyList<string> Validate(string gcodeText)
    {
        var errors = new List<string>();
        if (string.IsNullOrEmpty(gcodeText))
            return errors;

        var parser = new GCodeParser();
        var n = 0;
        foreach (var raw in gcodeText.Split(["\r\n", "\n", "\r"], StringSplitOptions.None))
        {
            n++;
            var trimmed = raw.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith("(") || trimmed.StartsWith(";") || trimmed.StartsWith("%"))
                continue;

            try
            {
                var block = trimmed;
                parser.ParseBlock(ref block, false);
            }
            catch (Exception ex)
            {
                // Any per-line parser failure is a validation error, never a crash:
                // the ported scanner can throw raw IndexOutOfRangeException on bad input.
                if (errors.Count < MaxErrors)
                    errors.Add($"Line {n}: {Describe(ex)}");
            }
        }
        return errors;
    }

    internal static string Describe(Exception ex)
    {
        var msg = ex.Message;
        var cur = ex.InnerException;
        while (cur is not null)
        {
            if (!string.IsNullOrWhiteSpace(cur.Message))
                msg = string.IsNullOrWhiteSpace(msg) ? cur.Message : $"{msg} ({cur.Message})";
            cur = cur.InnerException;
        }
        return string.IsNullOrWhiteSpace(msg) ? "Invalid GCode" : msg;
    }
}
