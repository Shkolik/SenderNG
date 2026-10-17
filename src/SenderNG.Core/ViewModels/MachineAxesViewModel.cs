/*
 * MachineAxesViewModel.cs - machine profile axis editor (topology card + axis lists)
 */

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
using System.Globalization;
using System.Linq;
using CNC.Core.Services;
using SenderNG.Core.Commands;

namespace CNC.Core.ViewModels;

/* ---------------------------------------------------------------------------
 * Edit-time choices
 * ------------------------------------------------------------------------- */

/// <summary>
/// One entry of the edit-time axis-index picker: a conventional grbl letter and the
/// index it means. The picker offers the letters because that is how firmware docs
/// speak; the profile stores the <b>index</b>. The letter an axis actually displays is
/// the controller's, derived at render time — never persisted.
/// </summary>
public sealed class AxisIndexChoice
{
    public AxisIndexChoice(int index, char letter)
    {
        Index = index;
        Letter = letter;
    }

    /// <summary>The grbl axis index this choice stores.</summary>
    public int Index { get; }

    /// <summary>The conventional letter that means <see cref="Index"/>.</summary>
    public char Letter { get; }

    /// <summary>Picker label: the letter the user thinks in, plus the index it stores.</summary>
    public string Display => $"{Letter} ({Index.ToString(CultureInfo.CurrentCulture)})";
}

/// <summary>One kind choice, labelled in the active locale.</summary>
public sealed class AxisKindChoice
{
    private readonly Func<string> _label;

    public AxisKindChoice(AxisKind value, Func<string> label)
    {
        Value = value;
        _label = label;
    }

    public AxisKind Value { get; }
    public string Display => _label();
}

/// <summary>One direction choice, labelled in the active locale.</summary>
public sealed class AxisDirectionChoice
{
    private readonly Func<string> _label;

    public AxisDirectionChoice(AxisDirection value, Func<string> label)
    {
        Value = value;
        _label = label;
    }

    public AxisDirection Value { get; }
    public string Display => _label();
}

/// <summary>
/// One validation problem, rendered as text. <see cref="KinematicsProblem"/> is
/// returned by the resolver as <b>data</b>; the editor localizes its
/// <see cref="KinematicsProblem.Code"/> and never parses the developer message — a
/// hand-edited profile file with an impossible layout shows up here instead of crashing.
/// </summary>
public sealed class MachineAxisProblem
{
    public MachineAxisProblem(bool isError, string text)
    {
        IsError = isError;
        Text = text;
    }

    /// <summary>Error = unusable as written; false = a warning the consumers tolerate.</summary>
    public bool IsError { get; }

    public bool IsWarning => !IsError;

    public string Text { get; }
}

/* ---------------------------------------------------------------------------
 * Rows
 * ------------------------------------------------------------------------- */

/// <summary>One plane of the machine: its title and its locked, ordered axis rows.</summary>
public sealed class MachinePlaneViewModel : ViewModelBase
{
    private readonly MachineAxesViewModel _owner;

    internal MachinePlaneViewModel(MachineAxesViewModel owner, int number)
    {
        _owner = owner;
        Number = number;
    }

    /// <summary>1-based plane number, as the topology picture labels the columns.</summary>
    public int Number { get; }

    public string Title => _owner.Resolve("appSettings.axes.plane", "Plane {0}")
        .Replace("{0}", Number.ToString(CultureInfo.CurrentCulture));

    /// <summary>Ordered axes: slot 0 is the plane's horizontal, slot 1 its vertical,
    /// optional slot 2 the perpendicular. The order is the plane's orientation.</summary>
    public ObservableCollection<MachineAxisRowViewModel> Axes { get; } = new();

    internal void Refresh() => OnPropertyChanged(nameof(Title));
}

/// <summary>
/// One axis row: the index picker (letters offered, index stored), kind, role, the
/// inverted-direction checkbox and the optional inbound CAM alias, plus the two pivot
/// inputs of a rotary axis. A base row is locked by the preset — no delete, no reorder —
/// while an optional row is removable.
/// </summary>
public sealed class MachineAxisRowViewModel : ViewModelBase
{
    private readonly MachineAxesViewModel _owner;
    private readonly MachineAxis _axis;

