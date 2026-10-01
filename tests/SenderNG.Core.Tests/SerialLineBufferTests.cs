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

namespace SenderNG.Core.Tests;

public class SerialLineBufferTests
{
    [Fact]
    public void Splits_a_chunk_into_lines_and_keeps_the_remainder()
    {
        var buffer = new SerialLineBuffer();

        buffer.Append("ok\r\nerror:20\n[PRB:1.000:2.000");

        Assert.True(buffer.TryTakeLine(out var first));
        Assert.Equal("ok", first);
        Assert.True(buffer.TryTakeLine(out var second));
        Assert.Equal("error:20", second);
        Assert.False(buffer.TryTakeLine(out _));
    }

    [Fact]
    public void A_line_spread_across_several_chunks_is_reassembled()
    {
        var buffer = new SerialLineBuffer();

        buffer.Append("ok");
        Assert.False(buffer.TryTakeLine(out _));
        buffer.Append("\n");
        Assert.True(buffer.TryTakeLine(out var line));
        Assert.Equal("ok", line);
    }

    [Fact]
    public void An_unterminated_line_past_the_cap_does_not_wedge_the_reader()
    {
        var buffer = new SerialLineBuffer();

        // 8 KB of framing garbage with no terminator - electrical noise or a
        // firmware debug dump. Before the fix the buffer simply stopped
        // accepting bytes here, so TryTakeLine never returned true again and
        // every later ok/error/[PRB:] line was dropped for the life of the
        // connection while the app still looked connected.
        buffer.Append(new string('x', 8192));
        Assert.False(buffer.TryTakeLine(out _));

        // The controller's next reply must still come through.
        buffer.Append("\nok\n");

        Assert.True(buffer.TryTakeLine(out var line), "the buffer never recovered from the oversized line");
        Assert.Equal("ok", line);
    }

    [Fact]
    public void An_unterminated_garbage_run_never_grows_past_the_cap()
    {
        var buffer = new SerialLineBuffer();

        buffer.Append(new string('x', 200_000));
        buffer.Append("more\n");

        Assert.True(buffer.Length <= SerialLineBuffer.MaxLineLength,
            $"buffered {buffer.Length} bytes against a {SerialLineBuffer.MaxLineLength} cap");
    }

    [Fact]
    public void The_cap_judges_an_unterminated_run_not_a_complete_line()
    {
        // A read that happens to carry a complete line is unambiguous framed
        // data, so it is delivered even when longer than the cap. The cap only
        // decides what to do with a run that has not been terminated yet —
        // that ambiguity is the wedge, and a discarded complete line would just
        // be data loss.
        var buffer = new SerialLineBuffer();
        var complete = new string('d', 5000);

        buffer.Append(complete + "\nok\n");

        Assert.True(buffer.TryTakeLine(out var line));
        Assert.Equal(complete, line);
        Assert.True(buffer.TryTakeLine(out var next));
        Assert.Equal("ok", next);
    }

    [Fact]
    public void A_chunked_run_past_the_cap_is_discarded_and_the_reader_resyncs()
    {
        // The realistic shape: a 5 KB garbage run arriving across several reads,
        // so the cap is crossed before the terminator shows up. This is the
        // case the wedge fix exists for.
        var buffer = new SerialLineBuffer();
        var huge = new string('d', 5000);

        for (var at = 0; at < huge.Length; at += 512)
            buffer.Append(huge.Substring(at, Math.Min(512, huge.Length - at)));
        buffer.Append("\nok\n");

        Assert.True(buffer.TryTakeLine(out var line), "the reader never resynced after a chunked garbage run");
        Assert.Equal("ok", line);
    }

    [Fact]
    public void A_resync_does_not_swallow_the_lines_that_follow_the_terminator()
    {
        var buffer = new SerialLineBuffer();

        buffer.Append(new string('x', 8192));
        buffer.Append("ignored\ngood1\ngood2\n");

        Assert.True(buffer.TryTakeLine(out var first));
        Assert.Equal("good1", first);
        Assert.True(buffer.TryTakeLine(out var second));
        Assert.Equal("good2", second);
        Assert.False(buffer.TryTakeLine(out _));
    }

    [Fact]
    public void Back_to_back_oversized_lines_each_resync()
    {
        var buffer = new SerialLineBuffer();

        buffer.Append(new string('x', 5000));
        buffer.Append(new string('y', 5000));
        buffer.Append("\nok\n");

        Assert.True(buffer.TryTakeLine(out var line), "second resync did not complete");
        Assert.Equal("ok", line);
    }
}
