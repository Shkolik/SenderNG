/*
 * HeightMapViewerSettingsViewModel.cs - AppSettings editor for the viewer section
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

namespace SenderNG.Modules.HeightMapViewer.Settings;

/// <summary>AppSettings editor for the <c>heightmap-viewer</c> section: render
/// supersampling and multisample mode.</summary>
public sealed class HeightMapViewerSettingsViewModel : CNC.Core.ViewModelBase
{
    private readonly SettingsService _settingsService;
    private readonly LocalizationService? _loc;
    private readonly MachineProfile _profile;
    private HeightMapViewerSettings _settings;
    private bool _saving;

    public HeightMapViewerSettingsViewModel(SettingsService settingsService,
        LocalizationService? localization = null)
    {
        _settingsService = settingsService;
        _loc = localization;
        _profile = _settingsService.Load();
        _settings = HeightMapViewerConfig.Load(_profile);

        // Re-resolve the code-side labels on every locale switch (Language picker
        // changes are applied immediately, without restarting the app).
        if (localization is not null)
            localization.Changed += (_, _) => RelocalizeLabels();
    }

    private void RelocalizeLabels()
    {
        OnPropertyChanged(nameof(SupersampleLabel));
        OnPropertyChanged(nameof(MsaaLabel));
        OnPropertyChanged(nameof(ShowOriginLabel));
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

    /// <summary>The <see cref="MsaaMode"/> as the ComboBox's SelectedIndex over
    /// <see cref="MsaaChoices"/> (the enum members are contiguous from zero, so
    /// the index and the value are the same number). Out-of-range writes — the
    /// transient -1 a ComboBox reports while it has no matching selection — are
    /// ignored: not converted to <see cref="MsaaMode.None"/> and persisted
    /// behind the user's back.</summary>
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

    /// <summary>Show the X/Y/Z origin axis arrows. Runtime — the view toggles the
    /// scene each tick. Default ON.</summary>
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

    /// <summary>Localized "Show origin" label for the axes toggle.</summary>
    public string ShowOriginLabel =>
        Resolve("module.heightmap-viewer.settings.showOrigin", "Show origin");

    /// <summary>Localized "2x supersample" label for the render-quality toggle.</summary>
    public string SupersampleLabel =>
        Resolve("module.heightmap-viewer.settings.supersample", "2x supersample (AA at 2x render)");

    /// <summary>Localized label for the MSAA mode selector row.</summary>
    public string MsaaLabel =>
        Resolve("module.heightmap-viewer.settings.msaa", "MSAA (edge AA)");

    /// <summary>Writes the section to the active profile and saves it (profile-save cadence).</summary>
    public void Save()
    {
        if (_saving)
            return;
        _saving = true;
        try
        {
            HeightMapViewerConfig.Save(_profile, _settings);
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

    private string Resolve(string key, string fallback) => _loc?.Resolve(key, fallback) ?? fallback;
}
