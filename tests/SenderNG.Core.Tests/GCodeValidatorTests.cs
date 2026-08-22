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
