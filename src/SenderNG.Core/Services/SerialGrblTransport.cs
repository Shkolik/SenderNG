using System.IO.Ports;
using CNC.Core.Abstractions;

namespace CNC.Core.Services;

/// <summary>
/// Production <see cref="IGrblTransport"/> over <c>System.IO.Ports</c>. Read is
/// done asynchronously on a dedicated background task so the caller never blocks
/// the UI thread on a stuck adapter.
/// </summary>
public sealed class SerialGrblTransport : IGrblTransport
{
    private const int DefaultBaudRate = 115200;
    private SerialPort? _port;

    public bool IsOpen => _port is { IsOpen: true };

    public void Open(string target, int baudRate)
    {
        if (_port is not null)
            _port.Dispose();

        _port = new SerialPort(target, baudRate > 0 ? baudRate : DefaultBaudRate, Parity.None, 8, StopBits.One)
        {
            // Long enough to let a status report / reply arrive without spinning.
            ReadTimeout = 1000,
            WriteTimeout = 2000,
        };
        _port.Open();
    }

    public void Close()
    {
        if (_port is { IsOpen: true })
            _port.Close();
        _port = null;
    }

    public Task WriteLineAsync(string line)
    {
        var port = _port;
        if (port is not { IsOpen: true })
            throw new InvalidOperationException("Not connected.");

        // SerialPort is not thread-safe against our own background reader, but
        // grbl treats writes as atomic line frames and the port buffers bytes, so
        // WriteLine is synchronous enough here. SerialPort has no async write API.
        port.WriteLine(line);
        return Task.CompletedTask;
    }

    public void WriteByte(byte b)
    {
        var port = _port;
        if (port is not { IsOpen: true })
            throw new InvalidOperationException("Not connected.");
        port.Write(new[] { b }, 0, 1);
    }

    public async Task<string?> ReadLineAsync(CancellationToken ct)
    {
        var port = _port;
        if (port is not { IsOpen: true })
            throw new OperationCanceledException(ct);

        try
        {
            var line = await Task.Run(() => port.ReadLine(), ct);
            return line?.TrimEnd('\r');
        }
        catch (TimeoutException)
        {
            // Nothing arrived in ReadTimeout; signal the reader to loop on.
            return null;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return null;
        }
    }

    public void Dispose() => Close();
}