    // ObservableCollection, not a plain List: Pivots keeps the same instance for the life of
    // the row, so mutating a List behind the property is invisible to the ItemsControl that
    // renders it — the containers would keep the pre-change pivot VMs and their labels would
    // only correct themselves when the whole card is rebuilt (a reload). The collection
    // notifications are what make a Role change re-letter the pivot labels live.
    private readonly ObservableCollection<AxisPivotRowViewModel> _pivots = new();

    internal MachineAxisRowViewModel(MachineAxesViewModel owner, MachineAxis axis, bool isBase)
    {
        _owner = owner;
        _axis = axis;
        IsBase = isBase;
        RemoveCommand = new DelegateCommand(_ => _owner.RemoveOptional(this), _ => CanRemove && _owner.IsEditable);
        RebuildPivots();
    }

    /// <summary>The profile axis this row edits (the persisted model object).</summary>
    public MachineAxis Model => _axis;

    /// <summary>True for a preset base axis: count and order are fixed, no delete.</summary>
    public bool IsBase { get; }

    public bool CanRemove => !IsBase;

    public bool IsRotary => _axis.Kind == AxisKind.Rotary;

    public int Index => _axis.Index;
    public AxisKind Kind => _axis.Kind;
    public AxisDirection Direction => _axis.Direction;
    public int Sign => _axis.Sign;
    public char? Alias => _axis.Alias;

    /// <summary>The letter the <b>controller</b> reports for this index (conventional
    /// when it reported none) — the only letter this row displays as the axis's name.</summary>
    public string BoardLetterText => _owner.Kinematics.LetterFor(_axis.Index).ToString();

    /// <summary>The Direction column's checkbox: checked means the axis counts negative
    /// (<c>Sign &lt; 0</c>). Display only — the write goes through <see cref="SetInverted"/>.</summary>
    public bool IsInverted => _axis.Sign < 0;

    public string AliasText => _axis.Alias?.ToString() ?? string.Empty;

    /// <summary>Tooltip: the locking rule for a base row, the placement of an optional one.</summary>
    public string RowHint => IsBase
        ? _owner.Resolve("appSettings.axes.baseHint",
            "Fixed by the machine type: the axis count and its order are locked — only the index it uses is yours.")
        : _owner.Resolve("appSettings.axes.optionalRowHint",
            "An optional axis: outside every plane. A rotary axis always lives here.");

    /// <summary>Delete-button label (localized; short, to keep the optional rows compact).</summary>
    public string RemoveText => _owner.Resolve("appSettings.axes.remove", "Del");

    public IReadOnlyList<AxisIndexChoice> IndexChoices => _owner.IndexChoices;
    public IReadOnlyList<AxisKindChoice> KindChoices => _owner.KindChoices;
    public IReadOnlyList<AxisDirectionChoice> DirectionChoices => _owner.DirectionChoices;

    /// <summary>Current index as the picker's choice, or null when the stored index is
    /// outside 0..8 (a hand-edited file — the resolver reports it, the row shows blank).</summary>
    public AxisIndexChoice? SelectedIndex =>
        _owner.IndexChoices.FirstOrDefault(c => c.Index == _axis.Index);

    public AxisKindChoice? SelectedKind =>
        _owner.KindChoices.FirstOrDefault(c => c.Value == _axis.Kind);

    public AxisDirectionChoice? SelectedDirection =>
        _owner.DirectionChoices.FirstOrDefault(c => c.Value == _axis.Direction);

    /// <summary>Exactly the two perpendicular pivot inputs of a rotary axis; empty for a
    /// linear one (invariant 7: a linear axis carries no rotation origin).</summary>
    public IReadOnlyList<AxisPivotRowViewModel> Pivots => _pivots;

    public DelegateCommand RemoveCommand { get; }

    /// <summary>Store a new grbl index (the picker's letter resolved to its index).</summary>
    public void ApplyIndex(int index)
    {
        if (_axis.Index == index)
            return;
        _axis.Index = index;
        _owner.CommitEdit();
    }

