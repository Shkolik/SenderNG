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
