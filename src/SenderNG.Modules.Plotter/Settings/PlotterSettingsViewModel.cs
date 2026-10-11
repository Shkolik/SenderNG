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

using System.Collections.ObjectModel;
using System.Globalization;
using CNC.Core;
using CNC.Core.Services;
using SenderNG.Core.Localization;
using SenderNG.Modules.Plotter;

namespace SenderNG.Modules.Plotter.Settings;

/// <summary>App-Settings editor for the <c>plotter</c> section: the four view
/// toggles, and per plane the axis→world permutation plus the iso origin
/// offsets. ONE instance binds both editor surfaces — the App Settings leaf
/// and the plot view's overlay — so a flip on either is visible on both.
/// <para>Disposable because it subscribes to the app-lifetime
/// <see cref="LocalizationService"/>, which outlives a machine switch: without
/// an unsubscribe the model leaked one locale subscription per profile switch,
/// and a single language change then ran one relabel pass per model ever built.</para></summary>
public sealed class PlotterSettingsViewModel : ViewModelBase, IDisposable
{
    private readonly SettingsService _settingsService;
    private readonly LocalizationService? _loc;
    private readonly MachineProfile _profile;

    /// <summary>Raised after any edit that persisted, so the plot view can
    /// re-read the flags and the mappings. <c>System.Action</c> is fully
    /// qualified for clarity and consistency.</summary>
    public event System.Action? Changed;

    public PlotterSettingsViewModel(SettingsService settingsService, LocalizationService? localization = null)
    {
        _settingsService = settingsService;
        _loc = localization;
        _profile = settingsService.Load();
        var type = MachineTypeCatalog.Resolve(_profile.MachineTypeId);
        Model = PlotterConfig.Load(_profile, type);
        PlaneRows = new ObservableCollection<PlotterPlaneRow>(
            Model.Planes.Select((_, p) => new PlotterPlaneRow(this, p)));

        if (localization is not null)
            localization.Changed += OnLocalizationChanged;
    }

    /// <summary>Named so <see cref="Dispose"/> can take the subscription back
    /// off the locale service. The lambda this replaced was reachable only from
    /// inside the ctor, so the subscription could never be undone.</summary>
    private void OnLocalizationChanged(object? sender, System.EventArgs e) => RelocalizeLabels();

    /// <summary>Detaches the locale subscription. The module calls this before
    /// dropping the model on a profile switch, because the locale service is
    /// app-lifetime and the model is profile-scoped. After this the model is
    /// inert — its labels no longer follow a language change — so the module
    /// must not hand it out again; the App Settings dialog builds its leaf over
    /// the replacement model instead.</summary>
    public void Dispose()
    {
        if (_loc is not null)
            _loc.Changed -= OnLocalizationChanged;
    }

    /// <summary>The live settings. Mutating it is the module's job; the rows
    /// and the plot view both read through this instance.</summary>
    public PlotterSettings Model { get; }

    /// <summary>One row per carriage plane, in plane order.</summary>
    public ObservableCollection<PlotterPlaneRow> PlaneRows { get; }

    /// <summary>The view toggles as one value, for the plot view's canvases.</summary>
    public ChromeFlags Flags =>
        new(Model.ShowGrid, Model.ShowOrigin, Model.ShowEnvelope, Model.ShowRunState);

    public bool ShowGrid
    {
        get => Model.ShowGrid;
        set { if (Model.ShowGrid != value) { Model.ShowGrid = value; Persist(); } }
    }

    public bool ShowOrigin
    {
        get => Model.ShowOrigin;
        set { if (Model.ShowOrigin != value) { Model.ShowOrigin = value; Persist(); } }
    }

    public bool ShowEnvelope
    {
        get => Model.ShowEnvelope;
        set { if (Model.ShowEnvelope != value) { Model.ShowEnvelope = value; Persist(); } }
    }

    public bool ShowRunState
    {
        get => Model.ShowRunState;
        set { if (Model.ShowRunState != value) { Model.ShowRunState = value; Persist(); } }
    }

    // Plugin views cannot use the app-only {Loc} markup, so every label
    // resolves code-side against the module's own locale files.
    public string ViewLabel => Resolve("module.plotter.settings.view", "View");
    public string ShowGridLabel => Resolve("module.plotter.settings.showGrid", "Show grid");
    public string ShowOriginLabel => Resolve("module.plotter.settings.showOrigin", "Show origin");
    public string ShowEnvelopeLabel => Resolve("module.plotter.settings.showEnvelope", "Show envelope");
    public string ShowRunStateLabel => Resolve("module.plotter.settings.showRunState", "Show run state");
    public string WorldAxesLabel => Resolve("module.plotter.settings.worldAxes", "World axes");
    public string OriginLabel => Resolve("module.plotter.settings.origin", "Origin");
    public string ZoomInLabel => Resolve("module.plotter.zoomIn", "Zoom in");
    public string ZoomOutLabel => Resolve("module.plotter.zoomOut", "Zoom out");
    public string ZoomFitLabel => Resolve("module.plotter.zoomFit", "Fit");
    public string SavePngLabel => Resolve("module.plotter.savePng", "Save PNG");

