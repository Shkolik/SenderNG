using System.Collections.ObjectModel;
using System.Globalization;
using System.Reactive.Linq;
using CNC.Core.Abstractions;
using CNC.Core.Services;
using SenderNG.Avalonia.ViewModels;
using SenderNG.Core.Commands;

namespace SenderNG.Avalonia.Modules.Plugins;

public sealed class EditableOffsetRow : ViewModelBase
{
    public EditableOffsetRow(string code, int axisCount)
    {
        Code = code;
        Values = new ObservableCollection<string>(Enumerable.Repeat("0.000", axisCount));
    }

    public string Code { get; }
    public string DisplayName => Code;
    public ObservableCollection<string> Values { get; }
    public string X { get => GetValue(0); set => SetValue(0, value); }
    public string Y { get => GetValue(1); set => SetValue(1, value); }
    public string Z { get => GetValue(2); set => SetValue(2, value); }
    public string A { get => GetValue(3); set => SetValue(3, value); }
    public string B { get => GetValue(4); set => SetValue(4, value); }
    public string C { get => GetValue(5); set => SetValue(5, value); }
    public string? Error { get; set; }

    private string GetValue(int index) => index < Values.Count ? Values[index] : string.Empty;
    public void SetValueAt(int index, string value)
    {
        if (index >= 0 && index < Values.Count)
            Values[index] = value;
    }

    private void SetValue(int index, string value) => SetValueAt(index, value);
}

public sealed class EditableToolRow : ViewModelBase
{
    public EditableToolRow(int id, int axisCount)
    {
        Id = id;
        Offsets = new ObservableCollection<string>(Enumerable.Repeat("0.000", axisCount));
        Radius = "0.000";
    }

    public int Id { get; }
    public string DisplayName => Id == 0 ? "None" : $"T{Id}";
    public ObservableCollection<string> Offsets { get; }
    public string Radius { get; set; }
    public string? Error { get; set; }
}

public sealed class OffsetsViewModel : ViewModelBase
{
    private static readonly string[] OffsetCodes = ["G54", "G55", "G56", "G57", "G58", "G59", "G28", "G30", "G92"];
    private static readonly string[] AxisLetters = ["X", "Y", "Z", "A", "B", "C", "U", "V", "W"];
    private readonly IGrblService _grbl;
    private int _axisCount;
    private string _selectedOffset = "G54";
    private string _status = string.Empty;

    public OffsetsViewModel(IGrblService grbl)
    {
        _grbl = grbl ?? throw new ArgumentNullException(nameof(grbl));
        _axisCount = Math.Clamp(grbl.Capabilities.NumAxes, 1, AxisLetters.Length);
        BuildRows(grbl.WorkParameters);

        SelectedOffset = grbl.WorkParameters.ActiveCoordinateSystem;
        RefreshCommand = new DelegateCommand(_ => Refresh());
        SetAxisCommand = new DelegateCommand(p => _ = SetAxisAsync(p));
        GetCurrentPositionCommand = new DelegateCommand(_ => GetCurrentPosition());
        ClearAllCommand = new DelegateCommand(_ => ClearAll());
        SetAllCommand = new DelegateCommand(_ => _ = SetAllAsync());
        SaveToolCommand = new DelegateCommand(p => _ = SaveToolAsync(p));
    }

    public ObservableCollection<EditableOffsetRow> Rows { get; } = new();
    public ObservableCollection<EditableToolRow> Tools { get; } = new();
    public IReadOnlyList<string> AxisNames => AxisLetters[.._axisCount];

    public string SelectedOffset
    {
        get => _selectedOffset;
        set
        {
            if (SetField(ref _selectedOffset, value))
                OnPropertyChanged(nameof(SelectedRow));
        }
    }

    public EditableOffsetRow? SelectedRow => Rows.FirstOrDefault(row => row.Code == SelectedOffset);

    public string Status
    {
        get => _status;
        private set => SetField(ref _status, value);
    }

    public DelegateCommand RefreshCommand { get; }
    public DelegateCommand SetAxisCommand { get; }
    public DelegateCommand GetCurrentPositionCommand { get; }
    public DelegateCommand ClearAllCommand { get; }
    public DelegateCommand SetAllCommand { get; }
    public DelegateCommand SaveToolCommand { get; }

    public void Refresh()
    {
        var parameters = _grbl.WorkParameters;
        var axisCount = Math.Clamp(parameters.CoordinateSystems.Select(x => x.Values.Length).DefaultIfEmpty(_axisCount).Max(), 1, AxisLetters.Length);
        if (axisCount != _axisCount || Rows.Count != OffsetCodes.Length || Tools.Count != parameters.Tools.Count)
        {
            _axisCount = axisCount;
            BuildRows(parameters);
        }
        foreach (var row in Rows)
        {
            var source = parameters.CoordinateSystems.FirstOrDefault(item => item.Code == row.Code);
            if (source is null)
                continue;
            for (var i = 0; i < _axisCount && i < source.Values.Length && i < row.Values.Count; i++)
                row.Values[i] = Format(source.Values[i]);
        }
        SelectedOffset = parameters.ActiveCoordinateSystem;
        Status = "Offsets refreshed";
    }

