
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
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
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

    /// <summary>Module-provided settings UI; when set it replaces the generic field lists.</summary>
    public object? SettingsView { get; }

    public bool ShowGenericFields => SettingsView is null;

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
 * Profiles leaf — the profile manager
 * ------------------------------------------------------------------------- */

/// <summary>
/// Outcome of a host-performed profile switch. The three states are deliberately
/// distinct because they leave the app in different places: a successful switch moved
/// the machine and may or may not still be connected, a reconnect failure moved the
/// machine but left it offline, and an activation failure changed nothing at all.
/// </summary>
/// <param name="Activated">The target profile is the active one now.</param>
/// <param name="Title">Localized pop-up title; null = nothing to report.</param>
/// <param name="Message">Localized pop-up body; null = nothing to report.</param>
public sealed record ProfileSwitchResult(bool Activated, string? Title = null, string? Message = null)
{
    /// <summary>The switch completed and the transport came back up.</summary>
    public static readonly ProfileSwitchResult Success = new(true);

    /// <summary>The profile was activated but its connection could not be re-opened.</summary>
    public static ProfileSwitchResult ReconnectFailed(string title, string message) =>
        new(true, title, message);

    /// <summary>The activation itself was refused — nothing moved.</summary>
    public static ProfileSwitchResult ActivationFailed(string message) =>
        new(false, null, message);
}

/// <summary>
/// One row in a profile list: display name, machine-type badge and active marker, plus
/// (<paramref name="owner"/> permitting) the in-place rename state. Shared by the
/// settings leaf and the connect dialog's profile selector — the dialog's rows are
/// read-only display items and pass no owner, so their pencil command does nothing.
/// The name is the only editable field, so the row owns its own edit box instead of the
/// leaf keeping one for the selection: an inline editor is bound to the row it belongs
/// to, while a single shared box below the list always had to be re-synced to whichever
/// row was selected.
/// </summary>
public sealed class ProfileRow : ViewModelBase
{
    private readonly ProfileSettingsLeaf? _owner;
    private string _name;
    private bool _isActive;
    private bool _isSelected;
    private bool _isEditing;
    private bool _removeArmed;
    private string _renameText;
    private string? _renameError;

    public ProfileRow(string id, string name, string machineTypeId, ProfileSettingsLeaf? owner = null,
        string? renameHint = null)
    {
        Id = id;
        _name = name;
        _renameText = name;
        MachineTypeId = machineTypeId;
        RenameHint = renameHint;
        _owner = owner;
        // Every command re-selects its own row first: the actions used to run against the
        // leaf's selection, and the row they belong to is now the only sane target. The
        // rename command too — the context menu can reach a row the pointer never selected.
        BeginRenameCommand = new DelegateCommand(_ =>
        {
            if (_owner is null)
                return;
            _owner.SelectedProfile = this;
            _owner.BeginRename(this);
        });
        ActivateCommand = new DelegateCommand(_ => Select().Activate(this));
        DuplicateCommand = new DelegateCommand(_ => Select().Duplicate(this));
        RemoveCommand = new DelegateCommand(_ => Select().Remove(this));    }

    public string Id { get; }
    public string MachineTypeId { get; }

    /// <summary>The rename pencil's tooltip. The row itself carries no tip: the other
    /// actions explain themselves on their own glyphs.</summary>
    public string? RenameHint { get; }

    /// <summary>Opens this row's name for editing (the pencil and the F2/double-click path).</summary>
    public DelegateCommand BeginRenameCommand { get; }

    /// <summary>Makes this row's profile the active one (shown only while it is not active).</summary>
    public DelegateCommand ActivateCommand { get; }

    /// <summary>Copies this row's profile to a new one (never activates it).</summary>
    public DelegateCommand DuplicateCommand { get; }

    /// <summary>Deletes this row's profile; needs the second click to confirm.</summary>
    public DelegateCommand RemoveCommand { get; }

    private ProfileSettingsLeaf Select()
    {
        if (_owner is not null)
            _owner.SelectedProfile = this;
        return _owner!;
    }

    /// <summary>Display name. Only <see cref="ProfileSettingsLeaf"/> writes it (the
    /// rename path swaps in the name the service returned).</summary>
    public string Name { get => _name; internal set => SetField(ref _name, value); }

    public bool IsActive { get => _isActive; set => SetField(ref _isActive, value); }

    /// <summary>True for the row the leaf currently has selected — the action buttons show
    /// only there, so the list does not turn into a wall of buttons.</summary>
    public bool IsSelected { get => _isSelected; internal set => SetField(ref _isSelected, value); }

