
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

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using CNC.Core.Abstractions;
using CNC.Core.Services;
using SenderNG.Core.Commands;
using SenderNG.Core.Localization;
using SenderNG.Core.Modules;
using SenderNG.Core.Services;

namespace CNC.Core.ViewModels;

/* ---------------------------------------------------------------------------
 * Tree model
 * ------------------------------------------------------------------------- */

/// <summary>A group node in the App Settings tree. The TreeViewItem binds IsExpanded
/// two-way so expansion survives (mirrors GrblSettings' group node).</summary>
public sealed class AppSettingsGroupItem : ViewModelBase
{
    private bool _isExpanded;
    private string _name;

    public string Name
    {
        get => _name;
        internal set => SetField(ref _name, value);
    }
    public ObservableCollection<object> Children { get; } = new();

    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetField(ref _isExpanded, value);
    }

    public AppSettingsGroupItem(string name) => _name = name;

    internal void Relocalize(string name, string description)
    {
        _ = description;
        Name = name;
    }
}

/// <summary>Base for a selectable leaf in the App Settings tree.</summary>
public abstract class AppSettingsLeaf : ViewModelBase
{
    /// <summary>Leaves have no children, so they can never be expanded, but the tree style
    /// binds TreeViewItem.IsExpanded two-way against every item's DataContext. This no-op
    /// accessor keeps that binding valid for leaf nodes (no binding error on materialize).</summary>
    public bool IsExpanded
    {
        get => false;
        set { }
    }

    public string Id { get; }
    public string Name { get; protected set; }
    public string Description { get; protected set; }

    public bool HasDescription => !string.IsNullOrWhiteSpace(Description);

    protected AppSettingsLeaf(string id, string name, string description)
    {
        Id = id;
        Name = name;
        Description = description;
    }

    /// <summary>Re-name/re-summarize in the current locale (raises change notifications).</summary>
    internal void Relocalize(string name, string description)
    {
        var changed = !string.Equals(Name, name, StringComparison.Ordinal)
                    || !string.Equals(Description, description, StringComparison.Ordinal);
        Name = name;
        Description = description;
        if (changed)
        {
            OnPropertyChanged(nameof(Name));
            OnPropertyChanged(nameof(Description));
            OnPropertyChanged(nameof(HasDescription));
        }
    }
}

/// <summary>Global &gt; Language leaf: locale dropdown that persists and re-applies at runtime.
/// The selection is backed by the live current locale (a provider delegate into the VM), so
/// the dropdown always reflects the active language whenever the leaf is re-selected — a
/// locally cached copy could go stale and show a stale/blank value on focus loss.</summary>
public sealed class LanguageSettingsLeaf : AppSettingsLeaf
{
    private readonly Func<string> _getCurrentLocale;
    private readonly Action<string> _setLocale;

    public LanguageSettingsLeaf(Func<string> getCurrentLocale, IReadOnlyList<string> availableLocales,
        Action<string> setLocale, string name, string description)
        : base("language", name, description)
    {
        _getCurrentLocale = getCurrentLocale;
        AvailableLocales = availableLocales;
        _setLocale = setLocale;
    }

    public IReadOnlyList<string> AvailableLocales { get; }

    public string SelectedLocale
    {
        get => _getCurrentLocale();
        set
        {
            // Avalonia detaches invisible controls from the visual tree and can push
            // null through a TwoWay SelectedItem on detach (leaf switch hides this
            // pane). Never treat that as a locale change — it would blank the current
            // locale and the dropdown would stay empty on re-select.
            if (string.IsNullOrEmpty(value)
                || string.Equals(_getCurrentLocale(), value, StringComparison.OrdinalIgnoreCase))
                return;
            _setLocale(value);
            OnPropertyChanged();
        }
    }
}

/// <summary>One editable numeric field exposed by a module (e.g. Jog distances).</summary>
public sealed class NumericSettingEntry : ViewModelBase
{
    private readonly Func<double> _get;
    private readonly Action<double> _set;
    private double _value;

    public NumericSettingEntry(string label, Func<double> get, Action<double> set)
        : this(label, null, get, set) { }

    public NumericSettingEntry(string label, string? key, Func<double> get, Action<double> set)
    {
        ArgumentNullException.ThrowIfNull(get);
        ArgumentNullException.ThrowIfNull(set);
        _label = label;
        Key = key;
        _get = get;
        _set = set;
        _value = get();
    }

    private string _label;

    /// <summary>Optional localization key; re-resolved on Relocalize().</summary>
    public string? Key { get; }

    public string Label
    {
        get => _label;
        internal set
        {
            if (string.Equals(_label, value, StringComparison.Ordinal))
                return;
            _label = value;
            OnPropertyChanged(nameof(Label));
        }
    }

    public double Value
    {
        get => _value;
        set
        {
            if (_value == value)
                return;
            _value = value;
            OnPropertyChanged(nameof(Value));
            _set(value);
        }
    }

    public void Refresh()
    {
        var v = _get();
        if (_value == v)
            return;
        _value = v;
        OnPropertyChanged(nameof(Value));
    }
}

