
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

using System.Net;
using System.Net.Sockets;
using CNC.Core.Services;

namespace SenderNG.Core.Tests;

public class TcpGrblTransportTests
{
    [Theory]
    [InlineData("192.168.1.100:23", "192.168.1.100", 23)]
    [InlineData("10.0.0.5:8080", "10.0.0.5", 8080)]
    [InlineData("cnc.local", "cnc.local", 23)]
    [InlineData("cnc.local:5000", "cnc.local", 5000)]
    [InlineData("[::1]:23", "::1", 23)]
    public void TryParseEndpoint_parses_host_and_port(string target, string host, int port)
    {
        Assert.True(TcpGrblTransport.TryParseEndpoint(target, out var h, out var p));
        Assert.Equal(host, h);
        Assert.Equal(port, p);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("host:")]
    [InlineData("host:abc")]
    [InlineData("host:0")]
    [InlineData("host:70000")]
    public void TryParseEndpoint_rejects_invalid_targets(string target)
    {
        Assert.False(TcpGrblTransport.TryParseEndpoint(target, out _, out _));
    }

    [Fact]
    public async Task Lines_roundtrip_over_a_real_socket()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            using var transport = new TcpGrblTransport();
            transport.Open($"127.0.0.1:{port}", 0);

            var server = await listener.AcceptTcpClientAsync();
            await using var serverStream = server.GetStream();

            var writeTask = transport.WriteLineAsync("<Idle|WPos:1,2,3>");
            var buffer = new byte[256];
            var read = await serverStream.ReadAsync(buffer);
            Assert.Equal("<Idle|WPos:1,2,3>\n", System.Text.Encoding.UTF8.GetString(buffer, 0, read));

            // Reply with a line the transport surfaces to the reader.
            await serverStream.WriteAsync("ok\n"u8.ToArray());
            var reply = await transport.ReadLineAsync(CancellationToken.None);
            Assert.Equal("ok", reply);

            await writeTask;
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public void Open_rejects_bad_endpoint()
    {
        using var transport = new TcpGrblTransport();
        Assert.Throws<ArgumentException>(() => transport.Open("bad:endpoint:x", 0));
    }
}