    /// <summary>The row's actions are offered on the selected row and hidden while its name
    /// is being edited (the row is then a text field, not a toolbar) or while the list is
    /// gated (a session is running, a restore is pending): every action would be refused.</summary>
    public bool ShowActions => _isSelected && !_isEditing && CanRename;

    // Button visibility: an action belongs to the selected row only. The context menu
    // binds the bare Can* flags instead — a row may be managed from its menu without the
    // pointer ever selecting it.
    public bool ShowActivate => ShowActions && CanActivate;
    public bool ShowDuplicate => ShowActions && CanDuplicate;
    public bool ShowRemove => ShowActions && CanRemove;

    /// <summary>True while this row's name is being edited in place.</summary>
    public bool IsEditing
    {
        get => _isEditing;
        set
        {
            if (SetField(ref _isEditing, value))
            {
                OnPropertyChanged(nameof(IsNotEditing));
                OnPropertyChanged(nameof(ShowActions));
                OnPropertyChanged(nameof(ShowActivate));
                OnPropertyChanged(nameof(ShowDuplicate));
                OnPropertyChanged(nameof(ShowRemove));
            }
        }
    }

    /// <summary>Inverse of <see cref="IsEditing"/> — what the display TextBlock binds.</summary>
    public bool IsNotEditing => !_isEditing;

    /// <summary>Delete is destructive, so its first click arms this row and the second one
    /// deletes. Arming is per row: arming one profile must not delete another.</summary>
    public bool RemoveArmed
    {
        get => _removeArmed;
        internal set
        {
            if (SetField(ref _removeArmed, value))
                OnPropertyChanged(nameof(RemoveHint));
        }
    }

    /// <summary>The delete glyph's tooltip: what it will delete, or — once the row is
    /// armed — the ask for the confirming second click. The glyph's face never changes
    /// (it is a pictogram); the reddened glyph and this tip are the confirmation state.</summary>
    public string? RemoveHint => RemoveArmed
        ? _owner?.ResolveRemoveConfirmHint()
        : _owner?.ResolveRemoveHint();

    /// <summary>What the activate button says it will do.</summary>
    public string? ActivateHint => _owner?.ResolveActivateHint();

    /// <summary>What the duplicate button says it will do.</summary>
    public string? DuplicateHint => _owner?.ResolveDuplicateHint();

    // Each action's availability is the list's gate plus what is true of THIS profile: the
    // active one cannot be activated again or deleted, and the last profile cannot be
    // deleted. A row button is hidden when its flag is false; the context menu keeps the
    // item, disabled. They are re-raised with the rest of the row's action state.
    public bool CanRename => _owner?.CanSwitchProfiles == true;
    public bool CanActivate => _owner?.CanActivate(this) == true;
    public bool CanDuplicate => _owner?.CanDuplicate(this) == true;
    public bool CanRemove => _owner?.CanRemove(this) == true;

    /// <summary>Re-raises the localized labels and hints (a rebuild or a locale change).</summary>
    internal void RefreshActionText()
    {
        OnPropertyChanged(nameof(RemoveHint));
        OnPropertyChanged(nameof(ActivateHint));
        OnPropertyChanged(nameof(DuplicateHint));
        OnPropertyChanged(nameof(ShowActions));
        OnPropertyChanged(nameof(ShowActivate));
        OnPropertyChanged(nameof(ShowDuplicate));
        OnPropertyChanged(nameof(ShowRemove));
        OnPropertyChanged(nameof(CanRename));
        OnPropertyChanged(nameof(CanActivate));
        OnPropertyChanged(nameof(CanDuplicate));
        OnPropertyChanged(nameof(CanRemove));
    }

    /// <summary>The edit box's draft text.</summary>
    public string RenameText
    {
        get => _renameText;
        set => SetField(ref _renameText, value);
    }

    /// <summary>Why the last commit was refused (blank or duplicate name); null when clean.</summary>
    public string? RenameError
    {
        get => _renameError;
        set
        {
            if (SetField(ref _renameError, value))
                OnPropertyChanged(nameof(HasRenameError));
        }
    }

    public bool HasRenameError => !string.IsNullOrEmpty(_renameError);
}

