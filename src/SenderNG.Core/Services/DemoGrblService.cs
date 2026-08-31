
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

using System.Reactive.Subjects;
using CNC.Core.Abstractions;
using SenderNG.Core.Localization;
using SenderNG.Core.Settings;

namespace CNC.Core.Services;

public class DemoGrblService : IGrblService, IGrblSettingsProvider, IDisposable
{
    private readonly BehaviorSubject<MachineState> _subject;
    private readonly object _lock = new();
    private readonly int _pollIntervalMs;
    private Timer? _pollTimer;
    private int _distanceMode = 90; // modal G90/G91
    private double _lastSpeed;
    // Per-axis work offset: machine = work + offset. G92 shifts this so the
    // work coordinate takes the requested value while the machine stays put.
    private double[] _workOffset = [];
    private readonly string? _settingsFilePath;
    private Dictionary<int, string> _settings;

    public bool IsConnected { get; private set; }

    public event EventHandler? ConnectionChanged;

    public IObservable<MachineState> State => _subject;

    // Demo reports a full capability set so both coolant modes stay visible.
    public ControllerCapabilities Capabilities => ControllerCapabilities.Full;

    public WorkParameters WorkParameters { get; } = WorkParameters.CreateDefault(3, 4);

    private readonly BehaviorSubject<WorkParameters> _workParametersChanges =
        new(WorkParameters.CreateDefault(3, 4));

    /// <summary>Emits a fresh work-parameters snapshot whenever a write mutates it
    /// (G10, coordinate/tool selection), so widgets stay in sync without polling.</summary>
    public IObservable<WorkParameters> WorkParametersChanges => _workParametersChanges;

    private void PublishWorkParameters() =>
        _workParametersChanges.OnNext(WorkParameters.Clone());

    /// <summary>Demo work parameters are already live after every write; nothing to re-read.</summary>
    public Task RefreshWorkParametersAsync() => Task.CompletedTask;

    private readonly LocalizationService? _localization;

    public DemoGrblService(AppSettings settings, string? settingsFilePath = null, int pollIntervalMs = 200,
        LocalizationService? localization = null)
    {
        _localization = localization;
        // Seed the full optional-signal set (demo reports Full capabilities) so
        // the signals panel shows every LED, as before gating was introduced.
        _subject = new BehaviorSubject<MachineState>(new MachineState(settings.Current.DemoAxes)
        {
            OptionalSignals = ControllerCapabilities.Full.OptionalSignals,
        });
        var demoAxes = Math.Clamp(settings.Current.DemoAxes, 1, CNC.Core.GrblInfo.AxisLetters.Length);
        ResizeWorkParameters(demoAxes);
        _workOffset = new double[demoAxes];
        _pollIntervalMs = Math.Max(10, pollIntervalMs);
        _settingsFilePath = settingsFilePath ??
            Path.Combine(SettingsService.DefaultDir(), "grbl-settings-demo.txt");
        _settings = LoadSettings(_settingsFilePath);
        PublishWorkParameters();
    }

