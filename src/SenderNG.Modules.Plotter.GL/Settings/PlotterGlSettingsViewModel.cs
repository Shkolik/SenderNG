/*
 * PlotterGlSettingsViewModel.cs - AppSettings editor for the plotter-gl section
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

using System.Collections.ObjectModel;
using CNC.Core.Services;
using SenderNG.Core.Localization;

namespace SenderNG.Modules.Plotter.GL.Settings;

/// <summary>AppSettings editor for the <c>plotter-gl</c> section: wire length
/// (2+ planes), per-slot world-axis selection, per-plane origin offsets, scene
/// view toggles, and render supersampling.</summary>
public sealed class PlotterGlSettingsViewModel : CNC.Core.ViewModelBase
{
    private readonly SettingsService _settingsService;
    private readonly LocalizationService? _loc;
    private readonly MachineProfile _profile;
    private PlotterGlSettings _settings;
    private bool _saving;

    public PlotterGlSettingsViewModel(SettingsService settingsService, LocalizationService? localization = null)
    {
        _settingsService = settingsService;
        _loc = localization;
        _profile = _settingsService.Load();
        var type = MachineTypeCatalog.Resolve(_profile.MachineTypeId);
        _settings = PlotterGlConfig.Load(_profile, type);
        PlaneRows = new ObservableCollection<PlotterGlPlaneRow>(
            _settings.Planes.Select((_, p) => new PlotterGlPlaneRow(this, p)));
        SyncRows();

        // Re-resolve the code-side labels on every locale switch (Language picker
        // changes are applied immediately, without restarting the app).
        if (localization is not null)
            localization.Changed += (_, _) => RelocalizeLabels();
    }

    private void RelocalizeLabels()
    {
        OnPropertyChanged(nameof(WireLengthLabel));
        OnPropertyChanged(nameof(ViewLabel));
        OnPropertyChanged(nameof(ShowOriginLabel));
        OnPropertyChanged(nameof(ShowEnvelopeLabel));
        OnPropertyChanged(nameof(ShowRunStateLabel));
        OnPropertyChanged(nameof(ShowGridLabel));
        OnPropertyChanged(nameof(ShowLoftLabel));
        OnPropertyChanged(nameof(SupersampleLabel));
        OnPropertyChanged(nameof(MsaaLabel));
    }

    /// <summary>True when the machine has more than one plane (wire length applies).</summary>
    public bool ShowWireLength => _settings.Planes.Count > 1;

    /// <summary>True when the machine has at least two planes — a sheet
    /// spans the space BETWEEN planes, so a single-plane mill has nothing to
    /// show and neither toolbar nor settings carry the loft toggle. Same
    /// shape and refresh semantics as <see cref="ShowWireLength"/>: the
    /// profile switch rebuilds this VM, so no PropertyChanged refresh is
    /// raised here.</summary>
    public bool HasLoft => _settings.Planes.Count >= 2;

    public double WireLength
    {
        get => _settings.WireLength;
        set
        {
            if (Math.Abs(_settings.WireLength - value) < 1e-9)
                return;
            _settings.WireLength = value;
            PlotterGlConfig.RecenterOnWire(_settings);
            SyncRows();
            Save();
            OnPropertyChanged();
        }
    }

    /// <summary>Localized "Wire length (tower distance)" label (plugin views
    /// cannot use the app-only {Loc} markup, so labels resolve code-side).</summary>
    public string WireLengthLabel => Resolve("module.plotter-gl.settings.wireLength", "Wire length (tower distance)");

    public bool ShowOrigin
    {
        get => _settings.ShowOrigin;
        set
        {
            if (_settings.ShowOrigin == value) return;
            _settings.ShowOrigin = value;
            Save();
            OnPropertyChanged();
        }
    }

    public bool ShowEnvelope
    {
        get => _settings.ShowEnvelope;
        set
        {
            if (_settings.ShowEnvelope == value) return;
            _settings.ShowEnvelope = value;
            Save();
            OnPropertyChanged();
        }
    }

    public bool ShowRunState
    {
        get => _settings.ShowRunState;
        set
        {
            if (_settings.ShowRunState == value) return;
            _settings.ShowRunState = value;
            Save();
            OnPropertyChanged();
        }
    }

    public bool ShowGrid
    {
        get => _settings.ShowGrid;
        set
        {
            if (_settings.ShowGrid == value) return;
            _settings.ShowGrid = value;
            Save();
            OnPropertyChanged();
        }
    }

    /// <summary>Show the translucent loft sheet between the carriage planes.
    /// Runtime — the next frame's draw pass reads the gate. Default ON.
    /// Hidden entirely on single-plane machines via <see cref="HasLoft"/>.</summary>
    public bool ShowLoft
    {
        get => _settings.ShowLoft;
        set
        {
            if (_settings.ShowLoft == value) return;
            _settings.ShowLoft = value;
            Save();
            OnPropertyChanged();
        }
    }

    /// <summary>Render the view at 2x panel resolution (hairline AA). Runtime —
    /// the next tick re-sizes the render target. Default ON.</summary>
    public bool Supersample
    {
        get => _settings.Supersample;
        set
        {
            if (_settings.Supersample == value) return;
            _settings.Supersample = value;
            Save();
            OnPropertyChanged();
        }
    }

    /// <summary>Multisample mode of the readback target. Runtime — the view's
    /// next tick re-allocates the FBO. Default None.</summary>
    public MsaaMode MsaaMode
    {
        get => _settings.MsaaMode;
        set
        {
            if (_settings.MsaaMode == value) return;
            _settings.MsaaMode = value;
            Save();
            OnPropertyChanged();
            OnPropertyChanged(nameof(MsaaIndex)); // the ComboBox selection follows
        }
    }

    /// <summary>The <see cref="MsaaMode"/> as the ComboBox's SelectedIndex
    /// over <see cref="MsaaChoices"/> (the enum members are contiguous from
    /// zero, so the index and the value are the same number). Out-of-range
    /// writes — the transient -1 a ComboBox reports while it has no matching
    /// selection, or a stale binding's junk — are ignored: not converted to
    /// <see cref="MsaaMode.None"/> and persisted behind the user's back.</summary>
    public int MsaaIndex
    {
        get => (int)_settings.MsaaMode;
        set
        {
            if (!Enum.IsDefined(typeof(MsaaMode), value))
                return;
            MsaaMode = (MsaaMode)value;
        }
    }

    /// <summary>The ComboBox options, in enum order (None, 2x, 4x, 8x).</summary>
    public string[] MsaaChoices => ["Off", "2x", "4x", "8x"];

    /// <summary>Localized "View" section heading.</summary>
    public string ViewLabel => Resolve("module.plotter-gl.settings.view", "View");

    /// <summary>Localized "Show origin" toggle label.</summary>
    public string ShowOriginLabel => Resolve("module.plotter-gl.settings.showOrigin", "Show origin");

    /// <summary>Localized "Show envelope" toggle label.</summary>
    public string ShowEnvelopeLabel => Resolve("module.plotter-gl.settings.showEnvelope", "Show envelope");

    /// <summary>Localized "Show run state" toggle label.</summary>
    public string ShowRunStateLabel => Resolve("module.plotter-gl.settings.showRunState", "Show run state");

    /// <summary>Localized "Show grid" toggle label.</summary>
    public string ShowGridLabel => Resolve("module.plotter-gl.settings.showGrid", "Show grid");

    /// <summary>Localized "Show loft" toggle label.</summary>
    public string ShowLoftLabel => Resolve("module.plotter-gl.settings.showLoft", "Show loft");

    /// <summary>Localized "2x supersample" label for the render-quality toggle.</summary>
    public string SupersampleLabel => Resolve("module.plotter-gl.settings.supersample", "2x supersample (AA at 2x render)");

    /// <summary>Localized label for the MSAA mode selector row.</summary>
    public string MsaaLabel => Resolve("module.plotter-gl.settings.msaa", "MSAA (edge AA)");

    public ObservableCollection<PlotterGlPlaneRow> PlaneRows { get; }

    /// <summary>Writes the section to the active profile and saves it (profile-save cadence).</summary>
    public void Save()
    {
        if (_saving)
            return;
        _saving = true;
        try
        {
            PlotterGlConfig.Save(_profile, _settings);
            _settingsService.Save(_profile);
        }
        catch
        {
            // Best-effort persistence; settings still apply for the session.
        }
        finally
        {
            _saving = false;
        }
    }

    /// <summary>Applies one edited row back into the settings object and persists.</summary>
    internal void ApplyRow(PlotterGlPlaneRow row)
    {
        _settings.Planes[row.PlaneIndex] = row.ToGeometry();
        Save();
    }

    private void SyncRows()
    {
        for (var p = 0; p < PlaneRows.Count && p < _settings.Planes.Count; p++)
            PlaneRows[p].Apply(_settings.Planes[p]);
    }

    internal string Resolve(string key, string fallback) => _loc?.Resolve(key, fallback) ?? fallback;

    internal string ResolveFormat(string key, string fallback, params object?[] args)
    {
        try
        {
            return string.Format(Resolve(key, fallback), args);
        }
        catch (FormatException)
        {
            return fallback;
        }
    }

    /// <summary>Number of machine slots (roles) a schema plane maps.</summary>
    internal int PlaneRowRoleCount(int plane)
    {
        var type = MachineTypeCatalog.Resolve(_profile.MachineTypeId);
        return plane >= 0 && plane < type.Planes.Count ? type.Planes[plane].Roles.Count : 0;
    }
}

/// <summary>One plane's editor row: per-slot world axis + origin (X/Y/Z) editors.</summary>
public sealed class PlotterGlPlaneRow : CNC.Core.ViewModelBase
{
    private readonly PlotterGlSettingsViewModel _owner;
    private bool _resolving;
    private double _originX;
    private double _originY;
    private double _originZ;

    public PlotterGlPlaneRow(PlotterGlSettingsViewModel owner, int planeIndex)
    {
        _owner = owner;
        PlaneIndex = planeIndex;
        Slots = new ObservableCollection<WorldAxisRow>(
            Enumerable.Range(0, owner.PlaneRowRoleCount(planeIndex))
                .Select(s => new WorldAxisRow(this, s)));
    }

    public int PlaneIndex { get; }

    public string Title => _owner.ResolveFormat("module.plotter-gl.settings.plane", "Plane {0}", PlaneIndex + 1);

    public string WorldAxesLabel => _owner.Resolve("module.plotter-gl.settings.worldAxes", "World axes");

    public string OriginLabel => _owner.Resolve("module.plotter-gl.settings.origin", "Origin");

    public ObservableCollection<WorldAxisRow> Slots { get; }

    public double OriginX { get => _originX; set => SetOrigin(0, value, ref _originX); }
    public double OriginY { get => _originY; set => SetOrigin(1, value, ref _originY); }
    public double OriginZ { get => _originZ; set => SetOrigin(2, value, ref _originZ); }

    internal void Apply(GlPlaneGeometry g)
    {
        _originX = g.Origin.Length > 0 ? g.Origin[0] : 0;
        _originY = g.Origin.Length > 1 ? g.Origin[1] : 0;
        _originZ = g.Origin.Length > 2 ? g.Origin[2] : 0;
        for (var s = 0; s < Slots.Count && s < g.WorldAxes.Length; s++)
            Slots[s].Axis = g.WorldAxes[s];
        OnPropertyChanged(nameof(OriginX));
        OnPropertyChanged(nameof(OriginY));
        OnPropertyChanged(nameof(OriginZ));
    }

    internal GlPlaneGeometry ToGeometry() => new()
    {
        WorldAxes = [.. Slots.Select(s => s.Axis)],
        Origin = [OriginX, OriginY, OriginZ],
    };

    internal void OnAxisChanged()
    {
        EnforceDistinct();
    }

    private void EnforceDistinct()
    {
        if (_resolving)
            return;
        _resolving = true;
        try
        {
            var used = new bool[3];
            for (var i = 0; i < Slots.Count; i++)
            {
                if (used[Slots[i].Axis])
                {
                    for (var f = 0; f < 3; f++)
                    {
                        if (used[f] || Slots.Any(s => s.Axis == f))
                            continue;
                        Slots[i].Axis = f; // triggers OnAxisChanged -> re-entered (guarded)
                        Slots[i].Notify();
                        break;
                    }
                }
                used[Slots[i].Axis] = true;
            }
        }
        finally
        {
            _resolving = false;
        }
        _owner.ApplyRow(this);
    }

    private void SetOrigin(int index, double value, ref double field)
    {
        if (SetField(ref field, value))
        {
            _owner.ApplyRow(this);
        }
    }
}

/// <summary>One slot's world-axis selection (0=X, 1=Y, 2=Z).</summary>
public sealed class WorldAxisRow : CNC.Core.ViewModelBase
{
    private readonly PlotterGlPlaneRow _row;
    private int _axis;

    public WorldAxisRow(PlotterGlPlaneRow row, int slotIndex)
    {
        _row = row;
        SlotIndex = slotIndex;
    }

    public int SlotIndex { get; }

    public int Axis
    {
        get => _axis;
        set
        {
            if (SetField(ref _axis, Math.Clamp(value, 0, 2)))
                _row.OnAxisChanged();
        }
    }

    public string[] Choices => ["X", "Y", "Z"];

    internal void Notify() => OnPropertyChanged(nameof(Axis));
}