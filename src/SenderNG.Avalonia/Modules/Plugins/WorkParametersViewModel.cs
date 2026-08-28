using System.Collections.ObjectModel;
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
    public string DisplayName => Id == 0 ? "None" : $"T{Id}";
    public ObservableCollection<double> Offsets { get; }
    public double Radius { get; set; }
}

public sealed class WorkParametersViewModel : ViewModelBase
{
    private readonly IGrblService _grbl;
    private readonly IDisposable _stateSubscription;
    private readonly IDisposable _workParametersSubscription;
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
        SelectCoordinateSystemCommand = new DelegateCommand(p => _ = SelectCoordinateSystemAsync(p as string));
        SelectToolCommand = new DelegateCommand(p => _ = SelectToolAsync(p));
        _stateSubscription = grbl.State.Subscribe(s => UiThread.Post(() => ApplyState(s)));
        _workParametersSubscription = grbl.WorkParametersChanges.Subscribe(
            p => UiThread.Post(() => Refresh(p)));
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
        private set
        {
            if (SetField(ref _selectedTool, value))
                OnPropertyChanged(nameof(SelectedToolRow));
        }
    }

    public ToolRow? SelectedToolRow => Tools.FirstOrDefault(tool => tool.Id == SelectedTool);

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
    public DelegateCommand SelectCoordinateSystemCommand { get; }
    public DelegateCommand SelectToolCommand { get; }

    public void Refresh() => Refresh(_grbl.WorkParameters);

    /// <summary>Rebuilds rows from a work-parameters snapshot (used on live updates).</summary>
    private void Refresh(WorkParameters parameters)
    {
        CoordinateSystems.Clear();
        foreach (var coordinateSystem in parameters.CoordinateSystems)
            CoordinateSystems.Add(new WorkCoordinateRow(coordinateSystem));
        Tools.Clear();
        foreach (var tool in parameters.Tools)
            Tools.Add(new ToolRow(tool));
        SelectedCoordinateSystem = parameters.CoordinateSystems.Any(x => x.Code == parameters.ActiveCoordinateSystem)
            ? parameters.ActiveCoordinateSystem
            : SelectedCoordinateSystem;
        SelectedTool = parameters.ActiveTool;
        IsTloActive = parameters.ToolLengthOffset.Any(value => Math.Abs(value) > double.Epsilon);
        IsTloReferenceSet = !double.IsNaN(parameters.ToolLengthOffsetReference);
        Status = parameters.CoordinateSystems.Count == 0 ? "No work parameters reported" : string.Empty;
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

    private void ApplyState(MachineState state)
    {
        if (state is null)
            return;
        IsToolChanging = state.IsToolChanging;
    }
}