    /// <summary>
    /// Store a new kind. The editor's own writes keep invariant 7 satisfiable: a rotary
    /// axis gets exactly two perpendicular pivot coordinates, a linear axis carries none.
    /// </summary>
    public void ApplyKind(AxisKind kind)
    {
        if (_axis.Kind == kind)
            return;
        _axis.Kind = kind;
        if (kind == AxisKind.Rotary)
        {
            // An existing hand-edited arity (three values) is normalized to the two the
            // model defines; a rotary row always shows exactly two inputs.
            if (_axis.RotationOrigin is not { Length: 2 })
                _axis.RotationOrigin = NormalizeOrigin(_axis.RotationOrigin);
        }
        else
        {
            _axis.RotationOrigin = null;
        }
        RebuildPivots();
        _owner.CommitEdit();
    }

    /// <summary>Store a new direction; the perpendicular pair (and so the pivot labels)
    /// follow it.</summary>
    public void ApplyDirection(AxisDirection direction)
    {
        if (_axis.Direction == direction)
            return;
        _axis.Direction = direction;
        RebuildPivots();
        _owner.CommitEdit();
    }

    /// <summary>Store the axis's sense from the Direction column's checkbox. The stored
    /// semantics are unchanged: <c>Sign</c> is <c>-1</c> when inverted, <c>+1</c> otherwise.</summary>
    public void SetInverted(bool inverted)
    {
        var sign = inverted ? -1 : 1;
        if (_axis.Sign == sign)
            return;
        _axis.Sign = sign;
        _owner.CommitEdit();
    }

    /// <summary>Store the optional inbound CAM alias. Empty clears it; anything that is
    /// not a single resolvable axis letter clears it too, because such text can never
    /// match a program word.</summary>
    public void ApplyAlias(string? text)
    {
        var alias = NormalizeAlias(text);
        if (_axis.Alias == alias)
            return;
        _axis.Alias = alias;
        _owner.CommitEdit();
    }

    /// <summary>Exactly one upper-case axis letter (<c>XYZABCUVW</c>) after trimming, or
    /// null for anything else: empty, longer text, or a character the parser can never
    /// resolve — a stored-but-dead alias would validate clean and then silently do
    /// nothing.</summary>
    public static char? NormalizeAlias(string? text)
    {
        var trimmed = (text ?? string.Empty).Trim();
        if (trimmed.Length != 1)
            return null;
        var letter = char.ToUpperInvariant(trimmed[0]);
        return MachineType.ConventionalLetters.Contains(letter) ? letter : null;
    }

    /// <summary>The stored pivot coordinate for one of the two perpendicular slots.</summary>
    public double OriginValue(int slot)
    {
        var origin = _axis.RotationOrigin;
        return origin is not null && slot >= 0 && slot < origin.Length ? origin[slot] : 0;
    }

    /// <summary>Store one pivot coordinate. The array is normalized to exactly two
    /// values, in canonical Width/Length/Height order of the perpendicular directions.</summary>
    public void ApplyOrigin(int slot, double value)
    {
        var origin = NormalizeOrigin(_axis.RotationOrigin);
        if (origin[slot] == value)
            return;
        origin[slot] = value;
        _axis.RotationOrigin = origin;
        _owner.CommitEdit();
    }

    private static double[] NormalizeOrigin(double[]? origin) =>
    [
        origin is { Length: > 0 } ? origin[0] : 0,
        origin is { Length: > 1 } ? origin[1] : 0,
    ];

    private void RebuildPivots()
    {
        // An explicit Clear/Add pair (not a reassignment): the collection is observable, so
        // the rendered pivot rows are re-created and pick up the new perpendicular pair.
        _pivots.Clear();
        if (_axis.Kind == AxisKind.Rotary)
        {
            var perpendicular = MachineAxesViewModel.PerpendicularDirections(_axis.Direction);
            for (var slot = 0; slot < perpendicular.Count; slot++)
                _pivots.Add(new AxisPivotRowViewModel(_owner, this, slot, perpendicular[slot]));
        }
        OnPropertyChanged(nameof(Pivots));
    }

    /// <summary>Re-read everything the resolved machine (letters, validation) feeds.
    /// Display text is re-raised here, never while a text field is being typed in.</summary>
    internal void RefreshFromKinematics()
    {
        OnPropertyChanged(nameof(BoardLetterText));
        OnPropertyChanged(nameof(IsInverted));
        OnPropertyChanged(nameof(AliasText));
        OnPropertyChanged(nameof(IsRotary));
        OnPropertyChanged(nameof(RowHint));
        OnPropertyChanged(nameof(SelectedIndex));
        OnPropertyChanged(nameof(SelectedKind));
        OnPropertyChanged(nameof(SelectedDirection));
        foreach (var pivot in _pivots)
            pivot.Refresh();
    }

