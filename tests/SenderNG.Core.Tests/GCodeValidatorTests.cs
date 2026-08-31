
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

public class GCodeValidatorTests
{
    [Fact]
    public void Validate_empty_text_returns_no_errors()
    {
        Assert.Empty(GCodeValidator.Validate(string.Empty));
        Assert.Empty(GCodeValidator.Validate("   "));
    }

    [Fact]
    public void Validate_valid_gcode_returns_no_errors()
    {
        var gcode = "G1 X10 Y20 F200\r\nG0 X5\r\nM3 S1000";
        Assert.Empty(GCodeValidator.Validate(gcode));
    }

    [Fact]
    public void Validate_comments_and_blank_lines_are_valid()
    {
        var gcode = "; header comment\n\n(inline comment)\nG1 X10 F100";
        Assert.Empty(GCodeValidator.Validate(gcode));
    }

    [Fact]
    public void Validate_invalid_line_reports_line_number()
    {
        var gcode = "G1 X10 F200\nX\nG0 Y5";
        var errors = GCodeValidator.Validate(gcode);

        Assert.NotEmpty(errors);
        Assert.Contains(errors, e => e.StartsWith("Line 2:"));
        // valid lines around the bad one must not be reported
        Assert.DoesNotContain(errors, e => e.StartsWith("Line 1:"));
        Assert.DoesNotContain(errors, e => e.StartsWith("Line 3:"));
    }
}
