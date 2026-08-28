using System.Collections.ObjectModel;
using System.Globalization;
using CNC.Core.Abstractions;
using CNC.Core.Services;
using SenderNG.Avalonia.ViewModels;
using SenderNG.Core.Commands;

namespace SenderNG.Avalonia.Modules.Fixed;

/// <summary>
/// Bindable table cell that proxies to a row's string store and re-raises when
/// the row is updated programmatically (Get current position, Clear all, board
/// readback), so TextBox bindings in the view stay live.
/// </summary>
public sealed class OffsetCellViewModel : ViewModelBase
{
    private readonly Func<string> _getValue;
    private readonly Action<string> _setValue;

    public OffsetCellViewModel(Func<string> getValue, Action<string> setValue, Action<EventHandler> subscribeChanged)
    {
        _getValue = getValue;
        _setValue = setValue;
        subscribeChanged((_, _) => OnPropertyChanged(nameof(Value)));
    }

    public string Value
    {
        get => _getValue();
        set => _setValue(value);
    }
}

public sealed class EditableOffsetRow : ViewModelBase
{
    public EditableOffsetRow(string code, int axisCount)
    {
        Code = code;
        Values = new ObservableCollection<string>(Enumerable.Repeat("0.000", axisCount));
        for (var i = 0; i < axisCount; i++)
        {
            var index = i;
            Cells.Add(new OffsetCellViewModel(
                () => GetValueAt(index),
                value => SetValueAt(index, value),
                handler => ValuesChanged += handler));
        }
    }

    public string Code { get; }
    public string DisplayName => Code;
    public ObservableCollection<string> Values { get; }
    public ObservableCollection<OffsetCellViewModel> Cells { get; } = new();
    public string X { get => GetValueAt(0); set => SetValueAt(0, value); }
    public string Y { get => GetValueAt(1); set => SetValueAt(1, value); }
    public string Z { get => GetValueAt(2); set => SetValueAt(2, value); }
    public string A { get => GetValueAt(3); set => SetValueAt(3, value); }
    public string B { get => GetValueAt(4); set => SetValueAt(4, value); }
    public string C { get => GetValueAt(5); set => SetValueAt(5, value); }
    public string? Error { get; set; }

    public event EventHandler? ValuesChanged;
    public string GetValueAt(int index) => index >= 0 && index < Values.Count ? Values[index] : string.Empty;
    public void SetValueAt(int index, string value)
    {
        if (index >= 0 && index < Values.Count && Values[index] != value)
        {
            Values[index] = value;
            OnPropertyChanged(nameof(Values));
            ValuesChanged?.Invoke(this, EventArgs.Empty);
            OnPropertyChanged(index switch { 0 => nameof(X), 1 => nameof(Y), 2 => nameof(Z), 3 => nameof(A), 4 => nameof(B), 5 => nameof(C), _ => string.Empty });
        }
    }
}

public sealed class EditableToolRow : ViewModelBase
{
    private string _radius = "0.000";

    public EditableToolRow(int id, int axisCount)
    {
        Id = id;
        Offsets = new ObservableCollection<string>(Enumerable.Repeat("0.000", axisCount));
        Radius = "0.000";
        for (var i = 0; i < axisCount; i++)
        {
            var index = i;
            Cells.Add(new OffsetCellViewModel(
                () => GetOffsetAt(index),
                value => SetOffsetAt(index, value),
                handler => ValuesChanged += handler));
        }
    }

    public int Id { get; }
    public string DisplayName => Id == 0 ? "None" : $"T{Id}";
    public ObservableCollection<string> Offsets { get; }
    public ObservableCollection<OffsetCellViewModel> Cells { get; } = new();
    public event EventHandler? ValuesChanged;
    public string Radius
    {
        get => _radius;
        set => SetField(ref _radius, value);
    }
    public string GetOffsetAt(int index) => index >= 0 && index < Offsets.Count ? Offsets[index] : string.Empty;
    public void SetOffsetAt(int index, string value)
    {
        if (index >= 0 && index < Offsets.Count && Offsets[index] != value)
        {
            Offsets[index] = value;
            OnPropertyChanged(nameof(Offsets));
            ValuesChanged?.Invoke(this, EventArgs.Empty);
        }
    }
    public string? Error { get; set; }
}

public sealed class OffsetsViewModel : ViewModelBase
{
    private static readonly string[] AxisLetters = ["X", "Y", "Z", "A", "B", "C", "U", "V", "W"];

    private readonly IGrblService _grbl;
    private readonly IDisposable _stateSubscription;
    private readonly IDisposable _workParametersSubscription;
    private MachineState _lastState = new();
    private int _axisCount = 3;
    private string _selectedOffset = "G54";
    private string _status = string.Empty;

