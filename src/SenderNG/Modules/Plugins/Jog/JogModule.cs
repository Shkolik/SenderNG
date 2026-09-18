
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

public class JogModule : IModule, IModuleSettingsProvider, ILocalizedModule, IHotkeyActionProvider
{
    private SettingsService? _settingsService;
    private IServiceProvider? _services;

    public string Id => "jog";
    public string Name => "Jog";
    public string Description => "Manual jog with configurable step distances and speeds.";
    public int Order => 15;
    // Flyout: the widget is wide in foam/multiaxis layouts — drawer overlay suits it better.
    public PanelHint Panel => PanelHint.Flyout;
    public bool IsVisibleByDefault => true;
    public bool CanHide => false;
    public IReadOnlyList<HotKey> HotKeys { get; } =
    [
        new("Ctrl+NumPad0", "jog.step.0"),
        new("Ctrl+NumPad1", "jog.step.1"),
        new("Ctrl+NumPad2", "jog.step.2"),
        new("Ctrl+NumPad3", "jog.step.3"),
        new("Ctrl+NumPad4", "jog.feed.0"),
        new("Ctrl+NumPad5", "jog.feed.1"),
        new("Ctrl+NumPad6", "jog.feed.2"),
        new("NumPad6", "jog.step.inc"),
        new("NumPad4", "jog.step.dec"),
        new("NumPad8", "jog.feed.inc"),
        new("NumPad2", "jog.feed.dec"),
        new("Left", "jog.left"),
        new("Right", "jog.right"),
        new("Up", "jog.up"),
        new("Down", "jog.down"),
        new("PageUp", "jog.z.plus"),
        new("PageDown", "jog.z.minus"),
        new("Home", "jog.a.plus"),
        new("End", "jog.a.minus"),
        new("Alt+Left", "jog.together.left"),
        new("Alt+Right", "jog.together.right"),
        new("Alt+Up", "jog.together.up"),
        new("Alt+Down", "jog.together.down"),
    ];

    public IReadOnlyDictionary<string, string> LocalizationKeys { get; } =
        new Dictionary<string, string>
        {
            ["module.jog.distances"] = "Distances (mm)",
            ["module.jog.speeds"] = "Speeds (mm/min)",
            ["module.jog.step1"] = "Step 1",
            ["module.jog.step2"] = "Step 2",
            ["module.jog.step3"] = "Step 3",
            ["module.jog.speed1"] = "Speed 1",
            ["module.jog.speed2"] = "Speed 2",
            ["module.jog.speed3"] = "Speed 3",
            ["module.jog.diagonal"] = "Diagonal jogging",
            ["module.jog.distance"] = "Distance (mm)",
            ["module.jog.speed"] = "Speed (mm/min)",
            ["module.jog.feedHold"] = "Feed hold",
            ["module.jog.syncGantries"] = "Synchronize both gantries",
            ["module.jog.planeHidden"] = "Second carriage plane hidden — the controller reports fewer axes",
            ["module.jog.continuous"] = "Continuous",
        };

    public void RegisterServices(IServiceCollection services) =>
        services.AddSingleton<JogViewModel>();

    // No seeding needed: JogConfigStore supplies defaults for both modes.

    public object CreateViewModel(IServiceProvider serviceProvider)
    {
        _settingsService = serviceProvider.GetRequiredService<SettingsService>();
        return serviceProvider.GetRequiredService<JogViewModel>();
    }

    // Fields must work before the Jog view is ever opened (App Settings dialog).
    public void AttachServices(IServiceProvider services)
    {
        _services = services;
        _settingsService ??= services.GetRequiredService<SettingsService>();
    }

    // Module-provided settings UI: the same selector controls as the widget,
    // bound to the shared JogViewModel (singleton), so changes apply live.
    public object? CreateSettingsView() =>
        _services is null
            ? null
            : new JogSettingsView { DataContext = _services.GetRequiredService<JogViewModel>() };

    public object CreateView(object viewModel) =>
        new JogView { DataContext = viewModel };

    public IReadOnlyList<IHotkeyAction> CreateHotkeyActions(IServiceProvider services)
        => JogHotkeyProvider.Create(services);
}