/// <summary>One editable boolean field exposed by a module (e.g. Jog diagonals).</summary>
public sealed class BoolSettingEntry : ViewModelBase
{
    private readonly Func<bool> _get;
    private readonly Action<bool> _set;
    private bool _value;

    public BoolSettingEntry(string label, Func<bool> get, Action<bool> set)
        : this(label, null, get, set) { }

    public BoolSettingEntry(string label, string? key, Func<bool> get, Action<bool> set)
    {
        ArgumentNullException.ThrowIfNull(get);
        ArgumentNullException.ThrowIfNull(set);
        _label = label;
        Key = key;
        _get = get;
        _set = set;
        _value = get();
    }

    private string _label;

    /// <summary>Optional localization key; re-resolved on Relocalize().</summary>
    public string? Key { get; }

    public string Label
    {
        get => _label;
        internal set
        {
            if (string.Equals(_label, value, StringComparison.Ordinal))
                return;
            _label = value;
            OnPropertyChanged(nameof(Label));
        }
    }

    public bool Value
    {
        get => _value;
        set
        {
            if (_value == value)
                return;
            _value = value;
            OnPropertyChanged(nameof(Value));
            _set(value);
        }
    }

    public void Refresh()
    {
        var v = _get();
        if (_value == v)
            return;
        _value = v;
        OnPropertyChanged(nameof(Value));
    }
}

/// <summary>Modules &gt; module leaf: localizable name + optional description, a visibility toggle
/// (hideable modules only), and either a module-provided settings view or generic fields.</summary>
public sealed class ModuleSettingsLeaf : AppSettingsLeaf
{
    private readonly System.Action<string, bool> _visibilityChanged;
    private readonly Func<string, string, string> _resolveSettingKey;
    private bool _isVisible;

    public ModuleSettingsLeaf(
        string id,
        string name,
        string description,
        bool isVisible,
        bool canHide,
        System.Action<string, bool> visibilityChanged,
        object? settingsView,
        IEnumerable<NumericSettingEntry>? fields,
        IEnumerable<BoolSettingEntry>? boolFields,
        Func<string, string, string> resolveSettingKey)
        : base(id, name, description)
    {
        _resolveSettingKey = resolveSettingKey;
        CanHide = canHide;
        _isVisible = canHide ? isVisible : true; // fixed modules are always visible
        _visibilityChanged = visibilityChanged;
        SettingsView = settingsView;
        if (fields is not null)
            foreach (var f in fields)
                Fields.Add(f);
        if (boolFields is not null)
            foreach (var f in boolFields)
                BoolFields.Add(f);
    }

    public bool CanHide { get; }

    /// <summary>Fixed modules (DRO, Program, Console, Jog) render a plain-name header (no checkbox).</summary>
    public bool ShowNameOnly => !CanHide;

    /// <summary>Module-provided settings UI; when set it replaces the generic field lists.</summary>
    public object? SettingsView { get; }

    public bool ShowGenericFields => SettingsView is null;

    public bool ShowAlwaysVisibleCaption => ShowNameOnly && SettingsView is null;

    public ObservableCollection<NumericSettingEntry> Fields { get; } = new();
    public ObservableCollection<BoolSettingEntry> BoolFields { get; } = new();

    public void RefreshFields()
    {
        foreach (var f in Fields)
        {
            if (!string.IsNullOrWhiteSpace(f.Key))
                f.Label = _resolveSettingKey(f.Key, f.Label);
            f.Refresh();
        }
        foreach (var f in BoolFields)
        {
            if (!string.IsNullOrWhiteSpace(f.Key))
                f.Label = _resolveSettingKey(f.Key, f.Label);
            f.Refresh();
        }
    }

    public bool IsVisible
    {
        get => _isVisible;
        set
        {
            if (_isVisible == value)
                return;
            _isVisible = value;
            OnPropertyChanged(nameof(IsVisible));
            _visibilityChanged(Id, value);
        }
    }
}

/* ---------------------------------------------------------------------------
 * Profiles leaf — the profile manager (docs/settings-migration.md §7)
 * ------------------------------------------------------------------------- */

/// <summary>One row in the profile list: display name, machine-type badge, active marker.</summary>
public sealed class ProfileItem : ViewModelBase
{
    public string Id { get; }
    public string Name { get; }
    public string MachineTypeId { get; }

    private bool _isActive;
    public bool IsActive { get => _isActive; set => SetField(ref _isActive, value); }

    public ProfileItem(string id, string name, string machineTypeId)
    {
        Id = id;
        Name = name;
        MachineTypeId = machineTypeId;
    }
}

/// <summary>
/// Global &gt; Profiles leaf: one profile per machine with add / duplicate /
/// rename / remove / activate and the machine-type badge. All mutations go
/// through <see cref="SettingsService"/>; switching is gated by
/// <see cref="SetSwitchGuard"/> — the dialog host blocks it while the
/// controller is connected or a session is running.
/// </summary>
public sealed class ProfileSettingsLeaf : AppSettingsLeaf
{
    private readonly SettingsService _settings;
    private readonly Func<string, string, string> _resolve;
    private Func<bool>? _switchAllowed;
    private ProfileItem? _selected;
    private string _newName = string.Empty;
    private string _newType = MachineTypes.Mill;
    private string _renameName = string.Empty;
    private string _machineTypeDraft = MachineTypes.Mill;
    private bool _confirmRemove;
    private bool _confirmTypeChange;

