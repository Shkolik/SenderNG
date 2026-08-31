
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
    /// True when opening the connection resets the controller (e.g. the DTR edge
    /// on a USB-serial port reboots an Arduino-style board through its bootloader).
    /// <see cref="RealGrblService"/> then waits for the boot banner before writing
    /// anything — bytes sent during the bootloader window can stall the board.
    /// In-memory test doubles report false so the wait is skipped in unit tests.
    /// </summary>
    bool ResetsOnOpen { get; }

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