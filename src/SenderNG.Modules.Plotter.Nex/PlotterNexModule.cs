/*
 * PlotterNexModule.cs - G-code 3D plotter plugin module (HelixToolkit.Nex)
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
using SenderNG.Modules.Plotter.Nex.Settings;

namespace SenderNG.Modules.Plotter.Nex;

/// <summary>
/// G-code 3D path plotter as an external plugin using the HelixToolkit.Nex
/// GPU render stack (the Vulkan-based successor to the Skia 2D plotter's
/// path). Claims a Center tab next to the Skia "Plot" tab and renders the
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
public class PlotterNexModule : IModule, ILocalizedModule, IPluginInfo, IPluginDependencies, IModuleSettingsProvider
{
    private IServiceProvider? _services;
    private PlotterNexSettingsViewModel? _sharedSettingsVm;
    private bool _profileSwitchedSubscribed;
    private PlotterNexView? _plotView;

    public string Id => "plotter-nex";
    public string Name => "Plot 3D (Nex)";
    public string Description => "G-code 3D path plotter: program toolpath with live run overlay.";
    // Right after the Skia plotter (22) — the two plot tabs read as one group,
    // and the 3D one sits behind it in the tab order.
    public int Order => 23;
    public PanelHint Panel => PanelHint.Center;
    // Not visible by default: the 3D plotter is A/B'd behind the scenes until
    // the plugin proves itself, so the Skia plotter stays the default.
    public bool IsVisibleByDefault => false;
    // Hideable: a third-party visualizer can replace it the same way as Skia.
    public bool CanHide => true;
    public IReadOnlyList<HotKey> HotKeys { get; } = [];

    // Optional plugin metadata — shown on the About screen.
    public string Version => "0.1.0";
    public string Author => "Andrew Shkolik";
    public string License => "BSD-3-Clause";

    public IReadOnlyList<PluginDependency> Dependencies { get; } =
    [
        new("HelixToolkit.Nex.Engine", "1.3.0-alpha.41", "MIT"),
        new("HelixToolkit.Nex.Graphics.Vulkan", "1.3.0-alpha.41", "MIT"),
        new("HelixToolkit.Nex.Interop", "1.3.0-alpha.41", "MIT"),
    ];

    public IReadOnlyDictionary<string, string> LocalizationKeys { get; } =
        new Dictionary<string, string>
        {
            ["module.plotter-nex.name"] = "Plot 3D (Nex)",
            ["module.plotter-nex.description"] = "G-code 3D path plotter: program toolpath with live run overlay.",
            ["module.plotter-nex.settings.wireLength"] = "Wire length (tower distance)",
            ["module.plotter-nex.settings.worldAxes"] = "World axes",
            ["module.plotter-nex.settings.origin"] = "Origin",
            ["module.plotter-nex.settings.plane"] = "Plane {0}",
            ["module.plotter-nex.settings.view"] = "View",
            ["module.plotter-nex.settings.showOrigin"] = "Show origin",
            ["module.plotter-nex.settings.showEnvelope"] = "Show envelope",
            ["module.plotter-nex.settings.showRunState"] = "Show run state",
            ["module.plotter-nex.settings.showGrid"] = "Show grid",
            ["module.plotter-nex.settings.antiAliasing"] = "Rendering / Anti-aliasing",
            ["module.plotter-nex.settings.aaMode"] = "Mode",
            ["module.plotter-nex.settings.aaQuality"] = "Quality",
            ["module.plotter-nex.settings.supersample"] = "2x supersample (AA at 2x render)",
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
        // PlotterNexViewModel never initialize it and stay inline; see
        // PlotterNexUi).
        PlotterNexUi.Initialize();
        return new PlotterNexViewModel(
            serviceProvider.GetRequiredService<ILoadedProgramSource>(),
            serviceProvider.GetRequiredService<IGrblService>(),
            serviceProvider.GetRequiredService<SettingsService>(),
            serviceProvider.GetRequiredService<LocalizationService>());
    }

    // Settings fields must work before the module's own view is opened (App Settings dialog).
    public void AttachServices(IServiceProvider services) =>
        _services = services;

    /// <summary>The SINGLE <see cref="PlotterNexSettingsViewModel"/> both editor
    /// surfaces bind: the App Settings leaf (<see cref="CreateSettingsView"/>) and
    /// the 3D view's overlay toggle bar (<see cref="CreateView"/>). One shared VM on
    /// the app's DI <see cref="SettingsService"/> — no second service and no second
    /// clone — so a flip on either surface persists through the shared service and
    /// both stay live on the SAME model. A profile switch rebuilds it fresh
    /// (<see cref="OnProfileSwitched"/>), preserving the NotSame-across-profiles
    /// behavior the module tests pin.</summary>
    private PlotterNexSettingsViewModel? SharedSettingsVm()
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
            _sharedSettingsVm = new PlotterNexSettingsViewModel(settings, _services!.GetService<LocalizationService>());
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
            : new PlotterNexSettingsView { DataContext = SharedSettingsVm() };

    public object CreateView(object viewModel)
    {
        var view = new PlotterNexView(
            _services?.GetService<IFileDialogService>()) { DataContext = viewModel };
        _plotView = view;
        if (_services is not null)
            view.OverlaySettings = SharedSettingsVm();
        return view;
    }
}