    /// <summary>Raised when a profile became active (host rebuilds profile-scoped UI).</summary>
    public event Action<string>? Activated;

    /// <summary>Raised after any list-affecting mutation (add/duplicate/rename/remove/type).</summary>
    public event System.Action? ProfilesChanged;

    public ProfileSettingsLeaf(SettingsService settings, Func<string, string, string> resolve,
        string name, string description) : base("profiles", name, description)
    {
        _settings = settings;
        _resolve = resolve;
        WireCommands();
        Refresh();
    }

    public ObservableCollection<ProfileItem> Profiles { get; } = new();

    public IReadOnlyList<string> MachineTypeOptions { get; } = [MachineTypes.Mill, MachineTypes.Foam];

    public ProfileItem? SelectedProfile
    {
        get => _selected;
        set
        {
            if (SetField(ref _selected, value))
            {
                _renameName = value?.Name ?? string.Empty;
                _machineTypeDraft = value?.MachineTypeId ?? MachineTypes.Mill;
                _confirmRemove = false;
                _confirmTypeChange = false;
                OnPropertyChanged(nameof(RenameName));
                OnPropertyChanged(nameof(SelectedMachineType));
                RefreshCommands();
            }
        }
    }

    public string NewName
    {
        get => _newName;
        set { if (SetField(ref _newName, value)) RefreshCommands(); }
    }

    public string NewType
    {
        get => _newType;
        set { if (SetField(ref _newType, value)) RefreshCommands(); }
    }

    /// <summary>Display-name edit box; synced to the selection.</summary>
    public string RenameName
    {
        get => _renameName;
        set { if (SetField(ref _renameName, value)) RefreshCommands(); }
    }

    /// <summary>Machine-type draft for the selected profile (commit via <see cref="ApplyTypeCommand"/>).</summary>
    public string SelectedMachineType
    {
        get => _machineTypeDraft;
        set
        {
            if (SetField(ref _machineTypeDraft, value))
                RefreshCommands();
        }
    }

    public bool CanSwitchProfiles => _switchAllowed?.Invoke() != false;

    /// <summary>Inverse of <see cref="CanSwitchProfiles"/> — the caption shown when switching is blocked.</summary>
    public bool ShowSwitchBlocked => !CanSwitchProfiles;

    public string RemoveButtonText => _confirmRemove
        ? _resolve("appSettings.profiles.removeConfirm", "Remove again to confirm")
        : _resolve("appSettings.profiles.remove", "Remove");

    public string TypeButtonText => _confirmTypeChange
        ? _resolve("appSettings.profiles.typeConfirm", "Change again to confirm")
        : _resolve("appSettings.profiles.typeChange", "Change type");

    public string SwitchBlockedText => _resolve("appSettings.profiles.switchBlocked",
        "Profile switching is disabled while connected or a session is running.");

    public DelegateCommand AddCommand { get; private set; } = null!;
    public DelegateCommand DuplicateCommand { get; private set; } = null!;
    public DelegateCommand RenameCommand { get; private set; } = null!;
    public DelegateCommand RemoveCommand { get; private set; } = null!;
    public DelegateCommand ActivateCommand { get; private set; } = null!;
    public DelegateCommand ApplyTypeCommand { get; private set; } = null!;

    private void WireCommands()
    {
        AddCommand = new DelegateCommand(_ => Add(),
            _ => CanSwitchProfiles && !string.IsNullOrWhiteSpace(NewName));
        DuplicateCommand = new DelegateCommand(_ => Duplicate(),
            _ => CanSwitchProfiles && SelectedProfile is not null);
        RenameCommand = new DelegateCommand(_ => Rename(),
            _ => CanSwitchProfiles && SelectedProfile is not null
                && !string.IsNullOrWhiteSpace(RenameName) && RenameName != SelectedProfile.Name);
        RemoveCommand = new DelegateCommand(_ => Remove(),
            _ => CanSwitchProfiles && SelectedProfile is not null
                && !SelectedProfile.IsActive && Profiles.Count > 1);
        ActivateCommand = new DelegateCommand(_ => Activate(),
            _ => CanSwitchProfiles && SelectedProfile is not null && !SelectedProfile.IsActive);
        ApplyTypeCommand = new DelegateCommand(_ => ApplyType(),
            _ => CanSwitchProfiles && SelectedProfile is not null
                && SelectedMachineType != SelectedProfile.MachineTypeId);
    }

    private void Add()
    {
        var profile = _settings.Activate(_settings.CreateProfile(NewName.Trim(), NewType).Id);
        NewName = string.Empty;
        Refresh();
        Activated?.Invoke(profile.Id);
    }

    private void Duplicate()
    {
        var selected = SelectedProfile!;
        var name = string.IsNullOrWhiteSpace(NewName) ? $"{selected.Name} copy" : NewName.Trim();
        _settings.CreateProfile(name, selected.MachineTypeId, copyFromId: selected.Id);
        NewName = string.Empty;
        Refresh();
        ProfilesChanged?.Invoke();
    }

