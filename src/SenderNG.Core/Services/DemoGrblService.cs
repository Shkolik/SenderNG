using System.Reactive.Subjects;
using CNC.Core.Abstractions;

namespace CNC.Core.Services;

public class DemoGrblService : IGrblService
{
    private readonly BehaviorSubject<MachineState> _subject;
    private readonly object _lock = new();

    public bool IsConnected { get; private set; }

    public IObservable<MachineState> State => _subject;

    public DemoGrblService(AppSettings settings)
    {
        _subject = new BehaviorSubject<MachineState>(new MachineState(settings.DemoAxes));
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

    private static void ApplyMotion(string line, MachineState state)
    {
        if (!(line.StartsWith('G') && (line.Length > 1 && (line[1] == '0' || line[1] == '1'))))
            return;

        foreach (var part in line[2..].Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (part.Length < 2)
                continue;

            var axis = char.ToUpperInvariant(part[0]);
            if (!double.TryParse(part[1..], System.Globalization.CultureInfo.InvariantCulture, out var value))
                continue;

            var index = axis switch
            {
                'X' => 0,
                'Y' => 1,
                'Z' => 2,
                _ => -1
            };

            if (index >= 0 && index < state.Positions.Length)
                state.Positions[index] = value;
        }
    }
}
