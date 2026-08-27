namespace CNC.Core.Abstractions;

/// <summary>
/// Minimal serial-style transport abstraction used by <see cref="RealGrblService"/>.
/// Swapped at construction so unit tests can feed canned lines without hardware;
/// the production implementation wraps <c>System.IO.Ports.SerialPort</c>.
/// </summary>
public interface IGrblTransport : IDisposable
{
    bool IsOpen { get; }

    /// <summary>
    /// Open the underlying connection. Baud rate 0 keeps a default. When
    /// <paramref name="resetMode"/> is not <see cref="ConnectResetMode.None"/>, a
    /// DTR/RTS pulse is applied right after the port opens, exactly like ioSender
    /// (an 8-bit Arduino needs Toggle DTR to reset its bootloader on connect).
    /// </summary>
    void Open(string target, int baudRate, ConnectResetMode resetMode = ConnectResetMode.None);

    void Close();

    /// <summary>Write a line terminated by the protocol's line ending.</summary>
    Task WriteLineAsync(string line);

    /// <summary>Write a single raw byte (e.g. the realtime '?' status byte).</summary>
    void WriteByte(byte b);

    /// <summary>
    /// Read one complete line without its terminator, or <c>null</c> on timeout /
    /// transient condition (caller continues). Throws <see cref="OperationCanceledException"/>
    /// when cancelled or closed.
    /// </summary>
    Task<string?> ReadLineAsync(CancellationToken ct);
}

/// <summary>
/// Optional capability on a transport: reports whether the underlying connection
/// is still physically present — e.g. the serial port is still enumerated, or the
/// TCP socket is still live. <see cref="RealGrblService"/> uses it to detect
/// silent disconnects (an unplugged USB-serial adapter, a remote close) that may
/// not surface as a read/write exception, and to flip the app to NOT CONNECTED.
/// Transports that don't implement it (e.g. in-memory test doubles) are treated
/// as always healthy.
/// </summary>
public interface IPortPresenceCheck
{
    bool IsPortPresent();
}

/// <summary>
/// Serial "On connect" reset action, mirroring ioSender's Port-dialog dropdown
/// (SerialStream.cs ConnectModes). An 8-bit Arduino bootloader typically needs its
/// DTR pulsed low when the port opens to reset grbl cleanly; ESP32-style boards use
/// RTS instead. <see cref="None"/> is the safe default.
/// </summary>
public enum ConnectResetMode
{
    None,
    ToggleDtr,
    ToggleRts,
}