using CNC.Core.Services;

namespace CNC.Core.Abstractions;

public interface IGrblService
{
    bool IsConnected { get; }
    Task ConnectAsync(string port);
    void Disconnect();
    Task SendAsync(string line);
    IObservable<MachineState> State { get; }
}
