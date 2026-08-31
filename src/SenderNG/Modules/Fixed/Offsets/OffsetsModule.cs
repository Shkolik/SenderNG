/*
 * OffsetsModule.cs - Offsets module
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
using SenderNG.Core.Modules;

namespace SenderNG.Modules.Fixed;

public sealed class OffsetsModule : IModule, IModuleSettingsProvider, ILocalizedModule
{
    private SettingsService? _settingsService;
    private IServiceProvider? _services;

    public string Id => "offsets-table";
    public string Name => "Offsets";
    public string Description => "Edit work offsets (G54..G59) and the tool table, saved back to the controller.";
    public int Order => 30;
    public PanelHint Panel => PanelHint.Flyout;
    public bool IsVisibleByDefault => true;
    // Core functionality: always visible, no hide checkbox in App Settings.
    public bool CanHide => false;
    public IReadOnlyList<HotKey> HotKeys { get; } = [];

    public IReadOnlyDictionary<string, string> LocalizationKeys { get; } =
        new Dictionary<string, string>
        {
            ["module.offsets-table.showToolTable"] = "Show tool table",
        };

    public void RegisterServices(IServiceCollection services) =>
        services.AddSingleton<OffsetsViewModel>();

    public void RegisterSettings(AppSettings settings) { }

    public object CreateViewModel(IServiceProvider serviceProvider) =>
        serviceProvider.GetRequiredService<OffsetsViewModel>();

    // Fields must work before the Offsets view is ever opened (App Settings dialog).
    public void AttachServices(IServiceProvider services)
    {
        _services = services;
        _settingsService ??= services.GetRequiredService<SettingsService>();
    }

    // Boolean checkbox in the App Settings card: show/hide the tool table in the
    // Offsets editor (hidden by default on controllers with no G10 L1 tool table).
    public IEnumerable<ModuleSettingBool> GetBoolSettingFields()
    {
        if (_settingsService is null || _services is null)
            return [];
        var vm = _services.GetRequiredService<OffsetsViewModel>();
        return [new ModuleSettingBool("Show tool table", () => vm.ShowToolTable, v => vm.ShowToolTable = v,
            key: "module.offsets-table.showToolTable")];
    }

    public object CreateView(object viewModel) =>
        new OffsetsView { DataContext = viewModel };
}
