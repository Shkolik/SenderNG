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

public class SerialGrblTransportTests
{
    [Fact]
    public void IsOpen_is_false_when_never_opened()
    {
        var transport = new SerialGrblTransport();

        Assert.False(transport.IsOpen);
    }

    [Fact]
    public void ResetsOnOpen_is_true()
    {
        // RealGrblService waits for the grbl boot banner before writing when this
        // is true: the DTR edge at open resets an Arduino-style board through its
        // bootloader, and bytes sent during that window stall it. The in-memory
        // FakeGrblTransport reports false so the wait is skipped; this pins the
        // production value the service actually branches on.
        var transport = new SerialGrblTransport();

        Assert.True(transport.ResetsOnOpen);
    }

    [Fact]
    public async Task WriteLineAsync_throws_InvalidOperationException_when_not_connected()
    {
        var transport = new SerialGrblTransport();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => transport.WriteLineAsync("G0 X0"));
    }

    [Fact]
    public void WriteByte_throws_InvalidOperationException_when_not_connected()
    {
        var transport = new SerialGrblTransport();

        Assert.Throws<InvalidOperationException>(() => transport.WriteByte((byte)'?'));
    }

    [Fact]
    public async Task ReadLineAsync_throws_OperationCanceledException_when_not_connected()
    {
        // There is no separate disconnect exception: the transport reports "the
        // reader loop must end" as OperationCanceledException, so RealGrblService
        // needs no extra branch for a port that is not open.
        var transport = new SerialGrblTransport();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => transport.ReadLineAsync(CancellationToken.None));
    }

    [Fact]
    public void IsPortPresent_is_false_when_never_opened()
    {
        var transport = new SerialGrblTransport();

        Assert.False(transport.IsPortPresent());
    }

    [Fact]
    public void Close_is_idempotent_when_never_opened()
    {
        var transport = new SerialGrblTransport();

        transport.Close();
        transport.Close();
    }

    [Fact]
    public void Dispose_is_idempotent_and_repeatable()
    {
        var transport = new SerialGrblTransport();

        transport.Dispose();
        transport.Dispose();
    }

    [Fact]
    public async Task WriteLineAsync_after_Close_throws_InvalidOperationException()
    {
        var transport = new SerialGrblTransport();
        transport.Close();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => transport.WriteLineAsync("G0 X0"));
    }

    [Fact]
    public void Open_with_an_impossible_target_fails_and_leaves_transport_closed()
    {
        // Deliberately agnostic about which exception: it must pass whether .NET
        // rejects the name at construction, fails CreateFile, or reports the API
        // as unsupported. The contract is that Open() either fully connects or
        // throws - it never leaves a half-open transport that still claims
        // IsOpen, which would make RealGrblService write into a dead port.
        using var transport = new SerialGrblTransport();

        Assert.ThrowsAny<Exception>(() => transport.Open("com4096_ngc", 115200));
        Assert.False(transport.IsOpen, "a failed Open must not report a connected transport");
    }

    [Fact]
    public void IsPortPresent_is_false_after_failed_open()
    {
        using var transport = new SerialGrblTransport();

        Assert.ThrowsAny<Exception>(() => transport.Open("com4096_ngc", 115200));

        Assert.False(transport.IsPortPresent());
    }
}
