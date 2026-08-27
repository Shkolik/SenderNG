namespace CNC.Core.Abstractions;

public interface IComPortService
{
    /// <summary>Enumerated COM port names, deduplicated and sorted (e.g. "COM7").</summary>
    string[] GetPorts();

    /// <summary>
    /// Enumerated ports with the Windows device description attached when known
    /// (ioSender-style "COM7 - USB Serial Device"). Falls back to plain names on
    /// platforms without a description source. Entries are deduplicated by name.
    /// </summary>
    IReadOnlyList<SerialPortEntry> GetPortEntries();
}
