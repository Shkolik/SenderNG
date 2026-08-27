namespace CNC.Core.Abstractions;

/// <summary>
/// A serial port as shown in the connect dialog: <see cref="Name"/> is the plain
/// COM port ("COM7") used for opening the port, <see cref="FullName"/> adds the
/// device description Windows exposes ("COM7 - USB Serial Device") like ioSender.
/// On platforms without a device-description source, <c>FullName</c> equals
/// <c>Name</c>.
/// <para>
/// <see cref="Preferred"/> marks ports worth preselecting on first run — real
/// USB/PCI serial devices as opposed to phantom/Bluetooth virtual ports that
/// Windows keeps around. The connect dialog falls back to a preferred port when
/// nothing is persisted; ports without a registry match default to not preferred
/// so a stale entry is never picked over a real device.
/// </para>
/// </summary>
public sealed record SerialPortEntry(string Name, string FullName, bool Preferred = false)
{
    /// <summary>Entry for platforms/ports without a device description.</summary>
    public static SerialPortEntry Plain(string name, bool preferred = false) => new(name, name, preferred);
}
