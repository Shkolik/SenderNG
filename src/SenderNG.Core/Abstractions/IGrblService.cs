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

    /// <summary>Sends a raw realtime byte (e.g. grblHAL override toggles 0xA2/0xA3).</summary>
    void SendRealtime(byte code);

    IObservable<MachineState> State { get; }

    /// <summary>
    /// Controller capability snapshot from the connect-time handshake. Defaults to
    /// <see cref="ControllerCapabilities.Unknown"/> so lightweight test doubles and
    /// services that haven't handshaken still compile; <see cref="DemoGrblService"/>
    /// and <see cref="RealGrblService"/>/<see cref="GrblServiceProxy"/> override it.
    /// </summary>
    ControllerCapabilities Capabilities => ControllerCapabilities.Unknown;
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
