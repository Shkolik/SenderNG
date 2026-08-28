using System.Collections.ObjectModel;
using System.Globalization;
using CNC.Core.Abstractions;
using CNC.Core.Services;
using SenderNG.Avalonia.ViewModels;
using SenderNG.Core.Commands;

namespace SenderNG.Avalonia.Modules.Plugins;

public sealed class WorkCoordinateRow : ViewModelBase
{
    public WorkCoordinateRow(WorkCoordinateSystem source)
    {
        Source = source;
        Code = source.Code;
        Values = new ObservableCollection<double>(source.Values);
    }

    public WorkCoordinateSystem Source { get; }
    public string Code { get; }
    public ObservableCollection<double> Values { get; }
}

public sealed class ToolRow : ViewModelBase
{
    public ToolRow(ToolTableEntry source)
    {
        Source = source;
        Id = source.Id;
        Offsets = new ObservableCollection<double>(source.Offsets);
        Radius = source.Radius;
    }

    public ToolTableEntry Source { get; }
    public int Id { get; }
    public ObservableCollection<double> Offsets { get; }
    public double Radius { get; set; }
}

public sealed class WorkParametersViewModel : ViewModelBase
{
    private readonly IGrblService _grbl;
    private readonly IDisposable _stateSubscription;
    private string _selectedCoordinateSystem = "G54";
    private int _selectedTool;
    private bool _isTloActive;
    private bool _isTloReferenceSet;
    private bool _isToolChanging;
    private string _status = string.Empty;

    public WorkParametersViewModel(IGrblService grbl)
    {
        _grbl = grbl ?? throw new ArgumentNullException(nameof(grbl));
        RefreshCommand = new DelegateCommand(_ => Refresh());
        SaveCommand = new DelegateCommand(_ => _ = SaveAsync(), _ => _grbl.IsConnected);
        SelectCoordinateSystemCommand = new DelegateCommand(p => _ = SelectCoordinateSystemAsync(p as string));
        SelectToolCommand = new DelegateCommand(p => _ = SelectToolAsync(p));
        _stateSubscription = grbl.State.Subscribe(s => UiThread.Post(() => ApplyState(s)));
        Refresh();
    }

    public ObservableCollection<WorkCoordinateRow> CoordinateSystems { get; } = new();
    public ObservableCollection<ToolRow> Tools { get; } = new();

    public IReadOnlyList<string> CoordinateSystemCodes { get; } = WorkParameters.CoordinateSystemCodes;

    public string SelectedCoordinateSystem
    {
        get => _selectedCoordinateSystem;
        private set => SetField(ref _selectedCoordinateSystem, value);
    }

    public int SelectedTool
    {
        get => _selectedTool;
        private set => SetField(ref _selectedTool, value);
    }

    public bool IsTloActive
    {
        get => _isTloActive;
        private set => SetField(ref _isTloActive, value);
    }

    public bool IsTloReferenceSet
    {
        get => _isTloReferenceSet;
        private set => SetField(ref _isTloReferenceSet, value);
    }

    public bool IsToolChanging
    {
        get => _isToolChanging;
        private set => SetField(ref _isToolChanging, value);
    }

    public string Status
    {
        get => _status;
        private set => SetField(ref _status, value);
    }

    public DelegateCommand RefreshCommand { get; }
    public DelegateCommand SaveCommand { get; }
    public DelegateCommand SelectCoordinateSystemCommand { get; }
    public DelegateCommand SelectToolCommand { get; }

    public void Refresh()
    {
        var parameters = _grbl.WorkParameters;
        CoordinateSystems.Clear();
        foreach (var coordinateSystem in parameters.CoordinateSystems)
            CoordinateSystems.Add(new WorkCoordinateRow(coordinateSystem));
        Tools.Clear();
        foreach (var tool in parameters.Tools)
            Tools.Add(new ToolRow(tool));
        SelectedCoordinateSystem = parameters.ActiveCoordinateSystem;
        SelectedTool = parameters.ActiveTool;
        IsTloActive = parameters.ToolLengthOffset.Any(value => Math.Abs(value) > double.Epsilon);
        IsTloReferenceSet = !double.IsNaN(parameters.ToolLengthOffsetReference);
        Status = parameters.CoordinateSystems.Count == 0 ? "No work parameters reported" : string.Empty;
        SaveCommand.RaiseCanExecuteChanged();
    }

    private async Task SelectCoordinateSystemAsync(string? code)
    {
        if (string.IsNullOrWhiteSpace(code) || !CoordinateSystemCodes.Contains(code))
            return;
        try
        {
            await _grbl.SendAsync(code);
            SelectedCoordinateSystem = code;
            Status = $"Selected {code}";
        }
        catch (Exception ex)
        {
            Status = $"Offset selection failed: {ex.Message}";
        }
    }

    private async Task SelectToolAsync(object? parameter)
    {
        var id = parameter switch
        {
            ToolRow row => row.Id,
            int value => value,
            _ => -1,
        };
        if (id < 0)
            return;
        try
        {
            await _grbl.SendAsync($"T{id}");
            SelectedTool = id;
            Status = $"Tool T{id} selected";
        }
        catch (Exception ex)
        {
            Status = $"Tool selection failed: {ex.Message}";
        }
    }

    private async Task SaveAsync()
    {
        // Tool-table and coordinate editing will use G10/G43 semantics. Keep this
        // command present now, but do not silently write values until the explicit
        // editor fields have been changed and validated.
        await Task.CompletedTask;
        Status = "Edit values then save (controller write-back pending)";
    }

    private void ApplyState(MachineState state)
    {
        IsToolChanging = state.IsToolChanging;
        SaveCommand.RaiseCanExecuteChanged();
    }
}
