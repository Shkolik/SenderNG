/*
 * ProgramModule.cs - Program module
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

public class ProgramModule : IModule, IModuleSettingsProvider
{
    private SettingsService? _settingsService;
    private IServiceProvider? _services;

    public string Id => "program";
    public string Name => "Program";
    public string Description => "Load, view and run G-code programs with per-line status tracking.";
    public int Order => 20;
    public PanelHint Panel => PanelHint.Center;
    public bool IsVisibleByDefault => true;
    public bool CanHide => false;
    public IReadOnlyList<HotKey> HotKeys { get; } = [];

    public void RegisterServices(IServiceCollection services)
    {
        services.AddSingleton<ProgramViewModel>();
        // The plugin-facing program surface: external modules (the plotter)
        // resolve this interface — never the app-assembly view-model type.
        services.AddSingleton<ILoadedProgramSource>(sp => sp.GetRequiredService<ProgramViewModel>());
        // The write-capable transform surface (height-map / rotation apply in
        // the probing plugin reads and reloads the program through this).
        services.AddSingleton<IProgramTransformTarget>(sp => sp.GetRequiredService<ProgramViewModel>());
    }

    // Program settings are machine-coupled (profile-scoped): one machine's
    // Compress preferences are not another's.
    public SettingsScope Scope => SettingsScope.Profile;

    public void AttachServices(IServiceProvider services)
    {
        _services = services;
        _settingsService ??= services.GetRequiredService<SettingsService>();
    }

    // Module-provided settings UI: the compress comment-mode selector, bound to
    // a view model that persists through ProgramConfigStore on selection.
    public object? CreateSettingsView() =>
        _services is null || _settingsService is null
            ? null
            : new ProgramSettingsView
            {
                DataContext = new ProgramSettingsViewModel(
                    _settingsService,
                    _services.GetService<SenderNG.Core.Localization.LocalizationService>()),
            };

    public object CreateViewModel(IServiceProvider serviceProvider) =>
        serviceProvider.GetRequiredService<ProgramViewModel>();

    public object CreateView(object viewModel) =>
        new ProgramView { DataContext = viewModel };
}
