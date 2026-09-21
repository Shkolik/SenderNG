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
/// through <see cref="MachineTypeCatalog"/> and controller letters through
/// <see cref="IGrblService.Capabilities"/>. No app-assembly type is referenced,
/// so the plugin stays swappable by any third-party visualizer.
/// </para>
/// </summary>
public class PlotterModule : IModule, ILocalizedModule, IPluginInfo, IPluginDependencies
{
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

    public IReadOnlyList<PluginDependency> Dependencies { get; } =
        [new("Avalonia", "12.0.0", "MIT")];

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
            ["module.plotter.isometric"] = "Iso",
            ["module.plotter.loft"] = "Loft",
        };

    public void RegisterServices(IServiceCollection services) { }

    public object CreateViewModel(IServiceProvider serviceProvider)
    {
        // The host realizes module view models on the UI thread — capture the
        // dispatcher for the VM's event marshaling (headless constructions of
        // PlotterViewModel never initialize it and stay inline; see PlotterUi).
        PlotterUi.Initialize();
        return new PlotterViewModel(
            serviceProvider.GetRequiredService<ILoadedProgramSource>(),
            serviceProvider.GetRequiredService<IGrblService>(),
            serviceProvider.GetRequiredService<SettingsService>(),
            serviceProvider.GetRequiredService<LocalizationService>());
    }

    public object CreateView(object viewModel) =>
        new PlotterView { DataContext = viewModel };
}
