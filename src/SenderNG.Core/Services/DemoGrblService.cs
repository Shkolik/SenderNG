using System.Reactive.Subjects;
using CNC.Core.Abstractions;

namespace CNC.Core.Services;

public class DemoGrblService : IGrblService
{
    private readonly BehaviorSubject<MachineState> _subject;
    private readonly object _lock = new();
    private int _distanceMode = 90; // modal G90/G91

    public bool IsConnected { get; private set; }

    public IObservable<MachineState> State => _subject;

    public DemoGrblService(AppSettings settings)
    {
        _subject = new BehaviorSubject<MachineState>(new MachineState(settings.DemoAxes));
    }

    // Fresh state resets positions — acceptable when reconfiguring the machine.
    public void SetNumAxes(int numAxes)
    {
        var clamped = Math.Clamp(numAxes, 1, CNC.Core.GrblInfo.AxisLetters.Length);

        lock (_lock)
        {
            _subject.OnNext(new MachineState(clamped));
        }
    }

    public Task ConnectAsync(string port)
    {
        IsConnected = true;
        return Task.CompletedTask;
    }

    public void Disconnect()
    {
        IsConnected = false;
    }

    public Task SendAsync(string line)
    {
        if (!IsConnected)
            throw new InvalidOperationException("Not connected.");

        var trimmed = line?.Trim() ?? string.Empty;

        lock (_lock)
        {
            var state = _subject.Value;

            if (trimmed == "?")
            {
                state.Status = "Idle";
                _subject.OnNext(state);
                return Task.CompletedTask;
            }

            ApplyMotion(trimmed, state);
            state.Status = "Idle";
            _subject.OnNext(state);
        }

        return Task.CompletedTask;
    }

    // Hand-parses motion lines: supports $J= jog commands, modal G90/G91
    // distance modes, G92 coordinate setting and the X/Y/Z/A/B axis set,
    // clamped to NumAxes.
    private void ApplyMotion(string line, MachineState state)
    {
        var rest = line;
        if (rest.StartsWith("$J=", StringComparison.OrdinalIgnoreCase))
            rest = rest[3..];

        var incremental = _distanceMode == 91;
        var isG92 = false;

        foreach (var part in rest.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (part.Length < 2)
                continue;

            var letter = char.ToUpperInvariant(part[0]);

            if (letter == 'G')
            {
                switch (part[1..])
                {
                    case "90":
                        _distanceMode = 90;
                        incremental = false;
                        break;
                    case "91":
                        _distanceMode = 91;
                        incremental = true;
                        break;
                    case "92":
                        isG92 = true;
                        break;
                    // G0/G1/other G-words: motion mode only, no position effect here.
                }
                continue;
            }

            var index = letter switch
            {
                'X' => 0,
                'Y' => 1,
                'Z' => 2,
                'A' => 3,
                'B' => 4,
                _ => -1
            };

            if (index < 0 || index >= state.Positions.Length || index >= state.NumAxes)
                continue;

            if (!double.TryParse(part[1..], System.Globalization.CultureInfo.InvariantCulture, out var value))
                continue;

            state.Positions[index] = isG92 ? value : incremental ? state.Positions[index] + value : value;
        }
    }
}