    /// <summary>The locale changed: the option lists are new instances, so the row must
    /// re-raise them for its combo boxes to pick up the translated labels.</summary>
    internal void RefreshChoices()
    {
        OnPropertyChanged(nameof(IndexChoices));
        OnPropertyChanged(nameof(KindChoices));
        OnPropertyChanged(nameof(DirectionChoices));
        OnPropertyChanged(nameof(SelectedIndex));
        OnPropertyChanged(nameof(SelectedKind));
        OnPropertyChanged(nameof(SelectedDirection));
        OnPropertyChanged(nameof(RowHint));
        OnPropertyChanged(nameof(RemoveText));
    }
}

/// <summary>
/// One pivot input of a rotary axis. The label is the direction's <b>board letter</b>
/// when an axis occupies that direction, and the bare direction name when none does (a
/// direction with no axis has no letter to show). The value is a machine coordinate.
/// </summary>
public sealed class AxisPivotRowViewModel : ViewModelBase
{
    private readonly MachineAxesViewModel _owner;
    private readonly MachineAxisRowViewModel _row;

    internal AxisPivotRowViewModel(MachineAxesViewModel owner, MachineAxisRowViewModel row, int slot,
        AxisDirection direction)
    {
        _owner = owner;
        _row = row;
        Slot = slot;
        Direction = direction;
    }

    /// <summary>Which of the axis's two perpendicular coordinates this input edits.</summary>
    public int Slot { get; }

    public AxisDirection Direction { get; }

    public string Label =>
        _owner.LetterForDirection(Direction)?.ToString() ?? _owner.DirectionName(Direction);

    public double Value => _row.OriginValue(Slot);

    public string ValueText => Value.ToString("0.###", CultureInfo.CurrentCulture);

    /// <summary>The board letter of the direction, as a hint for why the label reads so.</summary>
    public string LabelHint => _owner.Resolve("appSettings.axes.pivotHint",
        "Two coordinates: the two directions perpendicular to the axis. The coordinate along it is free, so it is not stored.");

    public void ApplyValue(double value)
    {
        if (Value == value)
            return;
        _row.ApplyOrigin(Slot, value);
    }

    /// <summary>Only the label moves under an edit (an axis can enter or leave the
    /// direction); the value text is left alone so a half-typed number is never rewritten.</summary>
    internal void Refresh() => OnPropertyChanged(nameof(Label));
}

/* ---------------------------------------------------------------------------
 * The editor
 * ------------------------------------------------------------------------- */

/// <summary>
/// The machine card's axis editor: the topology picture for the machine type, an ordered
/// row per axis of every plane, the optional-axis list with add/remove, the foam's
/// plane-to-plane distance and the resolver's problems.
/// <para>
/// The stored truth is the grbl <b>index</b>; the index picker offers the nine
/// conventional letters because that is how firmware docs speak, and the letter shown
/// for an axis is always the one the controller reports. Every edit is committed to the
/// profile and immediately re-resolved through <see cref="MachineKinematicsResolver"/>,
/// so the invariant problems the user sees are the same data the consumers act on.
/// </para>
/// <para>
/// Behind a machine card this is a master/detail editor: the rows live in an
/// <c>ItemsControl</c> whose containers are reused when the selected profile changes, the
/// machine type is switched or a row is added/removed. The view therefore binds its
/// editors <c>OneWay</c> and writes only from an explicit, type-guarded, deferred
/// gesture handler (see <c>MachineAxesView.axaml.cs</c>) — a recycled control can raise a
/// Change event carrying the previous row's state.
/// </para>
/// </summary>
public sealed class MachineAxesViewModel : ViewModelBase
{
    private const string TopologyAssetBase = "avares://SenderNG/Assets/machine-topology/machine-topology-";

    private readonly SettingsService _settings;
    private readonly Func<string, string, string> _resolve;
    private readonly IMachineKinematicsProvider? _machines;

    private MachineProfile _profile;
    private MachineType _preset;
    private MachineKinematics _kinematics;
    private IReadOnlyList<MachineAxisProblem> _problems = [];

