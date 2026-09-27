/*

Copyright (c) 2026 Andrew Shkolik
All rights reserved.

Redistribution and use in source and binary forms, with or without modification,
are permitted provided that the following conditions are met:

· Redistributions of source code must retain the above copyright notice, this
list of conditions and the following disclaimer.

· Redistributions in binary form must reproduce the above copyright notice,
this list of conditions and the following disclaimer in the documentation and/or
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

using Xunit;

namespace SenderNG.Tests;

/// <summary>The pixel reader is now the whole assembly's only place that
/// interprets a framebuffer, so its decode rules are pinned here — with NO
/// Avalonia runtime, because <c>PlotterPixels.Frame</c> is a plain record over
/// a <c>byte[]</c> and every rule below is arithmetic on it. <c>Snapshot</c>
/// itself is the one member that needs a real bitmap, so what is pinned here is
/// everything it produces: the channel order it claims to have resolved, the
/// out-of-range value the rect probes depend on for clipping, and the
/// two-axis counting they were rewritten to get right.</summary>
public class PlotterPixelsTests
{
    private static bool IsRed(byte r, byte g, byte b) => r > 200 && g < 100 && b < 100;

    /// <summary>The red the probe fixtures paint — clear of the
    /// <see cref="IsRed"/> thresholds on both sides, so a fixture can never
    /// fail by landing exactly on one.</summary>
    private const byte Red = 220;

    /// <summary>A frame holding one opaque <paramref name="r,g,b"/> pixel
    /// wherever <paramref name="paint"/> says so and black elsewhere — the
    /// shape a probe actually sees, without a bitmap. The bytes go down in the
    /// order the frame's own flag claims, so a frame that LIES about its
    /// format produces visibly wrong colours instead of plausible ones.</summary>
    private static PlotterPixels.Frame Painted(
        int width, int height, Func<int, int, bool> paint, byte r, byte g, byte b, bool bgra = false)
    {
        var pixels = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                if (!paint(x, y))
                    continue;
                var o = (y * width + x) * 4;
                if (bgra)
                {
                    pixels[o] = b;
                    pixels[o + 1] = g;
                    pixels[o + 2] = r;
                }
                else
                {
                    pixels[o] = r;
                    pixels[o + 1] = g;
                    pixels[o + 2] = b;
                }
                pixels[o + 3] = 255;
            }
        }
        return new PlotterPixels.Frame(width, height, pixels, bgra);
    }

    /// <summary>Whichever byte order the framebuffer name said, the colour a
    /// pixel was PAINTED comes back as r,g,b. The second half of each case
    /// checks the memory order really flipped, so swapping the two
    /// interpretations could not pass both.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void At_decodes_both_channel_orders_into_the_painted_colour(bool bgra)
    {
        var frame = Painted(2, 1, (_, _) => true, 10, 20, 30, bgra);
        Assert.Equal(new PlotterPixels.Rgb(10, 20, 30), PlotterPixels.At(frame, 0, 0));
        Assert.Equal(new PlotterPixels.Rgb(10, 20, 30), PlotterPixels.At(frame, 1, 0));
        Assert.Equal(bgra ? (byte)30 : (byte)10, frame.Pixels[0]);
    }

    /// <summary>Out of range reads as black — which is what lets a rect probe
    /// run past the frame edge without clamping the bounds itself. It also
    /// means an out-of-range hit is indistinguishable from a painted black
    /// pixel, so the colour predicates must not match black (none of the
    /// chrome probes do).</summary>
    [Fact]
    public void At_outside_the_frame_reads_black()
    {
        var frame = Painted(4, 3, (_, _) => true, 10, 20, 30);
        var black = new PlotterPixels.Rgb(0, 0, 0);
        Assert.Equal(black, PlotterPixels.At(frame, -1, 0));
        Assert.Equal(black, PlotterPixels.At(frame, 0, -1));
        Assert.Equal(black, PlotterPixels.At(frame, 4, 0));
        Assert.Equal(black, PlotterPixels.At(frame, 0, 3));
    }

    /// <summary>Inclusive on all four bounds, clipped by the frame, and empty
    /// when the upper bound is below the lower one.</summary>
    [Fact]
    public void CountRect_counts_an_inclusive_rect_clipped_to_the_frame()
    {
        // One red pixel per row, each on a different column: (0,0) (1,1) (2,2) (3,3).
        var frame = Painted(4, 4, (x, y) => x == y, Red, 0, 0);
        Assert.Equal(2, PlotterPixels.CountRect(frame, IsRed, x0: 1, y0: 1, x1: 2, y1: 2));
        Assert.Equal(4, PlotterPixels.CountRect(frame, IsRed, x0: -5, y0: -5, x1: 99, y1: 99));
        Assert.Equal(0, PlotterPixels.CountRect(frame, IsRed, x0: 2, y0: 2, x1: 1, y1: 1));
    }

    /// <summary>The x and y bounds are SEPARATE. A wide-short rect and a
    /// narrow-tall one must disagree, and transposing the upper bounds must
    /// change the answer — the mix-up this reader replaced, where a Y filter
    /// was handed to the X parameter and the two counts came out
    /// interchangeable.</summary>
    [Fact]
    public void CountRect_filters_each_axis_independently()
    {
        // Row 1's two reds are far apart in X and share one Y, so every rect
        // below is biased to the axis it names.
        var frame = Painted(4, 4, (x, y) => y == 1 && (x == 1 || x == 3), Red, 0, 0);
        Assert.Equal(2, PlotterPixels.CountRect(frame, IsRed, x0: 0, y0: 1, x1: 3, y1: 1));
        Assert.Equal(1, PlotterPixels.CountRect(frame, IsRed, x0: 1, y0: 0, x1: 1, y1: 3));
        // The first rect's upper bounds, transposed.
        Assert.Equal(1, PlotterPixels.CountRect(frame, IsRed, x0: 0, y0: 1, x1: 1, y1: 3));
    }

    [Fact]
    public void Count_honors_both_axis_filters_and_a_match()
    {
        var frame = Painted(4, 4, (x, y) => x == y, Red, 0, 0);
        Assert.Equal(4, PlotterPixels.Count(frame, IsRed));
        Assert.Equal(2, PlotterPixels.Count(frame, IsRed, yFilter: y => y < 2));
        Assert.Equal(1, PlotterPixels.Count(frame, IsRed, xFilter: x => x < 1, yFilter: y => y < 2));
        Assert.Equal(0, PlotterPixels.Count(frame, IsRed, yFilter: y => y < 0));
    }

    [Fact]
    public void Centroid_reports_the_match_count_and_its_center()
    {
        var frame = Painted(6, 4, (x, y) => y == 1 && (x == 1 || x == 3), Red, 0, 0);
        var (count, x, y) = PlotterPixels.Centroid(frame, IsRed);
        Assert.Equal(2, count);
        Assert.Equal(2, x);
        Assert.Equal(1, y);
    }

    /// <summary>The glyph and grey-path probes pass an x filter to narrow the
    /// frame before taking the center, so that filter has to constrain the
    /// mean and not merely the count.</summary>
    [Fact]
    public void Centroid_honors_the_x_filter_the_glyph_probes_pass()
    {
        var frame = Painted(8, 2, (x, y) => y == 0 && (x == 1 || x == 5), Red, 0, 0);
        var left = PlotterPixels.Centroid(frame, IsRed, xFilter: x => x < 4);
        Assert.Equal(1, left.Count);
        Assert.Equal(1, left.X);
        Assert.Equal(0, left.Y);
        var right = PlotterPixels.Centroid(frame, IsRed, xFilter: x => x >= 4);
        Assert.Equal(1, right.Count);
        Assert.Equal(5, right.X);
    }

    /// <summary>A vanished glyph must report itself as the zeroed triple
    /// rather than dividing by zero — the whole point of the count the glyph
    /// probes assert on.</summary>
    [Fact]
    public void Centroid_of_a_frame_with_no_match_is_the_zeroed_triple()
    {
        var frame = Painted(4, 4, (_, _) => true, 10, 20, 30);
        Assert.Equal((0, 0d, 0d), PlotterPixels.Centroid(frame, IsRed));
    }
}
