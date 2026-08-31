
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