    /// <summary>
    /// Creates the editor for one profile. <paramref name="machines"/> is optional: with a
    /// provider the rows show the controller's letters and the aliases are validated
    /// against them; without one (unit tests, no provider registered) the conventional
    /// <c>XYZABCUVW</c> fallback applies, exactly as it does for a disconnected machine.
    /// </summary>
    /// <param name="settings">The settings store the profile is read from and written to.</param>
    /// <param name="resolve">Locale key resolver (key, English fallback).</param>
    /// <param name="profileId">The profile this card edits (the selected one).</param>
    /// <param name="machines">Kinematics provider; used read-only for the board's letters
    /// and reported axis count.</param>
    public MachineAxesViewModel(SettingsService settings, Func<string, string, string> resolve,
        string profileId, IMachineKinematicsProvider? machines = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(resolve);

        _settings = settings;
        _resolve = resolve;
        _machines = machines;

        IndexChoices = BuildIndexChoices();
        KindChoices = BuildKindChoices();
        DirectionChoices = BuildDirectionChoices();

        _profile = new MachineProfile();
        _preset = MachineTypeCatalog.Resolve(_profile.MachineTypeId);
        _kinematics = MachineKinematicsResolver.Resolve(_profile, _preset, null, 0);

        AddAxisCommand = new DelegateCommand(_ => AddOptionalAxis(), _ => CanAddAxis);
        Load(profileId);
    }

    /// <summary>Index picker entries: the nine conventional letters, each with its index.</summary>
    public IReadOnlyList<AxisIndexChoice> IndexChoices { get; private set; }

    public IReadOnlyList<AxisKindChoice> KindChoices { get; private set; }

    public IReadOnlyList<AxisDirectionChoice> DirectionChoices { get; private set; }

    /// <summary>The profile being edited (its id is stable across a rename).</summary>
    public string ProfileId => _profile.Id;

    /// <summary>The machine type id — the same value the profile manager shows.</summary>
    public string MachineTypeId => _preset.Id;

    public string MachineTypeName =>
        Resolve($"appSettings.mode.{_preset.Id.ToLowerInvariant()}", _preset.Name);

    /// <summary>The type's topology picture, or null for a type that ships no art. Roles
    /// only, never letters: a static image cannot track a remapped board.</summary>
    public string? TopologyImage => _preset.Id.ToLowerInvariant() switch
    {
        "mill" => TopologyAssetBase + "mill.png",
        "foam" => TopologyAssetBase + "foam.png",
        _ => null,
    };

    public bool HasTopologyImage => TopologyImage is not null;

    /// <summary>The machine's planes, in machine order, each with its ordered axis rows.</summary>
    public ObservableCollection<MachinePlaneViewModel> Planes { get; } = new();

    /// <summary>Axes outside every plane; a rotary axis is only ever here.</summary>
    public ObservableCollection<MachineAxisRowViewModel> OptionalAxes { get; } = new();

    public bool HasOptionalAxes => OptionalAxes.Count > 0;

    /// <summary>True for a multi-plane machine — the only kind that carries a separation.</summary>
    public bool IsMultiPlane => _preset.BasePlanes.Count > 1;

    /// <summary>Plane-to-plane distance (the foam's wire length), in machine coordinates.</summary>
    public bool HasSeparation => IsMultiPlane;

    public double Separation
    {
        get => _profile.Separation;
        set
        {
            if (_profile.Separation == value)
                return;
            _profile.Separation = value;
            CommitEdit();
        }
    }

    /// <summary>Display text for the separation box (read on load; never re-pushed while
    /// the field is being typed in).</summary>
    public string SeparationText => _profile.Separation.ToString("0.###", CultureInfo.CurrentCulture);

    /// <summary>False after a settings restore, when every write is a no-op anyway.</summary>
    public bool IsEditable => !_settings.RestorePending;

    /// <summary>The resolver's problems, localized. Empty for a clean profile.</summary>
    public IReadOnlyList<MachineAxisProblem> Problems => _problems;

    public bool HasProblems => _problems.Count > 0;
    public bool IsValid => !_problems.Any(p => p.IsError);

    /// <summary>Append an optional axis (rotary or extra linear). Null while all nine
    /// grbl indices are taken.</summary>
    public DelegateCommand AddAxisCommand { get; }

