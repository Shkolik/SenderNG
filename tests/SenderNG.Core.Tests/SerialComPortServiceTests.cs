using CNC.Core.Services;

namespace SenderNG.Core.Tests;

public class SerialComPortServiceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "senderng-tests-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void GetPorts_returns_ttyUSB_and_ttyACM_sorted()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "ttyUSB0"), "");
        File.WriteAllText(Path.Combine(_dir, "ttyACM1"), "");

        var ports = new SerialComPortService(_dir).GetPorts();

        Assert.Equal(["ttyACM1", "ttyUSB0"], ports);
    }

    [Fact]
    public void GetPorts_returns_empty_for_empty_dir()
    {
        Directory.CreateDirectory(_dir);

        var ports = new SerialComPortService(_dir).GetPorts();

        Assert.Empty(ports);
    }

    [Fact]
    public void GetPorts_returns_empty_for_missing_dir()
    {
        var missing = Path.Combine(_dir, "does-not-exist");

        var ports = new SerialComPortService(missing).GetPorts();

        Assert.Empty(ports);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
        GC.SuppressFinalize(this);
    }
}
