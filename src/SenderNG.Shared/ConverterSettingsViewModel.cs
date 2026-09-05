/*
 * ConverterSettingsViewModel.cs - converter module settings leaf (App Settings)
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

using CNC.Core;
using CNC.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using SenderNG.Core.Localization;

namespace SenderNG.Modules.Shared;

/// <summary>
/// App Settings leaf for one converter module: the job parameters plus the
/// "Ask for parameters on open" checkbox (re-enables the dialog after
/// "Do not show again"). Profile-scoped (ConverterSettingsStore section under
/// the module id), persisted on every change and re-synced when the profile
/// switches while App Settings is open.
/// </summary>
public class ConverterSettingsViewModel : ViewModelBase
{
    private readonly SettingsService _settings;
    private readonly MachineProfile _profile;
    private readonly string _moduleId;
    private readonly string _moduleName;
    private readonly LocalizationService? _loc;
    private ConverterSettings _current;

    public ConverterSettingsViewModel(IServiceProvider services, string moduleId, string moduleName, bool showCutMode = false)
    {
        _settings = services.GetRequiredService<SettingsService>();
        _profile = _settings.Load();
        _moduleId = moduleId;
        _moduleName = moduleName;
        _loc = services.GetService<LocalizationService>();
        _current = ConverterSettingsStore.Load(_profile, moduleId);
        ShowCutMode = showCutMode;
        _settings.Changed += OnSettingsChanged;
    }

    /// <summary>Heading shown above the field grid (module display name).</summary>
    public string ModuleName => _moduleName;

    /// <summary>Whether the cut/laser selector is shown (HPGL yes, Excellon no).</summary>
    public bool ShowCutMode { get; }

    // --- values (persisted on every change) ---

    public bool ShowDialog { get => _current.ShowDialog; set { _current.ShowDialog = value; Save(); } }
    public bool CutMode { get => _current.CutMode; set { _current.CutMode = value; Save(); } }
    public double ZHome { get => _current.ZHome; set { _current.ZHome = value; Save(); } }
    public double ZRapids { get => _current.ZRapids; set { _current.ZRapids = value; Save(); } }
    public double ZSafe { get => _current.ZSafe; set { _current.ZSafe = value; Save(); } }
    public double ZMin { get => _current.ZMin; set { _current.ZMin = value; Save(); } }
    public double RPM { get => _current.RPM; set { _current.RPM = value; Save(); } }
    public double ToolDiameter { get => _current.ToolDiameter; set { _current.ToolDiameter = value; Save(); } }
    public double FeedRate { get => _current.FeedRate; set { _current.FeedRate = value; Save(); } }
    public double PlungeRate { get => _current.PlungeRate; set { _current.PlungeRate = value; Save(); } }
    public double ScaleX { get => _current.ScaleX; set { _current.ScaleX = value; Save(); } }
    public double ScaleY { get => _current.ScaleY; set { _current.ScaleY = value; Save(); } }

    // --- localized labels (plugin views cannot use the app-only {Loc} markup) ---

    public string ShowDialogLabel => L("module.converters.showDialog", "Ask for parameters on open");
    public string CutModeLabel => L("module.converters.cutMode", "Spindle cut (uncheck for laser)");
    public string ZHomeLabel => L("module.converters.zHome", "Z home");
    public string ZRapidsLabel => L("module.converters.zRapids", "Z rapids");
    public string ZSafeLabel => L("module.converters.zSafe", "Z safe");
    public string ZMinLabel => L("module.converters.zMin", "Z min");
    public string ScaleXLabel => L("module.converters.scaleX", "Scale X");
    public string RpmLabel => L("module.converters.rpm", "RPM");
    public string ToolDiameterLabel => L("module.converters.toolDiameter", "Tool Ø");
    public string FeedRateLabel => L("module.converters.feedRate", "Feed");
    public string PlungeRateLabel => L("module.converters.plungeRate", "Plunge");
    public string ScaleYLabel => L("module.converters.scaleY", "Scale Y");

    private void OnSettingsChanged()
    {
        // Profile may have switched while App Settings is open — re-sync.
        _current = ConverterSettingsStore.Load(_settings.Load(), _moduleId);
        OnPropertyChanged(nameof(ShowDialog));
        OnPropertyChanged(nameof(CutMode));
        OnPropertyChanged(nameof(ZHome));
        OnPropertyChanged(nameof(ZRapids));
        OnPropertyChanged(nameof(ZSafe));
        OnPropertyChanged(nameof(ZMin));
        OnPropertyChanged(nameof(RPM));
        OnPropertyChanged(nameof(ToolDiameter));
        OnPropertyChanged(nameof(FeedRate));
        OnPropertyChanged(nameof(PlungeRate));
        OnPropertyChanged(nameof(ScaleX));
        OnPropertyChanged(nameof(ScaleY));
    }

    private void Save()
    {
        ConverterSettingsStore.Save(_profile, _moduleId, _current);
        _settings.Save(_profile);
    }

    private string L(string key, string fallback) => _loc?.Resolve(key, fallback) ?? fallback;
}