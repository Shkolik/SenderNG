/*
 * ConverterParametersViewModel.cs - shared converter job parameters dialog
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

using System.Windows.Input;
using CNC.Core;
using CNC.Core.Abstractions;
using SenderNG.Core.Commands;
using SenderNG.Core.Localization;

namespace SenderNG.Modules.Shared;

/// <summary>
/// Converter parameters dialog (the ioSender JobParameters shape): Z heights,
/// spindle/tool, feeds and XY scale, plus a "Do not show again" checkbox that
/// flips the module's <see cref="ConverterSettings.ShowDialog"/> flag — accept
/// once, convert silently afterwards, re-enable from App Settings.
/// Plugin views can't use the app-only <c>{Loc}</c> markup extension, so all
/// labels are resolved here through the hosting module's OWN locale catalog
/// (module.converters.* keys) — resolved once, which is safe: the dialog is
/// modal, so the locale cannot change while it is open.
/// </summary>
public class ConverterParametersViewModel : ViewModelBase, IClosableDialog
{
    private readonly LocalizationService? _loc;
    private readonly bool _showCutMode;
    private double _zHome;
    private double _zRapids;
    private double _zSafe;
    private double _zMin;
    private double _rpm;
    private double _toolDiameter;
    private double _feedRate;
    private double _plungeRate;
    private double _scaleX;
    private double _scaleY;
    private bool _cutMode;
    private bool _doNotShowAgain;

    public ConverterParametersViewModel(ConverterSettings stored, bool showCutMode = false, LocalizationService? loc = null)
    {
        _loc = loc;
        _showCutMode = showCutMode;
        _zHome = stored.ZHome;
        _zRapids = stored.ZRapids;
        _zSafe = stored.ZSafe;
        _zMin = stored.ZMin;
        _rpm = stored.RPM;
        _toolDiameter = stored.ToolDiameter;
        _feedRate = stored.FeedRate;
        _plungeRate = stored.PlungeRate;
        _scaleX = stored.ScaleX;
        _scaleY = stored.ScaleY;
        _cutMode = stored.CutMode;
        _doNotShowAgain = !stored.ShowDialog;

        OkCommand = new DelegateCommand(_ => RequestClose(true));
        CancelCommand = new DelegateCommand(_ => RequestClose(false));
    }

    public event EventHandler<bool>? CloseRequested;

    public double ZHome { get => _zHome; set => SetField(ref _zHome, value); }
    public double ZRapids { get => _zRapids; set => SetField(ref _zRapids, value); }
    public double ZSafe { get => _zSafe; set => SetField(ref _zSafe, value); }
    public double ZMin { get => _zMin; set => SetField(ref _zMin, value); }
    public double RPM { get => _rpm; set => SetField(ref _rpm, value); }
    public double ToolDiameter { get => _toolDiameter; set => SetField(ref _toolDiameter, value); }
    public double FeedRate { get => _feedRate; set => SetField(ref _feedRate, value); }
    public double PlungeRate { get => _plungeRate; set => SetField(ref _plungeRate, value); }
    public double ScaleX { get => _scaleX; set => SetField(ref _scaleX, value); }
    public double ScaleY { get => _scaleY; set => SetField(ref _scaleY, value); }

    /// <summary>Whether the cut/laser selector is shown (HPGL yes, Excellon no).</summary>
    public bool ShowCutMode => _showCutMode;

    /// <summary>Spindle cut vs laser preamble (HPGL only).</summary>
    public bool CutMode { get => _cutMode; set => SetField(ref _cutMode, value); }

    /// <summary>"Do not show again": persist <see cref="ConverterSettings.ShowDialog"/>
    /// = false on OK, so subsequent opens convert silently.</summary>
    public bool DoNotShowAgain { get => _doNotShowAgain; set => SetField(ref _doNotShowAgain, value); }

    // Localized labels (plugin views cannot use the app-only {Loc} markup).
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
    public string CutModeLabel => L("module.converters.cutMode", "Spindle cut (uncheck for laser)");
    public string DoNotShowAgainLabel => L("module.converters.doNotShowAgain", "Do not show again");
    public string OkLabel => L("app.dialog.ok", "OK");
    public string CancelLabel => L("app.dialog.cancel", "Cancel");

    /// <summary>Builds the settings to persist (accepted parameters, ShowDialog
    /// per the checkbox, HasValues = true).</summary>
    public ConverterSettings ToSettings() => new()
    {
        ShowDialog = !DoNotShowAgain,
        ZHome = ZHome,
        ZRapids = ZRapids,
        ZSafe = ZSafe,
        ZMin = ZMin,
        RPM = RPM,
        ToolDiameter = ToolDiameter,
        FeedRate = FeedRate,
        PlungeRate = PlungeRate,
        ScaleX = ScaleX,
        ScaleY = ScaleY,
        CutMode = CutMode,
        HasValues = true,
    };

    public ICommand OkCommand { get; }
    public ICommand CancelCommand { get; }

    private void RequestClose(bool result) => CloseRequested?.Invoke(this, result);

    private string L(string key, string fallback) => _loc?.Resolve(key, fallback) ?? fallback;
}