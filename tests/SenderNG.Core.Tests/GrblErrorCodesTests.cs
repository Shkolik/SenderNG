using CNC.Core.Services;

namespace SenderNG.Core.Tests;

public class GrblErrorCodesTests
{
    [Theory]
    [InlineData(16, "invalid jog command")]
    [InlineData(21, "modal group")]
    [InlineData(9, "locked out")]
    [InlineData(79, "critical alarm")]
    public void Describes_known_codes(int code, string expectedFragment)
    {
        var text = GrblErrorCodes.Describe(code);
        Assert.NotNull(text);
        Assert.Contains(expectedFragment, text, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(18)] // classic grbl has no code 18 (grblHAL does)
    [InlineData(99)]
    [InlineData(-1)]
    public void Unknown_codes_return_null(int code)
        => Assert.Null(GrblErrorCodes.Describe(code));
}
