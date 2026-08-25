using CNC.Core.Services;

namespace CNC.Core.Abstractions;

public interface IGrblService
{
    bool IsConnected { get; }
    Task ConnectAsync(string port);
    void Disconnect();
    Task SendAsync(string line);

    /// <summary>Sends a raw realtime byte (e.g. grblHAL override toggles 0xA2/0xA3).</summary>
    void SendRealtime(byte code);

    IObservable<MachineState> State { get; }
}

/// <summary>grblHAL realtime command bytes.</summary>
public static class GrblRealtime
{
    public const byte CoolantFloodToggle = 0xA2;
    public const byte CoolantMistToggle = 0xA3;
    public const byte CycleStart = 0x7E; // '~'
    public const byte FeedHold = 0x21;   // '!'
    public const byte Reset = 0x18;
}
