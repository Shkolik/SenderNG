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
/// One loaded program line. <see cref="Sequence"/> is the 1-based send order;
/// <see cref="FileLine"/> the 1-based physical line in the file (displayed in
/// the program list). Lines the controller must never receive (percent
/// demarcation, realtime characters) are flagged <see cref="IsSendable"/> =
/// false and skipped by the streamer without affecting row statuses.
/// </summary>
public sealed record GCodeProgramEntry(
    int Sequence,
    int FileLine,
    string Text,
    bool IsComment,
    bool IsProgramEnd,
    bool IsDemarcation,
    bool IsSendable);

/// <summary>
/// Unified load model: parses a G-code file once with the engine
/// (<see cref="GCodeParser"/>), giving the program list rows (file line
/// numbers), the token stream for limits computation, and authoritative
/// parse errors ("Line {n}: {msg}") — the SAME parse pass the streamer and the
/// limits calculator consume, so a file the engine rejects can never be
/// loaded, and a file that loads always streams.
/// </summary>
public sealed class GCodeProgram
{
    private const int MaxErrors = 10;

    public IReadOnlyList<GCodeProgramEntry> Entries { get; init; } = [];
    public IReadOnlyList<GCodeToken> Tokens { get; init; } = [];
    public IReadOnlyList<string> Errors { get; init; } = [];

    /// <summary>Significant decimals the parser tracked while reading the file
    /// (grows with coordinate word precision, 4 after a G20) — transforms use it
    /// to round rotated values to the file's own resolution.</summary>
    public int Decimals { get; init; } = 3;

    public bool HasErrors => Errors.Count > 0;

    public static GCodeProgram Parse(string? text)
    {
        var entries = new List<GCodeProgramEntry>();
        var errors = new List<string>();
        var parser = new GCodeParser();

        if (!string.IsNullOrEmpty(text))
        {
            var sequence = 0;
            var fileLine = 0;
            foreach (var raw in text.Split(["\r\n", "\n", "\r"], StringSplitOptions.None))
            {
                fileLine++;
                var trimmed = raw.Trim();
                if (trimmed.Length == 0)
                    continue;

                sequence++;
                var isComment = trimmed[0] == '(' || trimmed[0] == ';';
                var isDemarcation = trimmed[0] == '%';

                // Parser.ProgramEnd is sticky (M2/M30 or the second % once parsed);
                // the program-end marker belongs to the FIRST line that set it.
                var endBefore = parser.ProgramEnd;
                var isProgramEnd = false;
                var parseFailed = false;
                try
                {
                    var block = trimmed;
                    if (parser.ParseBlock(ref block, false))
                        isProgramEnd = parser.ProgramEnd && !endBefore;
                }
                catch (Exception ex)
                {
                    parseFailed = true;
                    if (errors.Count < MaxErrors)
                        errors.Add($"Line {fileLine}: {GCodeValidator.Describe(ex)}");
                }

                // A line the engine could not parse must never reach the controller.
                var sendable = !parseFailed && !isDemarcation && trimmed[0] is not ('$' or '!' or '~' or '?' or ';');
                entries.Add(new GCodeProgramEntry(sequence, fileLine, trimmed, isComment,
                    isProgramEnd, isDemarcation, sendable));
            }
        }

        return new GCodeProgram
        {
            Entries = entries,
            Tokens = parser.Tokens,
            Errors = errors,
            Decimals = parser.Decimals,
        };
    }
}