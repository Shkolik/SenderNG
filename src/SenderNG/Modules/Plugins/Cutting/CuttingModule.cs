
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
using SenderNG.Core.Modules;

namespace SenderNG.Modules.Plugins;

public class CuttingModule : IModule, ILocalizedModule
{
    public string Id => "cutting";
    public string Name => "Cutting";
    public string Description => "Feed/spindle overrides and cycle controls for manual cutting.";
    public int Order => 25;
    public PanelHint Panel => PanelHint.Right;
    public bool IsVisibleByDefault => true;
    public IReadOnlyList<HotKey> HotKeys { get; } = [];

    public IReadOnlyDictionary<string, string> LocalizationKeys { get; } =
        new Dictionary<string, string>
        {
            ["module.cutting.feed"] = "Feed",
            ["module.cutting.speed"] = "Speed",
            ["module.cutting.feedOverride"] = "Feed override",
            ["module.cutting.rapidsOverride"] = "Rapids override",
            ["module.cutting.speedOverride"] = "Speed override",
            ["module.cutting.reset100"] = "Reset to 100%",
            ["module.cutting.spindleRpmHint"] = "Spindle speed (RPM) used by CW/CCW",
            ["module.cutting.cw"] = "CW",
            ["module.cutting.ccw"] = "CCW",
            ["module.cutting.off"] = "Off",
            ["module.cutting.spindleCwHint"] = "Spindle on, clockwise (M3)",
            ["module.cutting.spindleCcwHint"] = "Spindle on, counter-clockwise (M4)",
            ["module.cutting.spindleOffHint"] = "Spindle off (M5)",
        };

    public void RegisterServices(IServiceCollection services) =>
        services.AddSingleton<CuttingViewModel>();


    public object CreateViewModel(IServiceProvider serviceProvider) =>
        serviceProvider.GetRequiredService<CuttingViewModel>();

    public object CreateView(object viewModel) =>
        new CuttingView { DataContext = viewModel };
}
