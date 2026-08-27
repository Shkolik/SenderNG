namespace CNC.Core.Abstractions;

/// <summary>
/// A serial port as shown in the connect dialog: <see cref="Name"/> is the plain
/// COM port ("COM7") used for opening the port, <see cref="FullName"/> adds the
/// device description Windows exposes ("COM7 - USB Serial Device") like ioSender.
/// On platforms without a device-description source, <c>FullName</c> equals
/// <c>Name</c>.
/// </summary>
public sealed record SerialPortEntry(string Name, string FullName)
{
    /// <summary>Entry for platforms/ports without a device description.</summary>
    public static SerialPortEntry Plain(string name) => new(name, name);
}