    private void Rename()
    {
        var selected = SelectedProfile!;
        _settings.RenameProfile(selected.Id, RenameName.Trim());
        Refresh();
        ProfilesChanged?.Invoke();
    }

    private void Remove()
    {
        if (!_confirmRemove)
        {
            _confirmRemove = true;
            RefreshCommands();
            return;
        }
        _confirmRemove = false;
        var selected = SelectedProfile!;
        _settings.DeleteProfile(selected.Id);
        Refresh();
        ProfilesChanged?.Invoke();
    }

    private void Activate()
    {
        var selected = SelectedProfile!;
        _settings.Activate(selected.Id);
        Refresh();
        Activated?.Invoke(selected.Id);
    }

    private void ApplyType()
    {
        if (!_confirmTypeChange)
        {
            _confirmTypeChange = true;
            RefreshCommands();
            return;
        }
        _confirmTypeChange = false;
        var selected = SelectedProfile!;
        _settings.SetProfileMachineType(selected.Id, SelectedMachineType);
        Refresh();
        ProfilesChanged?.Invoke();
    }

    /// <summary>Rebuilds the list from disk/registry and keeps the selection.</summary>
    public void Refresh()
    {
        var activeId = _settings.Global.ActiveProfileId;
        Profiles.Clear();
        foreach (var p in _settings.AllProfiles()
            .OrderByDescending(p => p.Id == activeId)
            .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase))
        {
            Profiles.Add(new ProfileItem(p.Id, p.Name, p.MachineTypeId) { IsActive = p.Id == activeId });
        }
        var keep = _selected is null ? null : Profiles.FirstOrDefault(p => p.Id == _selected.Id);
        SelectedProfile = keep ?? Profiles.FirstOrDefault();
        RefreshCommands();
    }

    /// <summary>Host-provided gate: null = switching allowed.</summary>
    public void SetSwitchGuard(Func<bool>? allowed)
    {
        _switchAllowed = allowed;
        RefreshCommands();
    }

    /// <summary>Re-resolve button labels after a locale change.</summary>
    public void RelocalizeButtons()
    {
        OnPropertyChanged(nameof(RemoveButtonText));
        OnPropertyChanged(nameof(TypeButtonText));
        OnPropertyChanged(nameof(SwitchBlockedText));
        OnPropertyChanged(nameof(CanSwitchProfiles));
        OnPropertyChanged(nameof(ShowSwitchBlocked));
    }

    private void RefreshCommands()
    {
        AddCommand?.RaiseCanExecuteChanged();
        DuplicateCommand?.RaiseCanExecuteChanged();
        RenameCommand?.RaiseCanExecuteChanged();
        RemoveCommand?.RaiseCanExecuteChanged();
        ActivateCommand?.RaiseCanExecuteChanged();
        ApplyTypeCommand?.RaiseCanExecuteChanged();
        OnPropertyChanged(nameof(RemoveButtonText));
        OnPropertyChanged(nameof(TypeButtonText));
        OnPropertyChanged(nameof(SwitchBlockedText));
        OnPropertyChanged(nameof(CanSwitchProfiles));
        OnPropertyChanged(nameof(ShowSwitchBlocked));
    }
}

/* ---------------------------------------------------------------------------
 * Hotkeys leaf — global keymap editor
 * ------------------------------------------------------------------------- */

/// <summary>One action row in the hotkeys editor.</summary>
public sealed class HotkeyEditorRow : ViewModelBase
{
    private readonly HotkeysSettingsLeaf _owner;
    private string? _gesture;
    private string? _message;
    private bool _isRecording;
    private bool _isVisible = true;

    internal HotkeyEditorRow(HotkeysSettingsLeaf owner, IHotkeyAction action)
    {
        _owner = owner;
        Action = action;
        ClearCommand = new DelegateCommand(_ => _owner.Clear(this));
        RestoreCommand = new DelegateCommand(_ => _owner.RestoreOne(this));
        RecordCommand = new DelegateCommand(_ => _owner.BeginRecord(this));
    }

    public IHotkeyAction Action { get; }
    public string ActionId => Action.ActionId;
    public string DisplayName => _owner.LocalizeAction(Action);

    public string? DefaultGesture { get; internal set; }

    public string? Gesture
    {
        get => _gesture;
        internal set
        {
            if (SetField(ref _gesture, value))
                RaiseComputed();
        }
    }

    public string GestureText => _gesture ?? _owner.Resolve("appSettings.hotkeys.unbound", "(none)");
    public bool IsUnbound => _gesture is null;
    public bool HasDefault => !string.IsNullOrEmpty(DefaultGesture);
    public bool CanClear => _gesture is not null;
    public bool CanRestore => HasDefault && !string.Equals(_gesture, DefaultGesture, StringComparison.Ordinal);

    public bool IsRecording
    {
        get => _isRecording;
        internal set
        {
            if (SetField(ref _isRecording, value))
                OnPropertyChanged(nameof(RecordText));
        }
    }

