/*
 * PlotterNexSettingsViewModel.cs - AppSettings editor for the plotter-nex section
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

namespace SenderNG.Modules.Plotter.Nex.Settings;

/// <summary>AppSettings editor for the <c>plotter-nex</c> section: wire length
/// (2+ planes), per-slot world-axis selection, per-plane origin offsets, scene
/// view toggles, and anti-aliasing mode/quality.</summary>
public sealed class PlotterNexSettingsViewModel : CNC.Core.ViewModelBase
{
    private readonly SettingsService _settingsService;
    private readonly LocalizationService? _loc;
    private readonly MachineProfile _profile;
    private PlotterNexSettings _settings;
    private bool _saving;

    public PlotterNexSettingsViewModel(SettingsService settingsService, LocalizationService? localization = null)
    {
        _settingsService = settingsService;
        _loc = localization;
        _profile = _settingsService.Load();
        var type = MachineTypeCatalog.Resolve(_profile.MachineTypeId);
        _settings = PlotterNexConfig.Load(_profile, type);
        PlaneRows = new ObservableCollection<PlotterNexPlaneRow>(
            _settings.Planes.Select((_, p) => new PlotterNexPlaneRow(this, p)));
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
        OnPropertyChanged(nameof(AntiAliasingLabel));
        OnPropertyChanged(nameof(AaModeLabel));
        OnPropertyChanged(nameof(AaQualityLabel));
    }

    /// <summary>True when the machine has more than one plane (wire length applies).</summary>
    public bool ShowWireLength => _settings.Planes.Count > 1;

    public double WireLength
    {
        get => _settings.WireLength;
        set
        {
            if (Math.Abs(_settings.WireLength - value) < 1e-9)
                return;
            _settings.WireLength = value;
            PlotterNexConfig.RecenterOnWire(_settings);
            SyncRows();
            Save();
            OnPropertyChanged();
        }
    }

    /// <summary>Localized "Wire length (tower distance)" label (plugin views
    /// cannot use the app-only {Loc} markup, so labels resolve code-side).</summary>
    public string WireLengthLabel => Resolve("module.plotter-nex.settings.wireLength", "Wire length (tower distance)");

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

    public int AaMode
    {
        get => _settings.AaMode;
        set
        {
            if (_settings.AaMode == value) return;
            _settings.AaMode = value;
            Save();
            OnPropertyChanged();
        }
    }

    public int AaQuality
    {
        get => _settings.AaQuality;
        set
        {
            if (_settings.AaQuality == value) return;
            _settings.AaQuality = value;
            Save();
            OnPropertyChanged();
        }
    }

    /// <summary>Localized "View" section heading.</summary>
    public string ViewLabel => Resolve("module.plotter-nex.settings.view", "View");

    /// <summary>Localized "Show origin" toggle label.</summary>
    public string ShowOriginLabel => Resolve("module.plotter-nex.settings.showOrigin", "Show origin");

    /// <summary>Localized "Show envelope" toggle label.</summary>
    public string ShowEnvelopeLabel => Resolve("module.plotter-nex.settings.showEnvelope", "Show envelope");

    /// <summary>Localized "Show run state" toggle label.</summary>
    public string ShowRunStateLabel => Resolve("module.plotter-nex.settings.showRunState", "Show run state");

    /// <summary>Localized "Show grid" toggle label.</summary>
    public string ShowGridLabel => Resolve("module.plotter-nex.settings.showGrid", "Show grid");

    /// <summary>Localized "Rendering / Anti-aliasing" section heading.</summary>
    public string AntiAliasingLabel => Resolve("module.plotter-nex.settings.antiAliasing", "Rendering / Anti-aliasing");

    /// <summary>Localized "Mode" label for the AA method combo.</summary>
    public string AaModeLabel => Resolve("module.plotter-nex.settings.aaMode", "Mode");

    /// <summary>Localized "Quality" label for the AA quality combo.</summary>
    public string AaQualityLabel => Resolve("module.plotter-nex.settings.aaQuality", "Quality");

    /// <summary>AA method combo items (0=Off, 1=SMAA, 2=FXAA).</summary>
    public string[] AaModeChoices => ["Off", "SMAA", "FXAA"];

    /// <summary>AA quality combo items (0=Low, 1=Medium, 2=High, 3=Ultra).</summary>
    public string[] AaQualityChoices => ["Low", "Medium", "High", "Ultra"];

    public ObservableCollection<PlotterNexPlaneRow> PlaneRows { get; }

    /// <summary>Writes the section to the active profile and saves it (profile-save cadence).</summary>
    public void Save()
    {
        if (_saving)
            return;
        _saving = true;
        try
        {
            PlotterNexConfig.Save(_profile, _settings);
            _settingsService.Save(_profile);
        }
        finally
        {
            _saving = false;
        }
    }

    /// <summary>Applies one edited row back into the settings object and persists.</summary>
    internal void ApplyRow(PlotterNexPlaneRow row)
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
public sealed class PlotterNexPlaneRow : CNC.Core.ViewModelBase
{
    private readonly PlotterNexSettingsViewModel _owner;
    private bool _resolving;
    private double _originX;
    private double _originY;
    private double _originZ;

    public PlotterNexPlaneRow(PlotterNexSettingsViewModel owner, int planeIndex)
    {
        _owner = owner;
        PlaneIndex = planeIndex;
        Slots = new ObservableCollection<WorldAxisRow>(
            Enumerable.Range(0, owner.PlaneRowRoleCount(planeIndex))
                .Select(s => new WorldAxisRow(this, s)));
    }

    public int PlaneIndex { get; }

    public string Title => _owner.ResolveFormat("module.plotter-nex.settings.plane", "Plane {0}", PlaneIndex + 1);

    public string WorldAxesLabel => _owner.Resolve("module.plotter-nex.settings.worldAxes", "World axes");

    public string OriginLabel => _owner.Resolve("module.plotter-nex.settings.origin", "Origin");

    public ObservableCollection<WorldAxisRow> Slots { get; }

    public double OriginX { get => _originX; set => SetOrigin(0, value, ref _originX); }
    public double OriginY { get => _originY; set => SetOrigin(1, value, ref _originY); }
    public double OriginZ { get => _originZ; set => SetOrigin(2, value, ref _originZ); }

    internal void Apply(NexPlaneGeometry g)
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

    internal NexPlaneGeometry ToGeometry() => new()
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
    private readonly PlotterNexPlaneRow _row;
    private int _axis;

    public WorldAxisRow(PlotterNexPlaneRow row, int slotIndex)
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