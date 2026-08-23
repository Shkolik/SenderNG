using CNC.Core.Services;

namespace SenderNG.Core.Tests;

public class GCodeValidatorLineEndingTests
{
    [Fact]
    public void Validate_crcrlf_endings_foam_file_parses_clean()
    {
        // Reproduces D:\WASP\Block cut.gcode: every line ends \r\r\n
        var lines = new[]
        {
            ";*** FOAM BLOCK ***",
            "M03 S500.00",
            "G01 X9.40 Y150.00 Z9.40 A150.00 F420.0",
            "G01 X8.60 Y1.10 Z8.60 A1.10 F420.0",
            "M05",
        };
        var text = string.Join("\r\r\n", lines) + "\r\r\n";

        var errors = GCodeValidator.Validate(text);

        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_lone_cr_endings_parses_clean()
    {
        var text = "M03 S500.00\rG01 X1 Y2 Z3 F100\r";

        var errors = GCodeValidator.Validate(text);

        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_error_messages_are_never_empty()
    {
        var errors = GCodeValidator.Validate("G01 #\r\n");

        Assert.NotEmpty(errors);
        Assert.All(errors, e => Assert.False(string.IsNullOrWhiteSpace(e.SubstringAfter(": ").Trim())));
    }
}

file static class StringExtensions
{
    public static string SubstringAfter(this string s, string sep)
    {
        var i = s.IndexOf(sep, StringComparison.Ordinal);
        return i < 0 ? s : s[(i + sep.Length)..];
    }
}