    public string RecordText => IsRecording
        ? _owner.Resolve("appSettings.hotkeys.pressShortcut", "Press shortcut…")
        : GestureText;

    public string? Message
    {
        get => _message;
        internal set
        {
            if (SetField(ref _message, value))
                OnPropertyChanged(nameof(HasMessage));
        }
    }

    public bool HasMessage => !string.IsNullOrWhiteSpace(_message);

    public bool IsVisible
    {
        get => _isVisible;
        internal set => SetField(ref _isVisible, value);
    }

    public DelegateCommand ClearCommand { get; }
    public DelegateCommand RestoreCommand { get; }
    public DelegateCommand RecordCommand { get; }

    internal void Refresh()
    {
        OnPropertyChanged(nameof(DisplayName));
        RaiseComputed();
    }

    private void RaiseComputed()
    {
        OnPropertyChanged(nameof(GestureText));
        OnPropertyChanged(nameof(IsUnbound));
        OnPropertyChanged(nameof(CanClear));
        OnPropertyChanged(nameof(CanRestore));
        OnPropertyChanged(nameof(RecordText));
    }
}

/// <summary>A scope section (Global, or one contextual module) of hotkey rows.</summary>
public sealed class HotkeyGroup : ViewModelBase
{
    private bool _isVisible = true;

    public HotkeyGroup(string name) => Name = name;

    public string Name { get; }
    public ObservableCollection<HotkeyEditorRow> Rows { get; } = new();

    public bool IsVisible
    {
        get => _isVisible;
        internal set => SetField(ref _isVisible, value);
    }
}

/// <summary>Global &gt; Hotkeys leaf: remap every registered action, grouped by scope.</summary>
public sealed class HotkeysSettingsLeaf : AppSettingsLeaf
{
    private readonly HotkeyService _service;
    private readonly Func<string, string, string> _resolve;
    private readonly Dictionary<string, string> _moduleNames;
    private HotkeyEditorRow? _recordingRow;
    private string _searchText = string.Empty;

    public HotkeysSettingsLeaf(
        HotkeyService service,
        Func<string, string, string> resolve,
        IEnumerable<IModule> modules,
        string name,
        string description)
        : base("hotkeys", name, description)
    {
        _service = service;
        _resolve = resolve;
        _moduleNames = modules.ToDictionary(
            m => m.Id,
            m => resolve($"module.{m.Id}.name", m.Name));
        RestoreAllCommand = new DelegateCommand(_ => _service.ResetAll());
        Build();
        _service.Changed += (_, _) => RefreshAll();
    }

    public ObservableCollection<HotkeyGroup> Groups { get; } = new();

    public DelegateCommand RestoreAllCommand { get; }

    public bool IsRecording => _recordingRow is not null;

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetField(ref _searchText, value))
                ApplyFilter();
        }
    }

    internal string Resolve(string key, string fallback) => _resolve(key, fallback);

    internal string LocalizeAction(IHotkeyAction action)
        => string.IsNullOrWhiteSpace(action.LocaleKey)
            ? action.DisplayName
            : Resolve(action.LocaleKey!, action.DisplayName);

    public void BeginRecord(HotkeyEditorRow row)
    {
        if (_recordingRow is not null && !ReferenceEquals(_recordingRow, row))
            _recordingRow.IsRecording = false;
        _recordingRow = row;
        row.IsRecording = true;
        row.Message = null;
        OnPropertyChanged(nameof(IsRecording));
    }

    public void CancelRecord()
    {
        if (_recordingRow is null)
            return;
        _recordingRow.IsRecording = false;
        _recordingRow = null;
        OnPropertyChanged(nameof(IsRecording));
    }

    public void ReportModifierOnly()
    {
        if (_recordingRow is null)
            return;
        _recordingRow.Message = Resolve("appSettings.hotkeys.addKeyHint", "Add a key");
    }

    public void ReportInvalid()
    {
        if (_recordingRow is null)
            return;
        _recordingRow.Message = Resolve("appSettings.hotkeys.invalid", "Unsupported shortcut");
    }

    /// <summary>Commit a gesture captured by the host recorder. Conflicts inside the
    /// same scope are warning-and-unbind-the-displaced (cross-scope is legal).</summary>
    public void ApplyRecordedGesture(string gesture)
    {
        var row = _recordingRow;
        if (row is null)
            return;

        row.IsRecording = false;
        _recordingRow = null;
        OnPropertyChanged(nameof(IsRecording));

        var conflictId = _service.FindConflict(gesture, row.Action.Scope, row.Action.ModuleId, row.ActionId);
        if (conflictId is not null)
        {
            var other = _service.Find(conflictId);
            _service.ClearGesture(conflictId);
            var otherName = other is null ? conflictId : LocalizeAction(other.Action);
            row.Message = Resolve("appSettings.hotkeys.conflict", "Used by {0} — unbound there")
                .Replace("{0}", otherName);
        }
        else
        {
            row.Message = null;
        }

        _service.SetGesture(row.ActionId, gesture);
    }

    public void Clear(HotkeyEditorRow row) => _service.ClearGesture(row.ActionId);

    public void RestoreOne(HotkeyEditorRow row) => _service.ResetOne(row.ActionId);

    /// <summary>Re-resolve localized names/labels after a locale switch.</summary>
    public void RelocalizeAll()
    {
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Description));
        foreach (var group in Groups)
            foreach (var row in group.Rows)
                row.Refresh();
    }

    private void Build()
    {
        Groups.Clear();

        var global = new HotkeyGroup("Global");
        Groups.Add(global);
        var byModule = new Dictionary<string, HotkeyGroup>(StringComparer.Ordinal);

        foreach (var binding in _service.Bindings)
        {
            HotkeyGroup group;
            if (binding.Action.Scope == HotkeyScope.Module && binding.Action.ModuleId is { } moduleId)
            {
                if (!byModule.TryGetValue(moduleId, out var existing))
                {
                    existing = new HotkeyGroup(_moduleNames.GetValueOrDefault(moduleId, moduleId));
                    byModule[moduleId] = existing;
                    Groups.Add(existing);
                }
                group = existing;
            }
            else
            {
                group = global;
            }

            var row = new HotkeyEditorRow(this, binding.Action)
            {
                DefaultGesture = _service.DefaultGestureFor(binding.ActionId),
                Gesture = _service.GestureFor(binding.ActionId),
            };
            group.Rows.Add(row);
        }

        ApplyFilter();
    }

    private void RefreshAll()
    {
        foreach (var group in Groups)
            foreach (var row in group.Rows)
            {
                row.DefaultGesture = _service.DefaultGestureFor(row.ActionId);
                row.Gesture = _service.GestureFor(row.ActionId);
                row.Refresh();
            }
    }

    private void ApplyFilter()
    {
        var query = _searchText.Trim();
        foreach (var group in Groups)
        {
            foreach (var row in group.Rows)
                row.IsVisible = query.Length == 0
                    || row.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase);
            group.IsVisible = group.Rows.Any(r => r.IsVisible);
        }
    }
}

