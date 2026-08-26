using System.IO.Ports;
using System.Reactive.Subjects;
using CNC.Core.Abstractions;

namespace CNC.Core.Services;

public class RealGrblService : IGrblService, IGrblSettingsProvider
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

    public void SendRealtime(byte code)
    {
        if (!IsConnected || _port is not { IsOpen: true })
            throw new InvalidOperationException("Not connected.");
        // Realtime bytes bypass the line protocol - no response is sent.
        _port.Write(new[] { code }, 0, 1);
    }

    // $$ report: collect "$N=value" lines until the trailing "ok".
    public Task<IReadOnlyList<string>> GetSettingLinesAsync()
    {
        if (!IsConnected || _port is not { IsOpen: true })
            throw new InvalidOperationException("Not connected.");

        var lines = new List<string>();
        _port.WriteLine("$$");
        var deadline = DateTime.UtcNow.AddSeconds(2);
        while (DateTime.UtcNow < deadline)
        {
            string line;
            try
            {
                line = _port.ReadLine();
            }
            catch (TimeoutException)
            {
                break;
            }

            line = line.Trim();
            if (line.Length == 0)
                continue;
            if (line == "ok")
                break;
            if (line.StartsWith("error", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(line);
            if (line.StartsWith('$'))
                lines.Add(line);
        }

        return Task.FromResult<IReadOnlyList<string>>(lines);
    }

    private void ParseStatus(string response)
    {
        if (string.IsNullOrEmpty(response))
            return;

        // Probe completion report: [PRB:x,y,z:1] (grblHAL sends it as its own
        // bracket line when a G38.x move finishes).
        if (response.StartsWith("[PRB:", StringComparison.Ordinal) && response.EndsWith(']'))
        {
            var body = response[5..^1];
            var colon = body.LastIndexOf(':');
            var ok = false;
            if (colon > 0 && bool.TryParse(body[(colon + 1)..], out var success))
                ok = success;

            var probeState = _subject.Value;
            var pos = (colon > 0 ? body[..colon] : body).Split(',');
            probeState.ProbePosition = new double[probeState.Positions.Length];
            for (var i = 0; i < pos.Length && i < probeState.ProbePosition.Length; i++)
            {
                if (double.TryParse(pos[i], System.Globalization.CultureInfo.InvariantCulture, out var value))
                    probeState.ProbePosition[i] = value;
            }
            probeState.ProbeSuccess = ok;
            _subject.OnNext(probeState);
            return;
        }

        if (!response.StartsWith('<') || !response.EndsWith('>'))
            return;

        // Report format: <Status|WPos:x,y,z|FS:feed,speed|Ov:feed,rapid,spindle|...>
        var fields = response[1..^1].Split('|');
        var state = _subject.Value;
        state.Status = fields[0];

        foreach (var field in fields.Skip(1))
        {
            if (field.StartsWith("WPos:", StringComparison.Ordinal) || field.StartsWith("MPos:", StringComparison.Ordinal))
            {
                var pos = field[5..].Split(',');
                for (var i = 0; i < pos.Length && i < state.Positions.Length; i++)
                {
                    if (double.TryParse(pos[i], System.Globalization.CultureInfo.InvariantCulture, out var value))
                        state.Positions[i] = value;
                }
            }
            else if (field.StartsWith("FS:", StringComparison.Ordinal))
            {
                var fs = field[3..].Split(',');
                if (fs.Length >= 1 && double.TryParse(fs[0], System.Globalization.CultureInfo.InvariantCulture, out var feed))
                    state.Feed = feed;
                if (fs.Length >= 2 && double.TryParse(fs[1], System.Globalization.CultureInfo.InvariantCulture, out var speed))
                    state.Speed = speed;
            }
            else if (field.StartsWith("Ov:", StringComparison.Ordinal))
            {
                var ov = field[3..].Split(',');
                if (ov.Length >= 1 && int.TryParse(ov[0], out var feedOvr))
                    state.FeedOvr = feedOvr;
                if (ov.Length >= 2 && int.TryParse(ov[1], out var rapidOvr))
                    state.RapidOvr = rapidOvr;
                if (ov.Length >= 3 && int.TryParse(ov[2], out var spindleOvr))
                    state.SpindleOvr = spindleOvr;
            }
            else if (field.StartsWith("Pn:", StringComparison.Ordinal))
            {
                // Probe pin state: 'P' asserted, 'O' probe disconnected.
                var flags = field[3..];
                if (flags.Contains('P'))
                    state.Signals |= CNC.Core.Signals.Probe;
                if (flags.Contains('O'))
                    state.Signals |= CNC.Core.Signals.ProbeDisconnected;
            }
        }

        _subject.OnNext(state);
    }
}

// Telnet and WebSocket transports are stubbed out until needed.
