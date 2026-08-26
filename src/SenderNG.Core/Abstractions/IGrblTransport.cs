namespace CNC.Core.Abstractions;

/// <summary>
/// Minimal serial-style transport abstraction used by <see cref="RealGrblService"/>.
/// Swapped at construction so unit tests can feed canned lines without hardware;
/// the production implementation wraps <c>System.IO.Ports.SerialPort</c>.
/// </summary>
public interface IGrblTransport : IDisposable
{
    bool IsOpen { get; }

    /// <summary>Open the underlying connection. Baud rate 0 keeps a default.</summary>
    void Open(string target, int baudRate);

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