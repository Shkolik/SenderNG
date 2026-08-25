using System.Reactive.Subjects;
using CNC.Core.Abstractions;

namespace CNC.Core.Services;

public class DemoGrblService : IGrblService, IGrblSettingsProvider
{
    private readonly BehaviorSubject<MachineState> _subject;
    private readonly object _lock = new();
    private int _distanceMode = 90; // modal G90/G91
    private readonly string? _settingsFilePath;
    private Dictionary<int, string> _settings;

    public bool IsConnected { get; private set; }

    public IObservable<MachineState> State => _subject;

    public DemoGrblService(AppSettings settings, string? settingsFilePath = null)
    {
        _subject = new BehaviorSubject<MachineState>(new MachineState(settings.Current.DemoAxes));
        _settingsFilePath = settingsFilePath ??
            Path.Combine(SettingsService.DefaultDir(), "grbl-settings-demo.txt");
        _settings = LoadSettings(_settingsFilePath);
    }

    // Demo settings persist across sessions: defaults overlaid by $N=value lines from disk.
    private static Dictionary<int, string> LoadSettings(string path)
    {
        var store = CreateDefaultSettings();
        try
        {
            if (!File.Exists(path))
                return store;
            foreach (var line in File.ReadAllLines(path))
            {
                if (TryParseSetting(line.Trim(), out var number, out var value))
                    store[number] = value;
            }
        }
        catch
        {
            // Unreadable file falls back to defaults.
        }
        return store;
    }

    private void PersistSettings()
    {
        if (_settingsFilePath is null)
            return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_settingsFilePath)!);
            File.WriteAllText(_settingsFilePath,
                string.Join(Environment.NewLine, _settings.OrderBy(kv => kv.Key).Select(kv => $"${kv.Key}={kv.Value}")));
        }
        catch
        {
            // Best-effort persistence; demo keeps working in memory.
        }
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

    public void SendRealtime(byte code)
    {
        if (!IsConnected)
            throw new InvalidOperationException("Not connected.");

        lock (_lock)
        {
            var state = _subject.Value;
            switch (code)
            {
                case GrblRealtime.CoolantFloodToggle:
                    state.Flood = !state.Flood;
                    break;
                case GrblRealtime.CoolantMistToggle:
                    state.Mist = !state.Mist;
                    break;
                default:
                    return;
            }
            _subject.OnNext(state);
        }
    }

    public Task<IReadOnlyList<string>> GetSettingLinesAsync()
    {
        lock (_lock)
        {
            return Task.FromResult<IReadOnlyList<string>>(
                _settings.OrderBy(kv => kv.Key).Select(kv => $"${kv.Key}={kv.Value}").ToList());
        }
    }

    private static bool TryParseSetting(string line, out int number, out string value)
    {
        number = 0;
        value = string.Empty;
        var eq = line.IndexOf('=');
        if (eq <= 1 || !int.TryParse(line[1..eq], out number))
            return false;
        value = line[(eq + 1)..].Trim();
        return value.Length > 0;
    }

    private static Dictionary<int, string> CreateDefaultSettings() => new()
    {
        [0] = "10",       // step pulse, us
        [1] = "25",       // step idle delay, ms
        [2] = "0",        // step port invert
        [3] = "0",        // direction port invert
        [4] = "0",        // step enable invert
        [5] = "0",        // limit pins invert
        [6] = "0",        // probe pin invert
        [10] = "1",       // status report mask
        [11] = "0.010",   // junction deviation, mm
        [12] = "0.002",   // arc tolerance, mm
        [13] = "0",       // report inches
        [20] = "0",       // soft limits
        [21] = "0",       // hard limits
        [22] = "0",       // homing cycle
        [23] = "0",       // homing dir invert
        [24] = "25.000",  // homing feed, mm/min
        [25] = "500.000", // homing seek, mm/min
        [26] = "250",     // homing debounce, ms
        [27] = "1.000",   // homing pull-off, mm
        [30] = "1000.000",// max spindle speed
        [31] = "0.000",   // min spindle speed
        [32] = "0",       // laser mode
        [100] = "250.000",// x steps/mm
        [101] = "250.000",// y steps/mm
        [102] = "250.000",// z steps/mm
        [110] = "500.000",// x max rate
        [111] = "500.000",// y max rate
        [112] = "500.000",// z max rate
        [120] = "10.000", // x acceleration
        [121] = "10.000", // y acceleration
        [122] = "10.000", // z acceleration
    };

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
            // $N=value setting assignment: update the demo settings store.
            var assign = trimmed.Length > 2 && trimmed[0] == '$' && !trimmed.StartsWith("$$") && !trimmed.StartsWith("$J");
            if (assign && TryParseSetting(trimmed, out var number, out var value))
            {
                _settings[number] = value;
                PersistSettings();
                return Task.CompletedTask;
            }

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