/* ---------------------------------------------------------------------------
 * View model
 * ------------------------------------------------------------------------- */

public class AppSettingsViewModel : ViewModelBase, IClosableDialog
{
    private MachineProfile _profile;
    private readonly GlobalSettings _global;
    private readonly SettingsService _settingsService;
    private readonly LocalizationService _localization;
    private readonly Dictionary<string, bool> _defaults = new();
    private readonly Dictionary<string, IModule> _moduleById;
    private readonly List<AppSettingsLeaf> _globalLeaves = new();
    private ProfileSettingsLeaf? _profilesLeaf;
    private readonly HotkeyService? _hotkeys;
    private HotkeysSettingsLeaf? _hotkeysLeaf;
    private Func<bool>? _profileSwitchAllowed;
    private bool _preselectProfiles;
    private bool _updatingSections;
    private string _currentLanguage;

    public event EventHandler<bool>? CloseRequested;

    public AppSettingsViewModel(
        SettingsService settingsService,
        IEnumerable<IModule> modules,
        LocalizationService? localization = null,
        Func<bool>? profileSwitchAllowed = null,
        HotkeyService? hotkeys = null)
    {
        ArgumentNullException.ThrowIfNull(settingsService);
        ArgumentNullException.ThrowIfNull(modules);

        _settingsService = settingsService;
        _localization = localization ?? DefaultLocalization();
        _profile = settingsService.Load();
        _global = settingsService.Global;
        _currentLanguage = string.IsNullOrWhiteSpace(_global.Language) ? "en" : _global.Language;
        if (!_localization.AvailableLocales.Contains(_currentLanguage, StringComparer.OrdinalIgnoreCase))
            _currentLanguage = "en";

        _moduleById = modules.ToDictionary(m => m.Id);
        _profileSwitchAllowed = profileSwitchAllowed;
        _hotkeys = hotkeys;
        BuildModuleLeaves(modules);
        BuildTree();

        _localization.Changed += OnLocaleChanged;
        CloseCommand = new DelegateCommand(_ => RequestClose(true));
    }

    /// <summary>
    /// Host-provided gate for profile switching (blocked while the controller is
    /// connected or a session runs). Null = allowed. Applied live, so the dialog
    /// honors a connect that happens while it is open.
    /// </summary>
    public Func<bool>? ProfileSwitchAllowed
    {
        get => _profileSwitchAllowed;
        set
        {
            _profileSwitchAllowed = value;
            _profilesLeaf?.SetSwitchGuard(value);
        }
    }

    /// <summary>When set before the dialog opens, the Profiles leaf is selected (Machine settings menu entry).</summary>
    public bool PreselectProfiles
    {
        get => _preselectProfiles;
        set
        {
            if (SetField(ref _preselectProfiles, value) && value && _profilesLeaf is not null)
                SelectedNode = _profilesLeaf;
        }
    }

    private static LocalizationService DefaultLocalization() =>
        LocalizationService.LoadFromDirectory(Path.Combine(AppContext.BaseDirectory, "Locale"));

