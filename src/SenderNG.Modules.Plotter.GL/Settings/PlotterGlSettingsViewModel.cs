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

using CNC.Core.Services;
using SenderNG.Core.Localization;
using SenderNG.Modules.Common.Render;

namespace SenderNG.Modules.Plotter.GL.Settings;

/// <summary>AppSettings editor for the <c>plotter-gl</c> section: the scene view
/// toggles, the loft toggle (gated by the machine's plane count) and render
/// quality (supersampling + MSAA).
/// <para>The wire-length field, the per-slot world-axis selectors and the origin
/// rows that used to live here are <b>deleted</b>: the plane layout and the
/// plane-to-plane separation are machine-model facts now
/// (<c>IMachineKinematicsProvider</c> / the profile's <c>Separation</c>), not a
/// per-plotter rendering choice. Within a plane slot <c>k</c> feeds world axis
/// <c>k</c>, so there was never a mapping to pick.</para></summary>
public sealed class PlotterGlSettingsViewModel : CNC.Core.ViewModelBase
{
    private readonly SettingsService _settingsService;
    private readonly LocalizationService? _loc;
    private readonly MachineProfile _profile;
    private readonly IMachineKinematicsProvider? _kinematics;
    private readonly PlotterGlSettings _settings;
    private bool _saving;

    public PlotterGlSettingsViewModel(
        SettingsService settingsService,
        LocalizationService? localization = null,
        IMachineKinematicsProvider? kinematics = null)
    {
        _settingsService = settingsService;
        _loc = localization;
        _kinematics = kinematics;
        _profile = _settingsService.Load();
        _settings = PlotterGlConfig.Load(_profile);

        // Re-resolve the code-side labels on every locale switch (Language picker
        // changes are applied immediately, without restarting the app).
        if (localization is not null)
            localization.Changed += (_, _) => RelocalizeLabels();
    }

    private void RelocalizeLabels()
    {
        OnPropertyChanged(nameof(ViewLabel));
        OnPropertyChanged(nameof(ShowOriginLabel));
        OnPropertyChanged(nameof(ShowEnvelopeLabel));
        OnPropertyChanged(nameof(ShowRunStateLabel));
        OnPropertyChanged(nameof(ShowGridLabel));
        OnPropertyChanged(nameof(ShowLoftLabel));
        OnPropertyChanged(nameof(SupersampleLabel));
        OnPropertyChanged(nameof(MsaaLabel));
    }

    /// <summary>The active machine's plane count: the resolved kinematics when
    /// the module injected its provider, otherwise the profile's own resolution
    /// (the editor is built with the module's DI, so the provider is the normal
    /// path; the fallback keeps a directly constructed editor working, and both
    /// answer the same number for the active profile).</summary>
    private int PlaneCount =>
        (_kinematics?.Current ?? MachineKinematicsResolver.Resolve(_profile, null, 0)).Planes.Count;

    /// <summary>True when the machine has at least two planes — a sheet
    /// spans the space BETWEEN planes, so a single-plane mill has nothing to
    /// show and neither toolbar nor settings carry the loft toggle. The
    /// profile switch rebuilds this VM, so no PropertyChanged refresh is
    /// raised here.</summary>
    public bool HasLoft => PlaneCount >= 2;

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

    internal string Resolve(string key, string fallback) => _loc?.Resolve(key, fallback) ?? fallback;
}
