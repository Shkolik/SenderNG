using System.Runtime.InteropServices;
using CNC.Core.Abstractions;

namespace CNC.Core.Services;

public class SerialComPortService : IComPortService
{
    private readonly string? _linuxDevDir;

    public SerialComPortService(string? linuxDevDir = null) => _linuxDevDir = linuxDevDir;

    public string[] GetPorts()
    {
        if (_linuxDevDir is null && RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            try
            {
                return [.. System.IO.Ports.SerialPort.GetPortNames().Order()];
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

        return [.. ports.Order()];
    }
}