    public string Resolve(string key, string fallback) =>
        _loc?.Resolve(key, fallback) is { Length: > 0 } s ? s : fallback;

    private void RelocalizeLabels()
    {
        // Every one of the eleven *Label properties above, no exceptions. This
        // list was written by hand and shipped with ZoomFitLabel missing, so the
        // "Fit" tooltip kept the old language while everything else updated.
        // `A_locale_change_re_notifies_every_label` enumerates them by name so
        // the next omission fails a test.
        OnPropertyChanged(nameof(ViewLabel));
        OnPropertyChanged(nameof(ShowGridLabel));
        OnPropertyChanged(nameof(ShowOriginLabel));
        OnPropertyChanged(nameof(ShowEnvelopeLabel));
        OnPropertyChanged(nameof(ShowRunStateLabel));
        OnPropertyChanged(nameof(WorldAxesLabel));
        OnPropertyChanged(nameof(OriginLabel));
        OnPropertyChanged(nameof(ZoomInLabel));
        OnPropertyChanged(nameof(ZoomOutLabel));
        OnPropertyChanged(nameof(ZoomFitLabel));
        OnPropertyChanged(nameof(SavePngLabel));
        foreach (var row in PlaneRows)
            row.RaiseLabelChanged();
    }

    private void Persist()
    {
        try
        {
            PlotterConfig.Save(_profile, Model);
            _settingsService.SaveProfile(_profile);
        }
        catch
        {
            // Best-effort persistence; settings still apply for the session.
        }
        // Every toggle notifies itself so a binding on THIS instance re-reads
        // it — without this the checkbox it is bound to keeps its stale visual
        // state after a flip. (The plot overlay that binds these too is wired
        // by Task 10; until then the App Settings leaf is the one consumer.)
        OnPropertyChanged(nameof(Flags));
        OnPropertyChanged(nameof(ShowGrid));
        OnPropertyChanged(nameof(ShowOrigin));
        OnPropertyChanged(nameof(ShowEnvelope));
        OnPropertyChanged(nameof(ShowRunState));
        Changed?.Invoke();
    }

    /// <summary>Writes plane <paramref name="index"/>'s world-axis mapping and
    /// persists it.</summary>
    internal void ApplyRow(PlotterPlaneRow row)
    {
        Model.Planes[row.PlaneIndex].WorldAxes = row.WorldAxes;
        Persist();
    }

    /// <summary>Writes one world-axis origin offset and persists it.</summary>
    internal void ApplyOrigin(int index, int worldAxis, double value)
    {
        if (Model.Planes[index].Origin is not { Length: PlotterPlaneMapper.WorldAxisCount } origin)
        {
            origin = [0, 0, 0];
            Model.Planes[index].Origin = origin;
        }
        origin[worldAxis] = value;
        Persist();
    }

    /// <summary>The machine's letters for the world axes, e.g. X/Y/Z.</summary>
    internal static string[] WorldAxisLetters() =>
        Enumerable.Range(0, PlotterPlaneMapper.WorldAxisCount)
            .Select(a => MachineTypeCatalog.Resolve(MachineTypes.Mill)
                .ConventionalLetter(a).ToString())
            .ToArray();

    /// <summary>One carriage plane's editable mapping: a world-axis combo per
    /// slot and the three world-axis origin offsets.</summary>
    public sealed class PlotterPlaneRow : ViewModelBase
    {
        private readonly PlotterSettingsViewModel _owner;
        private readonly int _index;
        private bool _resolving;

        internal PlotterPlaneRow(PlotterSettingsViewModel owner, int index)
        {
            _owner = owner;
            _index = index;
            var mapping = owner.Model.Planes[index].WorldAxes;
            Slots = new ObservableCollection<PlotterSlotRow>(
                Enumerable.Range(0, mapping.Length)
                    .Select(s => new PlotterSlotRow(
                        this, s, Math.Clamp(mapping[s], 0, PlotterPlaneMapper.WorldAxisCount - 1))));
            Origins = new ObservableCollection<PlotterOriginRow>(
                Enumerable.Range(0, PlotterPlaneMapper.WorldAxisCount)
                    .Select(a => new PlotterOriginRow(owner, index, a)));
        }

        public string PlaneLabel =>
            _owner.Resolve("module.plotter.settings.plane", "Plane {0}")
                .Replace("{0}", (_index + 1).ToString(CultureInfo.InvariantCulture));

