/*
 * OffsetsViewModel.cs - Offsets table editor view model
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
using System.Globalization;
using CNC.Core.Abstractions;
using CNC.Core.Services;
using SenderNG.Localization;
using SenderNG.ViewModels;
using SenderNG.Core.Commands;

namespace SenderNG.Modules.Fixed;

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
        Values = new ObservableCollection<string>(Enumerable.Repeat("0", axisCount));
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
    private string _radius = "0";
    private string _name = string.Empty;

    public EditableToolRow(int id, int axisCount)
    {
        Id = id;
        Offsets = new ObservableCollection<string>(Enumerable.Repeat("0", axisCount));
        Radius = "0";
        for (var i = 0; i < axisCount; i++)
        {
            var index = i;
            Cells.Add(new OffsetCellViewModel(
                () => GetOffsetAt(index),
                value => SetOffsetAt(index, value),
                handler => ValuesChanged += handler));
        }
        // Radius sits in the same uniform grid as the axis cells, so the column
        // is exactly as wide as an axis column (and the name column keeps the
        // remaining space).
        Cells.Add(new OffsetCellViewModel(
            () => Radius,
            value => Radius = value,
            handler => PropertyChanged += (_, _) => handler(this, EventArgs.Empty)));
    }

    public int Id { get; }

    /// <summary>Selector label: the tool name when set, else the slot fallback
    /// (T1, T2, ...). Slot 0 is always "None".</summary>
    public string DisplayName => Id == 0 ? "None" : string.IsNullOrWhiteSpace(Name) ? $"T{Id}" : Name;

    /// <summary>Ghost text shown in the name cell while it has no name (the
    /// placeholder "Tx" mirrors the selector fallback).</summary>
    public string NamePlaceholder => $"T{Id}";

    /// <summary>Editor-local tool name (firmware has no name field; persisted in
    /// OffsetsConfig.ToolNames on save).</summary>
    public string Name
    {
        get => _name;
        set
        {
            if (SetField(ref _name, value))
                OnPropertyChanged(nameof(DisplayName));
        }
    }

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

    /// <summary>Original values loaded from the board; a change marks the tool dirty
    /// so the single "Save tool table" button only writes edited rows.</summary>
    public string[] OriginalOffsets { get; private set; } = [];
    public string OriginalRadius { get; private set; } = "0";
    public string OriginalName { get; private set; } = string.Empty;
    public void SetOriginals(string[] offsets, string radius, string name)
    {
        OriginalOffsets = offsets;
        OriginalRadius = radius;
        OriginalName = name;
    }
    public bool IsDirty => OriginalName != Name || OriginalRadius != Radius ||
        OriginalOffsets.Length != Offsets.Count ||
        OriginalOffsets.Select((v, i) => v != GetOffsetAt(i)).Any(x => x);
}

public sealed class OffsetsViewModel : ViewModelBase
{
    private static readonly string[] AxisLetters = ["X", "Y", "Z", "A", "B", "C", "U", "V", "W"];

    private readonly IGrblService _grbl;
    private readonly SettingsService _settingsService;
    private readonly MachineProfile _profile;
    private readonly IDisposable _stateSubscription;
    private readonly IDisposable _workParametersSubscription;
    private MachineState _lastState = new();
    private int _axisCount = 3;
    private string _selectedOffset = "G54";
    private string _status = string.Empty;
    private bool _saving;
    private bool _isAlarm;
    private string _controllerKey = "unknown";
    private int _visibleTools = 8;

    public OffsetsViewModel(IGrblService grbl, SettingsService settingsService)
    {
        _grbl = grbl ?? throw new ArgumentNullException(nameof(grbl));
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _profile = _settingsService.Load();

        Config = OffsetsConfigStore.Load(_profile);
        Config.PropertyChanged += OnConfigPropertyChanged;
        _visibleTools = ReadVisibleTools();

        _stateSubscription = grbl.State.Subscribe(s => UiThread.Post(() => ApplyState(s)));
        _workParametersSubscription = grbl.WorkParametersChanges.Subscribe(
            p => UiThread.Post(() => ApplySnapshot(p)));
        ApplySnapshot(grbl.WorkParameters);
        // Initial selection is programmatic — never sends an activation.
        if (SetField(ref _selectedOffset, Rows.FirstOrDefault()?.Code ?? "G54"))
            OnPropertyChanged(nameof(SelectedRow));
        RefreshCommand = new DelegateCommand(_ => Refresh());
        SetAxisCommand = new DelegateCommand(p => _ = SetAxisAsync(p));
        GetCurrentPositionCommand = new DelegateCommand(_ => GetCurrentPosition());
        ClearAllCommand = new DelegateCommand(_ => ClearAll());
        SetAllCommand = new DelegateCommand(_ => LastSaveTask = SetAllAsync());
        GetToolPositionCommand = new DelegateCommand(_ => TouchOff(SelectedToolId));
        ClearToolsCommand = new DelegateCommand(_ => ClearTools());
        SetAllToolsCommand = new DelegateCommand(_ => LastSaveTask = SaveToolTableAsync());
        _settingsService.Changed += OnSettingsChanged;
    }

    public OffsetsConfig Config { get; private set; }

    public ObservableCollection<EditableOffsetRow> Rows { get; } = new();
    public ObservableCollection<EditableToolRow> Tools { get; } = new();
    public IReadOnlyList<string> AxisNames => AxisLetters[.._axisCount];

    /// <summary>Tool-table column headers: the axis letters plus the radius,
    /// rendered as one uniform grid so radius is as wide as an axis column.</summary>
    public IReadOnlyList<string> ToolColumnHeaders =>
        AxisNames.Append(LocText.Resolve("module.offsets-table.radius", "Radius")).ToArray();

    public IReadOnlyList<string> OffsetCodes => Rows.Select(row => row.Code).ToArray();

    public string SelectedOffset
    {
        get => _selectedOffset;
        set
        {
            if (!SetField(ref _selectedOffset, value))
                return;
            OnPropertyChanged(nameof(SelectedRow));
            // iSender parity: picking a system in the editor activates it on the
            // controller (the combo sends G54..G59.x), so the DRO zero (G10 L20
            // P0 = active system) and any writes land in the system the user is
            // looking at — not the previously active one.
            _ = ActivateOffsetAsync(value);
        }
    }

    private async Task ActivateOffsetAsync(string code)
    {
        if (!_grbl.IsConnected)
            return;
        try
        {
            await _grbl.SendAsync(code);
        }
        catch
        {
            // Selection is UI state; a failed activation must not disturb the editor.
        }
    }
    public EditableOffsetRow? SelectedRow => Rows.FirstOrDefault(row => row.Code == SelectedOffset);

    private int _selectedToolId;

    /// <summary>Selected tool slot (0 = "None"). Selection is independent of cell
    /// values, so switching rows never clobbers in-progress edits. The bottom
    /// selector drives it; the controller's active tool follows it when nothing
    /// is dirty.</summary>
    public int SelectedToolId
    {
        get => _selectedToolId;
        set
        {
            if (SetField(ref _selectedToolId, value))
                OnPropertyChanged(nameof(SelectedToolRow));
        }
    }

    /// <summary>Settable so the bottom ComboBox binds TwoWay to the row object
    /// (mirrors SelectedOffset/SelectedRow in the offsets table above).</summary>
    public EditableToolRow? SelectedToolRow
    {
        get => Tools.FirstOrDefault(tool => tool.Id == SelectedToolId);
        set { if (value is not null) SelectedToolId = value.Id; }
    }
    public string Status { get => _status; private set => SetField(ref _status, value); }
    public DelegateCommand RefreshCommand { get; }
    public DelegateCommand SetAxisCommand { get; }
    public DelegateCommand GetCurrentPositionCommand { get; }
    public DelegateCommand ClearAllCommand { get; }
    public DelegateCommand SetAllCommand { get; }
    public DelegateCommand GetToolPositionCommand { get; }
    public DelegateCommand ClearToolsCommand { get; }
    public DelegateCommand SetAllToolsCommand { get; }
    public Task? LastSaveTask { get; private set; }

    /// <summary>True when the connected controller can persist a tool table.
    /// G10 L1 tool-table writes only exist in grblHAL (a compile-time option);
    /// classic grbl 1.1h has no G10 L1, and classic forks that merely report tool
    /// slots in [OPT:] (e.g. mega5x 1.2i) reject the write with error 20. So both
    /// the HAL flag and reported tool slots are required.</summary>
    public bool ControllerSupportsToolTable =>
        _grbl.Capabilities.IsGrblHAL && _grbl.Capabilities.NumTools > 0;

    /// <summary>The tool table is shown only when the user enabled it in App
    /// Settings AND the controller can persist one — no grayed-out placeholder on
    /// controllers without G10 L1.</summary>
    public bool ShowToolTableSection => Config.ShowToolTable && ControllerSupportsToolTable && VisibleTools > 0;

    /// <summary>Coordinate systems the connected controller can actually write
    /// (classic grbl 1.1 offers only G54..G59; G59.x is grblHAL-only).</summary>
    public IReadOnlyList<string> SupportedSystems =>
        WorkParameters.SupportedSystems(_grbl.Capabilities.IsGrblHAL);

    /// <summary>Editor usable state: while the controller is in Alarm, grbl rejects
    /// every offset/tool write, so the editor disables itself.</summary>
    public bool IsReady => !_isAlarm;

    public bool ShowToolTable
    {
        get => Config.ShowToolTable;
        set => Config.ShowToolTable = value;
    }

    /// <summary>Visible tool slots for the current mode (T1..N, 0 = hidden grid),
    /// set in App Settings &gt; Offsets (mode-aware). The effective row count is
    /// further clamped to the connected controller's tool slots.</summary>
    public int VisibleTools => _visibleTools;

    private void OnConfigPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (_saving || e.PropertyName != nameof(OffsetsConfig.ShowToolTable))
            return;
        Save();
        OnPropertyChanged(nameof(ShowToolTable));
    }

    private void OnSettingsChanged()
    {
        if (_saving)
            return;
        Config = OffsetsConfigStore.Load(_profile);
        Config.PropertyChanged += OnConfigPropertyChanged;
        OnPropertyChanged(nameof(Config));
        OnPropertyChanged(nameof(ShowToolTable));
        // Mode-aware visible-tool count: re-read it (the active mode may have
        // changed or App Settings edited it) and rebuild the grid when it moved.
        // The fresh rows must immediately show the controller's current table
        // (values) and this machine's saved names — merging here is what keeps
        // the grid populated after the rebuild (no new snapshot arrives from
        // changing a setting; only an app restart used to re-merge via ctor).
        var visibleTools = ReadVisibleTools();
        if (visibleTools != _visibleTools)
        {
            _visibleTools = visibleTools;
            OnPropertyChanged(nameof(VisibleTools));
            BuildTools();
            MergeToolState(_grbl.WorkParameters, _grbl.ControllerKey ?? "unknown");
        }
    }

    private int ReadVisibleTools() =>
        Math.Clamp(_settingsService.Load().VisibleTools, 0, 64);

    public void Save()
    {
        _saving = true;
        try
        {
            OffsetsConfigStore.Save(_profile, Config);
            _settingsService.Save(_profile);
        }
        finally
        {
            _saving = false;
        }
    }

    /// <summary>Applies a new engine snapshot: resize rows, then refresh values for
    /// the G10-writable coordinate systems the controller reports.</summary>
    private void ApplySnapshot(WorkParameters parameters)
    {
        var caps = _grbl.Capabilities;
        // The controller's $I report is authoritative for its axis count. Deriving
        // purely from snapshot lengths would let a stale or over-wide snapshot
        // (e.g. the demo's resizable work parameters) inflate the grids beyond
        // what the machine actually has; an unknown (not-yet-handshaken)
        // controller falls back to the snapshot so the editor sizes itself
        // before connect.
        var snapshotAxes = parameters.CoordinateSystems
            .Select(x => x.Values.Length).DefaultIfEmpty(_axisCount).Max();
        var axisCount = Math.Clamp(caps.IsKnown && caps.NumAxes > 0 ? caps.NumAxes : snapshotAxes, 1, AxisLetters.Length);
        // Controller identity for scoping editor-local metadata (tool names):
        // the $I firmware fingerprint (no endpoint — clones share names, moving
        // ports keeps them), so a different machine never inherits this one's.
        var controllerKey = _grbl.ControllerKey ?? "unknown";
        var effectiveToolCount = EffectiveToolCount(caps);
        if (axisCount != _axisCount || Rows.Count == 0)
        {
            _axisCount = axisCount;
            BuildRows(parameters);
            OnPropertyChanged(nameof(AxisNames));
            OnPropertyChanged(nameof(ToolColumnHeaders));
            OnPropertyChanged(nameof(OffsetCodes));
        }
        else if (controllerKey != _controllerKey || Tools.Count != effectiveToolCount)
        {
            // A different controller connected (or the visible-tool count changed):
            // rebuild the tool rows so the grid matches the current machine's
            // table instead of keeping stale rows and names from the previous one.
            BuildTools();
        }
        _controllerKey = controllerKey;
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
        }            // Follow the controller's active tool, but never yank the selection away
            // from a row the user is mid-edit on. Evaluated against the pre-refresh
            // state, so a dirty row keeps selection even when this snapshot also
            // refreshes its values.
            if (parameters.ActiveTool > 0 && !Tools.Any(tool => tool.IsDirty) && SelectedToolId != parameters.ActiveTool)
                SelectedToolId = parameters.ActiveTool;

            // Follow the controller's ACTIVE coordinate system (from the $G modal
            // dump at connect, or the demo's selection). Report-driven — must never
            // re-send the activation (that would echo forever).
            FollowControllerActiveSystem(parameters.ActiveCoordinateSystem);

        MergeToolState(parameters, controllerKey);
    }

    /// <summary>Merges the controller snapshot (axis values, radius) and the
    /// per-controller name store into the tool rows, and records the originals
    /// so dirty-tracking compares against what the board has. Runs after EVERY
    /// rebuild (controller switch, visible-count change) and on every snapshot —
    /// a rebuild must never leave freshly built rows blank: BuildTools() only
    /// creates zeroed slots, so callers merge the current state right after.
    /// Slots the snapshot doesn't report stay zero (merged from the name store
    /// all the same — names are metadata, not board data).</summary>
    private void MergeToolState(WorkParameters parameters, string controllerKey)
    {
        var storedNames = Config.ToolNamesFor(controllerKey);
        foreach (var tool in Tools)
        {
            var source = parameters.Tools.FirstOrDefault(t => t.Id == tool.Id);
            if (source is not null)
            {
                for (var i = 0; i < _axisCount && i < source.Offsets.Length; i++)
                    tool.SetOffsetAt(i, Format(source.Offsets[i]));
                tool.Radius = Format(source.Radius);
            }
            // Local name metadata (grblHAL has no name field), scoped to the
            // current controller: merge the stored name into rows that don't
            // already carry one — never clobber a name the user is typing.
            if (string.IsNullOrEmpty(tool.Name) && storedNames.TryGetValue(tool.Id, out var name))
                tool.Name = name;
            // Zero-filled originals for slots the snapshot doesn't report, so a
            // row that was never touched compares clean (a bare `new string[n]`
            // would be null-filled and every absent slot would read as dirty).
            var originals = Enumerable.Repeat("0", _axisCount).ToArray();
            if (source is not null)
                for (var i = 0; i < _axisCount && i < source.Offsets.Length; i++)
                    originals[i] = Format(source.Offsets[i]);
            tool.SetOriginals(originals, source is null ? "0" : Format(source.Radius), tool.Name);
        }
    }

    private void ApplyState(MachineState state)
    {
        if (state is null)
            return;
        _lastState = state;
        var alarm = state.Status?.Contains("Alarm", StringComparison.OrdinalIgnoreCase) == true;
        if (alarm != _isAlarm)
        {
            _isAlarm = alarm;
            OnPropertyChanged(nameof(IsReady));
        }
        // grblHAL reports WCS: in every status report — the live active system.
        FollowControllerActiveSystem(state.ActiveCoordinateSystem);
    }

    /// <summary>Moves the editor selection to the controller's active coordinate
    /// system (report-driven, i.e. WITHOUT re-sending the activation — a send would
    /// echo back through the same path). Ignores unknown/empty values and systems
    /// not present in the editor.</summary>
    private void FollowControllerActiveSystem(string? code)
    {
        if (string.IsNullOrEmpty(code) || code == _selectedOffset)
            return;
        if (Rows.All(row => row.Code != code))
            return;
        if (SetField(ref _selectedOffset, code))
            OnPropertyChanged(nameof(SelectedRow));
    }

    public void Refresh()
    {
        ApplySnapshot(_grbl.WorkParameters);
        Status = LocText.Resolve("module.offsets-table.refreshed", "Offsets refreshed");
    }

    private async Task SetAxisAsync(object? parameter)
    {
        if (parameter is not string letter || !TryAxis(letter, out var index) || SelectedRow is null) return;
        await SetCoordinateValuesAsync(SelectedRow, index);
    }

    private async Task SetCoordinateValuesAsync(EditableOffsetRow row, int? onlyAxis = null)
    {
        if (!WorkParameters.IsG10Writable(row.Code)) { row.Error = LocText.Resolve("module.offsets-table.notWritable", "Only G54..G59.3 are writable"); Status = row.Error; return; }
        if (!TryValues(row.Values, out var values, out var error)) { row.Error = error; Status = error!; return; }
        var words = string.Concat(values.Select((value, index) => onlyAxis is null || index == onlyAxis ? $" {AxisLetters[index]}{Format(value)}" : string.Empty));
        if (words.Length == 0) return;
        var command = $"G10 L2 P{WorkParameters.CoordinateSystemNumber(row.Code)} {words}";
        try
        {
            await _grbl.SendAsync(command);
            row.Error = null;
            Status = LocText.Format("module.offsets-table.set", "Set {0}", row.Code);
            await SafeRefreshAsync();
        }
        catch (Exception ex) { Status = LocText.Format("module.offsets-table.setFailed", "Set {0} failed: {1}", row.Code, ex.Message); }
    }

    private void GetCurrentPosition()
    {
        if (!IsReady) { Status = LocText.Resolve("module.offsets-table.inAlarm", "Controller is in alarm — unlock or home first"); return; }
        var row = SelectedRow;
        if (row is null) return;
        var positions = _lastState.Positions ?? [];
        for (var i = 0; i < _axisCount && i < positions.Length; i++) row.SetValueAt(i, Format(positions[i]));
        Status = LocText.Format("module.offsets-table.copied", "Current position copied to {0}", SelectedOffset);
    }

    /// <summary>Touch-off: copies the current machine position (work position as a
    /// fallback when the controller didn't report MPos) into the tool's axis cells.
    /// Values are only filled locally — the user reviews them and "Set all"
    /// writes the row, matching the editor's single-write philosophy.</summary>
    public void TouchOff(int toolId)
    {
        if (!IsReady) { Status = LocText.Resolve("module.offsets-table.inAlarm", "Controller is in alarm — unlock or home first"); return; }
        var tool = Tools.FirstOrDefault(t => t.Id == toolId);
        if (tool is null || tool.Id == 0)
        {
            Status = LocText.Resolve("module.offsets-table.touchOffNone", "Touch-off applies to a numbered tool only");
            return;
        }
        SelectedToolId = toolId;
        var machine = _lastState.MachinePositions;
        var positions = machine ?? _lastState.Positions ?? [];
        for (var i = 0; i < _axisCount && i < positions.Length; i++)
            tool.SetOffsetAt(i, Format(positions[i]));
        Status = machine is null
            ? LocText.Format("module.offsets-table.touchOffCopiedWork", "Work position copied to T{0} (machine position unavailable) — review and press Set all", toolId)
            : LocText.Format("module.offsets-table.touchOffCopied", "Machine position copied to T{0} — review and press Set all", toolId);
    }

    /// <summary>Zeroes every tool's axis cells and radius in the editor; "Set all"
    /// writes them (mirrors the offsets table's Clear all / Set all pair).</summary>
    private void ClearTools()
    {
        if (!IsReady) { Status = LocText.Resolve("module.offsets-table.inAlarm", "Controller is in alarm — unlock or home first"); return; }
        foreach (var tool in Tools)
        {
            for (var i = 0; i < tool.Offsets.Count; i++)
                tool.SetOffsetAt(i, "0");
            tool.Radius = "0";
        }
        Status = LocText.Resolve("module.offsets-table.toolsCleared", "All tools cleared in editor; press Set all to write them");
    }

    private void ClearAll()
    {
        if (!IsReady) { Status = LocText.Resolve("module.offsets-table.inAlarm", "Controller is in alarm — unlock or home first"); return; }
        foreach (var row in Rows)
            for (var i = 0; i < row.Values.Count; i++) row.SetValueAt(i, "0");
        Status = LocText.Resolve("module.offsets-table.cleared", "All offsets cleared in editor; press Set all to write them");
    }

    public Task SetAllAsync() => SetAllCoreAsync();

    private async Task SetAllCoreAsync()
    {
        if (!IsReady) { Status = LocText.Resolve("module.offsets-table.inAlarm", "Controller is in alarm — unlock or home first"); return; }
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
        Status = failures == 0
            ? LocText.Format("module.offsets-table.saved", "Saved {0} offsets", sent)
            : LocText.Format("module.offsets-table.savedWithFailures", "Saved {0} offsets; {1} failed", sent, failures);
    }

    /// <summary>Writes only the tools the user actually changed (offsets or radius),
    /// so a single button refreshes the table without replaying unchanged rows.</summary>
    public Task SaveToolTableAsync() => SaveToolTableCoreAsync();

    private async Task SaveToolTableCoreAsync()
    {
        if (!IsReady) { Status = LocText.Resolve("module.offsets-table.inAlarm", "Controller is in alarm — unlock or home first"); return; }
        if (!ControllerSupportsToolTable)
        {
            Status = LocText.Resolve("module.offsets-table.noToolTable", "This controller has no G10 L1 tool table (classic grbl 1.1)");
            return;
        }

        var changed = new List<(EditableToolRow Tool, string Command)>();
        var failures = 0;
        foreach (var tool in Tools.Where(t => t.IsDirty))
        {
            if (!TryValues(tool.Offsets, out var values, out var error) ||
                !double.TryParse(tool.Radius, NumberStyles.Float, CultureInfo.InvariantCulture, out var radius))
            {
                tool.Error = LocText.Resolve("module.offsets-table.invalidToolValue", "Invalid tool-table value");
                failures++;
                continue;
            }
            var words = string.Concat(values.Select((value, index) => $"{AxisLetters[index]}{Format(value)}"));
            changed.Add((tool, $"G10L1P{tool.Id}{words}R{Format(radius)}"));
        }

        if (changed.Count == 0)
        {
            Status = LocText.Resolve("module.offsets-table.noToolChanges", "No tool changes to save");
            return;
        }

        var sent = 0;
        foreach (var (tool, command) in changed)
        {
            try
            {
                await _grbl.SendAsync(command);
                tool.Error = null;
                sent++;
            }
            catch (Exception ex)
            {
                tool.Error = ex.Message;
                failures++;
            }
        }

        // Names are editor-local metadata scoped to the current controller (grbl
        // has no G10 name word): persist them alongside the writes so they
        // survive reconnects and refreshes — for THIS machine only (namespaced
        // by the endpoint + firmware fingerprint, so another board on the same
        // COM port has its own set).
        var namesChanged = false;
        var storedNames = Config.ToolNamesFor(_controllerKey);
        foreach (var tool in Tools)
        {
            if (string.IsNullOrEmpty(tool.Name))
                namesChanged |= storedNames.Remove(tool.Id);
            else if (!storedNames.TryGetValue(tool.Id, out var existing) || existing != tool.Name)
            {
                storedNames[tool.Id] = tool.Name;
                namesChanged = true;
            }
        }
        if (namesChanged)
            Save();

        await SafeRefreshAsync();
        Status = failures == 0
            ? LocText.Format("module.offsets-table.savedTools", "Saved {0} changed tools", sent)
            : LocText.Format("module.offsets-table.savedToolFailures", "Saved {0} tools; {1} failed", sent, failures);
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
        for (var i = 0; i < values.Count; i++) if (!double.TryParse(values[i], NumberStyles.Float, CultureInfo.InvariantCulture, out result[i])) { error = LocText.Format("module.offsets-table.invalidAxisValue", "Invalid numeric value at axis {0}", i + 1); return false; }
        error = null; return true;
    }
    private void BuildRows(WorkParameters parameters)
    {
        Rows.Clear();
        // Offer exactly what the controller reports, clamped to the systems the
        // firmware actually supports (classic grbl: G54..G59 only). Never fabricate
        // rows the board rejects — a failed save of an unsupported system is what
        // produced error 29 / the vanishing G59.x rows on the mega5x fork.
        var reported = parameters.CoordinateSystems
            .Where(x => WorkParameters.IsG10Writable(x.Code) && SupportedSystems.Contains(x.Code))
            .Select(x => x.Code)
            .Distinct()
            .OrderBy(WorkParameters.CoordinateSystemNumber)
            .ToList();
        var codes = reported.Count > 0 ? reported : SupportedSystems.ToList();
        foreach (var code in codes)
            Rows.Add(new EditableOffsetRow(code, _axisCount));
        Tools.Clear();
        BuildTools();
        // Fallback keeps the selection valid without re-sending the activation
        // (row rebuilds are programmatic, not user selections).
        if (SelectedRow is null && SetField(ref _selectedOffset, Rows[0].Code))
            OnPropertyChanged(nameof(SelectedRow));
    }

    /// <summary>Tool rows to render: the mode's VisibleTools, clamped to the
    /// connected controller's tool slots (grblHAL NumTools). An unknown (not
    /// handshaken) controller doesn't clamp — the section is hidden until a
    /// tool-capable board connects anyway.</summary>
    private int EffectiveToolCount(ControllerCapabilities? caps = null)
    {
        caps ??= _grbl.Capabilities;
        return caps.IsKnown && caps.NumTools > 0 ? Math.Min(_visibleTools, caps.NumTools) : _visibleTools;
    }

    /// <summary>Rebuilds the tool rows as numbered slots T1..N (T0 "None" is a
    /// no-op — no offset written to it is ever applied — so the grid starts at
    /// T1; the probe picker keeps its own T0 option). Values merge from the
    /// snapshot in ApplySnapshot; slots the controller doesn't report stay zero.
    /// Rebuilt on connect/controller switch or when the visible-tool count
    /// changes. The selector preselects the first row (T1) and the combo's
    /// SelectedItem is re-raised so a rebuild can't leave it pointing at a
    /// stale row object (blank selector on first open).</summary>
    private void BuildTools()
    {
        Tools.Clear();
        var count = EffectiveToolCount();
        for (var id = 1; id <= count; id++)
            Tools.Add(new EditableToolRow(id, _axisCount));
        if (Tools.Count == 0 || Tools.All(t => t.Id != SelectedToolId))
            SelectedToolId = Tools.FirstOrDefault()?.Id ?? 0;
        OnPropertyChanged(nameof(SelectedToolRow));
    }
    private static string Format(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
}