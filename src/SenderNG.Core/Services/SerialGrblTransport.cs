using System.IO.Ports;
using System.Text;
using System.Threading;
using CNC.Core.Abstractions;

namespace CNC.Core.Services;

/// <summary>
/// Production <see cref="IGrblTransport"/> over <c>System.IO.Ports</c>. The read
/// side polls <c>BytesToRead</c>/<c>ReadExisting</c> instead of the blocking
/// <c>ReadLine</c>, so a quiet serial line never raises a
/// <c>TimeoutException</c> from System.IO.Ports (the old 1s <c>ReadTimeout</c>
/// threw one on every poll gap — a first-chance exception the debugger reports
/// as a flood even though it was caught). Lines are accumulated across polls
/// until a <c>'\n'</c> terminator; each poll cycle sleeps 10 ms.
/// </summary>
public sealed class SerialGrblTransport : IGrblTransport, IPortPresenceCheck
{
    private const int MaxLineLength = 4096;
    private const int PollDelayMs = 10;
    private const int DefaultBaudRate = 115200;

    private SerialPort? _port;
    private readonly StringBuilder _line = new();

    public bool IsOpen => _port is { IsOpen: true };

    /// <summary>
    /// Opening a USB-serial port with DTR asserted resets Arduino-style boards
    /// (CH340/FTDI adapters with the auto-reset circuit), which reboot through
    /// their AVR bootloader. The service must wait for the boot banner before
    /// writing — see <see cref="RealGrblService"/>.
    /// </summary>
    public bool ResetsOnOpen => true;

    public void Open(string target, int baudRate, ConnectResetMode resetMode = ConnectResetMode.None)
    {
        if (_port is not null)
            _port.Dispose();

        _port = new SerialPort(target, baudRate > 0 ? baudRate : DefaultBaudRate, Parity.None, 8, StopBits.One)
        {
            // ReadExisting never throws a timeout exception (it returns whatever
            // is buffered), so no ReadTimeout is needed on the read path. Writes
            // keep a bounded timeout; grbl drains its RX buffer continuously.
            WriteTimeout = 2000,
            // Assert DTR at open like ioSender does (SerialStream.cs). Two reasons:
            //  1. Arduino-style auto-reset circuits (mega5x / Nano) reset the board
            //     on the DTR edge at open — a clean boot; the $I retry absorbs it.
            //  2. CDC boards whose output is gated on line state (RP2040 native USB)
            //     deliver NOTHING for the '?' poll while DTR is de-asserted — verified
            //     on hardware: status reports start flowing only once DTR is high.
            DtrEnable = true,
        };
        _port.Open();

        // The "On connect" reset action is an explicit pulse after open
        // (SerialStream.cs connect code): Toggle DTR low ~5 ms then high again;
        // Toggle RTS asserts and stays asserted (ioSender semantics).
        ApplyResetMode(resetMode);

        _line.Clear();
    }

    /// <summary>ioSender's On-connect DTR/RTS pulse, applied once the port is open.</summary>
    private void ApplyResetMode(ConnectResetMode resetMode)
    {
        var port = _port;
        if (port is null || !port.IsOpen || resetMode == ConnectResetMode.None)
            return;
        try
        {
            switch (resetMode)
            {
                case ConnectResetMode.ToggleDtr:
                    port.DtrEnable = true; // ensure on before the low pulse
                    port.DtrEnable = false;
                    Thread.Sleep(5);
                    port.DtrEnable = true; // back high -> boot never held in reset
                    break;
                case ConnectResetMode.ToggleRts:
                    // ESP32-style reset; ioSender asserts and leaves it asserted.
                    port.RtsEnable = true;
                    Thread.Sleep(5);
                    break;
            }
        }
        catch
        {
            // Reset is best-effort; a controller that ignores it still connects.
        }
    }

    public void Close()
    {
        if (_port is { IsOpen: true })
            _port.Close();
        _port = null;
        _line.Clear();
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
            while (!ct.IsCancellationRequested)
            {
                if (port.BytesToRead > 0 && _line.Length < MaxLineLength)
                    _line.Append(port.ReadExisting());

                // Drain complete lines from the buffer even when no new bytes
                // arrived: a burst reply (e.g. $I) lands in one ReadExisting
                // chunk with several '\n'-terminated lines.
                if (TryTakeLine(out var line))
                    return line;

                await Task.Delay(PollDelayMs, ct).ConfigureAwait(false);
            }
            return null;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return null;
        }
        catch (IOException)
        {
            // Port closed / adapter hiccup — signal the reader loop to end.
            throw new OperationCanceledException(ct);
        }
        catch (ObjectDisposedException)
        {
            throw new OperationCanceledException(ct);
        }
        catch (InvalidOperationException)
        {
            throw new OperationCanceledException(ct);
        }
    }

    /// <summary>
    /// True while the COM port is still enumerated. Unplugging a USB-serial
    /// adapter removes its COM port on Windows, so this is a reliable disconnect
    /// signal even when reads/writes keep returning without throwing.
    /// </summary>
    public bool IsPortPresent()
    {
        var port = _port;
        if (port is null)
            return false;
        try
        {
            return SerialPort.GetPortNames()
                .Contains(port.PortName, StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return false; // enumeration hiccup → treat as gone (safe default)
        }
    }

    /// <summary>Extract the first complete '\n'-terminated line, keeping any remainder buffered.</summary>
    private bool TryTakeLine(out string line)
    {
        line = string.Empty;
        var s = _line.ToString();
        var idx = s.IndexOf('\n');
        if (idx < 0)
            return false;

        line = s[..idx].TrimEnd('\r');
        _line.Clear();
        if (idx + 1 < s.Length)
            _line.Append(s[(idx + 1)..]); // more data arrived with the terminator
        return true;
    }

    public void Dispose() => Close();
}
