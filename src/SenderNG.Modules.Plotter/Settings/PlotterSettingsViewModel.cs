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

using CNC.Core;
using CNC.Core.Services;
using SenderNG.Core.Localization;
using SenderNG.Modules.Plotter;

namespace SenderNG.Modules.Plotter.Settings;

/// <summary>App-Settings editor for the <c>plotter</c> section: the four view
/// toggles, and nothing else. ONE instance binds both editor surfaces — the App
/// Settings leaf and the plot view's overlay — so a flip on either is visible on
/// both.
/// <para>The per-plane world-axis combos and the origin rows that used to live
/// here are <b>deleted</b>: within a plane slot <c>k</c> feeds world axis
/// <c>k</c> by construction, so there was never a mapping for the user to
/// choose — the machine is drawn the way it is laid out, and the axis layout is
/// the profile editor's business (a machine-model concern), not the plotter's.</para>
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
        Model = PlotterConfig.Load(_profile);

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

    /// <summary>The live settings. Mutating it is the module's job; the plot
    /// view reads the flags through this instance.</summary>
    public PlotterSettings Model { get; }

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
    public string ZoomInLabel => Resolve("module.plotter.zoomIn", "Zoom in");
    public string ZoomOutLabel => Resolve("module.plotter.zoomOut", "Zoom out");
    public string ZoomFitLabel => Resolve("module.plotter.zoomFit", "Fit");
    public string SavePngLabel => Resolve("module.plotter.savePng", "Save PNG");

    public string Resolve(string key, string fallback) =>
        _loc?.Resolve(key, fallback) is { Length: > 0 } s ? s : fallback;

    private void RelocalizeLabels()
    {
        // Every one of the *Label properties above, no exceptions. This list was
        // written by hand and shipped with ZoomFitLabel missing, so the "Fit"
        // tooltip kept the old language while everything else updated.
        // `A_locale_change_re_notifies_every_label` enumerates them by name so
        // the next omission fails a test.
        OnPropertyChanged(nameof(ViewLabel));
        OnPropertyChanged(nameof(ShowGridLabel));
        OnPropertyChanged(nameof(ShowOriginLabel));
        OnPropertyChanged(nameof(ShowEnvelopeLabel));
        OnPropertyChanged(nameof(ShowRunStateLabel));
        OnPropertyChanged(nameof(ZoomInLabel));
        OnPropertyChanged(nameof(ZoomOutLabel));
        OnPropertyChanged(nameof(ZoomFitLabel));
        OnPropertyChanged(nameof(SavePngLabel));
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
        // state after a flip.
        OnPropertyChanged(nameof(Flags));
        OnPropertyChanged(nameof(ShowGrid));
        OnPropertyChanged(nameof(ShowOrigin));
        OnPropertyChanged(nameof(ShowEnvelope));
        OnPropertyChanged(nameof(ShowRunState));
        Changed?.Invoke();
    }
}