/// <summary>
/// Global &gt; Profiles leaf: one profile per machine with add / duplicate /
/// rename / remove / activate and the machine-type badge. All mutations go
/// through <see cref="SettingsService"/>; switching is gated by
/// <see cref="SetSwitchGuard"/> — the dialog host blocks it while a session is
/// running — and carried out through <see cref="SetSwitchPipeline"/>, which
/// lets the host take the transport down, activate the profile and bring the
/// transport back up on the new profile's own endpoint.
/// </summary>
public sealed class ProfileSettingsLeaf : AppSettingsLeaf
{
    private readonly SettingsService _settings;
    private readonly Func<string, string, string> _resolve;
    private Func<bool>? _switchAllowed;
    private Func<MachineProfile, Task<ProfileSwitchResult>>? _switchPipeline;
    private ProfileRow? _selected;
    private ProfileRow? _editing;
    private string _newName = string.Empty;
    private string _newType = MachineTypes.Mill;
    private string _machineTypeDraft = MachineTypes.Mill;
    private bool _switchBusy;
    private bool _confirmTypeChange;

    /// <summary>Raised when a profile became active (host rebuilds profile-scoped UI).</summary>
    public event Action<string>? Activated;

    /// <summary>Raised after any list-affecting mutation (add/duplicate/rename/remove/type).</summary>
    public event System.Action? ProfilesChanged;

    public ProfileSettingsLeaf(SettingsService settings, Func<string, string, string> resolve,
        string name, string description, IMachineKinematicsProvider? machines = null)
        : base("profiles", name, description)
    {
        _settings = settings;
        _resolve = resolve;
        // The machine card's axis editor follows the selection (one card, whichever
        // profile is selected) — the same master/detail shape as the settings tree.
        MachineAxes = new MachineAxesViewModel(
            settings, resolve, settings.Global.ActiveProfileId, machines);
        // The list row's picture is the selected profile's machine type, so the leaf
        // republishes it whenever the card re-points (selection, type change, reload).
        MachineAxes.PropertyChanged += OnMachineAxesChanged;
        WireCommands();
        Refresh();
    }

    /// <summary>The selected profile's axis editor: topology picture, per-plane axis
    /// rows, the optional-axis list, the separation and the validation problems.</summary>
    public MachineAxesViewModel MachineAxes { get; }

    /// <summary>The selected profile's machine-type picture, beside the list. Null for a
    /// type that ships no art.</summary>
    public string? TopologyImage => MachineAxes.TopologyImage;

    public bool HasTopologyImage => MachineAxes.HasTopologyImage;