    public OffsetsViewModel(IGrblService grbl)
    {
        _grbl = grbl ?? throw new ArgumentNullException(nameof(grbl));
        _stateSubscription = grbl.State.Subscribe(s => UiThread.Post(() => ApplyState(s)));
        _workParametersSubscription = grbl.WorkParametersChanges.Subscribe(
            p => UiThread.Post(() => ApplySnapshot(p)));
        ApplySnapshot(grbl.WorkParameters);
        SelectedOffset = Rows.FirstOrDefault()?.Code ?? "G54";
        RefreshCommand = new DelegateCommand(_ => Refresh());
        SetAxisCommand = new DelegateCommand(p => _ = SetAxisAsync(p));
        GetCurrentPositionCommand = new DelegateCommand(_ => GetCurrentPosition());
        ClearAllCommand = new DelegateCommand(_ => ClearAll());
        SetAllCommand = new DelegateCommand(_ => LastSaveTask = SetAllAsync());
        SaveToolCommand = new DelegateCommand(p => LastSaveTask = SaveToolAsync(p));
    }

    public ObservableCollection<EditableOffsetRow> Rows { get; } = new();
    public ObservableCollection<EditableToolRow> Tools { get; } = new();
    public IReadOnlyList<string> AxisNames => AxisLetters[.._axisCount];
    public IReadOnlyList<string> ToolAxisNames => AxisNames;
    public IReadOnlyList<string> OffsetCodes => Rows.Select(row => row.Code).ToArray();

    public string SelectedOffset { get => _selectedOffset; set { if (SetField(ref _selectedOffset, value)) OnPropertyChanged(nameof(SelectedRow)); } }
    public EditableOffsetRow? SelectedRow => Rows.FirstOrDefault(row => row.Code == SelectedOffset);
    public string Status { get => _status; private set => SetField(ref _status, value); }
    public DelegateCommand RefreshCommand { get; }
    public DelegateCommand SetAxisCommand { get; }
    public DelegateCommand GetCurrentPositionCommand { get; }
    public DelegateCommand ClearAllCommand { get; }
    public DelegateCommand SetAllCommand { get; }
    public DelegateCommand SaveToolCommand { get; }
    public Task? LastSaveTask { get; private set; }

    /// <summary>Applies a new engine snapshot: resize rows, then refresh values for
    /// the G10-writable coordinate systems the controller reports.</summary>
    private void ApplySnapshot(WorkParameters parameters)
    {
        var axisCount = Math.Clamp(parameters.CoordinateSystems
            .Select(x => x.Values.Length).DefaultIfEmpty(_axisCount).Max(), 1, AxisLetters.Length);
        if (axisCount != _axisCount || Rows.Count == 0)
        {
            _axisCount = axisCount;
            BuildRows(parameters);
            OnPropertyChanged(nameof(AxisNames));
            OnPropertyChanged(nameof(ToolAxisNames));
            OnPropertyChanged(nameof(OffsetCodes));
        }
        // Update row values from the snapshot, but only for rows still present
        // (never rebuild or reselect mid-edit). Classic grbl can't report the
        // active workspace in $#, so the editor selection stays user-driven like
        // ioSender instead of snapping back to a synthetic default.
        foreach (var row in Rows)
        {
            var source = parameters.CoordinateSystems
                .FirstOrDefault(x => x.Code == row.Code && WorkParameters.IsG10Writable(x.Code));
            if (source is null)
                continue;
            for (var i = 0; i < _axisCount && i < source.Values.Length; i++)
                row.SetValueAt(i, Format(source.Values[i]));
        }
    }

    private void ApplyState(MachineState state)
    {
        if (state is null)
            return;
        _lastState = state;
    }

    public void Refresh()
    {
        ApplySnapshot(_grbl.WorkParameters);
        Status = "Offsets refreshed";
    }

    private async Task SetAxisAsync(object? parameter)
    {
        if (parameter is not string letter || !TryAxis(letter, out var index) || SelectedRow is null) return;
        await SetCoordinateValuesAsync(SelectedRow, index);
    }

    private async Task SetCoordinateValuesAsync(EditableOffsetRow row, int? onlyAxis = null)
    {
        if (!WorkParameters.IsG10Writable(row.Code)) { row.Error = "Only G54..G59.3 are writable"; Status = row.Error; return; }
        if (!TryValues(row.Values, out var values, out var error)) { row.Error = error; Status = error!; return; }
        var words = string.Concat(values.Select((value, index) => onlyAxis is null || index == onlyAxis ? $" {AxisLetters[index]}{Format(value)}" : string.Empty));
        if (words.Length == 0) return;
        var command = $"G10 L2 P{WorkParameters.CoordinateSystemNumber(row.Code)} {words}";
        try
        {
            await _grbl.SendAsync(command);
            row.Error = null;
            Status = $"Set {row.Code}";
            await SafeRefreshAsync();
        }
        catch (Exception ex) { Status = $"Set {row.Code} failed: {ex.Message}"; }
    }

