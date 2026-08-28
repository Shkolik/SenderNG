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
using SenderNG.Avalonia.ViewModels;
using SenderNG.Core.Commands;
using SenderNG.Core.Localization;
using SenderNG.Core.Settings;

namespace SenderNG.Avalonia.Modules.Fixed;

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
            _owner.OnBitToggled();
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

    /// <summary>Radio option labels or bitfield labels (axis letters for axis masks).</summary>
    public string[] Options { get; }

    /// <summary>Original value from the board — the dirty comparison baseline.</summary>
    public string OriginalValue { get; private set; }

    public bool HasUnit => !string.IsNullOrWhiteSpace(Unit);
    public bool HasRange => Min is not null || Max is not null;
    public bool HasDescription => !string.IsNullOrWhiteSpace(Description);

    public string? Range
    {
        get
        {
            static string Fmt(double? v) => v?.ToString("0.######", CultureInfo.InvariantCulture) ?? "-";
            return HasRange ? $"Range: {Fmt(Min)} .. {Fmt(Max)}" : null;
        }
    }

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
                Bits.Add(new GrblSettingBit(this, Options[i], 1 << i, (value & (1 << i)) != 0));
        }
    }

    /// <summary>Recompute the raw value from the bit checkboxes (no resync bounce).</summary>
    internal void OnBitToggled()
    {
        var value = 0;
        foreach (var bit in Bits)
        {
            if (bit.IsChecked)
                value |= bit.Mask;
        }
        _value = value.ToString(CultureInfo.InvariantCulture);
        OnPropertyChanged(nameof(Value));
        OnPropertyChanged(nameof(IsDirty));
    }

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
    private readonly List<GrblSettingItem> _allItems = new();
    private readonly List<GrblSettingGroupItem> _allRoots = new();
    private string _search = string.Empty;
    private string _status = string.Empty;
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
                OnPropertyChanged(nameof(HasSelection));
                OnPropertyChanged(nameof(HasNoSelection));
                _ = LoadDescriptionAsync(item);
            }
        }
    }

    /// <summary>The chosen setting shown in the detail pane (right side).</summary>
    public GrblSettingItem? SelectedSetting { get; private set; }

    /// <summary>True when a setting is selected — toggles the detail pane vs the empty hint.</summary>
    public bool HasSelection => SelectedSetting is not null;

    public bool HasNoSelection => SelectedSetting is null;

    public DelegateCommand LoadCommand { get; }
    public DelegateCommand SaveCommand { get; }

    public GrblSettingsViewModel(IGrblSettingsProvider provider, IGrblService grbl, LocalizationService localization)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _grbl = grbl ?? throw new ArgumentNullException(nameof(grbl));
        _localization = localization ?? throw new ArgumentNullException(nameof(localization));

        LoadCommand = new DelegateCommand(_ => _ = LoadAsync());
        SaveCommand = new DelegateCommand(_ => _ = SaveAsync());
        _ = LoadAsync();
    }

    public async Task LoadAsync()
    {
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
            Status = $"Loaded {_allItems.Count} settings " +
                     (snapshot.MetadataFromController ? "(from controller)" : "(from catalog)");
        }
        catch (Exception ex)
        {
            Status = $"Load failed: {ex.Message}";
        }
    }

    public async Task SaveAsync()
    {
        try
        {
            var dirty = _allItems.Where(static i => i.IsDirty).OrderBy(static i => i.Id).ToList();
            if (dirty.Count == 0)
            {
                Status = "No changes";
                return;
            }

            foreach (var item in dirty)
            {
                await _grbl.SendAsync($"${item.Id}={item.Value}");
                item.Accept();
            }

            Status = dirty.Count == 1 ? "Saved 1 setting" : $"Saved {dirty.Count} settings";
        }
        catch (Exception ex)
        {
            Status = $"Save failed: {ex.Message}";
        }
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
