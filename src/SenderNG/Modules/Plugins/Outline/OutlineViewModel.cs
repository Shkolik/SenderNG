
/*

Copyright (c) 2018-2022 Io Engineering (Terje Io)

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

using System.Globalization;
using System.Text;
using CNC.Core.Abstractions;
using CNC.Core.Services;
using SenderNG.Localization;
using SenderNG.Modules.Fixed;
using SenderNG.ViewModels;
using SenderNG.Core.Commands;

namespace SenderNG.Modules.Plugins;

/// <summary>
/// Mirrors ioSender's Outline widget: moves the tool around the X/Y rectangle
/// bounding the loaded program's limits at a configurable feed rate, so the
/// operator can verify the part fits and is clear of clamps.
/// </summary>
public class OutlineViewModel : ViewModelBase
{
    public const int DefaultFeedRate = 500;

    private readonly IGrblService _grbl;
    private readonly SettingsService _settings;
    private readonly ProgramViewModel _program;
    private int _feedRate;
    private string _status = string.Empty;

    public OutlineViewModel(IGrblService grbl, SettingsService settings, ProgramViewModel program)
    {
        _grbl = grbl ?? throw new ArgumentNullException(nameof(grbl));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _program = program ?? throw new ArgumentNullException(nameof(program));

        _feedRate = ReadFeedRate(_settings);
        GoCommand = new DelegateCommand(_ => _ = GoAsync(), _ => HasProgramLimits);

        // Enable/disable the Go button as programs load and unload.
        _program.ProgramRows.CollectionChanged += (_, _) =>
            (GoCommand as DelegateCommand)?.RaiseCanExecuteChanged();
        _program.LimitRows.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasProgramLimits));
            (GoCommand as DelegateCommand)?.RaiseCanExecuteChanged();
        };

        // The feed rate is a per-profile module setting: follow profile switches
        // so the cached value never lingers from the previously active machine.
        _settings.Changed += OnSettingsChanged;
    }

    private void OnSettingsChanged()
    {
        var value = ReadFeedRate(_settings);
        if (value != _feedRate)
        {
            _feedRate = value;
            OnPropertyChanged(nameof(FeedRate));
        }
    }

    public DelegateCommand GoCommand { get; }

    public bool HasProgramLimits => _program.LimitRows.Count > 0;

    public string Status
    {
        get => _status;
        private set => SetField(ref _status, value);
    }

    public int FeedRate
    {
        get => _feedRate;
        set
        {
            if (value <= 0 || !SetField(ref _feedRate, value))
                return;
            SaveFeedRate(value);
        }
    }

    private async Task GoAsync()
    {
        if (!HasProgramLimits)
            return;

        var x = _program.LimitRows.FirstOrDefault(r => r.Letter == "X");
        var y = _program.LimitRows.FirstOrDefault(r => r.Letter == "Y");
        if (x is null || y is null)
        {
            Status = LocText.Resolve("module.outline.noXyLimits", "Outline: program has no X/Y limits");
            return;
        }

        // Same perimeter as ioSender: MinX,MinY -> MinX,MaxY -> MaxX,MaxY ->
        // MaxX,MinY -> MinX,MinY, all at the configured feed rate.
        var gcode = new StringBuilder();
        gcode.AppendLine($"G90G21G1F{FeedRate}");
        gcode.AppendLine($"X{Fmt(x.Min)}Y{Fmt(y.Min)}");
        gcode.AppendLine($"Y{Fmt(y.Max)}");
        gcode.AppendLine($"X{Fmt(x.Max)}");
        gcode.AppendLine($"Y{Fmt(y.Min)}");
        gcode.AppendLine($"X{Fmt(x.Min)}");

        try
        {
            await _grbl.SendAsync(gcode.ToString());
            Status = LocText.Resolve("module.outline.sent", "Outline sent");
        }
        catch (Exception ex)
        {
            Status = LocText.Format("module.outline.failed", "Outline failed: {0}", ex.Message);
        }
    }

    private static string Fmt(double value) =>
        value.ToString("0.###", CultureInfo.InvariantCulture);

    private static int ReadFeedRate(SettingsService settings)
    {
        var value = DefaultFeedRate;
        var app = settings.Load();
        if (app.ModuleSettings.TryGetValue(OutlineModule.SettingsSection, out var raw) &&
            raw is Dictionary<string, object> section &&
            section.TryGetValue(OutlineModule.FeedRateKey, out var stored) &&
            stored is double d && d > 0)
        {
            value = (int)Math.Round(d);
        }
        return value;
    }

    private void SaveFeedRate(int value)
    {
        var app = _settings.Load();
        if (!app.ModuleSettings.TryGetValue(OutlineModule.SettingsSection, out var raw) ||
            raw is not Dictionary<string, object> section)
        {
            section = new Dictionary<string, object>();
            app.ModuleSettings[OutlineModule.SettingsSection] = section;
        }
        section[OutlineModule.FeedRateKey] = (double)value;
        _settings.Save(app);
    }
}
