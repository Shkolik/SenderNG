/*
 * GrblSettingsViewModel.cs - GRBL settings tree view model
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
using SenderNG.Core.Localization;
using SenderNG.Core.Settings;

namespace SenderNG.Modules.Fixed;

/// <summary>
/// A group node in the settings tree. <see cref="Children"/> mixes nested groups and
/// settings (<see cref="GrblSettingItem"/> leaves). The TreeView's <c>TreeViewItem</c>
/// container binds <c>IsExpanded</c> two-way to <see cref="IsExpanded"/> so expansion
/// state survives reloads (see GrblSettingsView.axaml <c>TreeView.Styles</c>).
/// </summary>
public sealed class GrblSettingGroupItem : ViewModelBase
{
    private bool _isExpanded; // groups start collapsed (search auto-expands matches)

    public string Name { get; }
    public ObservableCollection<object> Children { get; } = new();

    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetField(ref _isExpanded, value);
    }

    public GrblSettingGroupItem(string name) => Name = name;
}

/// <summary>One checkbox of a bitfield / axis-mask setting; toggling recomputes the raw value.</summary>
public sealed class GrblSettingBit : ViewModelBase
{
    private bool _isChecked;
    private readonly GrblSettingItem _owner;

    public string Label { get; }
    public int Mask { get; }

    public bool IsChecked
    {
        get => _isChecked;
        set
        {
            if (_isChecked == value)
                return;
            _isChecked = value;
            OnPropertyChanged();
            _owner.OnBitToggled(this);
        }
    }

    internal GrblSettingBit(GrblSettingItem owner, string label, int mask, bool isChecked)
    {
        _owner = owner;
        Label = label;
        Mask = mask;
        _isChecked = isChecked;
    }

    /// <summary>Update from the raw value without bouncing back into the owner.</summary>
    internal void SetSilently(bool value)
    {
        if (_isChecked == value)
            return;
        _isChecked = value;
        OnPropertyChanged();
    }
}

/// <summary>Why <see cref="GrblSettingItem.Validate"/> rejects a setting's text.</summary>
internal enum GrblSettingValidationError
{
    None,
    NotNumeric,
    OutOfRange,
    MalformedIp,
}

/// <summary>
/// A single setting row. The editor rendered by the view is chosen via the
/// <c>IsBool</c>/<c>IsRadio</c>/<c>IsBits</c>/<c>ShowTextBox</c> flags, which mirror the
/// grblHAL data type (<see cref="GrblSettingType"/>). All editors funnel through
/// <see cref="Value"/> so dirty tracking stays in one place.
/// </summary>
public sealed class GrblSettingItem : ViewModelBase
{
    private string _value;
    private string? _description;

    public int Id { get; }
    public string Key => $"${Id}";
    public string Name { get; }
    public string Unit { get; }
    public GrblSettingType Type { get; }
    public string Format { get; }
    public double? Min { get; }
    public double? Max { get; }

    /// <summary>From the controller's metadata: the value only takes effect after a restart.</summary>
    public bool RebootRequired { get; }

    /// <summary>Radio option labels or bitfield labels (axis letters for axis masks).</summary>
    public string[] Options { get; }

    /// <summary>Original value from the board — the dirty comparison baseline.</summary>
    public string OriginalValue { get; private set; }

    public bool HasUnit => !string.IsNullOrWhiteSpace(Unit);
    public bool HasRange => Min is not null || Max is not null;
    public bool HasDescription => !string.IsNullOrWhiteSpace(Description);

    /// <summary>Invariant bound formatting, shared by <see cref="Range"/> and save-time messages.</summary>
    internal static string FormatBound(double? value) =>
        value?.ToString("0.######", CultureInfo.InvariantCulture) ?? "-";

    public string? Range =>
        HasRange ? $"Range: {FormatBound(Min)} .. {FormatBound(Max)}" : null;

    public ObservableCollection<GrblSettingBit> Bits { get; } = new();

    public string Value
    {
        get => _value;
        set
        {
            if (!SetField(ref _value, value))
                return;
            OnPropertyChanged(nameof(IsDirty));
            OnPropertyChanged(nameof(IsChecked));
            OnPropertyChanged(nameof(SelectedIndex));
            ResyncBits();
        }
    }

    public bool IsDirty => _value != OriginalValue;

    /// <summary>Mark the current value as saved (after a successful write).</summary>
    public void Accept()
    {
        OriginalValue = _value;
        OnPropertyChanged(nameof(IsDirty));
    }

    /// <summary>Apply a bool editor change (checkbox toggle).</summary>
    public void ApplyBool(bool value) => Value = value ? "1" : "0";

    /// <summary>Apply a radio editor change (combobox selection).</summary>
    public void ApplyRadio(int index) => Value = index.ToString(CultureInfo.InvariantCulture);

    /// <summary>Apply a text/numeric editor change.</summary>
    public void ApplyText(string text) => Value = text;

    /// <summary>
    /// Validate this setting's current <see cref="Value"/> — see <see cref="ValidateValue"/>.
    /// </summary>
    internal GrblSettingValidationError Validate() => ValidateValue(Value);

    /// <summary>
    /// Reject text a controller write cannot carry: a partial numeric keystroke
    /// ("-", "1e"), a non-finite float, or a value outside Min/Max. Free-form types
    /// (Text, Password) and the non-text editors always pass. Never throws.
    /// <paramref name="text"/> is checked against THIS item's metadata and nothing is
    /// mutated, so a restore can validate a candidate value from a backup file before
    /// the board is touched.
    /// </summary>
    internal GrblSettingValidationError ValidateValue(string text)
    {
        switch (Type)
        {
            case GrblSettingType.Integer:
                return ValidateInteger(text);

            case GrblSettingType.Float:
                return ValidateFloat(text);

            // A bit type whose Format yields no options has an empty Bits collection, so
            // IsBits is false and ShowTextBox is true: it renders as a text box holding a
            // raw integer mask, so validate it as one rather than waving it through. The
            // when-clause is needed on each label: a bare case label always matches.
            case GrblSettingType.Bitfield when !IsBits:
            case GrblSettingType.XBitfield when !IsBits:
            case GrblSettingType.AxisMask when !IsBits:
                return ValidateInteger(text);

            case GrblSettingType.Ip4:
                return IsDottedQuad(text)
                    ? GrblSettingValidationError.None
                    : GrblSettingValidationError.MalformedIp;

            default:
                return GrblSettingValidationError.None;
        }
    }

    private GrblSettingValidationError ValidateInteger(string text)
    {
        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
            return GrblSettingValidationError.NotNumeric;
        return InRange(n) ? GrblSettingValidationError.None : GrblSettingValidationError.OutOfRange;
    }

    private GrblSettingValidationError ValidateFloat(string text)
    {
        // Since .NET Core 3.0 "NaN"/"Infinity" parse successfully, and every comparison
        // against NaN is false - so a bounds check alone would wave them through.
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)
            || !double.IsFinite(d))
            return GrblSettingValidationError.NotNumeric;
        return InRange(d) ? GrblSettingValidationError.None : GrblSettingValidationError.OutOfRange;
    }

    private bool InRange(double parsed) =>
        (Min is not { } lo || parsed >= lo) && (Max is not { } hi || parsed <= hi);

    private static bool IsDottedQuad(string text)
    {
        var parts = text.Split('.');
        if (parts.Length != 4)
            return false;
        foreach (var part in parts)
            if (!int.TryParse(part.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var o)
                || o is < 0 or > 255)
                return false;
        return true;
    }

    // Editor selection flags (the view binds IsVisible to these).
    public bool IsBool => Type == GrblSettingType.Bool;
    public bool IsRadio => Type == GrblSettingType.RadioButtons;
    public bool IsBits => (Type is GrblSettingType.Bitfield or GrblSettingType.XBitfield or GrblSettingType.AxisMask) && Bits.Count > 0;
    public bool IsNumeric => Type is GrblSettingType.Integer or GrblSettingType.Float;
    public bool ShowTextBox => !IsBool && !IsRadio && !IsBits;

    public bool IsChecked
    {
        get => Value == "1";
        set => Value = value ? "1" : "0";
    }

    public int SelectedIndex
    {
        get => int.TryParse(Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) ? i : -1;
        set => Value = value.ToString(CultureInfo.InvariantCulture);
    }

    public string? Description
    {
        get => _description;
        set => SetField(ref _description, value);
    }

    public GrblSettingItem(GrblSetting setting, int numAxes)
    {
        Id = setting.Id;
        Name = setting.Name;
        Unit = setting.Unit;
        Type = setting.DataType;
        Format = setting.Format;
        Min = setting.Min;
        Max = setting.Max;
        RebootRequired = setting.RebootRequired;
        Description = setting.Description;
        _value = OriginalValue = setting.Value;

        Options = Type switch
        {
            GrblSettingType.RadioButtons => SplitOptions(Format),
            GrblSettingType.AxisMask => AxisLetters(numAxes),
            GrblSettingType.Bitfield or GrblSettingType.XBitfield => SplitOptions(Format),
            _ => [],
        };

        if (Options.Length > 0)
        {
            var value = int.TryParse(_value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0;
            for (var i = 0; i < Options.Length; i++)
            {
                // grblHAL marks unused bits "N/A" — skip them so no placeholder checkbox
                // is shown, but keep the real bits at their original mask positions.
                if (IsPlaceholder(Options[i]))
                    continue;
                Bits.Add(new GrblSettingBit(this, Options[i], 1 << i, (value & (1 << i)) != 0));
            }
        }
    }

    /// <summary>
    /// Flip just the toggled bit relative to the current value — never recompute from
    /// the visible bits, so hidden/unrendered bits (N/A positions, out-of-range axes)
    /// are preserved and no setting is spuriously marked dirty.
    /// </summary>
    internal void OnBitToggled(GrblSettingBit changed)
    {
        var current = int.TryParse(_value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0;
        current = changed.IsChecked ? current | changed.Mask : current & ~changed.Mask;
        _value = current.ToString(CultureInfo.InvariantCulture);
        OnPropertyChanged(nameof(Value));
        OnPropertyChanged(nameof(IsDirty));
    }

    private static bool IsPlaceholder(string label) =>
        string.IsNullOrEmpty(label) || label.Equals("n/a", StringComparison.OrdinalIgnoreCase);

    private void ResyncBits()
    {
        var value = int.TryParse(_value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0;
        foreach (var bit in Bits)
            bit.SetSilently((value & bit.Mask) != 0);
    }

    private static string[] SplitOptions(string format) =>
        string.IsNullOrWhiteSpace(format) ? [] : format.Split(',');

    private static string[] AxisLetters(int numAxes)
    {
        const string letters = "XYZABC";
        var count = Math.Clamp(numAxes, 1, letters.Length);
        return Enumerable.Range(0, count).Select(i => letters[i].ToString()).ToArray();
    }
}

/// <summary>
/// GRBL settings widget view model: loads the settings tree via
/// <see cref="GrblSettingsLoader"/> (controller metadata on grblHAL, embedded English
/// catalog on classic grbl), renders it as a searchable tree with typed editors, and
/// saves only the dirty settings as <c>$id=value</c> writes.
/// </summary>
public sealed class GrblSettingsViewModel : ViewModelBase
{
    private readonly IGrblSettingsProvider _provider;
    private readonly IGrblService _grbl;
    private readonly LocalizationService _localization;
    private readonly IFileDialogService? _dialogs;
    private readonly List<GrblSettingItem> _allItems = new();
    private readonly List<GrblSettingGroupItem> _allRoots = new();
    private GrblRestorePlan? _restorePlan;
    private string _search = string.Empty;
    private string _status = string.Empty;
    private string? _restoreSummary;
    private string? _loadError;
    private object? _selectedNode;

    /// <summary>The visible (filtered) tree roots bound to the TreeView.</summary>
    public ObservableCollection<object> Groups { get; } = new();

    public string Search
    {
        get => _search;
        set
        {
            if (SetField(ref _search, value))
                ApplyFilter();
        }
    }

    public string Status
    {
        get => _status;
        private set => SetField(ref _status, value);
    }

    /// <summary>TreeView SelectedItem — only leaves (settings) select; a group click is discarded.</summary>
    public object? SelectedNode
    {
        get => _selectedNode;
        set
        {
            if (!SetField(ref _selectedNode, value))
                return;
            if (value is GrblSettingItem item)
            {
                SelectedSetting = item;
                _ = LoadDescriptionAsync(item);
            }
        }
    }

    private GrblSettingItem? _selectedSetting;

    /// <summary>
    /// The chosen setting shown in the detail pane (right side). Must raise
    /// PropertyChanged on swap or every <c>SelectedSetting.*</c> binding in the
    /// detail pane would keep showing the previous selection.
    /// </summary>
    public GrblSettingItem? SelectedSetting
    {
        get => _selectedSetting;
        private set
        {
            if (SetField(ref _selectedSetting, value))
            {
                OnPropertyChanged(nameof(HasSelection));
                OnPropertyChanged(nameof(HasNoSelection));
                OnPropertyChanged(nameof(SelectedEditSummary));
            }
        }
    }

    /// <summary>True when a setting is selected — toggles the detail pane vs the empty hint.</summary>
    public bool HasSelection => SelectedSetting is not null;

    public bool HasNoSelection => SelectedSetting is null;

    /// <summary>Localized "Editing $N" header for the selected setting (fallback: plain key).</summary>
    public string SelectedEditSummary
    {
        get
        {
            if (SelectedSetting is not { } item)
                return string.Empty;
            var format = _localization.T("module.grbl-settings.editing");
            return string.Equals(format, "module.grbl-settings.editing", StringComparison.Ordinal)
                ? item.Key
                : string.Format(format, item.Key);
        }
    }

    public DelegateCommand LoadCommand { get; }
    public DelegateCommand SaveCommand { get; }
    public DelegateCommand BackupCommand { get; }
    public DelegateCommand RestoreCommand { get; }
    public DelegateCommand ApplyRestoreCommand { get; }
    public DelegateCommand CancelRestoreCommand { get; }

    /// <summary>
    /// Back up / restore are only meaningful against a live controller. The dialog is
    /// owner-modal and cannot observe a connection change while it is open, so this is a
    /// snapshot the view uses for button enablement; the commands re-check at run time.
    /// </summary>
    public bool CanTransfer => _grbl.IsConnected;

    // Button labels are view-model strings (not {loc:Loc} markup): a markup extension that
    // returns a binding is not applied to Button.Content in this Avalonia version — the
    // binding object lands as the content and never resolves. Resolving here keeps the
    // labels localized and directly assertable. The keys already exist in all catalogs.
    public string BackupText => _localization.Resolve("module.grbl-settings.backup", "Back up…");
    public string RestoreText => _localization.Resolve("module.grbl-settings.restore", "Restore…");
    public string ApplyRestoreText => _localization.Resolve("module.grbl-settings.restoreApply", "Apply restore");
    public string CancelRestoreText => _localization.Resolve("module.grbl-settings.restoreCancel", "Cancel");
    public string LoadText => _localization.Resolve("module.grbl-settings.load", "Load");
    public string SaveText => _localization.Resolve("module.grbl-settings.saveChanges", "Save changes");

    /// <summary>A parsed backup is waiting for Apply / Cancel — shows the inline confirm strip.</summary>
    public bool HasRestorePlan => _restorePlan is not null;

    /// <summary>The confirm strip's summary line (counts + fingerprint and unsaved-edit warnings).</summary>
    public string? RestoreSummary
    {
        get => _restoreSummary;
        private set => SetField(ref _restoreSummary, value);
    }

    /// <param name="dialogs">
    /// File picker for backup / restore. Optional trailing parameter: the module injects the
    /// host's service, and every existing construction (tests, headless views) stays valid
    /// without one — the commands then behave as if the picker was cancelled.
    /// </param>
    public GrblSettingsViewModel(IGrblSettingsProvider provider, IGrblService grbl,
        LocalizationService localization, IFileDialogService? dialogs = null)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _grbl = grbl ?? throw new ArgumentNullException(nameof(grbl));
        _localization = localization ?? throw new ArgumentNullException(nameof(localization));
        _dialogs = dialogs;

        LoadCommand = new DelegateCommand(_ => _ = LoadAsync());
        SaveCommand = new DelegateCommand(_ => _ = SaveAsync());
        BackupCommand = new DelegateCommand(_ => _ = BackupAsync());
        RestoreCommand = new DelegateCommand(_ => _ = RestoreAsync());
        ApplyRestoreCommand = new DelegateCommand(_ => _ = ApplyRestoreAsync());
        CancelRestoreCommand = new DelegateCommand(_ => CancelRestore());
        _ = LoadAsync();
    }

    public async Task<bool> LoadAsync()
    {
        // Reload builds brand-new item/group instances, so the current selection (a
        // reference into the old tree) would be detached — the tree would collapse and
        // the detail pane would show a stale row. Remember the id and restore it below.
        var selectedId = _selectedSetting?.Id;

        try
        {
            var snapshot = await new GrblSettingsLoader(_provider, _localization).LoadAsync();

            _allItems.Clear();
            _allRoots.Clear();

            var numAxes = _grbl.Capabilities.NumAxes;
            foreach (var setting in snapshot.Settings)
                _allItems.Add(new GrblSettingItem(setting, numAxes));

            var itemsById = _allItems.ToDictionary(static i => i.Id);
            foreach (var root in snapshot.TopGroups)
                _allRoots.Add(BuildGroupItem(root, itemsById));

            ApplyFilter();
            RestoreSelection(selectedId);
            var source = snapshot.MetadataFromController ? "(from controller)" : "(from catalog)";
            Status = LocText.Resolve("module.grbl-settings.loaded", "Loaded {0} settings")
                .Replace("{0}", _allItems.Count.ToString(CultureInfo.InvariantCulture)) + " " + source;
            _loadError = null;
            return true;
        }
        catch (Exception ex)
        {
            // False tells a caller that needs the fresh values (the restore verify) that
            // _allItems still holds the pre-reload tree, so it must not compare against it.
            _loadError = ex.Message;
            Status = LocText.Format("module.grbl-settings.loadFailed", "Load failed: {0}", ex.Message);
            return false;
        }
    }

    /// <summary>
    /// After a reload, keep the previously selected setting selected: find the same id
    /// in the freshly built (visible) tree, open its group chain, and re-select it so
    /// the tree no longer collapses and the detail pane stays in sync. If the item is
    /// no longer present (e.g. filtered out), clear the selection instead of leaving
    /// stale details on screen.
    /// </summary>
    private void RestoreSelection(int? selectedId)
    {
        if (selectedId is not { } id)
        {
            ClearSelection();
            return;
        }

        if (FindVisible(id) is { } found)
        {
            foreach (var group in found.Path)
                group.IsExpanded = true;
            SelectedNode = found.Item; // re-selecting also updates SelectedSetting
            return;
        }

        ClearSelection();
    }

    /// <summary>Locate a setting by id under a tree node, returning it and its ancestor group path (root-first).</summary>
    private static (GrblSettingItem Item, List<GrblSettingGroupItem> Path)? FindPath(object node, int id)
    {
        switch (node)
        {
            case GrblSettingItem item when item.Id == id:
                return (item, new List<GrblSettingGroupItem>());
            case GrblSettingGroupItem group:
                var path = new List<GrblSettingGroupItem>() { group };
                return FindWithin(group, id, path) is { } found
                    ? (found.Item, found.Path)
                    : null;
            default:
                return null;
        }
    }

    private static (GrblSettingItem Item, List<GrblSettingGroupItem> Path)? FindWithin(
        GrblSettingGroupItem group, int id, List<GrblSettingGroupItem> path)
    {
        foreach (var child in group.Children)
        {
            switch (child)
            {
                case GrblSettingItem item when item.Id == id:
                    return (item, new List<GrblSettingGroupItem>(path));
                case GrblSettingGroupItem childGroup:
                    path.Add(childGroup);
                    if (FindWithin(childGroup, id, path) is { } found)
                        return found;
                    path.RemoveAt(path.Count - 1);
                    break;
            }
        }
        return null;
    }

    private void ClearSelection()
    {
        SelectedNode = null;
        SelectedSetting = null;
    }

    public async Task SaveAsync()
    {
        try
        {
            var dirty = _allItems.Where(static i => i.IsDirty).OrderBy(static i => i.Id).ToList();
            if (dirty.Count == 0)
            {
                Status = LocText.Resolve("module.grbl-settings.noChanges", "No changes");
                return;
            }

            // Validate the whole dirty set before writing any of it. Checking inside the
            // send loop would be too late: items are ordered by id, so a valid setting
            // sorted ahead of a bad one would already be on the controller - and
            // Accept()ed - by the time the bad value was reached.
            var invalid = dirty
                .Where(static i => i.Validate() is not GrblSettingValidationError.None)
                .ToList();
            if (invalid.Count > 0)
            {
                Status = LocText.Format(
                    "module.grbl-settings.notSaved",
                    "Not saved — invalid value: {0}",
                    string.Join(", ", invalid.Select(DescribeInvalid)));
                SelectFirstInvalid(invalid[0]);
                return;
            }

            foreach (var item in dirty)
            {
                await _grbl.SendAsync($"${item.Id}={item.Value}");
                item.Accept();
            }

            Status = LocText.Format(
                dirty.Count == 1 ? "module.grbl-settings.savedOne" : "module.grbl-settings.savedMany",
                dirty.Count == 1 ? "Saved 1 setting" : "Saved {0} settings",
                dirty.Count);
        }
        catch (Exception ex)
        {
            Status = LocText.Format("module.grbl-settings.saveFailed", "Save failed: {0}", ex.Message);
        }
    }

    /// <summary>One rejected setting as "$1 (must be a number)", ready for the status line.</summary>
    private static string DescribeInvalid(GrblSettingItem item)
    {
        var reason = item.Validate() switch
        {
            GrblSettingValidationError.OutOfRange when item.Min is not null && item.Max is not null =>
                LocText.Format("module.grbl-settings.reason.outOfRange", "must be between {0} and {1}",
                    GrblSettingItem.FormatBound(item.Min), GrblSettingItem.FormatBound(item.Max)),
            GrblSettingValidationError.OutOfRange when item.Min is not null =>
                LocText.Format("module.grbl-settings.reason.minOnly", "must be at least {0}",
                    GrblSettingItem.FormatBound(item.Min)),
            GrblSettingValidationError.OutOfRange =>
                LocText.Format("module.grbl-settings.reason.maxOnly", "must be at most {0}",
                    GrblSettingItem.FormatBound(item.Max)),
            GrblSettingValidationError.MalformedIp =>
                LocText.Resolve("module.grbl-settings.reason.invalidIp", "must be a dotted-quad address"),
            _ => LocText.Resolve("module.grbl-settings.reason.notNumber", "must be a number"),
        };
        return $"{item.Key} ({reason})";
    }

    /// <summary>
    /// Write the controller's <em>current</em> settings to a JSON backup file. The read is
    /// always fresh (<see cref="GrblSettingsLoader"/>, never the loaded tree or its possibly
    /// edited values) and nothing is auto-saved first — so unsaved editor edits are never in
    /// the file and survive the backup untouched; the status line reports how many were left out.
    /// </summary>
    public async Task BackupAsync()
    {
        if (!_grbl.IsConnected)
        {
            Status = LocText.Resolve("module.grbl-settings.notConnected", "Connect to a controller first.");
            return;
        }

        if (_dialogs is null)
            return; // no picker in this host (headless / tests): behaves as a cancelled dialog

        try
        {
            var snapshot = await new GrblSettingsLoader(_provider, _localization).LoadAsync();
            var backedUp = snapshot.Settings.Count;
            if (backedUp == 0)
            {
                Status = LocText.Resolve("module.grbl-settings.backupEmpty", "The controller reported no settings.");
                return;
            }

            // Counted (never saved) after the fresh read: a backup must not touch the
            // editor tree, so the unsaved edits are reported, not reconciled.
            var excluded = _allItems.Count(static i => i.IsDirty);

            var path = await _dialogs.SaveFileAsync(
                LocText.Resolve("module.grbl-settings.backup", "Back up…"),
                $"grbl-settings-{DateTime.Now:yyyy-MM-dd}.json",
                "SenderNG grbl settings|*.json");
            if (string.IsNullOrEmpty(path))
                return; // cancelled — leave the status as it was

            var json = GrblSettingsBackup.Create(
                snapshot, _grbl.Capabilities, AppVersion, DateTimeOffset.UtcNow).ToJson();
            await File.WriteAllTextAsync(path, json);

            Status = LocText.Format("module.grbl-settings.backupSaved",
                "Backed up {0} settings from the controller to {1}.", backedUp, path);
            if (excluded > 0)
                Status += " " + LocText.Format("module.grbl-settings.backupExcluded",
                    "{0} unsaved edits were not included.", excluded);
        }
        catch (Exception ex)
        {
            Status = LocText.Format("module.grbl-settings.backupFailed", "Backup failed: {0}", ex.Message);
        }
    }

    /// <summary>
    /// Read a backup file and show an inline plan (apply / unchanged / unknown / invalid).
    /// Nothing reaches the controller here — the operator confirms with
    /// <see cref="ApplyRestoreAsync"/> or discards the plan with <see cref="CancelRestore"/>.
    /// </summary>
    public async Task RestoreAsync()
    {
        if (!_grbl.IsConnected)
        {
            Status = LocText.Resolve("module.grbl-settings.notConnected", "Connect to a controller first.");
            return;
        }

        if (_dialogs is null)
            return;

        try
        {
            var path = await _dialogs.OpenFileAsync(
                LocText.Resolve("module.grbl-settings.restore", "Restore…"), "SenderNG grbl settings|*.json");
            if (string.IsNullOrEmpty(path))
                return; // cancelled — leave the status as it was

            var content = await File.ReadAllTextAsync(path);
            var parsed = GrblSettingsBackup.Parse(content);
            if (!parsed.Ok)
            {
                Status = LocText.Format("module.grbl-settings.restoreInvalidFile",
                    "Not a SenderNG grbl settings backup: {0}", parsed.Error ?? path);
                return;
            }

            _restorePlan = GrblRestorePlan.Create(parsed.Backup!, _allItems, _grbl.Capabilities.ControllerKey);
            OnPropertyChanged(nameof(HasRestorePlan));
            RestoreSummary = BuildRestoreSummary(_restorePlan);
        }
        catch (Exception ex)
        {
            _restorePlan = null;
            OnPropertyChanged(nameof(HasRestorePlan));
            RestoreSummary = null;
            Status = LocText.Format("module.grbl-settings.restoreFailed", "Restore failed: {0}", ex.Message);
        }
    }

    /// <summary>
    /// Send exactly the planned writes, ascending id, each in its own try/catch so one
    /// rejected setting cannot abort the rest. A dropped link aborts the remaining writes
    /// and reports how many landed; afterwards the board is re-read (which also refreshes
    /// the tree and clears dirty state) and the applied ids are checked against their
    /// intended values, and the applied set is checked for reboot-required settings.
    /// </summary>
    public async Task ApplyRestoreAsync()
    {
        if (_restorePlan is not { } plan)
            return;

        _restorePlan = null;
        OnPropertyChanged(nameof(HasRestorePlan));

        var applied = new List<GrblRestorePlanItem>();
        var skipped = new List<string>();
        var connectionLost = false;

        foreach (var item in plan.Writes)
        {
            try
            {
                await _grbl.SendAsync($"${item.Id}={item.Value}");
                applied.Add(item);
            }
            catch (InvalidOperationException ex)
            {
                // The link dropped mid-restore: everything after this is unreachable.
                skipped.Add($"${item.Id} ({ExplainError(ex.Message)})");
                connectionLost = true;
                break;
            }
            catch (Exception ex)
            {
                skipped.Add($"${item.Id} ({ExplainError(ex.Message)})");
            }
        }

        RestoreSummary = null;

        if (connectionLost)
        {
            // The board cannot be re-read on a dead link, so skip the verify step and
            // report the real progress: what landed, and how far the restore got.
            var summary = LocText.Format("module.grbl-settings.restoreFailed",
                "Restore failed: {0}", skipped.Count > 0 ? skipped[0] : string.Empty);
            Status = summary + " " + LocText.Format("module.grbl-settings.restored",
                "Restored {0} settings.", applied.Count);
            return;
        }

        // Capture the reboot flag from the LIVE board metadata before the reload replaces
        // every item instance (the backup's own copy is informational, not the board's).
        var rebootIds = applied
            .Where(item => _allItems.FirstOrDefault(i => i.Id == item.Id)?.RebootRequired == true)
            .Select(static item => item.Id)
            .ToHashSet();

        // Re-read the board: LoadAsync rebuilds the tree from $$, so the restored rows
        // come back clean and show what the controller actually holds now.
        var reloaded = await LoadAsync();

        // Compare the board's fresh values against what the restore intended: a controller
        // can accept a write and still not hold the value (or clamp it). A failed re-read
        // leaves the pre-reload tree in place, so that comparison would mark every applied
        // setting as "did not stick" — skip it and report the load failure instead.
        var missed = reloaded
            ? applied
                .Where(item => _allItems.FirstOrDefault(i => i.Id == item.Id) is not { } board
                    || !string.Equals(board.Value, item.Value, StringComparison.Ordinal))
                .Select(static item => $"${item.Id}")
                .ToList()
            : [];

        var status = LocText.Format("module.grbl-settings.restored", "Restored {0} settings.", applied.Count);
        if (skipped.Count > 0)
            status += " " + LocText.Format("module.grbl-settings.restoreSkipped",
                "{0} settings were not applied: {1}", skipped.Count, string.Join(", ", skipped));
        if (!reloaded)
            status += " " + LocText.Format("module.grbl-settings.loadFailed",
                "Load failed: {0}", _loadError ?? string.Empty);
        if (missed.Count > 0)
            status += " " + LocText.Format("module.grbl-settings.restoreVerifyMismatch",
                "{0} settings did not take the restored value: {1}", missed.Count, string.Join(", ", missed));
        if (rebootIds.Count > 0)
            status += " " + LocText.Resolve("module.grbl-settings.rebootRequired",
                "Some restored settings need a controller restart to take effect.");
        Status = status;
    }

    /// <summary>Discard a pending restore plan without sending anything.</summary>
    public void CancelRestore()
    {
        if (_restorePlan is null)
            return;
        _restorePlan = null;
        RestoreSummary = null;
        OnPropertyChanged(nameof(HasRestorePlan));
    }

    /// <summary>
    /// The confirm strip's summary: what a restore would write, what it skips, plus the
    /// fingerprint and unsaved-edit warnings. The mismatch is a warning only — restoring a
    /// known-good file onto a rebuilt board is the point of the feature.
    /// </summary>
    private string BuildRestoreSummary(GrblRestorePlan plan)
    {
        var summary = LocText.Format("module.grbl-settings.restorePending",
            "Restore {0} of {1} settings? {2} unchanged · {3} not on this controller · {4} invalid.",
            plan.ApplyCount, plan.Items.Count, plan.UnchangedCount, plan.UnknownCount, plan.InvalidCount);

        if (plan.ControllerMismatch)
            summary += " " + LocText.Format("module.grbl-settings.restoreMismatch",
                "The backup came from a different controller ({0}).", plan.Items.Count);

        var dirty = _allItems.Count(static i => i.IsDirty);
        if (dirty > 0)
            summary += " " + LocText.Format("module.grbl-settings.restoreDiscardsEdits",
                "{0} unsaved edits will be discarded.", dirty);

        return summary;
    }

    /// <summary>
    /// Explain a rejected <c>$N</c> write: the controller's own text from the shared
    /// <see cref="GrblErrorCodes"/> table when the reply is an <c>error:NN</c>, else the raw
    /// text. No second error table is introduced here.
    /// </summary>
    private static string ExplainError(string reply)
    {
        var match = GrblErrorCodes.ErrorReply.Match(reply);
        if (match.Success && int.TryParse(match.Groups[1].Value, out var code)
            && GrblErrorCodes.Describe(code) is { } described)
            return described;
        return reply;
    }

    /// <summary>App version stamped into a backup file (same precedent as the About dialog).</summary>
    private static string AppVersion =>
        typeof(GrblSettingsViewModel).Assembly.GetName().Version?.ToString() ?? "0.0.0";

    private void SelectFirstInvalid(GrblSettingItem item)
    {
        if (FindVisible(item.Id) is null)
            Search = string.Empty; // re-runs ApplyFilter, revealing every group

        if (FindVisible(item.Id) is { } found)
            foreach (var group in found.Path)
                group.IsExpanded = true;

        SelectedNode = item;
    }

    /// <summary>Locate a setting id under the currently visible (filtered) tree roots.</summary>
    private (GrblSettingItem Item, List<GrblSettingGroupItem> Path)? FindVisible(int id)
    {
        foreach (var root in Groups)
            if (FindPath(root, id) is { } found)
                return found;
        return null;
    }

    /// <summary>
    /// Lazily fetch the long description for a setting whose metadata came from the
    /// controller ($SED on grblHAL). Catalog-sourced settings (classic grbl) already
    /// carry their description.
    /// </summary>
    private async Task LoadDescriptionAsync(GrblSettingItem item)
    {
        if (item.Description is not null)
            return;

        try
        {
            var line = await _provider.GetSettingDescriptionAsync(item.Id);
            if (GrblSettingsParser.ParseDescriptionLine(line) is { } parsed)
                item.Description = parsed.Description;
        }
        catch
        {
            // A failed $SED is non-fatal — the row stays without a description.
        }
    }

    private void ApplyFilter()
    {
        var search = Search.Trim();
        var roots = new List<object>();
        foreach (var root in _allRoots)
        {
            if (search.Length == 0)
            {
                roots.Add(root); // original instances keep their expansion state
                continue;
            }
            if (FilterGroup(root, search) is { } match)
                roots.Add(match);
        }

        Groups.Clear();
        foreach (var root in roots)
            Groups.Add(root);
    }

    /// <summary>Clone a group that matches or contains matching children; auto-expanded so results are visible.</summary>
    private static GrblSettingGroupItem? FilterGroup(GrblSettingGroupItem group, string search)
    {
        var matches = Contains(group.Name, search);
        var children = new List<object>();
        foreach (var child in group.Children)
        {
            switch (child)
            {
                case GrblSettingGroupItem childGroup when FilterGroup(childGroup, search) is { } match:
                    children.Add(match);
                    break;
                case GrblSettingItem setting when Matches(setting, search):
                    children.Add(setting);
                    break;
            }
        }

        if (!matches && children.Count == 0)
            return null;

        var clone = new GrblSettingGroupItem(group.Name) { IsExpanded = true };
        foreach (var child in children)
            clone.Children.Add(child);
        return clone;
    }

    private static bool Matches(GrblSettingItem item, string search) =>
        Contains(item.Name, search)
        || Contains(item.Description, search)
        || item.Key.Contains(search, StringComparison.OrdinalIgnoreCase)
        || item.Id.ToString(CultureInfo.InvariantCulture).Contains(search, StringComparison.OrdinalIgnoreCase);

    private static bool Contains(string? text, string search) =>
        text?.Contains(search, StringComparison.OrdinalIgnoreCase) == true;

    private static GrblSettingGroupItem BuildGroupItem(GrblSettingGroup group, Dictionary<int, GrblSettingItem> itemsById)
    {
        var node = new GrblSettingGroupItem(group.Name);
        foreach (var childGroup in group.Children)
            node.Children.Add(BuildGroupItem(childGroup, itemsById));
        foreach (var setting in group.Settings)
        {
            if (itemsById.TryGetValue(setting.Id, out var item))
                node.Children.Add(item);
        }
        return node;
    }
}
