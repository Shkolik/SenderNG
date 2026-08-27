using System.Runtime.InteropServices;
using CNC.Core.Abstractions;

namespace CNC.Core.Services;

/// <summary>
/// COM port enumeration. On Windows the plain <c>System.IO.Ports</c> names are
/// decorated with the device description Windows keeps in the registry (the same
/// information ioSender reads via WMI), e.g. <c>COM7 - USB Serial Device</c>.
/// The registry route is used instead of <c>System.Management</c> so the core
/// library stays dependency-free and cross-platform (the architecture explicitly
/// bans WPF/System.Management); on non-Windows platforms plain names are returned.
/// Enumeration is deduplicated by name: after a USB adapter is unplugged and
/// plugged back in, Windows can briefly report the COM port twice.
/// </summary>
public class SerialComPortService : IComPortService
{
    private readonly string? _linuxDevDir;

    public SerialComPortService(string? linuxDevDir = null) => _linuxDevDir = linuxDevDir;

    public string[] GetPorts()
    {
        var names = EnumerateNames();
        return names.Distinct(StringComparer.OrdinalIgnoreCase).Order().ToArray();
    }

    public IReadOnlyList<SerialPortEntry> GetPortEntries()
    {
        var names = GetPorts();
        if (names.Length == 0)
            return [];

        var descriptions = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? WindowsPortDescriptions(names)
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        return [.. names.Select(n =>
            descriptions.TryGetValue(n, out var desc) && !string.IsNullOrWhiteSpace(desc)
                ? new SerialPortEntry(n, $"{n} - {desc}")
                : SerialPortEntry.Plain(n))];
    }

    private string[] EnumerateNames()
    {
        if (_linuxDevDir is null && RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            try
            {
                return System.IO.Ports.SerialPort.GetPortNames();
            }
            catch
            {
                return [];
            }
        }

        var devDir = _linuxDevDir ?? "/dev";
        if (!Directory.Exists(devDir))
            return [];

        var ports = new List<string>();
        foreach (var pattern in (string[])["ttyUSB*", "ttyACM*"])
        {
            try
            {
                ports.AddRange(Directory.GetFiles(devDir, pattern).Select(p => Path.GetFileName(p)!));
            }
            catch
            {
                // Directory may be unreadable; skip.
            }
        }

        return [.. ports];
    }

    /// <summary>
    /// Map each requested COM port to its device description by walking the PnP
    /// device tree (<c>HKLM\SYSTEM\CurrentControlSet\Enum</c>). Serial devices
    /// carry <c>Device Parameters\PortName</c> (e.g. "COM9") and the instance
    /// key's <c>FriendlyName</c> (e.g. "USB Serial Device (COM9)"). The
    /// <c>(COMx)</c> suffix is stripped, so the entry reads "COM9 - USB Serial
    /// Device". The walk stops as soon as every requested port is resolved.
    /// </summary>
    private static Dictionary<string, string> WindowsPortDescriptions(IReadOnlyCollection<string> ports)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (ports.Count == 0)
            return result;

        try
        {
            using var enumRoot = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                @"SYSTEM\CurrentControlSet\Enum");
            if (enumRoot is null)
                return result;

            foreach (var className in enumRoot.GetSubKeyNames())
            {
                if (result.Count == ports.Count)
                    break;

                using var classKey = enumRoot.OpenSubKey(className);
                if (classKey is null)
                    continue;

                foreach (var deviceId in classKey.GetSubKeyNames())
                {
                    if (result.Count == ports.Count)
                        break;

                    using var deviceKey = classKey.OpenSubKey(deviceId);
                    if (deviceKey is null)
                        continue;

                    foreach (var instanceId in deviceKey.GetSubKeyNames())
                    {
                        if (result.Count == ports.Count)
                            break;

                        using var instance = deviceKey.OpenSubKey(instanceId);
                        if (instance is null)
                            continue;

                        string? portName = null;
                        try
                        {
                            using var deviceParams = instance.OpenSubKey("Device Parameters");
                            portName = deviceParams?.GetValue("PortName") as string;
                        }
                        catch
                        {
                            // Some keys are protected; skip this device.
                        }

                        if (string.IsNullOrEmpty(portName) ||
                            !ports.Contains(portName, StringComparer.OrdinalIgnoreCase))
                            continue;

                        var friendly = instance.GetValue("FriendlyName") as string
                            ?? instance.GetValue("DeviceDesc") as string;
                        if (string.IsNullOrWhiteSpace(friendly))
                            continue;

                        // Strip the "(COMx)" suffix: "USB Serial Device (COM9)" -> "USB Serial Device".
                        var paren = friendly.IndexOf('(');
                        if (paren > 0)
                            friendly = friendly[..paren].Trim();

                        result.TryAdd(portName, friendly);
                    }
                }
            }
        }
        catch
        {
            // Enumeration must never break the dialog; plain names remain usable.
        }

        return result;
    }
}
