
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

namespace CNC.Core.Abstractions;

public interface IGrblService
{
    bool IsConnected { get; }

    /// <summary>
    /// Raised whenever the connection state changes (connect / disconnect / inner
    /// swap through the proxy). Default no-op keeps lightweight test doubles
    /// compiling while services that track connection state opt in.
    /// </summary>
    event EventHandler? ConnectionChanged { add { } remove { } }

    Task ConnectAsync(string port);
    void Disconnect();
    Task SendAsync(string line);

    /// <summary>
    /// Human-readable identity of the current connection (demo, serial port or
    /// network endpoint) — shown in status text, not used for metadata scoping.
    /// Defaults to "unknown" so lightweight test doubles compile unchanged.
    /// </summary>
    string ConnectionId => "unknown";

    /// <summary>
    /// Stable identity for scoping editor-local metadata (e.g. tool names): the
    /// <c>$I</c> firmware fingerprint (firmware name, version + build, raw
    /// <c>[OPT:]</c>/<c>[AXS:]</c>) — deliberately without the endpoint, so moving a
    /// machine to another port keeps its names and byte-identical boards
    /// (clones) share one set. Defaults to <see cref="ConnectionId"/> —
    /// sufficient for test doubles, and what the real service returns until its
    /// <c>$I</c> handshake completes.
    /// </summary>
    string ControllerKey => ConnectionId;

    /// <summary>Sends a raw realtime byte (e.g. grblHAL override toggles 0xA2/0xA3).</summary>
    void SendRealtime(byte code);

    IObservable<MachineState> State { get; }

    /// <summary>
    /// Raw lines received from the controller, before status parsing — the console
    /// renders these with ioSender's filter settings. Defaults to an empty stream
    /// so lightweight test doubles (and the demo service, which has no real port)
    /// compile and simply show nothing.
    /// </summary>
    IObservable<string> ReceivedLines => System.Reactive.Linq.Observable.Empty<string>();

    /// <summary>
    /// Controller capability snapshot from the connect-time handshake. Defaults to
    /// <see cref="ControllerCapabilities.Unknown"/> so lightweight test doubles and
    /// services that haven't handshaken still compile; <see cref="DemoGrblService"/>
    /// and <see cref="RealGrblService"/>/<see cref="GrblServiceProxy"/> override it.
    /// </summary>
    ControllerCapabilities Capabilities => ControllerCapabilities.Unknown;

    /// <summary>
    /// Latest controller work-coordinate and tool-table snapshot. Treat as
    /// read-only: services publish fresh clones through
    /// <see cref="WorkParametersChanges"/> rather than mutating this one.
    /// Defaults to a stub so lightweight test doubles compile.
    /// </summary>
    WorkParameters WorkParameters => WorkParameters.CreateDefault(Capabilities.NumAxes, Capabilities.NumTools);

    /// <summary>
    /// Emits a fresh work-parameters snapshot whenever the controller reports
    /// new values (connect <c>$#</c> dump, live <c>[G54:..]</c>/<c>[TLO:..]</c>
    /// reports, tool selection). Defaults to an empty stream like
    /// <see cref="ReceivedLines"/> so test doubles stay cheap.
    /// </summary>
    IObservable<WorkParameters> WorkParametersChanges => System.Reactive.Linq.Observable.Empty<WorkParameters>();

    /// <summary>
    /// One-shot re-read of the controller's work parameters (<c>$#</c>) so the
    /// offsets editor / work-parameters widget reflect the board after a write or
    /// error. ioSender re-requests work parameters on view activation and when the
    /// controller signals an error. Default no-op keeps test doubles cheap; the
    /// demo is already live and the proxy forwards to the inner service.
    /// </summary>
    Task RefreshWorkParametersAsync() => Task.CompletedTask;
}

/// <summary>grblHAL realtime command bytes. Coolant toggle bytes are identical
/// in classic grbl 1.1 (0xA0/0xA1) and grblHAL; mist requires firmware M7 support.
/// Override bytes are also identical in both firmwares.</summary>
public static class GrblRealtime
{
    public const byte CoolantFloodToggle = 0xA0;
    public const byte CoolantMistToggle = 0xA1;
    public const byte CycleStart = 0x7E; // '~'
    public const byte FeedHold = 0x21;   // '!'
    public const byte Reset = 0x18;

    // Jog cancel is defined identically in classic grbl 1.1 (config.h CMD_JOG_CANCEL,
    // includes the mega5x 1.2i fork) and grblHAL (grbl.h CMD_JOG_CANCEL): it stops the
    // jog with deceleration, flushes remaining jog blocks and returns to IDLE — unlike
    // a feed hold ('!'), which leaves the machine in HOLD. Use this for the jog Stop.
    public const byte JogCancel = 0x85;

    // Feed override
    public const byte FeedOvrReset = 0x90;      // 100%
    public const byte FeedOvrCoarsePlus = 0x91; // +10%
    public const byte FeedOvrCoarseMinus = 0x92; // -10%
    public const byte FeedOvrFinePlus = 0x93;   // +1%
    public const byte FeedOvrFineMinus = 0x94;  // -1%

    // Rapid override
    public const byte RapidOvrReset = 0x95;     // 100%
    public const byte RapidOvrMedium = 0x96;    // 50%
    public const byte RapidOvrLow = 0x97;       // 25%

    // Spindle/speed override
    public const byte SpindleOvrReset = 0x99;       // 100%
    public const byte SpindleOvrCoarsePlus = 0x9A;  // +10%
    public const byte SpindleOvrCoarseMinus = 0x9B; // -10%
    public const byte SpindleOvrFinePlus = 0x9C;    // +1%
    public const byte SpindleOvrFineMinus = 0x9D;   // -1%
    public const byte SpindleStopToggle = 0x9E;
}