    private void ResizeWorkParameters(int axisCount)
    {
        for (var i = 0; i < WorkParameters.CoordinateSystems.Count; i++)
        {
            var source = WorkParameters.CoordinateSystems[i];
            var replacement = new WorkCoordinateSystem(source.Code, source.Id, axisCount);
            Array.Copy(source.Values, replacement.Values, Math.Min(source.Values.Length, axisCount));
            WorkParameters.CoordinateSystems[i] = replacement;
        }
        for (var i = 0; i < WorkParameters.Tools.Count; i++)
        {
            var source = WorkParameters.Tools[i];
            var replacement = new ToolTableEntry(source.Id, axisCount) { Radius = source.Radius };
            Array.Copy(source.Offsets, replacement.Offsets, Math.Min(source.Offsets.Length, axisCount));
            WorkParameters.Tools[i] = replacement;
        }
        WorkParameters.ToolLengthOffset = new double[axisCount];
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
            _workOffset = new double[clamped];
            _subject.OnNext(new MachineState(clamped)
            {
                OptionalSignals = ControllerCapabilities.Full.OptionalSignals,
            });
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
                case GrblRealtime.FeedOvrReset: state.FeedOvr = 100; break;
                case GrblRealtime.FeedOvrCoarsePlus: state.FeedOvr = Math.Min(200, state.FeedOvr + 10); break;
                case GrblRealtime.FeedOvrCoarseMinus: state.FeedOvr = Math.Max(10, state.FeedOvr - 10); break;
                case GrblRealtime.FeedOvrFinePlus: state.FeedOvr = Math.Min(200, state.FeedOvr + 1); break;
                case GrblRealtime.FeedOvrFineMinus: state.FeedOvr = Math.Max(10, state.FeedOvr - 1); break;
                case GrblRealtime.RapidOvrReset: state.RapidOvr = 100; break;
                case GrblRealtime.RapidOvrMedium: state.RapidOvr = 50; break;
                case GrblRealtime.RapidOvrLow: state.RapidOvr = 25; break;
                case GrblRealtime.SpindleOvrReset: state.SpindleOvr = 100; break;
                case GrblRealtime.SpindleOvrCoarsePlus: state.SpindleOvr = Math.Min(200, state.SpindleOvr + 10); break;
                case GrblRealtime.SpindleOvrCoarseMinus: state.SpindleOvr = Math.Max(10, state.SpindleOvr - 10); break;
                case GrblRealtime.SpindleOvrFinePlus: state.SpindleOvr = Math.Min(200, state.SpindleOvr + 1); break;
                case GrblRealtime.SpindleOvrFineMinus: state.SpindleOvr = Math.Max(10, state.SpindleOvr - 1); break;
                case GrblRealtime.SpindleStopToggle:
                    state.Speed = state.Speed > 0 ? 0 : _lastSpeed;
                    break;
                // Cycle controls: mirror the controller's state transitions so the
                // demo responds visibly to the cycle bar (grbl soft-reset semantics).
                case GrblRealtime.FeedHold:
                    state.Status = "Hold";
                    break;
                case GrblRealtime.CycleStart:
                    // ~ starts a buffered program (Idle) or resumes from Feed Hold (Hold).
                    if (state.Status is "Hold" or "Idle")
                        state.Status = "Run";
                    break;
                case GrblRealtime.Reset:
                    // Soft reset returns to Idle and loses position (until re-homed).
                    state.Status = "Idle";
                    state.IsHomed = false;
                    for (var i = 0; i < state.Positions.Length; i++)
                        state.Positions[i] = 0;
                    if (state.MachinePositions is not null)
                        for (var i = 0; i < state.MachinePositions.Length; i++)
                            state.MachinePositions[i] = 0;
                    _workOffset = new double[state.Positions.Length];
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

    // Demo emulates a grblHAL ENUMS board: serve $ES / $EG / $SED from the locale
    // catalog (injected via DI) so the settings UI shows the full tree + typed
    // editors in demo mode. When no catalog is injected (direct construction in
    // tests), demo serves no enums — matching a classic board.
    private IReadOnlyDictionary<int, GrblSettingDefinition> CatalogSettings =>
        _localization?.Settings ?? new Dictionary<int, GrblSettingDefinition>();

    private IReadOnlyDictionary<int, GrblSettingGroupDefinition> CatalogGroups =>
        _localization?.Groups ?? new Dictionary<int, GrblSettingGroupDefinition>();

    /// <summary>Serialize the catalog as [SETTING:...] lines (grblHAL $ES wire format).</summary>
    public Task<IReadOnlyList<string>> GetSettingDetailLinesAsync()
    {
        var catalog = CatalogSettings;
        var lines = catalog.Values
            .OrderBy(s => s.Id)
            .Select(s => $"[SETTING:{s.Id}|{s.GroupId}|{s.Name}|{s.Unit}|{(int)s.Type}|{s.Format}|" +
                         $"{Fmt(s.Min)}|{Fmt(s.Max)}|{(s.RebootRequired ? 1 : 0)}|{(s.AllowNull ? 1 : 0)}]")
            .ToList();
        return Task.FromResult<IReadOnlyList<string>>(lines);
    }

    /// <summary>Serialize the group tree as [SETTINGGROUP:...] lines ($EG wire format).</summary>
    public Task<IReadOnlyList<string>> GetSettingGroupLinesAsync()
    {
        var groups = CatalogGroups;
        var lines = groups.Values
            .OrderBy(g => g.Id)
            .Select(g => $"[SETTINGGROUP:{g.Id}|{g.ParentId}|{g.Name}]")
            .ToList();
        return Task.FromResult<IReadOnlyList<string>>(lines);
    }

    /// <summary>Serve the catalog description for one setting ($SED wire format, literal \n escapes).</summary>
    public Task<string?> GetSettingDescriptionAsync(int id)
    {
        var description = CatalogSettings.TryGetValue(id, out var s) ? s.Description : null;
        return Task.FromResult(description is null
            ? null
            : $"[SETTINGDESCR:{id}|{description.Replace("\n", "\\n")}]");
    }

    private static string Fmt(double? value) =>
        value is null ? string.Empty : value.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);

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
        ConnectionChanged?.Invoke(this, EventArgs.Empty);
        // Status polling: re-emit the current state on a timer, mirroring the
        // controller answering the host's periodic '?' with a status report.
        _pollTimer?.Dispose();
        _pollTimer = new Timer(_ => PollTick(), null, _pollIntervalMs, _pollIntervalMs);
        return Task.CompletedTask;
    }

    public void Disconnect()
    {
        IsConnected = false;
        ConnectionChanged?.Invoke(this, EventArgs.Empty);
        _pollTimer?.Dispose();
        _pollTimer = null;
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

            if (trimmed.StartsWith("G10", StringComparison.OrdinalIgnoreCase))
            {
                ApplyWorkParameterCommand(trimmed, state);
                _subject.OnNext(state);
                PublishWorkParameters();
                return Task.CompletedTask;
            }

            if (trimmed.StartsWith("G54", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("G55", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("G56", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("G57", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("G58", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("G59", StringComparison.OrdinalIgnoreCase))
            {
                var code = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0].ToUpperInvariant();
                if (WorkParameters.CoordinateSystems.Any(x => x.Code == code))
                    WorkParameters.ActiveCoordinateSystem = code;
                _subject.OnNext(state);
                PublishWorkParameters();
                return Task.CompletedTask;
            }

            if (trimmed.StartsWith("T", StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(trimmed[1..], out var tool))
            {
                WorkParameters.ActiveTool = tool;
                if (WorkParameters.Tools.All(x => x.Id != tool))
                    WorkParameters.Tools.Add(new ToolTableEntry(tool, state.NumAxes));
                _subject.OnNext(state);
                PublishWorkParameters();
                return Task.CompletedTask;
            }

            if (trimmed == "?")
            {
                // A status query just echoes the current state - it must not
                // clobber the live status (e.g. while emulating a running job).
                _subject.OnNext(state);
                return Task.CompletedTask;
            }

            if (trimmed == "$X")
            {
                // Unlock: clear an alarm so the machine can be homed again.
                if (state.Status.Contains("Alarm", StringComparison.OrdinalIgnoreCase))
                    state.Status = "Idle";
                _subject.OnNext(state);
                return Task.CompletedTask;
            }

            if (trimmed == "$H")
            {
                // Homing completes instantly in the demo (Home -> Idle). Machine
                // coordinates return to zero; work = machine - offset stays
                // shifted by any active G92 offset (as on a real controller).
                state.Status = "Idle";
                state.IsHomed = true;
                var machinePos = EnsureMachinePositions(state);
                for (var i = 0; i < machinePos.Length; i++)
                {
                    machinePos[i] = 0;
                    if (i < state.Positions.Length)
                        state.Positions[i] = -_workOffset[i];
                }
                _subject.OnNext(state);
                return Task.CompletedTask;
            }

            ApplyMotion(trimmed, state);
            state.Status = "Idle";

            // G38.x probing move: emulate the touch - record the position the
            // move landed on and assert the probe signal (as a real controller
            // would after a successful [PRB:...] report).
            if (trimmed.Contains("G38", StringComparison.OrdinalIgnoreCase))
            {
                state.ProbeSuccess = true;
                state.ProbePosition = (double[])state.Positions.Clone();
                state.Signals |= CNC.Core.Signals.Probe;
            }

            _subject.OnNext(state);        }

        return Task.CompletedTask;
    }

    // Hand-parses motion lines: supports $J= jog commands, modal G90/G91
    // distance modes, G92 coordinate setting and the X/Y/Z/A/B axis set,
    // clamped to NumAxes.
    private void ApplyWorkParameterCommand(string line, MachineState state)
    {
        var words = TokenizeWords(line).ToArray();
        var l1 = words.Any(x => x.Equals("L1", StringComparison.OrdinalIgnoreCase));
        var l2 = words.Any(x => x.Equals("L2", StringComparison.OrdinalIgnoreCase));
        var p = words.FirstOrDefault(x => x.StartsWith("P", StringComparison.OrdinalIgnoreCase));
        if (!int.TryParse(p?[1..], out var number)) return;
        if (l1)
        {
            var tool = WorkParameters.Tools.FirstOrDefault(x => x.Id == number) ?? new ToolTableEntry(number, state.NumAxes);
            if (!WorkParameters.Tools.Contains(tool)) WorkParameters.Tools.Add(tool);
            foreach (var word in words)
            {
                if (word.Length > 1 && "XYZABCUVW".Contains(char.ToUpperInvariant(word[0])) && double.TryParse(word[1..], System.Globalization.CultureInfo.InvariantCulture, out var value))
                    tool.Offsets["XYZABCUVW".IndexOf(char.ToUpperInvariant(word[0]))] = value;
                else if (word.Length > 1 && char.ToUpperInvariant(word[0]) == 'R' && double.TryParse(word[1..], System.Globalization.CultureInfo.InvariantCulture, out var radius))
                    tool.Radius = radius;
            }
            return;
        }
        if (!l2)
            return;
        var row = WorkParameters.CoordinateSystems.FirstOrDefault(x => x.Id == number);
        if (row is null)
            return;
        foreach (var word in words)
            if (word.Length > 1 && "XYZABCUVW".Contains(char.ToUpperInvariant(word[0])) && double.TryParse(word[1..], System.Globalization.CultureInfo.InvariantCulture, out var value))
                row.Values["XYZABCUVW".IndexOf(char.ToUpperInvariant(word[0]))] = value;
    }

    private void ApplyMotion(string line, MachineState state)
    {
        var rest = line;
        if (rest.StartsWith("$J=", StringComparison.OrdinalIgnoreCase))
            rest = rest[3..];

        var incremental = _distanceMode == 91;
        var isG92 = false;
        var g53 = false;

        // G-code words may be space-separated or packed ("G38.3F100Z-10");
        // tokenize on letter boundaries so both forms parse.
        foreach (var part in TokenizeWords(rest))
        {
            if (part.Length < 2)
                continue;

            var letter = char.ToUpperInvariant(part[0]);

            if (letter == 'F' && double.TryParse(part[1..], System.Globalization.CultureInfo.InvariantCulture, out var feed))
            {
                state.Feed = feed;
                continue;
            }

            if (letter == 'S' && double.TryParse(part[1..], System.Globalization.CultureInfo.InvariantCulture, out var speed))
            {
                state.Speed = speed;
                _lastSpeed = speed;
                continue;
            }

            if (letter == 'M')
            {
                // M3/M4 spindle on (restore last speed if S omitted), M5 off.
                switch (part[1..])
                {
                    case "3":
                    case "4":
                        if (state.Speed == 0)
                            state.Speed = _lastSpeed > 0 ? _lastSpeed : 1000;
                        break;
                    case "5":
                        state.Speed = 0;
                        break;
                }
                continue;
            }

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
                    case "53":
                        g53 = true; // G53 forces absolute machine coordinates.
                        break;
                    case "28":
                    case "30":
                        // G28/G30 move to the stored reference position, which the
                        // demo keeps at machine zero (as after $H). Axes moved
                        // immediately: work position follows the machine shift.
                        var refPos = EnsureMachinePositions(state);
                        for (var i = 0; i < refPos.Length; i++)
                        {
                            refPos[i] = 0;
                            if (i < state.Positions.Length)
                                state.Positions[i] = -_workOffset[i];
                        }
                        break;
                    case "28.1":
                    case "30.1":
                        // Record current position as the reference point. The demo
                        // home is fixed at machine zero, so this is a no-op that
                        // keeps the command flow working.
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

            var machinePos = EnsureMachinePositions(state);

            if (isG92)
            {
                // G92 sets the work coordinate; the machine coordinate is
                // unchanged, so the offset absorbs the difference.
                state.Positions[index] = value;
                _workOffset[index] = machinePos[index] - value;
            }
            else if (g53)
            {
                // G53 is always absolute machine coordinates.
                machinePos[index] = value;
                state.Positions[index] = value - _workOffset[index];
            }
            else
            {
                var work = incremental ? state.Positions[index] + value : value;
                state.Positions[index] = work;
                machinePos[index] = work + _workOffset[index];
            }
        }
    }

    private static double[] EnsureMachinePositions(MachineState state)
    {
        if (state.MachinePositions is null || state.MachinePositions.Length != state.Positions.Length)
            state.MachinePositions = new double[state.Positions.Length];
        return state.MachinePositions;
    }

    private static IEnumerable<string> TokenizeWords(string s)
    {
        var start = -1;
        for (var i = 0; i < s.Length; i++)
        {
            if (!char.IsLetter(s[i]))
                continue;
            if (start >= 0)
                yield return s[start..i].Trim();
            start = i;
        }
        if (start >= 0)
            yield return s[start..].Trim();
    }

    private void PollTick()
    {
        lock (_lock)
        {
            if (IsConnected)
                _subject.OnNext(_subject.Value);
        }
    }

    public void Dispose()
    {
        _pollTimer?.Dispose();
        _pollTimer = null;
    }
}