    /// <summary>True while a free grbl index exists and the profile is writable.</summary>
    public bool CanAddAxis => IsEditable && NextFreeIndex() >= 0;

    /// <summary>
    /// T1's note, surfaced: a machine-type switch replaces the layout but deliberately
    /// leaves <c>DemoAxes</c> alone, so a Mill→Foam switch can leave the demo simulating
    /// fewer axes than the two planes need — the second plane then stays hidden.
    /// </summary>
    public string? DemoAxesHint
    {
        get
        {
            if (!IsMultiPlane || _profile.DemoAxes >= _preset.DefaultDemoAxes)
                return null;
            return Resolve("appSettings.axes.demoAxesHint",
                    "The demo controller simulates {0} axes, but this machine type needs {1} for two " +
                    "planes — the demo's second plane stays hidden until the count is raised in the " +
                    "connection dialog.")
                .Replace("{0}", _profile.DemoAxes.ToString(CultureInfo.CurrentCulture))
                .Replace("{1}", _preset.DefaultDemoAxes.ToString(CultureInfo.CurrentCulture));
        }
    }

    public bool HasDemoAxesHint => DemoAxesHint is not null;

    /// <summary>The resolved machine the rows read their letters from. Re-resolved after
    /// every edit, so a row never shows a stale board letter.</summary>
    internal MachineKinematics Kinematics => _kinematics;

    /// <summary>Switches this card to another profile (or reloads the current one).</summary>
    public void Load(string profileId)
    {
        _profile = ResolveProfile(profileId);
        _preset = MachineTypeCatalog.Resolve(_profile.MachineTypeId);

        // A profile file written before the axis model existed has no planes: seed the
        // preset's base planes in memory so the editor shows the machine the resolver will
        // use. Deliberately not written back here — opening a settings dialog must not
        // rewrite a profile file; the seed reaches disk with the user's first edit, and a
        // newly created profile is seeded by SettingsService.CreateProfile already.
        // Optional axes are left alone: a hand-edited file may carry them without planes.
        if (_profile.Planes.Count == 0 && _preset.BasePlanes.Count > 0)
        {
            _profile.Planes = _preset.CreateDefaultPlanes();
            _profile.Separation = _preset.DefaultSeparation;
        }

        Rebuild();
    }

    /// <summary>Re-localize the axis editor's own strings (the machine card follows a
    /// locale switch while it is open).</summary>
    public void Relocalize()
    {
        KindChoices = BuildKindChoices();
        DirectionChoices = BuildDirectionChoices();
        foreach (var plane in Planes)
        {
            plane.Refresh();
            foreach (var row in plane.Axes)
                row.RefreshChoices();
        }
        foreach (var row in OptionalAxes)
            row.RefreshChoices();
        Revalidate(); // the problems' text is resolved from their codes
        RaiseComputed();
    }

    internal string Resolve(string key, string fallback) => _resolve(key, fallback);

    /// <summary>The locale name of a role (a Width/Length/Height direction) — the pivot
    /// label for a role no axis occupies, and the role picker's labels.</summary>
    internal string DirectionName(AxisDirection direction) => direction switch
    {
        AxisDirection.Width => Resolve("appSettings.axes.role.width", "Width"),
        AxisDirection.Length => Resolve("appSettings.axes.role.length", "Length"),
        _ => Resolve("appSettings.axes.role.height", "Height"),
    };

    /// <summary>The two directions perpendicular to <paramref name="direction"/>, in
    /// canonical Width, Length, Height order — the order the pivot coordinates store.</summary>
    internal static IReadOnlyList<AxisDirection> PerpendicularDirections(AxisDirection direction) => direction switch
    {
        AxisDirection.Width => [AxisDirection.Length, AxisDirection.Height],
        AxisDirection.Length => [AxisDirection.Width, AxisDirection.Height],
        _ => [AxisDirection.Width, AxisDirection.Length],
    };

    /// <summary>The board letter of the axis occupying a direction anywhere on the
    /// machine, or null when no axis does (a direction with no motor has no letter).</summary>
    internal char? LetterForDirection(AxisDirection direction)
    {
        for (var plane = 0; plane < _kinematics.Planes.Count; plane++)
        {
            var index = _kinematics.IndexForDirection(plane, direction);
            if (index >= 0)
                return _kinematics.LetterFor(index);
        }
        foreach (var axis in _kinematics.OptionalAxes)
        {
            if (axis.Direction == direction)
                return _kinematics.LetterFor(axis.Index);
        }
        return null;
    }

