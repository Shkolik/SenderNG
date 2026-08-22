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

        _port = new SerialPort(port, 115200, Parity.None, 8, StopBits.One)
        {
            // Prevent indefinite blocking on unresponsive adapters.
            ReadTimeout = 2000,
            WriteTimeout = 2000,
        };
        _port.Open();
        IsConnected = true;

        // Axis count from controller caps ([OPT:...] response), not a fixed default
        var caps = RequestCaps();
        if (caps != null)
            _subject.Value.NumAxes = caps.NumAxes;

        return Task.CompletedTask;
    }

    // TODO: full grbl handshake ($I / [OPT:...]) in later task; for now probe via version command
    private CNC.Core.CapsResult? RequestCaps()
    {
        try
        {
            _port!.WriteLine("$I");
            var deadline = DateTime.UtcNow.AddSeconds(2);
            while (DateTime.UtcNow < deadline)
            {
                var line = _port.ReadLine();
                if (line.StartsWith("[OPT:"))
                    return CNC.Core.Grbl.ParseCaps(line) is { } caps ? caps : null;
            }
        }
        catch (TimeoutException) { }
        return null;
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

        try
        {
            _port.WriteLine(line);
            var response = _port.ReadLine();
            ParseStatus(response);
        }
        catch (TimeoutException)
        {
            // Controller did not answer in time; report nothing but don't hang or crash.
        }

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
