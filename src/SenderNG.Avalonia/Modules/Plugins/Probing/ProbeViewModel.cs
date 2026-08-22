using System.Globalization;
using CNC.Core.Abstractions;
using CNC.Core.Services;
using SenderNG.Core.Commands;
using SenderNG.Avalonia.ViewModels;

namespace SenderNG.Avalonia.Modules.Plugins;

public class ProbeViewModel : ViewModelBase
{
    private readonly IGrblService _grbl;
    private readonly SettingsService _settingsService;
    private readonly AppSettings _appSettings;

    private double _probeDepth = 10;
    public double ProbeDepth
    {
        get => _probeDepth;
        set => SetField(ref _probeDepth, value);
    }

    private double _probeFeed = 100;
    public double ProbeFeed
    {
        get => _probeFeed;
        set => SetField(ref _probeFeed, value);
    }

    private string _lastProbeResult = string.Empty;
    public string LastProbeResult
    {
        get => _lastProbeResult;
        private set => SetField(ref _lastProbeResult, value);
    }

    public DelegateCommand ProbeZCommand { get; }
    public DelegateCommand ProbeXPlusCommand { get; }
    public DelegateCommand ProbeXMinusCommand { get; }
    public DelegateCommand ProbeYPlusCommand { get; }
    public DelegateCommand ProbeYMinusCommand { get; }

    public ProbeViewModel(IGrblService grbl, SettingsService settings)
    {
        _grbl = grbl ?? throw new ArgumentNullException(nameof(grbl));
        _settingsService = settings ?? throw new ArgumentNullException(nameof(settings));
        _appSettings = _settingsService.Load();

        Load();

        ProbeZCommand = new DelegateCommand(_ => _ = ProbeAsync("Z", -1));
        ProbeXPlusCommand = new DelegateCommand(_ => _ = ProbeAsync("X", +1));
        ProbeXMinusCommand = new DelegateCommand(_ => _ = ProbeAsync("X", -1));
        ProbeYPlusCommand = new DelegateCommand(_ => _ = ProbeAsync("Y", +1));
        ProbeYMinusCommand = new DelegateCommand(_ => _ = ProbeAsync("Y", -1));
    }

    public async Task ProbeAsync(string axis, int sign)
    {
        var distance = sign * ProbeDepth;
        var line = $"G38.2 {axis}{Fmt(distance)} F{Fmt(ProbeFeed)}";
        LastProbeResult = line;
        await _grbl.SendAsync(line);
    }

    public void Save()
    {
        _appSettings.Modules["Probing"] = new Dictionary<string, object>
        {
            ["ProbeDepth"] = ProbeDepth,
            ["ProbeFeed"] = ProbeFeed,
        };
        _settingsService.Save(_appSettings);
    }

    private void Load()
    {
        if (!_appSettings.Modules.TryGetValue("Probing", out var raw))
            return;

        Dictionary<string, object?>? section = raw switch
        {
            Dictionary<string, object> typed => new Dictionary<string, object?>(typed),
            System.Text.Json.JsonElement { ValueKind: System.Text.Json.JsonValueKind.Object } el => ReadObject(el),
            _ => null,
        };

        if (section is null)
            return;

        if (section.TryGetValue("ProbeDepth", out var depth) && ToDouble(depth) is { } d)
            ProbeDepth = d;
        if (section.TryGetValue("ProbeFeed", out var feed) && ToDouble(feed) is { } f)
            ProbeFeed = f;
    }

    private static Dictionary<string, object?> ReadObject(System.Text.Json.JsonElement el)
    {
        var result = new Dictionary<string, object?>();
        foreach (var prop in el.EnumerateObject())
            result[prop.Name] = prop.Value;
        return result;
    }

    private static double? ToDouble(object? value) => value switch
    {
        double d => d,
        int i => i,
        long l => l,
        System.Text.Json.JsonElement { ValueKind: System.Text.Json.JsonValueKind.Number } e when e.TryGetDouble(out var d) => d,
        _ => null,
    };

    private static string Fmt(double value) =>
        value.ToString("0.###", CultureInfo.InvariantCulture);
}