    private void BuildModuleLeaves(IEnumerable<IModule> modules)
    {
        foreach (var module in modules.OrderBy(m => m.Order))
        {
            _defaults[module.Id] = module.IsVisibleByDefault;
            IEnumerable<NumericSettingEntry>? fields = null;
            IEnumerable<BoolSettingEntry>? boolFields = null;
            object? settingsView = null;
            if (module is IModuleSettingsProvider provider)
            {
                settingsView = provider.CreateSettingsView();
                if (settingsView is null)
                {
                    fields = provider.GetSettingFields()
                        .Select(f => new NumericSettingEntry(
                            ResolveSettingKey(f.Key, f.Label), f.Key, f.Get, f.Set))
                        .ToList();
                    boolFields = provider.GetBoolSettingFields()
                        .Select(f => new BoolSettingEntry(
                            ResolveSettingKey(f.Key, f.Label), f.Key, f.Get, f.Set))
                        .ToList();
                }
            }

            // Only hideable modules or modules that expose a settings surface get a
            // leaf: fixed modules (CanHide=false) that expose nothing are not listed.
            if (!module.CanHide
                && settingsView is null
                && fields?.Any() != true
                && boolFields?.Any() != true)
                continue;

            ModuleLeaves.Add(new ModuleSettingsLeaf(
                module.Id,
                ModuleName(module),
                ModuleDescription(module),
                IsModuleVisible(module),
                module.CanHide,
                VisibilityChanged,
                settingsView,
                fields,
                boolFields,
                ResolveSettingKey));
        }
    }

    private void BuildTree()
    {
        Groups.Clear();

        var global = new AppSettingsGroupItem(GroupName("global", "Global"));
        _globalLeaves.Clear();
        // Profiles replaces the old machine-type (Mill/Foam) selector: a machine
        // is a profile now, chosen via the profile manager (settings split —
        // docs/settings-migration.md). Its editor runs the CRUD below.
        _profilesLeaf = new ProfileSettingsLeaf(_settingsService, Resolve,
            LeafName("profiles", "Profiles"),
            LeafDescription("profiles", "Machines and their settings — one profile per machine."));
        _profilesLeaf.SetSwitchGuard(_profileSwitchAllowed);
        _profilesLeaf.Activated += _ => ReloadProfile();
        _globalLeaves.Add(_profilesLeaf);

        var languageLeaf = new LanguageSettingsLeaf(() => _currentLanguage, _localization.AvailableLocales, SetLocale,
            LeafName("language", "Language"),
            LeafDescription("language", "Interface language. Applied immediately."));
        _globalLeaves.Add(languageLeaf);
        if (_hotkeys is not null)
        {
            _hotkeysLeaf = new HotkeysSettingsLeaf(_hotkeys, Resolve, _moduleById.Values,
                LeafName("hotkeys", "Hotkeys"),
                LeafDescription("hotkeys", "Global keyboard shortcuts and their scopes."));
            _globalLeaves.Add(_hotkeysLeaf);
        }
        foreach (var leaf in _globalLeaves)
            global.Children.Add(leaf);

        var modules = new AppSettingsGroupItem(GroupName("modules", "Modules"));
        foreach (var leaf in ModuleLeaves)
            modules.Children.Add(leaf);

        Groups.Add(global);
        Groups.Add(modules);

        global.IsExpanded = true;
        // Select a leaf so the detail pane is populated on open (Profiles wins
        // when the dialog was opened via the Machine settings menu entry).
        SelectedNode = _preselectProfiles ? _profilesLeaf : languageLeaf;
    }

    private AppSettingsGroupItem GlobalGroup => (AppSettingsGroupItem)Groups[0];
    private AppSettingsGroupItem ModulesGroup => (AppSettingsGroupItem)Groups[1];

    private string GroupName(string key, string fallback) => Resolve($"appSettings.{key}", fallback);
    private string LeafName(string key, string fallback) => Resolve($"appSettings.leaf.{key}", fallback);
    private string LeafDescription(string key, string fallback) => Resolve($"appSettings.leaf.{key}.description", fallback);
    private string ModuleName(IModule module) => Resolve($"module.{module.Id}.name", module.Name);
    private string ModuleDescription(IModule module) => Resolve($"module.{module.Id}.description", module.Description);

    // A field key is a full localization key; null/absent falls back to its literal label.
    private string ResolveSettingKey(string? key, string fallback) =>
        string.IsNullOrWhiteSpace(key) ? fallback : Resolve(key, fallback);

    private string Resolve(string key, string fallback) =>
        _localization.Resolve(key, fallback);

    /// <summary>Module leaves, ordered by module order.</summary>
    public ObservableCollection<ModuleSettingsLeaf> ModuleLeaves { get; } = new();

    /// <summary>The two top-level roots in the tree (Global, Modules).</summary>
    public ObservableCollection<object> Groups { get; } = new();

    public DelegateCommand CloseCommand { get; }

    /// <summary>TreeView SelectedItem — only leaves select; a group click is discarded.</summary>
    public object? SelectedNode
    {
        get => _selectedNode;
        set
        {
            if (!SetField(ref _selectedNode, value))
                return;
            SelectedSetting = value as AppSettingsLeaf;
        }
    }

    private object? _selectedNode;
    private AppSettingsLeaf? _selectedSetting;

