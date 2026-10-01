using System.Globalization;
using System.Text.RegularExpressions;
using SenderNG.Modules.Excellon;
using SenderNG.Modules.Shared;
using Xunit;

namespace SenderNG.Core.Tests;

// Excellon2GCode divides a drill/slot START coordinate by both the format factor
// and the unit scale, but divided the G85 END by the unit scale only
// (audit §1.17). Every header that declares a decimal format therefore produced
// a slot whose end is off by exactly that format's factor - 1000x for a
// 3-decimal file - which also inflated the interpolated G81 hole count by the
// same factor (the §1.8 length-derived count).
//
// The tests below pin the END against the START rather than against a magic
// number, because the START was already correct: that is what makes this a
// start/end disagreement rather than a shared misreading of the format.

public class ExcellonSlotScalingTests
{
    private static string Convert(string header, string body) =>
        new Excellon2GCode()
            .Convert("M48\n" + header + "\nT01C2.4\n%\nT01\n" + body + "\nM95\n",
                     new ConverterParameters()) ?? "";

    private static (double StartX, double StartY, double EndX, double EndY) Slot(string gcode)
    {
        var m = Regex.Match(gcode, @"\(Slot\s+([-\d.]+);([-\d.]+)\s+-\s+([-\d.]+);([-\d.]+)\)");
        Assert.True(m.Success, $"no slot comment in output:{Environment.NewLine}{gcode}");
        return (Num(m.Groups[1]), Num(m.Groups[2]), Num(m.Groups[3]), Num(m.Groups[4]));

        static double Num(Group g) => double.Parse(g.Value, CultureInfo.InvariantCulture);
    }

    [Fact]
    public void Excellon_slot_end_honours_a_header_declared_format()
    {
        // "METRIC,LZ,000.000" states 3 decimal places outright, so nothing is
        // being inferred: X001234 is 1.234mm and the G85 end X001500 is 1.5mm.
        var declared = Convert("METRIC,LZ,000.000", "X001234Y000500G85X001500Y000500");

        var (sx, sy, ex, ey) = Slot(declared);
        Assert.Equal(1.234, sx, 6);
        Assert.Equal(0.5, sy, 6);
        Assert.Equal(1.5, ex, 6);
        Assert.Equal(0.5, ey, 6);
    }

    [Fact]
    public void Excellon_slot_end_matches_the_start_scaling_across_every_format()
    {
        // The strong form of the same check. A header-declared format and the
        // explicit-decimal spelling describe identical geometry, so they must
        // produce byte-identical output - including every interpolated G81.
        var declared = Convert("METRIC,LZ,000.000", "X001234Y000500G85X001500Y000500");
        var spelledOut = Convert("METRIC", "X1.234Y0.5G85X1.5Y0.5");

        Assert.Equal(spelledOut, declared);
    }

    [Theory]
    [InlineData("METRIC,LZ,0000.0000", "X012340Y00500G85X015000Y00500", 1.234, 0.05, 1.5, 0.05)]
    [InlineData("METRIC,TZ", "X0001234Y0000500G85X0001500Y0000500", 1.234, 0.5, 1.5, 0.5)]
    public void Excellon_slot_end_honours_the_format_under_any_header(
        string header, string body,
        double sx, double sy, double ex, double ey)
    {
        var slot = Slot(Convert(header, body));

        Assert.Equal(sx, slot.StartX, 6);
        Assert.Equal(sy, slot.StartY, 6);
        Assert.Equal(ex, slot.EndX, 6);
        Assert.Equal(ey, slot.EndY, 6);
    }

    [Fact]
    public void Excellon_slot_end_honours_a_declared_format_in_inches()
    {
        // Same defect in inch mode: the end skipped the factor and came out at
        // 59.06in instead of 0.059in for a 1.266mm slot.
        var (sx, _, ex, _) = Slot(Convert("INCH,LZ,0000.0000", "X012340Y00500G85X015000Y00500"));

        Assert.Equal(12340 / 10000d / 25.4d, sx, 9);
        Assert.Equal(15000 / 10000d / 25.4d, ex, 9);
    }

    [Fact]
    public void Excellon_tz_slot_does_not_inflate_the_interpolated_hole_count()
    {
        // §1.17 amplified §1.8: the G81 count is derived from the slot length
        // (§1.8 :351), so a 1000x-too-long slot emitted ~1975 holes for a
        // 0.266mm slot that needs 2.
        var gcode = Convert("METRIC,TZ", "X0001234Y0000500G85X0001500Y0000500");

        Assert.Equal(2, gcode.Split('\n').Count(l => l.Contains("G81")));
    }

    [Fact]
    public void Excellon_slot_end_carries_the_start_for_an_absent_axis()
    {
        // Guard against over-fixing: when G85 omits an axis the end is modal and
        // must stay at the already-scaled start. Applying the factor to a
        // missing word, or scaling the start twice, would break this.
        var (sx, sy, ex, ey) = Slot(Convert("METRIC,LZ,000.000", "X001234Y000500G85X001500"));

        Assert.Equal(1.5, ex, 6);    // X end is declared, and scaled once
        Assert.Equal(sy, ey, 9);      // Y end absent, carries the start
        Assert.Equal(1.234, sx, 6);
        Assert.Equal(0.5, sy, 6);
    }
}
