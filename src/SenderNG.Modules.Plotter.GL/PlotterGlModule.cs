/*
 * PlotterGlModule.cs - G-code 3D plotter plugin module (OpenGL)
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
using SenderNG.Modules.Plotter.GL.Settings;

namespace SenderNG.Modules.Plotter.GL;

/// <summary>
/// G-code 3D path plotter as an external plugin using an OpenGL
/// render stack (the GL sibling of the Skia 2D plotter's path).
/// Claims a Center tab next to the Skia "Plot" tab and renders the
/// loaded program's toolpath in a freely-orbitable 3D viewport.
/// <para>
/// This task ships the plugin SKELETON: the module contract, DI resolution of
/// the program/controller services, and a placeholder view. The real 3D
/// viewport and geometry building land in later tasks.
/// </para>
/// <para>
/// Contract surface: resolves only Core types — the program comes through
/// <see cref="ILoadedProgramSource"/>, machine control through
/// <see cref="IGrblService"/>, and localization/settings through the Core
/// services. No app-assembly type is referenced, so the plugin stays swappable.
/// </para>
/// </summary>
public class PlotterGlModule : IModule, ILocalizedModule, IPluginInfo, IPluginDependencies, IModuleSettingsProvider
{
    private IServiceProvider? _services;
    private PlotterGlSettingsViewModel? _sharedSettingsVm;
    private bool _profileSwitchedSubscribed;
    private PlotterGlView? _plotView;

    public string Id => "plotter-gl";
    public string Name => "Plot 3D";
    public string Description => "G-code 3D path plotter: program toolpath with live run overlay.";
    // Right after the Skia plotter (22) — the two plot tabs read as one group.
    public int Order => 23;
    public PanelHint Panel => PanelHint.Center;
    // Not visible by default: the 3D plotter is A/B'd behind the scenes until
    // the plugin proves itself, so the Skia plotter stays the default.
    public bool IsVisibleByDefault => false;
    // Hideable: a third-party visualizer can replace it the same way as Skia.
    public bool CanHide => true;
    public IReadOnlyList<HotKey> HotKeys { get; } = [];

    // Optional plugin metadata — shown on the About screen.
    public string Version => "0.2.0";
    public string Author => "Andrew Shkolik";
    public string License => "BSD-3-Clause";

    public IReadOnlyList<PluginDependency> Dependencies { get; } =
    [
        new("Silk.NET.OpenGL", "2.23.0", "MIT"),
    ];

    public IReadOnlyDictionary<string, string> LocalizationKeys { get; } =
        new Dictionary<string, string>
        {
            ["module.plotter-gl.name"] = "Plot 3D",
            ["module.plotter-gl.description"] = "G-code 3D path plotter: program toolpath with live run overlay.",
            ["module.plotter-gl.settings.wireLength"] = "Wire length (tower distance)",
            ["module.plotter-gl.settings.worldAxes"] = "World axes",
            ["module.plotter-gl.settings.origin"] = "Origin",
            ["module.plotter-gl.settings.plane"] = "Plane {0}",
            ["module.plotter-gl.settings.view"] = "View",
            ["module.plotter-gl.settings.showOrigin"] = "Show origin",
            ["module.plotter-gl.settings.showEnvelope"] = "Show envelope",
            ["module.plotter-gl.settings.showRunState"] = "Show run state",
            ["module.plotter-gl.settings.showGrid"] = "Show grid",
            ["module.plotter-gl.settings.supersample"] = "2x supersample (AA at 2x render)",
            ["module.plotter-gl.settings.msaa"] = "MSAA (edge AA)",
            ["module.plotter-gl.settings.showLoft"] = "Show loft",
            ["module.plotter-gl.saveScreenshot"] = "Save screenshot (PNG)",
            ["module.plotter-gl.saveScreenshotErrorTitle"] = "Save screenshot",
        };

    public void RegisterServices(IServiceCollection services)
    {
        // Nothing to register: the module resolves the app's DI SettingsService
        // and LocalizationService directly. The settings VM is ONE shared model
        // (see SharedSettingsVm); it is rebuilt fresh on profile switch.
    }

    public object CreateViewModel(IServiceProvider serviceProvider)
    {
        // The host realizes module view models on the UI thread — capture the
        // dispatcher for the VM's event marshaling (headless constructions of
        // PlotterGlViewModel never initialize it and stay inline; see
        // PlotterGlUi).
        PlotterGlUi.Initialize();
        return new PlotterGlViewModel(
            serviceProvider.GetRequiredService<ILoadedProgramSource>(),
            serviceProvider.GetRequiredService<IGrblService>(),
            serviceProvider.GetRequiredService<SettingsService>(),
            serviceProvider.GetRequiredService<LocalizationService>(),
            // The machine seam: one resolved object (preset ⊕ profile ⊕ board)
            // shared by every consumer, instead of each module re-resolving the
            // preset at its own trigger point. Registered by HostSetup.
            kinematics: serviceProvider.GetRequiredService<IMachineKinematicsProvider>());
    }

    // Settings fields must work before the module's own view is opened (App Settings dialog).
    public void AttachServices(IServiceProvider services) =>
        _services = services;

    /// <summary>The SINGLE <see cref="PlotterGlSettingsViewModel"/> both editor
    /// surfaces bind: the App Settings leaf (<see cref="CreateSettingsView"/>) and
    /// the 3D view's overlay toggle bar (<see cref="CreateView"/>). One shared VM on
    /// the app's DI <see cref="SettingsService"/> — no second service and no second
    /// clone — so a flip on either surface persists through the shared service and
    /// both stay live on the SAME model. A profile switch rebuilds it fresh
    /// (<see cref="OnProfileSwitched"/>), preserving the NotSame-across-profiles
    /// behavior the module tests pin.</summary>
    private PlotterGlSettingsViewModel? SharedSettingsVm()
    {
        if (_sharedSettingsVm is not null)
            return _sharedSettingsVm;
        try
        {
            var services = _services!;
            var settings = services.GetRequiredService<SettingsService>();
            if (!_profileSwitchedSubscribed)
            {
                settings.ProfileSwitched += OnProfileSwitched;
                _profileSwitchedSubscribed = true;
            }
            _sharedSettingsVm = new PlotterGlSettingsViewModel(
                settings,
                services.GetService<LocalizationService>(),
                // The loft gate needs the machine's plane count; the provider is
                // the app's one authority on it.
                services.GetService<IMachineKinematicsProvider>());
            return _sharedSettingsVm;
        }
        catch
        {
            // A settings read failure must never take down the App Settings
            // dialog or the 3D viewport: both surfaces simply stay unbound
            // (the old view-local editor's catch behavior).
            return null;
        }
    }

    private void OnProfileSwitched(string _)
    {
        _sharedSettingsVm = null;
        if (_plotView is not null && _services is not null)
            _plotView.OverlaySettings = SharedSettingsVm();
    }

    public object? CreateSettingsView() =>
        _services is null
            ? null
            : new PlotterGlSettingsView { DataContext = SharedSettingsVm() };

    public object CreateView(object viewModel)
    {
        var view = new PlotterGlView(
            _services?.GetService<IFileDialogService>()) { DataContext = viewModel };
        _plotView = view;
        if (_services is not null)
            view.OverlaySettings = SharedSettingsVm();
        return view;
    }
}