    /// <summary>Persist the edited profile and re-resolve validation. Called by every row
    /// mutation; the display text of the fields being typed in is deliberately not
    /// re-raised.</summary>
    internal void CommitEdit()
    {
        Save();
        Revalidate();
    }

    internal void RemoveOptional(MachineAxisRowViewModel row)
    {
        if (row is null || row.IsBase)
            return; // base rows are locked by the preset
        if (!_profile.OptionalAxes.Remove(row.Model))
            return;
        OptionalAxes.Remove(row);
        CommitEdit();
        OnPropertyChanged(nameof(HasOptionalAxes));
        RefreshCommands();
    }

    private void AddOptionalAxis()
    {
        var index = NextFreeIndex();
        if (index < 0)
            return;
        var axis = new MachineAxis
        {
            Index = index,
            Kind = AxisKind.Linear,
            Direction = AxisDirection.Width,
            Sign = 1,
        };
        _profile.OptionalAxes.Add(axis);
        OptionalAxes.Add(new MachineAxisRowViewModel(this, axis, isBase: false));
        CommitEdit();
        OnPropertyChanged(nameof(HasOptionalAxes));
        RefreshCommands();
    }

    /// <summary>The lowest free grbl index, or -1 when all nine are taken.</summary>
    private int NextFreeIndex()
    {
        var used = new HashSet<int>();
        foreach (var axis in AllAxes())
            used.Add(axis.Index);
        for (var index = 0; index <= 8; index++)
        {
            if (!used.Contains(index))
                return index;
        }
        return -1;
    }

    /// <summary>Every axis of the profile, planes first. Null entries (a hand-edited
    /// file) are skipped rather than thrown on.</summary>
    private IEnumerable<MachineAxis> AllAxes()
    {
        foreach (var plane in _profile.Planes)
        {
            if (plane?.Axes is not { } axes)
                continue;
            foreach (var axis in axes)
            {
                if (axis is not null)
                    yield return axis;
            }
        }
        foreach (var axis in _profile.OptionalAxes)
        {
            if (axis is not null)
                yield return axis;
        }
    }

    private MachineProfile ResolveProfile(string profileId)
    {
        // The active profile's graph is shared: editing a copy loaded from disk would
        // leave every live consumer (jog, plotter, DRO) on the old layout.
        var live = _settings.Load();
        if (string.Equals(live.Id, profileId, StringComparison.Ordinal))
            return live;
        return _settings.AllProfiles()
                   .FirstOrDefault(p => string.Equals(p.Id, profileId, StringComparison.Ordinal))
               ?? live;
    }

    private void Save()
    {
        try
        {
            var live = _settings.Load();
            if (string.Equals(live.Id, _profile.Id, StringComparison.Ordinal))
                _settings.Save(_profile);
            else
                _settings.SaveProfile(_profile);
        }
        catch
        {
            // Best-effort persistence; the edit still applies for the session.
        }
    }

    private void Rebuild()
    {
        Planes.Clear();
        var number = 0;
        foreach (var raw in _profile.Planes)
        {
            number++;
            var plane = raw ?? new ProfilePlane();
            var vm = new MachinePlaneViewModel(this, number);
            foreach (var axis in plane.Axes)
            {
                if (axis is not null)
                    vm.Axes.Add(new MachineAxisRowViewModel(this, axis, isBase: true));
            }
            Planes.Add(vm);
        }

        OptionalAxes.Clear();
        foreach (var axis in _profile.OptionalAxes)
        {
            if (axis is not null)
                OptionalAxes.Add(new MachineAxisRowViewModel(this, axis, isBase: false));
        }

        Revalidate();
        RaiseComputed();
    }