    private async Task SetAxisAsync(object? parameter)
    {
        if (!TryAxis(parameter, out var index))
            return;
        await SetCoordinateValuesAsync(SelectedRow, index);
    }

    private async Task SetCoordinateValuesAsync(EditableOffsetRow row, int? onlyAxis = null)
    {
        if (!TryValues(row.Values, out var values, out var error))
        {
            row.Error = error;
            Status = error!;
            return;
        }
        var axisWords = string.Concat(values.Select((value, index) =>
            onlyAxis is null || onlyAxis == index ? $"{AxisLetters[index]}{Format(value)}" : string.Empty));
        if (axisWords.Length == 0)
            return;
        var p = CoordinateSystemNumber(row.Code);
        var command = $"G10L2P{p}{axisWords}";
        try
        {
            await _grbl.SendAsync(command);
            row.Error = null;
            Status = $"Set {row.Code}";
        }
        catch (Exception ex)
        {
            Status = $"Set {row.Code} failed: {ex.Message}";
        }
    }

    private void GetCurrentPosition()
    {
        var state = _grbl.State.FirstAsync().Wait();
        var row = SelectedRow;
        if (row is null)
            return;
        for (var i = 0; i < _axisCount && i < state.Positions.Length && i < row.Values.Count; i++)
            row.SetValueAt(i, Format(state.Positions[i]));
        Status = $"Current position copied to {SelectedOffset}";
    }

    private void ClearAll()
    {
        foreach (var row in Rows)
            for (var i = 0; i < row.Values.Count; i++)
                row.SetValueAt(i, "0.000");
        Status = "All offsets cleared in editor; press Set all to write them";
    }

    private async Task SetAllAsync()
    {
        foreach (var row in Rows)
            await SetCoordinateValuesAsync(row);
        Refresh();
        Status = "All offsets sent";
    }

    private async Task SaveToolAsync(object? parameter)
    {
        if (parameter is not EditableToolRow tool || !TryValues(tool.Offsets, out var values, out var error)
            || !double.TryParse(tool.Radius, NumberStyles.Float, CultureInfo.InvariantCulture, out var radius))
        {
            if (parameter is EditableToolRow invalid)
                invalid.Error = "Enter valid numeric tool offsets and radius";
            Status = "Invalid tool-table value";
            return;
        }
        var words = string.Concat(values.Select((value, index) => $"{AxisLetters[index]}{Format(value)}"));
        var command = $"G10L1P{tool.Id}{words}R{Format(radius)}";
        try
        {
            await _grbl.SendAsync(command);
            tool.Error = null;
            Status = $"Saved {tool.DisplayName}";
        }
        catch (Exception ex)
        {
            Status = $"Save {tool.DisplayName} failed: {ex.Message}";
        }
    }

    private bool TryAxis(object? parameter, out int index)
    {
        index = parameter switch
        {
            int i => i,
            string letter => Array.IndexOf(AxisLetters, letter),
            _ => -1,
        };
        return index >= 0 && index < _axisCount;
    }

    private static bool TryValues(IReadOnlyList<string> values, out double[] result, out string? error)
    {
        result = new double[values.Count];
        for (var i = 0; i < values.Count; i++)
        {
            if (!double.TryParse(values[i], NumberStyles.Float, CultureInfo.InvariantCulture, out result[i]))
            {
                error = $"Invalid numeric value at axis {i + 1}";
                return false;
            }
        }
        error = null;
        return true;
    }

    private static int CoordinateSystemNumber(string code) => code switch
    {
        "G54" => 1, "G55" => 2, "G56" => 3, "G57" => 4, "G58" => 5, "G59" => 6,
        "G92" => 0, "G28" => 0, "G30" => 0, _ => 1,
    };

    private void BuildRows(WorkParameters parameters)
    {
        Rows.Clear();
        foreach (var code in parameters.CoordinateSystems.Select(x => x.Code).Distinct())
            Rows.Add(new EditableOffsetRow(code, _axisCount));
        if (Rows.Count == 0)
            foreach (var code in OffsetCodes)
                Rows.Add(new EditableOffsetRow(code, _axisCount));
        Tools.Clear();
        foreach (var tool in parameters.Tools)
            Tools.Add(new EditableToolRow(tool.Id, _axisCount));
        if (Tools.Count == 0)
            Tools.Add(new EditableToolRow(0, _axisCount));
        if (!Rows.Any(row => row.Code == SelectedOffset))
            SelectedOffset = Rows[0].Code;
    }

    private static string Format(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
    private static string Format(double? value) => value is null ? "0.000" : Format(value.Value);
}
