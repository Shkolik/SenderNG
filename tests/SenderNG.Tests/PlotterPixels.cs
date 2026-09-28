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

using System.IO.Compression;
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

    /// <summary>Reads the pixels of a PNG FILE. This exists because Avalonia 12
    /// offers no way to get a framebuffer back out of a decoded
    /// <see cref="Bitmap"/> or a <c>RenderTargetBitmap</c> — <c>Lock</c> is on
    /// <see cref="WriteableBitmap"/> only, and <c>WriteableBitmap</c> has no
    /// drawing context — so a test that wants to see what the PNG export
    /// actually wrote has to decode the file itself. (Measured, not guessed:
    /// all four candidates were compiled against the real 12.0.0 assembly.)
    /// <para>Decoding the real file is also the stronger assertion: it proves the
    /// bytes on disk, which is what the user opens, rather than the in-memory
    /// bitmap the view happened to be holding.
    /// <para>Strict on purpose: 8-bit, non-interlaced, RGB or RGBA only, and
    /// anything else throws. A lenient decoder would quietly mis-read a file and
    /// hand back plausible colour counts, which is the exact failure this helper
    /// exists to rule out.</summary>
    internal static Frame SnapshotPng(string path)
    {
        var bytes = File.ReadAllBytes(path);
        ReadOnlySpan<byte> sig = [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A];
        if (bytes.Length < 8 || !bytes.AsSpan(0, 8).SequenceEqual(sig))
            throw new InvalidDataException($"{path} is not a PNG");

        var width = 0;
        var height = 0;
        var channels = 0;
        using var idat = new MemoryStream();
        var p = 8;
        while (p + 8 <= bytes.Length)
        {
            var len = ReadBe32(bytes, p);
            var type = System.Text.Encoding.ASCII.GetString(bytes, p + 4, 4);
            var data = p + 8;
            switch (type)
            {
                case "IHDR":
                    width = ReadBe32(bytes, data);
                    height = ReadBe32(bytes, data + 4);
                    var depth = bytes[data + 8];
                    var color = bytes[data + 9];
                    var interlace = bytes[data + 12];
                    if (depth != 8 || interlace != 0 || (color != 2 && color != 6))
                        throw new InvalidDataException(
                            $"{path}: unsupported PNG (depth {depth}, colour {color}, interlace {interlace})");
                    channels = color == 2 ? 3 : 4;
                    break;
                case "IDAT":
                    idat.Write(bytes, data, len);
                    break;
            }
            p = data + len + 4; // payload + CRC
        }
        if (width <= 0 || height <= 0)
            throw new InvalidDataException($"{path} has no IHDR");

        idat.Position = 0;
        using var inflate = new System.IO.Compression.ZLibStream(idat, CompressionMode.Decompress);
        using var raw = new MemoryStream();
        inflate.CopyTo(raw);
        var scan = raw.ToArray();

        // Each scanline is one filter byte then width*channels bytes; the filters
        // are defined against the PREVIOUS line, so unfiltering is sequential.
        var stride = width * channels;
        var pixels = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            var filter = scan[y * (stride + 1)];
            var src = y * (stride + 1) + 1;
            var dst = y * stride;
            for (var x = 0; x < stride; x++)
            {
                int rawByte = scan[src + x];
                int a = x >= channels ? scan[dst + x - channels] : 0;
                int b = y > 0 ? scan[dst - stride + x] : 0;
                int c = x >= channels && y > 0 ? scan[dst - stride + x - channels] : 0;
                var value = filter switch
                {
                    0 => rawByte,
                    1 => rawByte + a,
                    2 => rawByte + b,
                    3 => rawByte + (a + b) / 2,
                    4 => rawByte + Paeth(a, b, c),
                    _ => throw new InvalidDataException($"{path}: unknown PNG filter {filter} on row {y}"),
                };
                scan[dst + x] = (byte)(value & 0xFF);
            }
            for (var x = 0; x < width; x++)
            {
                var s = dst + x * channels;
                var o = (y * width + x) * 4;
                pixels[o] = scan[s];
                pixels[o + 1] = scan[s + 1];
                pixels[o + 2] = scan[s + 2];
                pixels[o + 3] = channels == 4 ? scan[s + 3] : (byte)255;
            }
        }
        return new Frame(width, height, pixels, false);
    }

    private static int ReadBe32(byte[] b, int o) =>
        (b[o] << 24) | (b[o + 1] << 16) | (b[o + 2] << 8) | b[o + 3];

    /// <summary>PNG's Paeth predictor: whichever of left / above / upper-left is
    /// closest to their linear estimate.</summary>
    private static int Paeth(int a, int b, int c)
    {
        var p = a + b - c;
        var pa = Math.Abs(p - a);
        var pb = Math.Abs(p - b);
        var pc = Math.Abs(p - c);
        if (pa <= pb && pa <= pc)
            return a;
        return pb <= pc ? b : c;
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
