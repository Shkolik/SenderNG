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
public class PlotterNexModule : IModule, ILocalizedModule, IPluginInfo
{
    public string Id => "plotter-nex";
    public string Name => "Plot 3D";
    public string Description => "G-code 3D path plotter: HelixToolkit.Nex orbit view (PoC).";
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

    public IReadOnlyDictionary<string, string> LocalizationKeys { get; } =
        new Dictionary<string, string>
        {
            ["module.plotterNex.name"] = "Plot 3D",
            ["module.plotterNex.description"] = "G-code 3D path plotter: HelixToolkit.Nex orbit view (PoC).",
            ["module.plotterNex.placeholder"] = "3D plotter (PoC)",
        };

    public void RegisterServices(IServiceCollection services) { }

    public object CreateViewModel(IServiceProvider serviceProvider) =>
        new PlotterNexViewModel(
            serviceProvider.GetRequiredService<ILoadedProgramSource>(),
            serviceProvider.GetRequiredService<IGrblService>(),
            serviceProvider.GetRequiredService<SettingsService>(),
            serviceProvider.GetRequiredService<LocalizationService>());

    public object CreateView(object viewModel) =>
        new PlotterNexView { DataContext = viewModel };
}
