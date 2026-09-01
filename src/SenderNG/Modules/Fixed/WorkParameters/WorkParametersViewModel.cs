/*
 * WorkParametersViewModel.cs - Work Parameters view model
 */

/*

Copyright (c) 2018-2022 Io Engineering (Terje Io)

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

using System.Collections.ObjectModel;
using CNC.Core.Abstractions;
using CNC.Core.Services;
using SenderNG.ViewModels;
using SenderNG.Core.Commands;

namespace SenderNG.Modules.Fixed;

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
    private string _name = string.Empty;

    public ToolRow(ToolTableEntry source, string name = "")
    {
        Source = source;
        Id = source.Id;
        Name = name;
        Offsets = new ObservableCollection<double>(source.Offsets);
        Radius = source.Radius;
    }

    public ToolTableEntry Source { get; }
    public int Id { get; }

    /// <summary>Editor-local tool name (selector label shows it in parentheses
    /// when present); refreshed from the OffsetsConfig store on snapshot/settings
    /// change so a name saved in the Offsets editor shows up here.</summary>
    public string Name
    {
        get => _name;
        set { if (SetField(ref _name, value)) OnPropertyChanged(nameof(DisplayName)); }
    }

    public string DisplayName =>
        Id == 0 ? "None" : string.IsNullOrWhiteSpace(Name) ? $"T{Id}" : $"T{Id} ({Name})";

    public ObservableCollection<double> Offsets { get; }
    public double Radius { get; set; }
}

public sealed class WorkParametersViewModel : ViewModelBase
{
    private readonly IGrblService _grbl;
    private readonly SettingsService? _settingsService;
    private readonly IDisposable _stateSubscription;
    private readonly IDisposable _workParametersSubscription;
    private string _selectedCoordinateSystem = "G54";
    private int _selectedTool;
    private bool _isTloActive;
    private bool _isTloReferenceSet;
    private bool _isToolChanging;
    private string _status = string.Empty;

    public WorkParametersViewModel(IGrblService grbl, SettingsService? settings = null)
    {
        _grbl = grbl ?? throw new ArgumentNullException(nameof(grbl));
        _settingsService = settings;
        RefreshCommand = new DelegateCommand(_ => Refresh());
        SelectCoordinateSystemCommand = new DelegateCommand(p => _ = SelectCoordinateSystemAsync(p as string));
        SelectToolCommand = new DelegateCommand(p => _ = SelectToolAsync(p));
        _stateSubscription = grbl.State.Subscribe(s => UiThread.Post(() => ApplyState(s)));
        _workParametersSubscription = grbl.WorkParametersChanges.Subscribe(
            p => UiThread.Post(() => Refresh(p)));
        if (_settingsService is not null)
            _settingsService.Changed += OnSettingsChanged;
        Refresh();
    }

    public ObservableCollection<WorkCoordinateRow> CoordinateSystems { get; } = new();
    public ObservableCollection<ToolRow> Tools { get; } = new();

    // Only offer systems the connected controller supports (classic grbl 1.1 has
    // no G59.x — selecting one would be rejected with error 29).
    public IReadOnlyList<string> CoordinateSystemCodes =>
        WorkParameters.SupportedSystems(_grbl.Capabilities.IsGrblHAL);

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

    /// <summary>Rebuilds rows from a work-parameters snapshot (used on live updates).
    /// The tool selector always offers "None" (slot 0) first — the controller
    /// reports no T0 in $#, and an unselected selector read as blank — followed
    /// by slots 1..N where N = min(the mode's "Visible tools" App-Settings
    /// value, the controller's slots) — the SAME rule as the Offsets editor
    /// grid, so the strip never lists all 32 firmware slots when the editor is
    /// configured for 8. Slot rows are synthesized when the snapshot doesn't
    /// report them (only labels are shown here); names are appended when the
    /// controller's editor-local name store has one.</summary>
    private void Refresh(WorkParameters parameters)
    {
        CoordinateSystems.Clear();
        foreach (var coordinateSystem in parameters.CoordinateSystems)
            CoordinateSystems.Add(new WorkCoordinateRow(coordinateSystem));
        var names = _settingsService is null
            ? new Dictionary<int, string>()
            : OffsetsConfigStore.ToolNamesFor(_settingsService.Load(), _grbl.ControllerKey);
        var actual = parameters.Tools.Where(t => t.Id > 0).ToDictionary(t => t.Id);
        var axisCount = parameters.CoordinateSystems.Select(x => x.Values.Length).DefaultIfEmpty(3).Max();
        Tools.Clear();
        // "None" (slot 0) is synthetic — the snapshot may itself report a T0
        // record (the demo seeds T0..N); it is excluded by the `t.Id > 0` filter
        // above so "None" never appears twice.
        Tools.Add(new ToolRow(new ToolTableEntry(0, 0)));
        for (var id = 1; id <= EffectiveToolCount(actual.Count); id++)
            Tools.Add(new ToolRow(
                actual.TryGetValue(id, out var source) ? source : new ToolTableEntry(id, axisCount),
                names.GetValueOrDefault(id, string.Empty)));
        SelectedCoordinateSystem = parameters.CoordinateSystems.Any(x => x.Code == parameters.ActiveCoordinateSystem)
            ? parameters.ActiveCoordinateSystem
            : SelectedCoordinateSystem;
        // Default selection is None (0): the board's ActiveTool may be 0 (no tool
        // selected) or absent from the reported table — the None row keeps the
        // selector from ever rendering blank.
        SelectedTool = Tools.Any(t => t.Id == parameters.ActiveTool) ? parameters.ActiveTool : 0;
        IsTloActive = parameters.ToolLengthOffset.Any(value => Math.Abs(value) > double.Epsilon);
        IsTloReferenceSet = !double.IsNaN(parameters.ToolLengthOffsetReference);
        Status = parameters.CoordinateSystems.Count == 0 ? "No work parameters reported" : string.Empty;
        // The row collection was rebuilt (new ToolRow instances): re-raise so the
        // view re-resolves the selection against the new rows. Without this, a
        // ComboBox SelectedItem bound OneWay keeps pointing at a removed row and
        // the strip renders BLANK even though SelectedToolRow still resolves —
        // the reported "nothing selected by default" regression.
        OnPropertyChanged(nameof(SelectedToolRow));
    }

    /// <summary>Tool slots offered by the selector: min(mode's VisibleTools,
    /// controller's NumTools). With no settings service (tests) the full
    /// controller/reported set is kept.</summary>
    private int EffectiveToolCount(int snapshotCount)
    {
        var controllerTools = _grbl.Capabilities.IsKnown && _grbl.Capabilities.NumTools > 0
            ? _grbl.Capabilities.NumTools : snapshotCount;
        if (_settingsService is null)
            return controllerTools;
        return Math.Min(Math.Clamp(_settingsService.Load().Current.VisibleTools, 0, 64), controllerTools);
    }

    /// <summary>Names are editor-local metadata saved by the Offsets editor, and
    /// the visible-tool count is a per-mode setting: on any settings change the
    /// row list rebuilds (count may have moved, e.g. App Settings → Offsets) and
    /// labels pick up freshly saved names in one pass.</summary>
    private void OnSettingsChanged()
    {
        if (_settingsService is null)
            return;
        Refresh(_grbl.WorkParameters);
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