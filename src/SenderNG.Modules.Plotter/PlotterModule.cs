/*
 * PlotterModule.cs - G-code plotter plugin module
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

using CNC.Core.Abstractions;
using CNC.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using SenderNG.Core.Localization;
using SenderNG.Core.Modules;
using SenderNG.Modules.Plotter.Settings;

namespace SenderNG.Modules.Plotter;

/// <summary>
/// G-code path plotter as a true external plugin (the converter model): drop
/// the DLL in <c>Modules/</c> and the "Plot" tab appears; remove it and the app
/// never knew it existed. Claims a Center tab (next to Program/Console) and
/// renders the loaded program's toolpath — mill (flat XY + isometric) and foam
/// (isometric loft | two-canvas flat) — following a live run's send frontier.
/// <para>
/// Contract surface: the module resolves only Core types — the program comes
/// through <see cref="ILoadedProgramSource"/> (implemented by the app's
/// ProgramViewModel and registered in DI alongside it), machine geometry
/// through <see cref="IMachineKinematicsProvider"/> and controller letters
/// through the same resolved object. No app-assembly type is referenced,
/// so the plugin stays swappable by any third-party visualizer.
/// </para>
/// </summary>
public class PlotterModule : IModule, ILocalizedModule, IPluginInfo, IModuleSettingsProvider
{
    private IServiceProvider? _services;
    private PlotterSettingsViewModel? _sharedSettingsVm;
    private bool _profileSwitchedSubscribed;
    private PlotterView? _plotView;
    private PlotterViewModel? _plotVm;

    public string Id => "plotter";
    public string Name => "Plot";
    public string Description => "G-code path plotter: program toolpath with live run overlay.";
    // Right after Program (20) and Console (21) — the three center tabs read as
    // one group. Fine-tune at implementation time if the ordering feels off.
    public int Order => 22;
    public PanelHint Panel => PanelHint.Center;
    public bool IsVisibleByDefault => true;
    // Hideable: a third-party visualizer replaces this plugin by the user
    // hiding the built-in Plot tab and enabling theirs.
    public bool CanHide => true;
    public IReadOnlyList<HotKey> HotKeys { get; } = [];

    // Optional plugin metadata — shown on the About screen.
    public string Version => "0.1.0";
    public string Author => "Andrew Shkolik";
    public string License => "BSD-3-Clause";

    /// <summary>Every user-visible string this module produces, per
    /// <see cref="ILocalizedModule"/>. The 12 view-chrome keys are here because
    /// the 3D sibling lists its own <c>settings.*</c> keys the same way: this
    /// dictionary is the module's declaration of what it can localize, and a key
    /// resolved only inside <c>PlotterSettingsViewModel</c> is invisible to
    /// <c>PlotterModuleTests.Every_declared_locale_key_exists_in_both_catalogs</c>.
    /// The values are the English fallbacks, and they match
    /// <c>Locale/en.json</c> byte for byte.</summary>
    public IReadOnlyDictionary<string, string> LocalizationKeys { get; } =
        new Dictionary<string, string>
        {
            ["module.plotter.name"] = "Plot",
            ["module.plotter.description"] = "G-code path plotter: program toolpath with live run overlay.",
            ["module.plotter.noProgram"] = "No program loaded.",
            ["module.plotter.noMotion"] = "Program contains no motion.",
            ["module.plotter.axisMismatch"] = "Machine needs {0} axes; program has {1}. Unrenderable planes are hidden.",
            ["module.plotter.buildFailed"] = "Program could not be plotted.",
            ["module.plotter.building"] = "Plotting…",
            ["module.plotter.settings.view"] = "View",
            ["module.plotter.settings.showGrid"] = "Show grid",
            ["module.plotter.settings.showOrigin"] = "Show origin",
            ["module.plotter.settings.showEnvelope"] = "Show envelope",
            ["module.plotter.settings.showRunState"] = "Show run state",
            ["module.plotter.zoomIn"] = "Zoom in",
            ["module.plotter.zoomOut"] = "Zoom out",
            ["module.plotter.zoomFit"] = "Fit",
            ["module.plotter.savePng"] = "Save PNG",
        };

    public void RegisterServices(IServiceCollection services) { }

    public SettingsScope Scope => SettingsScope.Profile;

    // Settings fields must work before the module's own view is opened: the App
    // Settings dialog enumerates the providers first.
    public void AttachServices(IServiceProvider services) => _services = services;

    /// <summary>The ONE settings model behind every surface: the App Settings
    /// leaf, the plot view's plane mappings and its canvas chrome. Built on
    /// first demand (a leaf, or the plot view opening) and rebuilt only by
    /// <see cref="OnProfileSwitched"/>.</summary>
    private PlotterSettingsViewModel? SharedSettingsVm()
    {
        if (_sharedSettingsVm is not null)
            return _sharedSettingsVm;
        try
        {
            var settings = _services!.GetRequiredService<SettingsService>();
            if (!_profileSwitchedSubscribed)
            {
                settings.ProfileSwitched += OnProfileSwitched;
                _profileSwitchedSubscribed = true;
            }
            _sharedSettingsVm = new PlotterSettingsViewModel(
                settings, _services!.GetService<LocalizationService>());
            _sharedSettingsVm.Changed += ApplySettings;
            return _sharedSettingsVm;
        }
        catch
        {
            // This swallows EVERY exception, not a named list: an unregistered
            // SettingsService, a corrupt profile file, a throw from the loader, and
            // also cancellation and threading failures. A bare catch cannot be
            // selective, so the doc must not imply otherwise. In practice the
            // method is fully synchronous with no token and no dispatcher hop, so
            // the last two cannot actually occur here.
            //
            // A module that cannot read its section must still leave the App
            // Settings dialog and the plot view standing, so both surfaces simply
            // stay unbound — which is why CreateSettingsView checks for null
            // services FIRST instead of relying on this.
            return null;
        }
    }

    /// <summary>Pushes the live view toggles into the plot view. The plane
    /// mapping is no longer pushed anywhere: the plot view reads the machine's
    /// axis layout from <see cref="IMachineKinematicsProvider"/> itself, so
    /// there is nothing for this module to copy onto it on a toggle.</summary>
    private void ApplySettings()
    {
        _plotView?.PushChromeFlags(_sharedSettingsVm?.Flags);
    }

    /// <summary>A machine switch invalidates the stored section: the new profile
    /// keeps its own toggles and plane mappings. The outgoing model is DISPOSED,
    /// not merely dropped — it holds a subscription to the app-lifetime
    /// <see cref="LocalizationService"/> that keeps a reference to it, so
    /// nulling the field alone leaked one live model per switch and one language
    /// change ran one relabel pass per switch ever made.</summary>
    private void OnProfileSwitched(string profileId)
    {
        if (_sharedSettingsVm is not null)
        {
            _sharedSettingsVm.Changed -= ApplySettings;
            _sharedSettingsVm.Dispose();
            _sharedSettingsVm = null;
        }
        // Rebuilt HERE, not lazily: ApplySettings below reads the new profile's
        // mappings, and a lazy build would push the empty list instead.
        _ = SharedSettingsVm();

        // The overlay must be re-pointed at the replacement. It was left bound
        // to the DISPOSED model above, so its checkboxes kept writing through a
        // disposed view model and Persist() saved the PREVIOUS profile's config
        // file — the new profile's toggles were silently discarded while the old
        // one was rewritten. The sibling module does exactly this.
        if (_plotView is not null)
            _plotView.OverlaySettings = _sharedSettingsVm;

        ApplySettings();
    }

    public object? CreateSettingsView() =>
        // No services means the module was never attached (constructed in a test
        // or outside the host): SharedSettingsVm() would swallow the failed
        // service lookup and bind the leaf to a null model, so decline outright
        // and leave the App Settings dialog with nothing to show for `plotter`.
        _services is null
            ? null
            : new PlotterSettingsView { DataContext = SharedSettingsVm() };

    public object CreateViewModel(IServiceProvider serviceProvider)
    {
        _services ??= serviceProvider;
        // The host realizes module view models on the UI thread — capture the
        // dispatcher for the VM's event marshaling (headless constructions of
        // PlotterViewModel never initialize it and stay inline; see PlotterUi).
        PlotterUi.Initialize();
        var vm = new PlotterViewModel(
            serviceProvider.GetRequiredService<ILoadedProgramSource>(),
            serviceProvider.GetRequiredService<IGrblService>(),
            serviceProvider.GetRequiredService<SettingsService>(),
            serviceProvider.GetRequiredService<LocalizationService>(),
            // The machine seam: one resolved object (preset ⊕ profile ⊕ board)
            // shared by every consumer, instead of each module re-resolving the
            // preset at its own trigger point. Registered by HostSetup.
            serviceProvider.GetRequiredService<IMachineKinematicsProvider>());
        _plotVm = vm;
        // The shared model FIRST: ApplySettings reads it, so calling ApplySettings
        // before this would push an EMPTY mapping list and silently ignore
        // whatever the active profile stores.
        _ = SharedSettingsVm();
        ApplySettings();
        return vm;
    }

    public object CreateView(object viewModel)
    {
        if (viewModel is PlotterViewModel vm)
            _plotVm = vm;

        // The dialog service, not the parameterless ctor. The module DOES have
        // the app's DI, and the old call dropped it, so _dialogs was null in the
        // only production path and the PNG button was inert — while the field's
        // own doc claimed null happened "wherever the module builds the view
        // without the app's DI", which was nowhere. As in the sibling module.
        var view = new PlotterView(_services?.GetService<IFileDialogService>())
        {
            DataContext = viewModel,
            // The overlay binds to the SAME instance the App Settings leaf
            // edits. Without this the checkboxes would be bound to nothing and
            // the two surfaces would drift apart.
            //
            // Through the ACCESSOR, not the field. Reading the field binds
            // whatever happens to be cached, which is null both when CreateView
            // runs before CreateViewModel and after SharedSettingsVm() swallowed
            // a service-lookup failure — and a null overlay model makes every
            // checkbox dead with no diagnostic anywhere. The accessor is the one
            // that builds on demand.
            OverlaySettings = SharedSettingsVm(),
        };
        _plotView = view;
        // The toggles live on the CANVASES, not on the view model, so the flags
        // are pushed here rather than in CreateViewModel — at that point this
        // view did not exist and there were no canvases to push them into.
        ApplySettings();
        return view;
    }
}
