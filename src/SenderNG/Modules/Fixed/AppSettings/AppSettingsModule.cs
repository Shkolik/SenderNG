/*
 * AppSettingsModule.cs - App settings module
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
using CNC.Core.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using SenderNG.Core.Modules;

namespace SenderNG.Modules.Fixed;

public class AppSettingsModule : IModule
{
    public string Id => "app-settings";
    public string Name => "App Settings";
    public int Order => 41;
    public PanelHint Panel => PanelHint.Menu;
    public bool IsVisibleByDefault => true;

    // A settings dialog, not a module card: never exposed as a leaf, never hideable.
    public bool CanHide => false;
    public IReadOnlyList<HotKey> HotKeys { get; } = [];

    public void RegisterServices(IServiceCollection services) =>
        // Transient: each dialog open builds fresh cards and fresh module settings
        // views (a view instance cannot be attached to a new visual parent twice).
        // The dialog service is optional: the dialog must still build without it
        // (headless tests do not register one) — its backup commands just stay disabled.
        services.AddTransient(sp => new AppSettingsViewModel(
            sp.GetRequiredService<SettingsService>(),
            sp.GetServices<IModule>(),
            sp.GetRequiredService<SenderNG.Core.Localization.LocalizationService>(),
            hotkeys: sp.GetService<SenderNG.Core.Services.HotkeyService>(),
            fileDialogs: sp.GetService<IFileDialogService>()));


    public object CreateViewModel(IServiceProvider serviceProvider) =>
        serviceProvider.GetRequiredService<AppSettingsViewModel>();

    public object CreateView(object viewModel) =>
        new AppSettingsView { DataContext = viewModel };

    // Content-heavy wrap grid: fixed resizable window instead of size-to-content.
    public (double Width, double Height)? DialogPreferredSize => (900.0, 560.0);
}
