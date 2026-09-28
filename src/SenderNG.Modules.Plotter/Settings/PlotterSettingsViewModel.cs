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
    /// re-read the flags and the mappings. Fully qualified: <c>using CNC.Core;</c>
    /// pulls in the <c>CNC.Core.Action</c> enum (GCodeJob) and would shadow
    /// <c>System.Action</c> into an ambiguity.</summary>
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
        PlotterConfig.Save(_profile, Model);
        _settingsService.SaveProfile(_profile);
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

    /// <summary>Writes plane <paramref name="index"/>'s permutation and
    /// persists it.</summary>
    internal void ApplyPermutation(int index, int permutationIndex)
    {
        var permutations = Permutations(Model.Planes[index].WorldAxes.Length);
        var axes = permutations[permutationIndex];
        Model.Planes[index].WorldAxes = [.. axes];
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

    /// <summary>Every permutation of the plane's <paramref name="roles"/> world
    /// axes, in the machine's own axis letters. The display strings are built
    /// from the conventional letters rather than the locale, so they need no
    /// translation and always match the rest of the UI.</summary>
    internal static IReadOnlyList<int[]> Permutations(int roles) =>
        Permute(Enumerable.Range(0, roles).ToArray())
            .Select(p => p)
            .ToArray();

    /// <summary>The index of <paramref name="want"/> among all permutations of
    /// its own length, compared BY VALUE. Do not use <c>List&lt;int[]&gt;.IndexOf</c>:
    /// it compares the arrays by reference, so a stored mapping never matches the
    /// freshly generated ones and the row silently falls back to index 0.</summary>
    internal static int IndexOfPermutation(int[] want) =>
        Permutations(want.Length)
            .Select((p, i) => (p, i))
            .FirstOrDefault(t => t.p.SequenceEqual(want)).i;

    private static IEnumerable<int[]> Permute(int[] items)
    {
        if (items.Length <= 1)
        {
            yield return [.. items];
            yield break;
        }
        for (var i = 0; i < items.Length; i++)
        {
            var rest = items.Where((_, j) => j != i).ToArray();
            foreach (var tail in Permute(rest))
                yield return [items[i], .. tail];
        }
    }

    /// <summary>"X→X, Y→Y" for one permutation.</summary>
    internal static string Describe(int[] worldAxes) =>
        string.Join(", ", worldAxes.Select((w, slot) =>
            $"{Letter(slot)}→{Letter(w)}"));

    private static string Letter(int worldAxis) =>
        MachineTypeCatalog.Resolve(MachineTypes.Mill).ConventionalLetter(worldAxis).ToString();

    /// <summary>One carriage plane's editable mapping.</summary>
    public sealed class PlotterPlaneRow : ViewModelBase
    {
        private readonly PlotterSettingsViewModel _owner;
        private readonly int _index;
        private int _selectedPermutationIndex;

        internal PlotterPlaneRow(PlotterSettingsViewModel owner, int index)
        {
            _owner = owner;
            _index = index;
            // Value equality, deliberately. `List<int[]>.IndexOf` compares the
            // int[] elements by REFERENCE, so passing a fresh `[.. axes]` always
            // missed and `Math.Max(-1, 0)` pinned every row to the first entry —
            // a saved [1, 0] displayed as "X→X, Y→Y".
            _selectedPermutationIndex = Math.Max(
                PlotterSettingsViewModel.IndexOfPermutation(
                    owner.Model.Planes[index].WorldAxes), 0);
            Origins = new ObservableCollection<PlotterOriginRow>(
                Enumerable.Range(0, PlotterPlaneMapper.WorldAxisCount)
                    .Select(a => new PlotterOriginRow(owner, index, a)));
        }

        public string PlaneLabel =>
            _owner.Resolve("module.plotter.settings.plane", "Plane {0}")
                .Replace("{0}", (_index + 1).ToString(CultureInfo.InvariantCulture));

        public string WorldAxesLabel => _owner.WorldAxesLabel;
        public string OriginLabel => _owner.OriginLabel;

        /// <summary>Every permutation of this plane's world axes, as display
        /// strings for the combo box.</summary>
        public IReadOnlyList<string> Permutations =>
            PlotterSettingsViewModel.Permutations(_owner.Model.Planes[_index].WorldAxes.Length)
                .Select(PlotterSettingsViewModel.Describe)
                .ToArray();

        public int SelectedPermutationIndex
        {
            get => _selectedPermutationIndex;
            set
            {
                if (_selectedPermutationIndex == value)
                    return;
                _selectedPermutationIndex = value;
                _owner.ApplyPermutation(_index, value);
                OnPropertyChanged();
                OnPropertyChanged(nameof(Permutations));
            }
        }

        /// <summary>The three world-axis origin offsets. The iso view is the
        /// only consumer; the flat panes ignore them.</summary>
        public ObservableCollection<PlotterOriginRow> Origins { get; }

        internal void RaiseLabelChanged()
        {
            OnPropertyChanged(nameof(PlaneLabel));
            OnPropertyChanged(nameof(WorldAxesLabel));
            OnPropertyChanged(nameof(OriginLabel));
            foreach (var origin in Origins)
                origin.RaiseLabelChanged();
        }
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