    public AppSettingsLeaf? SelectedSetting
    {
        get => _selectedSetting;
        private set
        {
            if (SetField(ref _selectedSetting, value))
            {
                OnPropertyChanged(nameof(HasSelection));
                OnPropertyChanged(nameof(HasNoSelection));
                OnPropertyChanged(nameof(IsProfileLeaf));
                OnPropertyChanged(nameof(IsLanguageLeaf));
                OnPropertyChanged(nameof(IsModuleLeaf));
                OnPropertyChanged(nameof(SelectedProfileLeaf));
                OnPropertyChanged(nameof(SelectedLanguageLeaf));
                OnPropertyChanged(nameof(SelectedModuleLeaf));
                OnPropertyChanged(nameof(IsHotkeysLeaf));
                OnPropertyChanged(nameof(SelectedHotkeysLeaf));
            }
        }
    }

    public bool HasSelection => SelectedSetting is not null;
    public bool HasNoSelection => SelectedSetting is null;

    // Editor-kind flags the detail pane binds IsVisible to (no converters needed).
    public bool IsProfileLeaf => SelectedSetting is ProfileSettingsLeaf;
    public bool IsLanguageLeaf => SelectedSetting is LanguageSettingsLeaf;
    public bool IsModuleLeaf => SelectedSetting is ModuleSettingsLeaf;

    // Typed selections so compiled XAML bindings (which are type-checked against the
    // property's declared type) can reach derived members — the bare SelectedSetting is
    // typed AppSettingsLeaf and cannot bind to leaf-specific properties.
    public ProfileSettingsLeaf? SelectedProfileLeaf => SelectedSetting as ProfileSettingsLeaf;
    public LanguageSettingsLeaf? SelectedLanguageLeaf => SelectedSetting as LanguageSettingsLeaf;
    public ModuleSettingsLeaf? SelectedModuleLeaf => SelectedSetting as ModuleSettingsLeaf;
    public HotkeysSettingsLeaf? SelectedHotkeysLeaf => SelectedSetting as HotkeysSettingsLeaf;

    public bool IsHotkeysLeaf => SelectedSetting is HotkeysSettingsLeaf;

    /* ---------- Locale ---------- */

    private void OnLocaleChanged(object? sender, EventArgs e)
    {
        GlobalGroup.Relocalize(GroupName("global", "Global"), string.Empty);
        ModulesGroup.Relocalize(GroupName("modules", "Modules"), string.Empty);

        var language = _globalLeaves.OfType<LanguageSettingsLeaf>().FirstOrDefault();
        language?.Relocalize(LeafName("language", "Language"),
            LeafDescription("language", "Interface language. Applied immediately."));

        foreach (var leaf in ModuleLeaves)
        {
            if (_moduleById.TryGetValue(leaf.Id, out var module))
                leaf.Relocalize(ModuleName(module), ModuleDescription(module));
            leaf.RefreshFields(); // also re-resolves generic field labels from their keys
        }

        _profilesLeaf?.Relocalize(
            LeafName("profiles", "Profiles"),
            LeafDescription("profiles", "Machines and their settings — one profile per machine."));
        _profilesLeaf?.RelocalizeButtons();
        _hotkeysLeaf?.Relocalize(
            LeafName("hotkeys", "Hotkeys"),
            LeafDescription("hotkeys", "Global keyboard shortcuts and their scopes."));
        _hotkeysLeaf?.RelocalizeAll();
    }

    /// <summary>
    /// The active profile changed: the Modules group must reflect the new
    /// profile's visibility/settings, so the module leaves rebuild and the tree
    /// re-renders them. The Profiles leaf itself stays selected.
    /// </summary>
    private void ReloadProfile()
    {
        _profile = _settingsService.Load();
        ModuleLeaves.Clear();
        BuildModuleLeaves(_moduleById.Values);
        ModulesGroup.Children.Clear();
        foreach (var leaf in ModuleLeaves)
            ModulesGroup.Children.Add(leaf);
    }

    private void SetLocale(string code)
    {
        _currentLanguage = code;
        _global.Language = code == "en" ? string.Empty : code;
        _localization.SetLocale(code);
        SaveGlobal();
    }

    /* ---------- Profile module visibility ---------- */

    private bool IsModuleVisible(IModule m) =>
        _profile.Modules.GetValueOrDefault(m.Id, m.IsVisibleByDefault);

    private void VisibilityChanged(string id, bool value)
    {
        if (_updatingSections)
            return;
        _profile.Modules[id] = value;
        SaveProfile();
    }

    private void RefreshSectionVisibility()
    {
        _updatingSections = true;
        try
        {
            foreach (var module in ModuleLeaves)
            {
                var visible = _profile.Modules.GetValueOrDefault(
                    module.Id, _defaults.GetValueOrDefault(module.Id, true));
                if (module.CanHide && module.IsVisible != visible)
                    module.IsVisible = visible;
                module.RefreshFields();
            }
        }
        finally
        {
            _updatingSections = false;
        }
    }

    private void SaveProfile() => _settingsService.Save(_profile);

    private void SaveGlobal() => _settingsService.SaveGlobal(_global);

    protected void RequestClose(bool result) => CloseRequested?.Invoke(this, result);
}