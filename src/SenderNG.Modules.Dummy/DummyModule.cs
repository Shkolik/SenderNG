
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
using Microsoft.Extensions.DependencyInjection;
using SenderNG.Core.Localization;
using SenderNG.Core.Modules;

namespace SenderNG.Modules.Dummy;

/// <summary>
/// Proof-of-concept external plugin module discovered via ModuleCatalog assembly scan.
/// Claims the Flyout panel and shows a "Dummy plugin works!" view.
/// <para>
/// Localization: this module serves its OWN locale catalog (the <c>Locale/*.json</c>
/// files copied to <c>OutputDir/Modules/dummy/Locale</c> and registered with the
/// <see cref="LocalizationService"/> by the host) instead of the app's global en/ru
/// files — the contract for 3rd-party plugins. Keys are namespaced <c>module.dummy.*</c>.
/// </para>
/// </summary>
public class DummyModule : IModule, ILocalizedModule
{
    public string Id => "dummy";
    public string Name => "Dummy";
    public string Description => "Example third-party plugin used to prove assembly-scanned module discovery.";
    public int Order => 100;
    public PanelHint Panel => PanelHint.Flyout;
    public bool IsVisibleByDefault => true;
    public IReadOnlyList<HotKey> HotKeys { get; } = [];

    public IReadOnlyDictionary<string, string> LocalizationKeys { get; } =
        new Dictionary<string, string>
        {
            ["module.dummy.name"] = "Dummy",
            ["module.dummy.description"] = "Example third-party plugin used to prove assembly-scanned module discovery.",
            ["module.dummy.tagline"] = "Dummy plugin works!",
        };

    public void RegisterServices(IServiceCollection services) { }

    public void RegisterSettings(MachineProfile settings)
    {
        if (!settings.ModuleSettings.ContainsKey("Dummy"))
            settings.ModuleSettings["Dummy"] = new List<object>();
    }

    public object CreateViewModel(IServiceProvider serviceProvider) =>
        new DummyViewModel(serviceProvider.GetRequiredService<LocalizationService>());

    public object CreateView(object viewModel) =>
        new DummyView { DataContext = viewModel };
}
