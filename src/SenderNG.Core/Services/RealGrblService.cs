using System.IO.Ports;
using System.Reactive.Subjects;
using CNC.Core.Abstractions;

namespace CNC.Core.Services;

public class RealGrblService : IGrblService
{
    private readonly BehaviorSubject<MachineState> _subject;
    private SerialPort? _port;

    public bool IsConnected { get; private set; }

    public IObservable<MachineState> State => _subject;

    public RealGrblService()
    {
        _subject = new BehaviorSubject<MachineState>(new MachineState());
    }

    public Task ConnectAsync(string port)
    {
        if (IsConnected)
            return Task.CompletedTask;

        _port = new SerialPort(port, 115200, Parity.None, 8, StopBits.One);
        _port.Open();
        IsConnected = true;
        return Task.CompletedTask;
    }

    public void Disconnect()
    {
        if (_port is { IsOpen: true })
            _port.Close();

        _port?.Dispose();
        _port = null;
        IsConnected = false;
    }

    public Task SendAsync(string line)
    {
        if (!IsConnected || _port is not { IsOpen: true })
            throw new InvalidOperationException("Not connected.");

        _port.WriteLine(line);
        var response = _port.ReadLine();
        ParseStatus(response);
        return Task.CompletedTask;
    }

    private void ParseStatus(string response)
    {
        if (string.IsNullOrEmpty(response) || !response.StartsWith('<') || !response.EndsWith('>'))
            return;

        var fields = response[1..^1].Split(',');
        var state = _subject.Value;
        state.Status = fields[0];

        for (var i = 1; i < fields.Length && i <= state.Positions.Length; i++)
        {
            if (double.TryParse(fields[i], System.Globalization.CultureInfo.InvariantCulture, out var value))
                state.Positions[i - 1] = value;
        }

        _subject.OnNext(state);
    }
}

// Telnet and WebSocket transports are stubbed out until needed.
