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

using CNC.Core;
using CNC.Core.Abstractions;
using CNC.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using SenderNG.Core.Modules;

namespace SenderNG.Modules.Plugins;

/// <summary>
/// Settings-menu module hosting the stepper calibration wizard. The distance field
/// lives in App Settings (profile scope); the wizard opens as a dialog from the menu.
/// </summary>
public class StepperCalibrationModule : IModule, IModuleSettingsProvider, ILocalizedModule
{
    private SettingsService? _settingsService;

    public string Id => "stepper-calibration";
    public string Name => "Stepper calibration";
    public string Description => "Measure and correct each axis's steps/mm and backlash.";
    public int Order => 45;
    public PanelHint Panel => PanelHint.Menu;
    public bool IsVisibleByDefault => true;
    public bool CanHide => false;
    public (double Width, double Height)? DialogPreferredSize => (560.0, 460.0);
    public IReadOnlyList<HotKey> HotKeys { get; } = [];

    public IReadOnlyDictionary<string, string> LocalizationKeys { get; } =
        new Dictionary<string, string>
        {
            ["module.stepper-calibration.name"] = "Stepper calibration",
            ["module.stepper-calibration.distance"] = "Distance (mm)",
            ["module.stepper-calibration.axis"] = "Axis",
            ["module.stepper-calibration.direction"] = "Direction",
            ["module.stepper-calibration.positive"] = "Positive (+)",
            ["module.stepper-calibration.negative"] = "Negative (−)",
            ["module.stepper-calibration.measured"] = "Measured distance (mm)",
            ["module.stepper-calibration.backlash"] = "Backlash (mm)",
            ["module.stepper-calibration.currentSteps"] = "Current steps/mm",
            ["module.stepper-calibration.newSteps"] = "New steps/mm",
            ["module.stepper-calibration.next"] = "Next",
            ["module.stepper-calibration.back"] = "Back",
            ["module.stepper-calibration.save"] = "Save",
            ["module.stepper-calibration.stop"] = "Stop",
            ["module.stepper-calibration.newCalibration"] = "New calibration",
        };

    public void RegisterServices(IServiceCollection services) =>
        services.AddTransient(sp => new StepperCalibrationViewModel(
            sp.GetRequiredService<IGrblService>(),
            sp.GetRequiredService<IGrblSettingsProvider>(),
            sp.GetRequiredService<GrblInfoState>(),
            sp.GetRequiredService<SettingsService>(),
            sp.GetRequiredService<SenderNG.Core.Localization.LocalizationService>()));

    public IEnumerable<ModuleSettingField> GetSettingFields()
    {
        yield return new ModuleSettingField(
            "Distance (mm)",
            () => _settingsService is null
                ? 100d
                : StepperCalibrationSettingsStore.Load(_settingsService.Load()).Distance,
            value =>
            {
                if (_settingsService is not { } service)
                    return;
                var profile = service.Load();
                StepperCalibrationSettingsStore.Save(profile,
                    new StepperCalibrationSettings { Distance = value });
                service.Save(profile);
            },
            key: "module.stepper-calibration.distance");
    }

    public void AttachServices(IServiceProvider services) =>
        _settingsService = services.GetService<SettingsService>();

    public object CreateViewModel(IServiceProvider serviceProvider) =>
        serviceProvider.GetRequiredService<StepperCalibrationViewModel>();

    public object CreateView(object viewModel) =>
        new StepperCalibrationView { DataContext = viewModel };
}