    /// <summary>Re-resolve the edited profile (preset ⊕ profile ⊕ board) and refresh every
    /// derived label. Problems come back as data and are rendered, never thrown.</summary>
    private void Revalidate()
    {
        // Board letters and the reported count are live controller state, not profile
        // state: read them from the same provider the consumers use. A null provider (or
        // a disconnected board) leaves the conventional fallback in place.
        var boardLetters = _machines?.Current.BoardLetters;
        var reportedAxes = _machines?.Current.ReportedAxes ?? 0;
        _kinematics = MachineKinematicsResolver.Resolve(_profile, _preset, boardLetters, reportedAxes);
        _problems = BuildProblems(_kinematics);

        foreach (var plane in Planes)
        {
            foreach (var row in plane.Axes)
                row.RefreshFromKinematics();
        }
        foreach (var row in OptionalAxes)
            row.RefreshFromKinematics();

        OnPropertyChanged(nameof(Problems));
        OnPropertyChanged(nameof(HasProblems));
        OnPropertyChanged(nameof(IsValid));
    }

    private void RaiseComputed()
    {
        OnPropertyChanged(nameof(MachineTypeId));
        OnPropertyChanged(nameof(MachineTypeName));
        OnPropertyChanged(nameof(TopologyImage));
        OnPropertyChanged(nameof(HasTopologyImage));
        OnPropertyChanged(nameof(IsMultiPlane));
        OnPropertyChanged(nameof(HasSeparation));
        OnPropertyChanged(nameof(Separation));
        OnPropertyChanged(nameof(SeparationText));
        OnPropertyChanged(nameof(DemoAxesHint));
        OnPropertyChanged(nameof(HasDemoAxesHint));
        OnPropertyChanged(nameof(HasOptionalAxes));
        OnPropertyChanged(nameof(IsEditable));
        OnPropertyChanged(nameof(CanAddAxis));
        RefreshCommands();
    }

    private void RefreshCommands()
    {
        AddAxisCommand?.RaiseCanExecuteChanged();
        foreach (var row in OptionalAxes)
            row.RemoveCommand.RaiseCanExecuteChanged();
    }

    private IReadOnlyList<MachineAxisProblem> BuildProblems(MachineKinematics kinematics)
    {
        if (kinematics.Problems.Count == 0)
            return [];
        var problems = new List<MachineAxisProblem>(kinematics.Problems.Count);
        foreach (var problem in kinematics.Problems)
            problems.Add(new MachineAxisProblem(problem.Severity == KinematicsSeverity.Error, Localize(problem)));
        return problems;
    }

    private string Localize(KinematicsProblem problem)
    {
        // The invariant's *code* is the localizable contract; Message is English
        // developer text and is only the fallback when a catalog lacks the key (so a
        // missing translation can never surface a raw key). The mapping itself lives in
        // KinematicsProblemText because the DRO's undeclared-axes warning renders the
        // same codes — one map, two consumers.
        return KinematicsProblemText.Localize(problem, Resolve);
    }

    private IReadOnlyList<AxisIndexChoice> BuildIndexChoices()
    {
        var letters = MachineType.ConventionalLetters;
        var choices = new List<AxisIndexChoice>(letters.Length);
        for (var index = 0; index < letters.Length; index++)
            choices.Add(new AxisIndexChoice(index, letters[index]));
        return choices;
    }

    private IReadOnlyList<AxisKindChoice> BuildKindChoices() =>
    [
        new AxisKindChoice(AxisKind.Linear, () => Resolve("appSettings.axes.type.linear", "Linear")),
        new AxisKindChoice(AxisKind.Rotary, () => Resolve("appSettings.axes.type.rotary", "Rotary")),
    ];

    private IReadOnlyList<AxisDirectionChoice> BuildDirectionChoices() =>
    [
        new AxisDirectionChoice(AxisDirection.Width, () => DirectionName(AxisDirection.Width)),
        new AxisDirectionChoice(AxisDirection.Length, () => DirectionName(AxisDirection.Length)),
        new AxisDirectionChoice(AxisDirection.Height, () => DirectionName(AxisDirection.Height)),
    ];

    /// <summary>Parses a numeric field: the current culture first, then invariant (a
    /// decimal point typed under a comma culture still reads). Empty means zero.
    /// Non-finite values (an overflow to infinity) are rejected: the field filter
    /// blocks letters but not digit overflow, and a stored infinity would make the
    /// settings serializer throw on the next save.</summary>
    public static bool TryParseNumber(string? text, out double value)
    {
        var trimmed = (text ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            value = 0;
            return true;
        }
        return (double.TryParse(trimmed, NumberStyles.Float, CultureInfo.CurrentCulture, out value)
            || double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
            && double.IsFinite(value);
    }
}
