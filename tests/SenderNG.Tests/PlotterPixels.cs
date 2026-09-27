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

using System.Runtime.InteropServices;
using Avalonia.Media.Imaging;

namespace SenderNG.Tests;

/// <summary>The one pixel reader for the plotter headless tests. Both
/// <c>PlotterViewHeadlessTests</c> and <c>PlotterLiveRunHeadlessTests</c> use
/// this instead of their own private row walkers, so a new chrome test never
/// has to guess which accessor exists — and so one implementation serves every
/// captured frame. The parameter is <see cref="WriteableBitmap"/> because
/// <c>Lock()</c> exists only there: Avalonia 12's <see cref="Bitmap"/> base type
/// (and <c>RenderTargetBitmap</c>, which derives from it) exposes no framebuffer
/// lock, so a read-only bitmap would need a <c>CopyPixels</c> scratch
/// framebuffer whose pixel format has to be negotiated up front.</summary>
internal static class PlotterPixels
{
    internal readonly record struct Rgb(byte R, byte G, byte B)
    {
        public override string ToString() => $"#{R:x2}{G:x2}{B:x2}";
    }

    /// <summary>Copies the framebuffer out, so callers read without holding a
    /// lock. The channel order is resolved once here.</summary>
    internal readonly record struct Frame(int Width, int Height, byte[] Pixels, bool Bgra);

    internal static Frame Snapshot(WriteableBitmap bmp)
    {
        using var fb = bmp.Lock();
        // The format NAME is the only evidence of channel order available, so
        // this gate accepts exactly the orders it can decode and refuses every
        // other name instead of guessing: a wrong guess silently returns
        // plausible colour counts, a refusal is loud. A leading "P" is
        // Avalonia's premultiplied flag, which changes alpha scaling and not
        // the byte order — alpha is ignored on both sides (Pbgra8888 has
        // always been read this way), so Pbgra and Prgba join their
        // non-premultiplied twins. "Rgb32" is deliberately NOT accepted: that
        // name states no order at all, so it must not be guessed either.
        var format = fb.Format.ToString();
        var bgra = format.Contains("Bgra") || format.Contains("Pbgra");
        if (!bgra && !(format.Contains("Rgba") || format.Contains("PArgb") || format.Contains("Prgba")))
            throw new InvalidOperationException($"unexpected framebuffer format: {format}");

        var width = fb.Size.Width;
        var height = fb.Size.Height;
        var copy = new byte[width * height * 4];
        var row = new byte[fb.RowBytes];
        for (var y = 0; y < height; y++)
        {
            Marshal.Copy(fb.Address + y * fb.RowBytes, row, 0, fb.RowBytes);
            Array.Copy(row, 0, copy, y * width * 4, width * 4);
        }
        return new Frame(width, height, copy, bgra);
    }

    internal static Rgb At(Frame frame, int x, int y)
    {
        if (x < 0 || y < 0 || x >= frame.Width || y >= frame.Height)
            return new Rgb(0, 0, 0);
        var o = (y * frame.Width + x) * 4;
        return frame.Bgra
            ? new Rgb(frame.Pixels[o + 2], frame.Pixels[o + 1], frame.Pixels[o])
            : new Rgb(frame.Pixels[o], frame.Pixels[o + 1], frame.Pixels[o + 2]);
    }

    internal static int Count(
        Frame frame, Func<byte, byte, byte, bool> match,
        Func<int, bool>? xFilter = null, Func<int, bool>? yFilter = null)
    {
        var count = 0;
        for (var y = 0; y < frame.Height; y++)
        {
            if (yFilter is not null && !yFilter(y))
                continue;
            for (var x = 0; x < frame.Width; x++)
            {
                if (xFilter is not null && !xFilter(x))
                    continue;
                var p = At(frame, x, y);
                if (match(p.R, p.G, p.B))
                    count++;
            }
        }
        return count;
    }

    internal static int CountRect(
        Frame frame, Func<byte, byte, byte, bool> match,
        int x0, int y0, int x1, int y1) =>
        Count(frame, match,
            xFilter: x => x >= x0 && x <= x1,
            yFilter: y => y >= y0 && y <= y1);

    /// <summary>How many pixels match, and where their center of mass sits —
    /// the tool-glyph probe, which is the one reader that needs a position and
    /// not just a tally. A frame with no match reads as the zeroed triple, so
    /// a glyph that vanished reports itself instead of dividing by zero.</summary>
    internal static (int Count, double X, double Y) Centroid(
        Frame frame, Func<byte, byte, byte, bool> match,
        Func<int, bool>? xFilter = null, Func<int, bool>? yFilter = null)
    {
        var count = 0;
        double sx = 0, sy = 0;
        for (var y = 0; y < frame.Height; y++)
        {
            if (yFilter is not null && !yFilter(y))
                continue;
            for (var x = 0; x < frame.Width; x++)
            {
                if (xFilter is not null && !xFilter(x))
                    continue;
                var p = At(frame, x, y);
                if (!match(p.R, p.G, p.B))
                    continue;
                count++;
                sx += x;
                sy += y;
            }
        }
        return count == 0 ? (0, 0d, 0d) : (count, sx / count, sy / count);
    }
}