    private void GetCurrentPosition()
    {
        var row = SelectedRow;
        if (row is null) return;
        var positions = _lastState.Positions ?? [];
        for (var i = 0; i < _axisCount && i < positions.Length; i++) row.SetValueAt(i, Format(positions[i]));
        Status = $"Current position copied to {SelectedOffset}";
    }

    private void ClearAll()
    {
        foreach (var row in Rows)
            for (var i = 0; i < row.Values.Count; i++) row.SetValueAt(i, "0.000");
        Status = "All offsets cleared in editor; press Set all to write them";
    }

    public Task SetAllAsync() => SetAllCoreAsync();

    private async Task SetAllCoreAsync()
    {
        // Build every command up front. Commands are read from the user-edited rows,
        // which the engine's live publications must not be allowed to overwrite in
        // between sends — otherwise the earlier writes re-render the rows from the
        // (still-stale) snapshot and later commands repeat the old values.
        var pending = new List<(EditableOffsetRow Row, string Command)>();
        var failures = 0;
        foreach (var row in Rows.Where(row => WorkParameters.IsG10Writable(row.Code)))
        {
            if (!TryValues(row.Values, out var values, out var error))
            {
                row.Error = error;
                failures++;
                continue;
            }
            var words = string.Concat(values.Select((value, index) => $" {AxisLetters[index]}{Format(value)}"));
            pending.Add((row, $"G10 L2 P{WorkParameters.CoordinateSystemNumber(row.Code)}{words}"));
        }

        var sent = 0;
        foreach (var (row, command) in pending)
        {
            try
            {
                await _grbl.SendAsync(command);
                row.Error = null;
                sent++;
            }
            catch (Exception ex)
            {
                row.Error = ex.Message;
                failures++;
            }
        }
        // Re-read from the board so the editor reflects what was actually written,
        // not just what we sent (ioSender re-requests work parameters after writes).
        await SafeRefreshAsync();
        Status = failures == 0 ? $"Saved {sent} offsets" : $"Saved {sent} offsets; {failures} failed";
    }

    public Task SaveToolAsync(object? parameter)
        => SaveToolCoreAsync(parameter);

    private async Task SaveToolCoreAsync(object? parameter)
    {
        if (parameter is not EditableToolRow tool || !TryValues(tool.Offsets, out var values, out var error) || !double.TryParse(tool.Radius, NumberStyles.Float, CultureInfo.InvariantCulture, out var radius))
        { Status = "Invalid tool-table value"; return; }
        var words = string.Concat(values.Select((value, index) => $"{AxisLetters[index]}{Format(value)}"));
        try
        {
            await _grbl.SendAsync($"G10L1P{tool.Id}{words}R{Format(radius)}");
            tool.Error = null;
            Status = $"Saved {tool.DisplayName}";
            await SafeRefreshAsync();
        }
        catch (Exception ex) { Status = $"Save {tool.DisplayName} failed: {ex.Message}"; }
    }

    /// <summary>One-shot board refresh; failures are non-fatal (keep last snapshot).</summary>
    private async Task SafeRefreshAsync()
    {
        try
        {
            await _grbl.RefreshWorkParametersAsync();
        }
        catch
        {
            // Non-fatal: the demo is already live and a failed $# read keeps the
            // last published snapshot.
        }
    }

    private bool TryAxis(string letter, out int index) { index = Array.IndexOf(AxisLetters, letter); return index >= 0 && index < _axisCount; }
    private static bool TryValues(IReadOnlyList<string> values, out double[] result, out string? error)
    {
        result = new double[values.Count];
        for (var i = 0; i < values.Count; i++) if (!double.TryParse(values[i], NumberStyles.Float, CultureInfo.InvariantCulture, out result[i])) { error = $"Invalid numeric value at axis {i + 1}"; return false; }
        error = null; return true;
    }
    private void BuildRows(WorkParameters parameters)
    {
        Rows.Clear();
        foreach (var item in parameters.CoordinateSystems.Where(x => WorkParameters.IsG10Writable(x.Code)))
            Rows.Add(new EditableOffsetRow(item.Code, _axisCount));
        if (Rows.Count == 0)
            foreach (var code in WorkParameters.CoordinateSystemCodes)
                Rows.Add(new EditableOffsetRow(code, _axisCount));
        Tools.Clear(); foreach (var item in parameters.Tools) Tools.Add(new EditableToolRow(item.Id, _axisCount));
        if (Tools.Count == 0) Tools.Add(new EditableToolRow(0, _axisCount));
        if (SelectedRow is null) SelectedOffset = Rows[0].Code;
    }
    private static string Format(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
}