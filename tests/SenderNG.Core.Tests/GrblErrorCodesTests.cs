using CNC.Core.Services;

namespace SenderNG.Core.Tests;

public class GrblErrorCodesTests
{
    [Theory]
    [InlineData(2, "numeric value format is not valid")]
    [InlineData(16, "prohibited g-code")]
    [InlineData(21, "modal group")]
    [InlineData(9, "locked out")]
    [InlineData(79, "critical event")]
    [InlineData(46, "Home machine")]
    public void Describes_known_codes(int code, string expectedFragment)
    {
        var text = GrblErrorCodes.Describe(code);
        Assert.NotNull(text);
        Assert.Contains(expectedFragment, text, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(0)]   // "no error" isn't a reply
    [InlineData(93)]  // beyond the table (grblHAL max is 92)
    [InlineData(200)]
    [InlineData(-1)]
    public void Unknown_codes_return_null(int code)
        => Assert.Null(GrblErrorCodes.Describe(code));

    [Fact]
    public void Decorate_appends_the_explanation_to_an_error_reply()
    {
        // ioSender console format: raw reply + " - " + dictionary text.
        Assert.Equal(
            "error:2 - Missing the expected G-code word value or numeric value format is not valid.",
            GrblErrorCodes.Decorate("error:2"));
    }

    [Fact]
    public void Decorate_passes_unknown_and_non_error_lines_through()
    {
        Assert.Equal("error:200", GrblErrorCodes.Decorate("error:200"));
        Assert.Equal("ok", GrblErrorCodes.Decorate("ok"));
        Assert.Equal("<Idle|WPos:0,0,0>", GrblErrorCodes.Decorate("<Idle|WPos:0,0,0>"));
    }

    [Fact]
    public void Hint_exists_for_actionable_cases()
    {
        Assert.Contains("Home", GrblErrorCodes.Hint(15));
        Assert.Contains("critical alarm", GrblErrorCodes.Hint(79));
        Assert.Null(GrblErrorCodes.Hint(21));
    }
}
