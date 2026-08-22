using System.Collections.ObjectModel;
using System.Text.Json;
using CNC.Core.Services;
using SenderNG.Core.Commands;
using SenderNG.Avalonia.ViewModels;

namespace SenderNG.Avalonia.Modules.Fixed;

public sealed class OffsetRow : ViewModelBase
{
    private double _x;
    private double _y;
    private double _z;

    public OffsetRow(string name) => Name = name;

    public string Name { get; }

    public double X
    {
        get => _x;
        set => SetField(ref _x, value);
    }

    public double Y
    {
        get => _y;
        set => SetField(ref _y, value);
    }

    public double Z
    {
        get => _z;
        set => SetField(ref _z, value);
    }
}

public class OffsetsViewModel : ViewModelBase
{
    private static readonly string[] WorkOffsets = ["G54", "G55", "G56", "G57", "G58", "G59"];

    private readonly SettingsService _settingsService;
    private readonly AppSettings _appSettings;

    public ObservableCollection<OffsetRow> Rows { get; } = new();

    public DelegateCommand SaveCommand { get; }

    public OffsetsViewModel(SettingsService settingsService)
    {
        ArgumentNullException.ThrowIfNull(settingsService);
        _settingsService = settingsService;
        _appSettings = settingsService.Load();

        foreach (var name in WorkOffsets)
            Rows.Add(new OffsetRow(name));

        if (_appSettings.Modules.TryGetValue("Offsets", out var raw) && Extract(raw) is { } saved)
            Apply(saved);

        SaveCommand = new DelegateCommand(_ => Save());
    }

    private void Apply(Dictionary<string, double[]> offsets)
    {
        foreach (var row in Rows)
        {
            if (!offsets.TryGetValue(row.Name, out var v))
                continue;

            row.X = v.Length > 0 ? v[0] : 0;
            row.Y = v.Length > 1 ? v[1] : 0;
            row.Z = v.Length > 2 ? v[2] : 0;
        }
    }

    private void Save()
    {
        _appSettings.Modules["Offsets"] = Rows.ToDictionary(
            r => r.Name,
            r => new[] { r.X, r.Y, r.Z });
        _settingsService.Save(_appSettings);
    }

    private static Dictionary<string, double[]>? Extract(object? raw)
    {
        switch (raw)
        {
            case Dictionary<string, double[]> typed:
                return typed;
            case JsonElement { ValueKind: JsonValueKind.Object } el:
                var result = new Dictionary<string, double[]>();
                foreach (var prop in el.EnumerateObject())
                {
                    if (prop.Value.ValueKind != JsonValueKind.Array)
                        continue;
                    var values = new List<double>();
                    foreach (var item in prop.Value.EnumerateArray())
                    {
                        if (item.TryGetDouble(out var d))
                            values.Add(d);
                    }
                    result[prop.Name] = values.ToArray();
                }
                return result;
            default:
                return null;
        }
    }
}
