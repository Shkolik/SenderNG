
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

using System.Net.Sockets;
using System.Text;
using CNC.Core.Abstractions;

namespace CNC.Core.Services;

/// <summary>
/// <see cref="IGrblTransport"/> over a plain TCP socket, for grblHAL boards with
/// an Ethernet/WiFi interface (the controller listens on a TCP port and answers
/// the same grbl line protocol with '\n' line endings). The target is a
/// "host:port" string ("host" alone defaults to port 23 / telnet). Read is
/// line-buffered so status reports and command replies flow through
/// <see cref="RealGrblService"/> unchanged.
/// </summary>
public sealed class TcpGrblTransport : IGrblTransport, IPortPresenceCheck
{
    private const int DefaultPort = 23;

    private TcpClient? _client;
    private NetworkStream? _stream;
    private StreamReader? _reader;

    public bool IsOpen => _client is { Connected: true };

    /// <summary>Opening a TCP connection never resets the controller (no DTR line).</summary>
    public bool ResetsOnOpen => false;

    /// <summary>Parse a "host:port" endpoint ("host" defaults to port 23). IPv6 hosts must be bracketed ([::1]:23).</summary>
    public static bool TryParseEndpoint(string? target, out string host, out int port)
    {
        host = string.Empty;
        port = DefaultPort;

        var t = target?.Trim() ?? string.Empty;
        if (t.Length == 0 || t.EndsWith(':'))
            return false;

        if (t.StartsWith('['))
        {
            // Bracketed IPv6: [::1] or [::1]:23
            var close = t.IndexOf(']');
            if (close <= 1)
                return false;
            host = t[1..close];
            var rest = t[(close + 1)..];
            if (rest.Length == 0)
                return host.Length > 0;
            return rest[0] == ':'
                && int.TryParse(rest[1..], out port) && port is > 0 and <= 65535;
        }

        var idx = t.LastIndexOf(':');
        if (idx > 0)
        {
            // host:port (the trailing-colon case was rejected above).
            host = t[..idx];
            return int.TryParse(t[(idx + 1)..], out port) && port is > 0 and <= 65535;
        }

        host = t;
        return host.Length > 0;
    }

    public void Open(string target, int baudRate, ConnectResetMode resetMode = ConnectResetMode.None)
    {
        // Reset mode is serial-only; a network console has no DTR/RTS line.
        _ = resetMode;
        Close();

        if (!TryParseEndpoint(target, out var host, out var port))
            throw new ArgumentException($"Invalid network endpoint: '{target}'. Expected host or host:port.", nameof(target));

        var client = new TcpClient();
        // Bound the blocking connect so a dead address can't freeze the UI for
        // the OS's default (~21s) timeout. IGrblTransport.Open is synchronous;
        // a fully async transport is future work.
        var connect = client.ConnectAsync(host, port);
        if (!connect.Wait(TimeSpan.FromSeconds(5)))
        {
            client.Dispose();
            throw new TimeoutException($"Connection to {target} timed out.");
        }
        connect.GetAwaiter().GetResult();

        _client = client;
        _stream = client.GetStream();
        _reader = new StreamReader(_stream, Encoding.UTF8);
    }

    public void Close()
    {
        _reader?.Dispose();
        _reader = null;
        _stream?.Dispose();
        _stream = null;
        _client?.Dispose();
        _client = null;
    }

    public Task WriteLineAsync(string line)
    {
        var stream = _stream;
        if (stream is null || _client is not { Connected: true })
            throw new InvalidOperationException("Not connected.");

        // grbl over TCP uses '\n' line endings (grblHAL Ethernet).
        var bytes = Encoding.UTF8.GetBytes(line + "\n");
        stream.Write(bytes, 0, bytes.Length);
        return Task.CompletedTask;
    }

    public void WriteByte(byte b)
    {
        var stream = _stream;
        if (stream is null || _client is not { Connected: true })
            throw new InvalidOperationException("Not connected.");
        stream.WriteByte(b);
    }

    public async Task<string?> ReadLineAsync(CancellationToken ct)
    {
        var reader = _reader;
        if (reader is null || _client is not { Connected: true })
            throw new OperationCanceledException(ct);

        try
        {
            var line = await reader.ReadLineAsync(ct);
            if (line is null)
            {
                // Remote closed the connection: signal the reader loop to end.
                throw new OperationCanceledException(ct);
            }
            return line;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return null;
        }
        catch (IOException)
        {
            // Socket reset / remote close.
            throw new OperationCanceledException(ct);
        }
        catch (ObjectDisposedException)
        {
            throw new OperationCanceledException(ct);
        }
    }

    /// <summary>
    /// Socket-level liveness. A peer that closed (or a dropped link) surfaces as
    /// readable-with-no-data or a poll error, neither of which
    /// <see cref="TcpClient.Connected"/> alone reports.
    /// </summary>
    public bool IsPortPresent()
    {
        try
        {
            if (_client is not { Connected: true } || _stream is null)
                return false;
            var socket = _client.Client;
            if (socket is null || !socket.Connected)
                return false;
            // SelectRead with a zero timeout: true when data is buffered OR the
            // socket is closed/failed. A closed peer reads as available == 0.
            return !(socket.Poll(0, SelectMode.SelectRead) && socket.Available == 0);
        }
        catch
        {
            return false; // disposed/failed socket → gone
        }
    }

    public void Dispose() => Close();
}