    private void OnMachineAxesChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(MachineAxesViewModel.TopologyImage)
            or nameof(MachineAxesViewModel.MachineTypeName)))
            return;
        OnPropertyChanged(nameof(TopologyImage));
        OnPropertyChanged(nameof(HasTopologyImage));
    }

    public ObservableCollection<ProfileRow> Profiles { get; } = new();

    public IReadOnlyList<string> MachineTypeOptions { get; } = [MachineTypes.Mill, MachineTypes.Foam];

    public ProfileRow? SelectedProfile
    {
        get => _selected;
        set
        {
            var previous = _selected;
            if (ReferenceEquals(previous, value))
                return;
            // Moving to another row finishes the edit that was open on the old one. The
            // list owns the pointer, so clicking away is the natural "done" gesture — and
            // leaving an editor armed on a row that is no longer selected would commit it
            // against whatever the operator does next.
            FinishRename();
            // Committing the rename rebuilds the rows, so the incoming instance is stale by
            // now; the selection is carried by profile id and re-resolved from the list
            // that is actually bound. The click is never lost that way.
            value = ResolveRow(value);
            if (!SetField(ref _selected, value))
                return;
            // The pencil's flag is derived state, so it is recomputed rather than
            // toggled: a rebuild, a re-entrant binding write or a detached instance can
            // each leave a second row flagged, and exactly one row may carry it.
            ClearSelectionFlags();
            if (previous is not null)
                previous.IsSelected = false;
            if (value is not null)
                value.IsSelected = true;
            // Arming delete is per row and does not survive leaving it.
            DisarmRemovals();
            _machineTypeDraft = value?.MachineTypeId ?? MachineTypes.Mill;
            _confirmTypeChange = false;
            OnPropertyChanged(nameof(SelectedMachineType));
            // The axis card always edits the selected profile, so it re-reads here.
            if (value is not null)
                MachineAxes.Load(value.Id);
            RefreshCommands();
        }
    }

    /// <summary>Drops the derived selected flag from every row in the list.</summary>
    private void ClearSelectionFlags()
    {
        foreach (var row in Profiles)
            row.IsSelected = false;
    }

    /// <summary>Drops the delete-confirmation arming from every row.</summary>
    private void DisarmRemovals()
    {
        foreach (var row in Profiles)
            row.RemoveArmed = false;
    }

    /// <summary>The live row for a row's profile id, or the row itself when the list does
    /// not hold it (a caller passing a detached instance is honoured, not silently
    /// dropped).</summary>
    private ProfileRow? ResolveRow(ProfileRow? row)
    {
        if (row is null || Profiles.Contains(row))
            return row;
        if (_selected is not null && _selected.Id == row.Id)
            return _selected;
        return Profiles.FirstOrDefault(p => p.Id == row.Id) ?? row;
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

    /// <summary>Machine-type draft for the selected profile (commit via <see cref="ApplyTypeCommand"/>).</summary>
    public string SelectedMachineType
    {
        get => _machineTypeDraft;
        set
        {
            if (SetField(ref _machineTypeDraft, value))
            {
                OnPropertyChanged(nameof(IsTypeChangePending));
                RefreshCommands();
            }
        }
    }

    /// <summary>True while the draft type differs from the profile's: the change is
    /// destructive (the new type's defaults replace the layout), so the warning shows and
    /// the button needs a second click.</summary>
    public bool IsTypeChangePending =>
        SelectedProfile is not null && SelectedMachineType != SelectedProfile.MachineTypeId;

    /// <summary>The destructive-switch warning, shown while a type change is pending.</summary>
    public string TypeChangeWarning => _resolve("appSettings.profiles.typeWarning",
        "Changing the machine type replaces this profile's axis layout, optional axes and " +
        "plane-to-plane distance with the new type's defaults — nothing is carried over.");

    /// <summary>False while a session is running or a restore is pending: the service
    /// refuses every mutation then, so the leaf must not offer one — the UI would
    /// otherwise report an add/rename/activate that never happened. Being merely
    /// *connected* is not a block any more: the host's switch pipeline takes the
    /// transport down and brings it back on the new profile's endpoint.</summary>
    public bool CanSwitchProfiles =>
        !_switchBusy && _switchAllowed?.Invoke() != false && !_settings.RestorePending;

    /// <summary>True while the host is mid-switch (disconnect → activate → reconnect).</summary>
    public bool SwitchBusy => _switchBusy;

    public string TypeButtonText => _confirmTypeChange
        ? _resolve("appSettings.profiles.typeConfirm", "Change again to confirm")
        : _resolve("appSettings.profiles.typeChange", "Change type");

    public DelegateCommand AddCommand { get; private set; } = null!;
    public DelegateCommand ApplyTypeCommand { get; private set; } = null!;

    /// <summary>Escape while a row's name is being edited: drop the draft, keep the name.</summary>
    public DelegateCommand CancelRenameCommand { get; private set; } = null!;

    private void WireCommands()
    {
        AddCommand = new DelegateCommand(_ => Add(),
            _ => CanSwitchProfiles && !string.IsNullOrWhiteSpace(NewName));
        ApplyTypeCommand = new DelegateCommand(_ => ApplyType(),
            _ => CanSwitchProfiles && SelectedProfile is not null
                && SelectedMachineType != SelectedProfile.MachineTypeId);
        CancelRenameCommand = new DelegateCommand(_ => CancelRename());
    }

    /* ---------- Row actions ---------- */

    /// <summary>May this row's profile be deleted? The active one never (it is what the
    /// app is running on) and never the last one.</summary>
    public bool CanRemove(ProfileRow item) =>
        CanSwitchProfiles && !item.IsActive && Profiles.Count > 1;

    /// <summary>May this row's profile be activated?</summary>
    public bool CanActivate(ProfileRow item) =>
        CanSwitchProfiles && !item.IsActive;

    /// <summary>May this row's profile be copied?</summary>
    public bool CanDuplicate(ProfileRow item) => CanSwitchProfiles;

    /// <summary>
    /// Makes a row's profile the active one. Row-scoped because the action now lives on the
    /// row: the row is selected first, then the switch runs (see
    /// <see cref="SetSwitchPipeline"/> for what the host does with it).
    /// </summary>
    public void Activate(ProfileRow item)
    {
        if (!CanActivate(item))
            return;
        _ = ActivateAsync(item);
    }

    /// <summary>
    /// Copies a profile to a new one. Scoped to the row it was invoked from, and never
    /// activates the copy: duplicating is how you build the next machine, not how you
    /// switch to it.
    /// </summary>
    public void Duplicate(ProfileRow item)
    {
        if (!CanSwitchProfiles)
            return;
        _settings.CreateProfile($"{item.Name} copy", item.MachineTypeId, copyFromId: item.Id);
        Refresh();
        ProfilesChanged?.Invoke();
    }

    /// <summary>
    /// Deletes a profile, in two steps: the first click arms that row, the second deletes.
    /// The arming is the row's own state, so arming one profile cannot delete another.
    /// </summary>
    public void Remove(ProfileRow item)
    {
        if (!CanSwitchProfiles)
            return;
        if (!item.RemoveArmed)
        {
            DisarmRemovals();
            item.RemoveArmed = true;
            RefreshCommands();
            return;
        }
        if (!CanRemove(item))
            return;
        item.RemoveArmed = false;
        _settings.DeleteProfile(item.Id);
        Refresh();
        ProfilesChanged?.Invoke();
    }

    internal string ResolveRemoveHint() =>
        _resolve("appSettings.profiles.removeHint", "Delete this profile");

    internal string ResolveRemoveConfirmHint() =>
        _resolve("appSettings.profiles.removeConfirmHint", "Click again to confirm");

    internal string ResolveActivateHint() =>
        _resolve("appSettings.profiles.activateHint", "Make this profile the active one");

    internal string ResolveDuplicateHint() =>
        _resolve("appSettings.profiles.duplicateHint", "Copy this profile to a new one");


    /* ---------- In-place rename ---------- */

    /// <summary>
    /// Opens the row's name for editing. Refused while the list is gated (the service
    /// would refuse the write). A refusal is not a no-op: this is also the path that runs
    /// when the gate closes mid-edit — MarkRestorePending raises nothing the leaf
    /// listens to — so any row still sitting in edit mode is closed first. Otherwise the
    /// editor would stay on screen and its commit would silently do nothing.
    /// </summary>
    public void BeginRename(ProfileRow item)
    {
        if (item is null || !CanSwitchProfiles)
        {
            CancelRename();
            return;
        }
        CancelRename();
        _editing = item;
        item.RenameError = null;
        item.RenameText = item.Name;
        item.IsEditing = true;
    }

    /// <summary>Keeps the draft text and flags why it cannot be committed yet: the
    /// operator sees the problem while they type rather than on commit.</summary>
    public void UpdateRename(ProfileRow item, string? text)
    {
        if (item is null || _editing is not null && !ReferenceEquals(_editing, item))
            return;
        item.RenameText = text ?? string.Empty;
        item.RenameError = ValidateRename(item, item.RenameText);
    }

    /// <summary>Enter or lost focus: writes the draft through the service. A refused
    /// draft keeps the row in edit mode with the reason shown.</summary>
    public void CommitRename(ProfileRow item)
    {
        if (item is null || !ReferenceEquals(_editing, item))
            return;
        if (ValidateRename(item, item.RenameText) is { } problem)
        {
            item.RenameError = problem;
            return;
        }
        if (item.RenameText == item.Name)
        {
            CancelRename();
            return;
        }

        _settings.RenameProfile(item.Id, item.RenameText.Trim());
        var renamed = _settings.AllProfiles().FirstOrDefault(p => p.Id == item.Id);
        _editing = null;
        item.RenameError = null;
        item.IsEditing = false;
        if (renamed is not null)
            item.Name = renamed.Name;
        Refresh();
        ProfilesChanged?.Invoke();
    }

    /// <summary>Abandons the draft (Escape, or opening another row's editor).</summary>
    public void CancelRename()
    {
        if (_editing is null)
            return;
        _editing.RenameError = null;
        _editing.RenameText = _editing.Name;
        _editing.IsEditing = false;
        _editing = null;
    }

    /// <summary>
    /// Ends the edit that is open, the way leaving the row should: a committable draft is
    /// written, a refused one falls back to the stored name. Cancel-only would silently
    /// throw away typing the operator never abandoned, and keep-edit would leave an armed
    /// editor on a row that is no longer selected.
    /// </summary>
    private void FinishRename()
    {
        if (_editing is null)
            return;
        var item = _editing;
        if (ValidateRename(item, item.RenameText) is null && item.RenameText != item.Name)
            CommitRename(item);
        else
            CancelRename();
    }

    /// <summary>Why the draft cannot be committed, or null when it can. Blank and
    /// duplicate names are refused here rather than letting the service slugify them:
    /// a rename that silently became "default-2" would look like a failure.</summary>
    private string? ValidateRename(ProfileRow item, string? text)
    {
        var name = text?.Trim() ?? string.Empty;
        if (name.Length == 0)
            return _resolve("appSettings.profiles.renameEmpty", "A profile name cannot be empty.");
        if (name == item.Name)
            return null;
        return _settings.AllProfiles().Any(p =>
            p.Id != item.Id && string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
            ? _resolve("appSettings.profiles.renameDuplicate", "Another profile already uses that name.")
            : null;
    }

    // Every mutator below re-checks the gate itself: a stale UI (or a direct Execute,
    // which bypasses CanExecute) must not report a mutation the service is about to
    // refuse — Add() would otherwise fire Activated for a profile that was never added.
    private void Add()
    {
        if (!CanSwitchProfiles)
            return;
        var profile = _settings.Activate(_settings.CreateProfile(NewName.Trim(), NewType).Id);
        NewName = string.Empty;
        Refresh();
        Activated?.Invoke(profile.Id);
    }

    /// <summary>
    /// Makes a row's profile active. When the host installed a
    /// <see cref="SetSwitchPipeline">switch pipeline</see> it owns the whole
    /// sequence — take the transport down, activate, bring it back up on the new
    /// profile's own endpoint — and reports the outcome as text. Without one
    /// (headless tests, a host with no transport) the activation is the plain
    /// local switch it always was.
    /// </summary>
    private async Task ActivateAsync(ProfileRow item)
    {
        if (!CanSwitchProfiles || _switchBusy)
            return;

        var profile = _settings.AllProfiles().FirstOrDefault(p => p.Id == item.Id);
        if (profile is null)
        {
            Refresh();
            return;
        }

        if (_switchPipeline is null)
        {
            _settings.Activate(profile.Id);
            Refresh();
            Activated?.Invoke(profile.Id);
            return;
        }

        SetSwitchBusy(true);
        ProfileSwitchResult result;
        try
        {
            result = await _switchPipeline(profile);
        }
        catch (Exception ex)
        {
            // The pipeline is documented not to throw; if it does, nothing is claimed.
            result = ProfileSwitchResult.ActivationFailed(ex.Message);
        }
        finally
        {
            SetSwitchBusy(false);
        }

        // The switch succeeded only once the host says the profile is active. A failed
        // activation republishes nothing: the tree and the active profile are whatever
        // they were.
        if (result.Activated)
        {
            Refresh();
            Activated?.Invoke(profile.Id);
        }
        else
        {
            Refresh();
        }

        // Nothing about a switch is written into the pane: the active row already carries
        // the checkmark, and a caption that appears above the list shoves the list down
        // for a moment. Failures go to a pop-up instead, which the host owns.
        if (result.Message is { Length: > 0 } message)
            ReportSwitchProblem?.Invoke(result.Title ?? profile.Name, message);
    }

    private void SetSwitchBusy(bool busy)
    {
        if (_switchBusy == busy)
            return;
        _switchBusy = busy;
        if (busy)
            CancelRename();
        RefreshCommands();
    }

    /// <summary>Rebuilds the list from disk/registry and keeps the selection.</summary>
    public void Refresh()
    {
        var activeId = _settings.Global.ActiveProfileId;
        // Captured per rebuild, so a locale change is picked up when the rows are.
        var hint = _resolve("appSettings.profiles.renameHint", "Rename this profile");
        Profiles.Clear();
        foreach (var p in _settings.AllProfiles()
            .OrderByDescending(p => p.Id == activeId)
            .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase))
        {
            Profiles.Add(new ProfileRow(p.Id, p.Name, p.MachineTypeId, this, hint)
            {
                IsActive = p.Id == activeId,
            });
        }
        var keep = _selected is null ? null : Profiles.FirstOrDefault(p => p.Id == _selected.Id);
        // The rows are new instances: a draft that belonged to the old ones is gone.
        _editing = null;
        // Re-pointed without going through the setter: a rebuild is not a selection change.
        // Routing it through the setter would re-run the commit-on-row-change rule and
        // re-flag the old row, which is exactly what the commit this rebuild follows is
        // trying to leave behind.
        _selected = keep ?? Profiles.FirstOrDefault();
        if (_selected is not null)
            _selected.IsSelected = true;
        _machineTypeDraft = _selected?.MachineTypeId ?? MachineTypes.Mill;
        OnPropertyChanged(nameof(SelectedProfile));
        OnPropertyChanged(nameof(SelectedMachineType));
        if (_selected is not null)
            MachineAxes.Load(_selected.Id);
        RefreshCommands();
    }

    /// <summary>Host-provided gate: null = switching allowed. Applied live, so the
    /// dialog honours a session that starts while it is open.</summary>
    public void SetSwitchGuard(Func<bool>? allowed)
    {
        _switchAllowed = allowed;
        RefreshCommands();
    }

    /// <summary>
    /// Host-provided switch pipeline: performs the whole profile change and reports what
    /// happened. The leaf calls it *instead of* <see cref="SettingsService.Activate"/>, so
    /// the host is free to disconnect first and reconnect to the new profile's endpoint
    /// afterwards. Null = the leaf activates the profile itself.
    /// </summary>
    public void SetSwitchPipeline(Func<MachineProfile, Task<ProfileSwitchResult>>? pipeline)
    {
        _switchPipeline = pipeline;
        RefreshCommands();
    }

    /// <summary>
    /// Host-provided failure reporting, raised with a localized title and message when a
    /// switch did not complete. A switch failure is a connection-level event the pane
    /// cannot express — the dialog is modal and about to be dismissed — so it is handed
    /// to the host, which shows it as a pop-up over the app.
    /// </summary>
    public Action<string, string>? ReportSwitchProblem { get; set; }

    private void ApplyType()
    {
        if (!CanSwitchProfiles)
            return;
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

    /// <summary>Re-resolve button labels after a locale change.</summary>
    public void RelocalizeButtons()
    {
        OnPropertyChanged(nameof(TypeButtonText));
        OnPropertyChanged(nameof(CanSwitchProfiles));
        OnPropertyChanged(nameof(TypeChangeWarning));
        foreach (var row in Profiles)
            row.RefreshActionText();
    }

    /// <summary>Re-localize everything the leaf renders itself (computed labels plus the
    /// machine card's axis editor).</summary>
    public void RelocalizeCard()
    {
        RelocalizeButtons();
        MachineAxes.Relocalize();
    }

    private void RefreshCommands()
    {
        AddCommand?.RaiseCanExecuteChanged();
        ApplyTypeCommand?.RaiseCanExecuteChanged();
        // The row-scoped actions carry localized labels and hints, and their enabled state
        // is the list's gate; a rebuild or a locale change has to reach every row.
        foreach (var row in Profiles)
            row.RefreshActionText();
        OnPropertyChanged(nameof(TypeButtonText));
        OnPropertyChanged(nameof(CanSwitchProfiles));
        OnPropertyChanged(nameof(SwitchBusy));
        OnPropertyChanged(nameof(IsTypeChangePending));
        OnPropertyChanged(nameof(TypeChangeWarning));
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
public sealed class HotkeysSettingsLeaf : AppSettingsLeaf, IDisposable
{
    private readonly HotkeyService _service;
    private readonly Func<string, string, string> _resolve;
    private readonly Dictionary<string, string> _moduleNames;
    private HotkeyEditorRow? _recordingRow;
    private bool _disposed;
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
        _service.Changed += OnHotkeysChanged;
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

    private void OnHotkeysChanged(object? sender, EventArgs e) => RefreshAll();

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _service.Changed -= OnHotkeysChanged;
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

public class AppSettingsViewModel : ViewModelBase, IClosableDialog, IDisposable
{
    private MachineProfile _profile;
    private readonly GlobalSettings _global;
    private readonly SettingsService _settingsService;
    private readonly LocalizationService _localization;
    private readonly Dictionary<string, bool> _defaults = new();
    private readonly Dictionary<string, IModule> _moduleById;
    private readonly IFileDialogService? _fileDialogs;
    private readonly List<AppSettingsLeaf> _globalLeaves = new();
    private ProfileSettingsLeaf? _profilesLeaf;
    private BackupSettingsLeaf? _backupLeaf;
    private readonly HotkeyService? _hotkeys;
    private HotkeysSettingsLeaf? _hotkeysLeaf;
    private readonly IMachineKinematicsProvider? _machines;
    private Func<bool>? _profileSwitchAllowed;
    private Func<MachineProfile, Task<ProfileSwitchResult>>? _profileSwitchPipeline;
    private Action<string, string>? _reportSwitchProblem;
    private bool _preselectProfiles;
    private bool _updatingSections;
    private bool _disposed;
    private string _currentLanguage;

    public event EventHandler<bool>? CloseRequested;

    public AppSettingsViewModel(
        SettingsService settingsService,
        IEnumerable<IModule> modules,
        LocalizationService? localization = null,
        Func<bool>? profileSwitchAllowed = null,
        HotkeyService? hotkeys = null,
        IFileDialogService? fileDialogs = null,
        IMachineKinematicsProvider? machines = null)
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
        _fileDialogs = fileDialogs;
        // Optional: the machine card reads the live board letters from it. Without one
        // (headless tests, a host that did not register it) the conventional fallback
        // letters apply, exactly as they do for a disconnected controller.
        _machines = machines;
        BuildModuleLeaves(modules);
        BuildTree();

        _localization.Changed += OnLocaleChanged;
        CloseCommand = new DelegateCommand(_ => RequestClose(true));
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _localization.Changed -= OnLocaleChanged;
        _hotkeysLeaf?.Dispose();
    }

    /// <summary>
    /// Host-provided gate for profile switching (blocked while a session runs).
    /// Null = allowed. Applied live, so the dialog honors a session that starts
    /// while it is open. The backup leaf's restore is gated by the same predicate
    /// — a mid-session restore invalidates the running session's loaded state.
    /// </summary>
    public Func<bool>? ProfileSwitchAllowed
    {
        get => _profileSwitchAllowed;
        set
        {
            _profileSwitchAllowed = value;
            _profilesLeaf?.SetSwitchGuard(value);
            _backupLeaf?.SetSwitchGuard(value);
        }
    }

    /// <summary>
    /// Host-provided transport-aware profile switch (disconnect → activate →
    /// reconnect). Only the Profiles leaf uses it; null leaves the plain local
    /// activation in place.
    /// </summary>
    public Func<MachineProfile, Task<ProfileSwitchResult>>? ProfileSwitchPipeline
    {
        get => _profileSwitchPipeline;
        set
        {
            _profileSwitchPipeline = value;
            _profilesLeaf?.SetSwitchPipeline(value);
        }
    }

    /// <summary>
    /// Host-provided failure reporting (see <see cref="ProfileSettingsLeaf.ReportSwitchProblem"/>),
    /// forwarded to the Profiles leaf.
    /// </summary>
    public Action<string, string>? ReportSwitchProblem
    {
        get => _reportSwitchProblem;
        set
        {
            _reportSwitchProblem = value;
            if (_profilesLeaf is not null)
                _profilesLeaf.ReportSwitchProblem = value;
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
        // is a profile now, chosen via the profile manager. Its editor runs the
        // CRUD below.
        _profilesLeaf = new ProfileSettingsLeaf(_settingsService, Resolve,
            LeafName("profiles", "Profiles"),
            LeafDescription("profiles", "Machines and their settings — one profile per machine."),
            _machines);
        _profilesLeaf.SetSwitchGuard(_profileSwitchAllowed);
        _profilesLeaf.SetSwitchPipeline(_profileSwitchPipeline);
        _profilesLeaf.ReportSwitchProblem = _reportSwitchProblem;
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
        // Appended last: existing tests address the profiles/language leaves by
        // position (Children[0] / Children[1]) and the hotkeys leaf predates it.
        _backupLeaf = new BackupSettingsLeaf(_settingsService, _fileDialogs, Resolve,
            LeafName("backup", "Backup & restore"),
            LeafDescription("backup", "Save every profile and app setting to one archive, or restore them from a backup."));
        _backupLeaf.SetSwitchGuard(_profileSwitchAllowed);
        _globalLeaves.Add(_backupLeaf);
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
                OnPropertyChanged(nameof(IsBackupLeaf));
                OnPropertyChanged(nameof(SelectedBackupLeaf));
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
    public BackupSettingsLeaf? SelectedBackupLeaf => SelectedSetting as BackupSettingsLeaf;

    public bool IsHotkeysLeaf => SelectedSetting is HotkeysSettingsLeaf;
    public bool IsBackupLeaf => SelectedSetting is BackupSettingsLeaf;

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
        _profilesLeaf?.RelocalizeCard();
        _hotkeysLeaf?.Relocalize(
            LeafName("hotkeys", "Hotkeys"),
            LeafDescription("hotkeys", "Global keyboard shortcuts and their scopes."));
        _hotkeysLeaf?.RelocalizeAll();

        _backupLeaf?.Relocalize(
            LeafName("backup", "Backup & restore"),
            LeafDescription("backup", "Save every profile and app setting to one archive, or restore them from a backup."));
        _backupLeaf?.RelocalizeButtons();
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

    private void SaveProfile()
    {
        try
        {
            _settingsService.Save(_profile);
        }
        catch
        {
            // Best-effort persistence; settings still apply for the session.
        }
    }

    private void SaveGlobal()
    {
        try
        {
            _settingsService.SaveGlobal(_global);
        }
        catch
        {
            // Best-effort persistence; settings still apply for the session.
        }
    }

    protected void RequestClose(bool result) => CloseRequested?.Invoke(this, result);
}