        public string WorldAxesLabel => _owner.WorldAxesLabel;
        public string OriginLabel => _owner.OriginLabel;
        public int PlaneIndex => _index;

        /// <summary>One world-axis row per carriage slot, in slot order. A slot
        /// picks which world axis that carriage feeds; the choices stay
        /// distinct (<see cref="OnAxisChanged"/>), mirroring the Nex module's
        /// axis editor.</summary>
        public ObservableCollection<PlotterSlotRow> Slots { get; }

        /// <summary>The current mapping as the plane geometry stores it.</summary>
        internal int[] WorldAxes => [.. Slots.Select(s => s.Axis)];

        /// <summary>The three world-axis origin offsets. The iso view is the
        /// only consumer; the flat panes ignore them.</summary>
        public ObservableCollection<PlotterOriginRow> Origins { get; }

        internal void OnAxisChanged(int slotIndex)
        {
            EnforceDistinct(slotIndex);
        }

        /// <summary>Every slot must feed a different world axis, the same
        /// constraint the machine catalog has. The slot the user just changed
        /// keeps its pick — moving it would discard the edit — and a sibling
        /// that now shares its axis is moved onto a free one. Re-entered from
        /// the Axis setter it guards itself with <see cref="_resolving"/>, so
        /// the nudge does not cascade into another persist: the LAST call
        /// persists once, with every slot settled.</summary>
        private void EnforceDistinct(int changedSlot)
        {
            if (_resolving)
                return;
            _resolving = true;
            try
            {
                var used = new bool[PlotterPlaneMapper.WorldAxisCount];
                used[Slots[changedSlot].Axis] = true;
                for (var i = 0; i < Slots.Count; i++)
                {
                    if (i == changedSlot)
                        continue;
                    if (used[Slots[i].Axis])
                        MoveOntoFree(i, used);
                    used[Slots[i].Axis] = true;
                }
            }
            finally
            {
                _resolving = false;
            }
            _owner.ApplyRow(this);
        }

        private void MoveOntoFree(int slot, bool[] used)
        {
            for (var a = 0; a < PlotterPlaneMapper.WorldAxisCount; a++)
            {
                if (used[a] || Slots.Any(s => s.Axis == a))
                    continue;
                Slots[slot].Axis = a; // re-enters OnAxisChanged (guarded)
                return;
            }
        }

        internal void RaiseLabelChanged()
        {
            OnPropertyChanged(nameof(PlaneLabel));
            OnPropertyChanged(nameof(WorldAxesLabel));
            OnPropertyChanged(nameof(OriginLabel));
            foreach (var origin in Origins)
                origin.RaiseLabelChanged();
        }
    }

    /// <summary>One slot's world-axis selection: which world axis the carriage
    /// feeds. 0 = X, 1 = Y, 2 = Z — the same indices the plane geometry's
    /// <c>WorldAxes</c> array stores.</summary>
    public sealed class PlotterSlotRow : ViewModelBase
    {
        private readonly PlotterPlaneRow _row;
        private int _axis;

        internal PlotterSlotRow(PlotterPlaneRow row, int slotIndex, int axis)
        {
            _row = row;
            SlotIndex = slotIndex;
            _axis = axis;
        }

        public int SlotIndex { get; }

        public int Axis
        {
            get => _axis;
            set
            {
                if (SetField(ref _axis, Math.Clamp(
                        value, 0, PlotterPlaneMapper.WorldAxisCount - 1)))
                    _row.OnAxisChanged(SlotIndex);
            }
        }

        /// <summary>The world-axis letters, e.g. X/Y/Z — the ComboBox choices.
        /// Built from the conventional letters so they need no translation and
        /// always match the origin rows' labels.</summary>
        public string[] Choices => PlotterSettingsViewModel.WorldAxisLetters();
    }

    /// <summary>One world axis's origin offset, in millimetres.</summary>
    public sealed class PlotterOriginRow : ViewModelBase
    {
        private readonly PlotterSettingsViewModel _owner;
        private readonly int _index;
        private readonly int _worldAxis;

        internal PlotterOriginRow(PlotterSettingsViewModel owner, int index, int worldAxis)
        {
            _owner = owner;
            _index = index;
            _worldAxis = worldAxis;
        }

        public string AxisLabel =>
            MachineTypeCatalog.Resolve(MachineTypes.Mill).ConventionalLetter(_worldAxis).ToString();

        public double Value
        {
            get => PlotterPlaneMapper.OriginFor(_owner.Model.Planes[_index], _worldAxis);
            set
            {
                if (Math.Abs(Value - value) < 1e-9)
                    return;
                _owner.ApplyOrigin(_index, _worldAxis, value);
                OnPropertyChanged();
            }
        }

        internal void RaiseLabelChanged() => OnPropertyChanged(nameof(AxisLabel));
    }
}