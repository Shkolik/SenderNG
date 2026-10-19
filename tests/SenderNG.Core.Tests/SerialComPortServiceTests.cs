
/*

Copyright (c) 2026 Andrew Shkolik

All rights reserved.

Redistribution and use in source and binary forms, with or without modification,
are permitted provided that the following conditions are met:

· Redistributions of source code must retain the above copyright notice, this
list of conditions and the following disclaimer.

· Redistributions in binary form must reproduce the above copyright notice, this
list of conditions and the following disclaimer in the documentation and/or
other materials provided with the distribution.

· Neither the name of the copyright holder nor the names of its contributors may
be used to endorse or promote products derived from this software without
specific prior written permission.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND
ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT OWNER OR CONTRIBUTORS BE LIABLE FOR
ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
(INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON
ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
(INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.

*/

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

        // Full device paths, not bare names: System.IO.Ports hands the port name
        // verbatim to open(2) on Unix, so a bare "ttyACM1" would be resolved
        // against the process working directory and fail with ENOENT.
        Assert.Equal(
            [Path.Combine(_dir, "ttyACM1"), Path.Combine(_dir, "ttyUSB0")],
            ports);
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

    [Fact]
    public void GetPortEntries_falls_back_to_plain_names_off_windows()
    {
        // On non-Windows (simulated by the injected dev dir) there is no device-
        // description source, so entries must still enumerate with FullName==Name.
        // Name is the full device path, which is what the transport opens.
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "ttyUSB0"), "");
        File.WriteAllText(Path.Combine(_dir, "ttyACM0"), "");

        var entries = new SerialComPortService(_dir).GetPortEntries();

        Assert.Equal(
            [Path.Combine(_dir, "ttyACM0"), Path.Combine(_dir, "ttyUSB0")],
            entries.Select(e => e.Name));
        Assert.All(entries, e => Assert.Equal(e.Name, e.FullName));
        // Every enumerated ttyUSB/ttyACM device is real — all preferred.
        Assert.All(entries, e => Assert.True(e.Preferred));
    }

    [Theory]
    [InlineData("BTHENUM")]
    [InlineData("BTH")]
    public void Bluetooth_enumerators_are_phantom(string className)
        => Assert.True(SerialComPortService.IsPhantomEnumerator(className));

    [Theory]
    [InlineData("USB")]
    [InlineData("FTDIBUS")]
    [InlineData("PCI")]
    [InlineData("HID")]
    [InlineData("")]
    public void Real_enumerators_are_not_phantom(string className)
        => Assert.False(SerialComPortService.IsPhantomEnumerator(className));

    [Fact]
    public void EnumerateMacPorts_returns_only_cu_devices_as_full_paths()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "cu.usbserial-1420"), "");
        File.WriteAllText(Path.Combine(_dir, "cu.Bluetooth-Incoming-Port"), "");
        File.WriteAllText(Path.Combine(_dir, "tty.usbserial-1420"), "");
        File.WriteAllText(Path.Combine(_dir, "ttyACM0"), "");

        var ports = SerialComPortService.EnumerateMacPorts(_dir);

        // Full device paths and cu.* only: tty.* is the incoming side that
        // blocks on DCD, and System.IO.Ports opens the name verbatim.
        Assert.Equal(
            [Path.Combine(_dir, "cu.Bluetooth-Incoming-Port"), Path.Combine(_dir, "cu.usbserial-1420")],
            ports);
    }

    [Fact]
    public void EnumerateMacPorts_returns_empty_for_missing_dir()
        => Assert.Empty(SerialComPortService.EnumerateMacPorts(Path.Combine(_dir, "does-not-exist")));

    [Theory]
    [InlineData("cu.usbserial-1420")]
    [InlineData("cu.wchusbserial1420")]
    [InlineData("cu.usbmodem2101")]
    [InlineData("cu.SLAB_USBtoUART")]
    public void Mac_real_ports_are_preferred(string name)
        => Assert.True(SerialComPortService.IsPreferredOnMac("/dev/" + name));

    [Theory]
    [InlineData("cu.Bluetooth-Incoming-Port")]
    [InlineData("cu.debug-console")]
    public void Mac_virtual_ports_are_not_preferred(string name)
        => Assert.False(SerialComPortService.IsPreferredOnMac("/dev/" + name));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
        GC.SuppressFinalize(this);
    }
}
