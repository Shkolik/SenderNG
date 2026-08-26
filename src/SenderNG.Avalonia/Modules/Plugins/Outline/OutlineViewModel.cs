using System.Globalization;
using System.Text;
using CNC.Core.Abstractions;
using CNC.Core.Services;
using SenderNG.Avalonia.Modules.Fixed;
using SenderNG.Avalonia.ViewModels;
using SenderNG.Core.Commands;

namespace SenderNG.Avalonia.Modules.Plugins;

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
            Status = "Outline: program has no X/Y limits";
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
            Status = "Outline sent";
        }
        catch (Exception ex)
        {
            Status = $"Outline failed: {ex.Message}";
        }
    }

    private static string Fmt(double value) =>
        value.ToString("0.###", CultureInfo.InvariantCulture);

    private static int ReadFeedRate(SettingsService settings)
    {
        var value = DefaultFeedRate;
        var app = settings.Load();
        if (app.Modules.TryGetValue(OutlineModule.SettingsSection, out var raw) &&
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
        if (!app.Modules.TryGetValue(OutlineModule.SettingsSection, out var raw) ||
            raw is not Dictionary<string, object> section)
        {
            section = new Dictionary<string, object>();
            app.Modules[OutlineModule.SettingsSection] = section;
        }
        section[OutlineModule.FeedRateKey] = (double)value;
        _settings.Save(app);
    }
}
