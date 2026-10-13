/*
 * HeightMapViewerModule.cs - plugin module for the 3D height-map viewer
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
using Microsoft.Extensions.DependencyInjection;
using SenderNG.Core.Localization;
using SenderNG.Core.Modules;

namespace SenderNG.Modules.HeightMapViewer;

public sealed class HeightMapViewerModule : IModule, IHeightMap3DRenderer,
    ILocalizedModule, IPluginInfo, IPluginDependencies, IModuleSettingsProvider
{
    private IServiceProvider? _services;
    private bool _dialogViewRegistered;

    public string Id => "heightmap-viewer";
    public string Name => "Height map 3D";
    public string Description => "3D view of the probed height map.";
    public int Order => 51;
    public PanelHint Panel => PanelHint.Dialog;
    public bool IsVisibleByDefault => false;
    public bool IsEnabledByDefault => true;
    public bool CanHide => true;
    public IReadOnlyList<HotKey> HotKeys => [];

    public string Version => "0.1.0";
    public string Author => "Andrew Shkolik";
    public string License => "BSD-3-Clause";
    public IReadOnlyList<PluginDependency> Dependencies =>
        [new("Silk.NET.OpenGL", "2.23.0", "MIT")];

    public IReadOnlyDictionary<string, string> LocalizationKeys { get; } =
        new Dictionary<string, string>
        {
            ["module.heightmap-viewer.name"] = "Height map 3D",
            ["module.heightmap-viewer.description"] = "3D view of the probed height map.",
            ["module.heightmap-viewer.empty"] = "No height map loaded. Probe or load a map first.",
            ["module.heightmap-viewer.title"] = "Height map 3D",
            ["module.heightmap-viewer.reset"] = "Reset view",
            ["module.heightmap-viewer.renderUnavailable"] = "3D view unavailable",
            ["module.heightmap-viewer.renderUnavailableHint"] =
                "No OpenGL context could be created.",
        };

    public void RegisterServices(IServiceCollection services)
        => services.AddSingleton<IHeightMap3DRenderer>(this);

    public object CreateViewModel(IServiceProvider sp)
        => new HeightMapViewerViewModel(
            sp.GetService<IHeightMapSource>(),
            sp.GetService<LocalizationService>());

    public object CreateView(object vm) => new HeightMapViewerView { DataContext = vm };

    public (double, double)? DialogPreferredSize => (900, 640);

    public SettingsScope Scope => SettingsScope.Profile;
    public object? CreateSettingsView() => null;

    /// <summary>
    /// Host hook, run on the UI thread before any view is realized. Captures the
    /// dispatcher for <see cref="UiThread"/> and idempotently registers the
    /// dialog's VM→view pair with the app's <see cref="IViewFactory"/> — without
    /// it <c>IHeightMap3DRenderer.ShowAsync</c> resolves the dialog view through
    /// the factory, which throws <see cref="NotSupportedException"/>.
    /// </summary>
    public void AttachServices(IServiceProvider services)
    {
        _services = services;
        UiThread.Initialize();
        if (_dialogViewRegistered)
            return;
        services.GetRequiredService<IViewFactory>()
            .Register<HeightMapViewerViewModel, HeightMapViewerView>();
        _dialogViewRegistered = true;
    }

    public async Task ShowAsync()
    {
        var dialogs = _services?.GetService<IDialogService>();
        if (dialogs is null)
            return;
        var source = _services?.GetService<IHeightMapSource>();
        var vm = new HeightMapViewerViewModel(source, _services?.GetService<LocalizationService>());
        try
        {
            await dialogs.ShowDialogAsync(
                _services?.GetService<LocalizationService>()?.Resolve("module.heightmap-viewer.title", "Height map 3D")
                    ?? "Height map 3D",
                vm, (900, 640));
        }
        finally
        {
            // The dialog owns a fresh VM per open; dispose it with the dialog so
            // its Snapshot subscription does not outlive the window.
            vm.Dispose();
        }
    }
}
