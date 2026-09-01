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

using CNC.Core.Services;
using Xunit;

namespace SenderNG.Core.Tests;

public class GCodeProgramTests
{
    [Fact]
    public void Clean_file_produces_entries_with_file_line_numbers_and_no_errors()
    {
        var text = "G21 G90\r\n; setup comment\nG0 X10 Y20\n\n( drilling )\nG1 X30 F100\nM5\n";
        var program = GCodeProgram.Parse(text);

        Assert.False(program.HasErrors);
        Assert.Empty(program.Errors);
        Assert.Equal(6, program.Entries.Count); // blank line is skipped
        Assert.NotEmpty(program.Tokens);

        Assert.Equal(1, program.Entries[0].FileLine);
        Assert.Equal("G21 G90", program.Entries[0].Text);
        Assert.False(program.Entries[0].IsComment);

        Assert.Equal(2, program.Entries[1].FileLine);
        Assert.True(program.Entries[1].IsComment); // ";" whole-line comment

        Assert.Equal(3, program.Entries[2].FileLine); // blank line #4 skipped, physical line kept
        Assert.Equal("G0 X10 Y20", program.Entries[2].Text);

        Assert.True(program.Entries[3].IsComment); // "( drilling )"
        Assert.Equal(5, program.Entries[3].FileLine);
        Assert.Equal(7, program.Entries[^1].FileLine);

        // Send order is dense even when physical lines are skipped.
        Assert.Equal(1, program.Entries[0].Sequence);
        Assert.Equal(6, program.Entries[^1].Sequence);
    }

    [Fact]
    public void Parse_errors_carry_exact_file_line_numbers()
    {
        var program = GCodeProgram.Parse("G21 G90\nG1 X5 X6\nG0 X1");

        Assert.True(program.HasErrors);
        var error = Assert.Single(program.Errors);
        Assert.StartsWith("Line 2:", error);

        // The bad line still appears as an entry (display) but is not sendable.
        Assert.Equal(3, program.Entries.Count);
        Assert.Equal("G1 X5 X6", program.Entries[1].Text);
        Assert.False(program.Entries[1].IsSendable);

        // Good lines around it parse normally.
        Assert.True(program.Entries[0].IsSendable);
        Assert.True(program.Entries[2].IsSendable);
    }

    [Fact]
    public void Errors_are_capped_at_ten()
    {
        var bad = string.Join("\n", Enumerable.Repeat("G1 X5 X6", 15));
        var program = GCodeProgram.Parse(bad);

        Assert.Equal(10, program.Errors.Count);
    }

    [Fact]
    public void M2_marks_the_program_end_and_post_end_lines_do_not()
    {
        var program = GCodeProgram.Parse("G21\nM2\nG0 X1");
        var entries = program.Entries;

        Assert.False(entries[0].IsProgramEnd);
        Assert.True(entries[1].IsProgramEnd);
        Assert.False(entries[2].IsProgramEnd); // sticky flag: only the FIRST end line is marked
    }

    [Fact]
    public void Percent_demarcation_is_never_sendable_and_closing_percent_ends()
    {
        var program = GCodeProgram.Parse("%\nG21\nG0 X1\n%");
        var entries = program.Entries;

        Assert.True(entries[0].IsDemarcation);
        Assert.False(entries[0].IsSendable);
        Assert.False(entries[0].IsProgramEnd);
        Assert.True(entries[^1].IsDemarcation);
        Assert.True(entries[^1].IsProgramEnd); // closing % ends the program
    }

    [Fact]
    public void Limits_from_tokens_match_the_string_path()
    {
        var text = "G21 G90\nG0 X10 Y20\nG1 X30 Y40 F100\n";
        var program = GCodeProgram.Parse(text);

        Assert.False(program.HasErrors);
        var fromTokens = GCodeLimitsCalculator.Compute(program.Tokens);
        var fromText = GCodeLimitsCalculator.Compute(text);

        Assert.NotNull(fromTokens);
        Assert.Equal(fromText!, fromTokens!);
        Assert.Equal(10, fromTokens!.First(l => l.Letter == 'X').Min);
        Assert.Equal(20, fromTokens.First(l => l.Letter == 'Y').Min);
    }

    [Fact]
    public void Feed_rate_is_modal_once_set_anywhere_before_the_first_G1()
    {
        // F is modal: one F word legalizes every later bare G1 (grbl semantics).
        // The gate parses the whole file with ONE parser instance, so the modal
        // feedRate state carries across lines — a file must not be rejected for
        // omitting F on later lines when it was set once at the start (or mid-file).
        var setFirst = GCodeProgram.Parse("G21\nF100\nG0 X10\nG1 X20\nG1 Y30\nM5");
        Assert.False(setFirst.HasErrors);
        Assert.Equal(6, setFirst.Entries.Count(e => e.IsSendable));

        var setMidFile = GCodeProgram.Parse("G21\nG0 X10\nF100\nG1 X20\nM5");
        Assert.False(setMidFile.HasErrors);

        // Only a file with no F anywhere is invalid (grbl rejects it too).
        var neverSet = GCodeProgram.Parse("G21\nG0 X10\nG1 X20\nM5");
        Assert.True(neverSet.HasErrors);
        Assert.Contains("feed rate is not set", neverSet.Errors[0]);
    }

    [Fact]
    public void Realtime_and_dollar_lines_are_never_sendable()
    {
        var program = GCodeProgram.Parse("?\n$10=250\nG0 X1\n~");
        var entries = program.Entries;

        Assert.False(entries[0].IsSendable); // '?'
        Assert.False(entries[1].IsSendable); // '$'
        Assert.True(entries[2].IsSendable);
        Assert.False(entries[3].IsSendable); // '~' realtime
    }
}