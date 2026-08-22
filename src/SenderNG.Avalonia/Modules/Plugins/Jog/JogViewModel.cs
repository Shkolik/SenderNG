using System.Collections.ObjectModel;
using System.Globalization;
using CNC.Core.Abstractions;
using CNC.Core.Services;
using SenderNG.Core.Commands;
using SenderNG.Avalonia.ViewModels;

namespace SenderNG.Avalonia.Modules.Plugins;

public class JogViewModel : ViewModelBase
{
    private readonly IGrblService _grbl;
    private readonly SettingsService _settingsService;
    private readonly AppSettings _appSettings;

    public ObservableCollection<double> StepDistances { get; } = new([0.1, 1, 10]);

    private int _selectedStepIndex = 1;
    public int SelectedStepIndex
    {
        get => _selectedStepIndex;
        set => SetField(ref _selectedStepIndex, Math.Clamp(value, 0, StepDistances.Count - 1));
    }

    public double SelectedStep =>
        StepDistances.Count == 0 ? 0 : StepDistances[SelectedStepIndex];

    private double _xySpeed = 1000;
    public double XySpeed
    {
        get => _xySpeed;
        set => SetField(ref _xySpeed, value);
    }

    private double _zSpeed = 500;
    public double ZSpeed
    {
        get => _zSpeed;
        set => SetField(ref _zSpeed, value);
    }

    private bool _padlockEngaged;
    public bool PadlockEngaged
    {
        get => _padlockEngaged;
        set => SetField(ref _padlockEngaged, value);
    }

    public string Mode => _appSettings.Mode;

    public DelegateCommand JogXPlusCommand { get; }
    public DelegateCommand JogXMinusCommand { get; }
    public DelegateCommand JogYPlusCommand { get; }
    public DelegateCommand JogYMinusCommand { get; }
    public DelegateCommand JogZPlusCommand { get; }
    public DelegateCommand JogZMinusCommand { get; }
    public DelegateCommand JogAPlusCommand { get; }
    public DelegateCommand JogAMinusCommand { get; }
    public DelegateCommand JogBPlusCommand { get; }
    public DelegateCommand JogBMinusCommand { get; }
    public DelegateCommand JogXYSwNECommand { get; }
    public DelegateCommand JogXYNEswCommand { get; }
    public DelegateCommand JogXYNwSeCommand { get; }
    public DelegateCommand JogXYSeNwCommand { get; }

    public JogViewModel(IGrblService grbl, SettingsService settingsService)
    {
        _grbl = grbl ?? throw new ArgumentNullException(nameof(grbl));
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _appSettings = _settingsService.Load();

        Load();

        JogXPlusCommand = new DelegateCommand(_ => _ = JogAsync("X", +1));
        JogXMinusCommand = new DelegateCommand(_ => _ = JogAsync("X", -1));
        JogYPlusCommand = new DelegateCommand(_ => _ = JogAsync("Y", +1));
        JogYMinusCommand = new DelegateCommand(_ => _ = JogAsync("Y", -1));
        JogZPlusCommand = new DelegateCommand(_ => _ = JogAsync("Z", +1));
        JogZMinusCommand = new DelegateCommand(_ => _ = JogAsync("Z", -1));
        JogAPlusCommand = new DelegateCommand(_ => _ = JogAsync("A", +1));
        JogAMinusCommand = new DelegateCommand(_ => _ = JogAsync("A", -1));
        JogBPlusCommand = new DelegateCommand(_ => _ = JogAsync("B", +1));
        JogBMinusCommand = new DelegateCommand(_ => _ = JogAsync("B", -1));

        JogXYSwNECommand = new DelegateCommand(_ => _ = JogDiagonalAsync(+1, +1));
        JogXYNEswCommand = new DelegateCommand(_ => _ = JogDiagonalAsync(-1, -1));
        JogXYNwSeCommand = new DelegateCommand(_ => _ = JogDiagonalAsync(-1, +1));
        JogXYSeNwCommand = new DelegateCommand(_ => _ = JogDiagonalAsync(+1, -1));
    }

    public async Task JogAsync(string axis, int sign)
    {
        var step = sign * SelectedStep;
        var speed = axis is "X" or "Y" ? XySpeed : ZSpeed;

        // Foam mode padlock: Z is slaved to A while engaged.
        if (axis == "Z" && Mode == "Foam" && PadlockEngaged)
        {
            await SendJog($"Z{Fmt(step)} A{Fmt(step)}", speed);
            return;
        }

        await SendJog($"{axis}{Fmt(step)}", speed);
    }

    public async Task JogDiagonalAsync(int xSign, int ySign)
    {
        await SendJog($"X{Fmt(xSign * SelectedStep)} Y{Fmt(ySign * SelectedStep)}", XySpeed);
    }

    public void Save()
    {
        _appSettings.Modules["Jog"] = new Dictionary<string, object>
        {
            ["XySpeed"] = XySpeed,
            ["ZSpeed"] = ZSpeed,
            ["Steps"] = StepDistances.ToList(),
            ["SelectedStepIndex"] = SelectedStepIndex,
        };
        _settingsService.Save(_appSettings);
    }

    private void Load()
    {
        if (!_appSettings.Modules.TryGetValue("Jog", out var raw))
            return;

        Dictionary<string, object?>? section = raw switch
        {
            Dictionary<string, object> typed => typed,
            System.Text.Json.JsonElement { ValueKind: System.Text.Json.JsonValueKind.Object } el => ReadObject(el),
            _ => null,
        };

        if (section is null)
            return;

        if (section.TryGetValue("XySpeed", out var xy) && ToDouble(xy) is { } xySpeed)
            XySpeed = xySpeed;
        if (section.TryGetValue("ZSpeed", out var z) && ToDouble(z) is { } zSpeed)
            ZSpeed = zSpeed;
        if (section.TryGetValue("SelectedStepIndex", out var idx) && ToDouble(idx) is { } index)
            SelectedStepIndex = (int)index;
        if (section.TryGetValue("Steps", out var steps))
        {
            var list = steps switch
            {
                IEnumerable<double> doubles => doubles.ToList(),
                System.Text.Json.JsonElement { ValueKind: System.Text.Json.JsonValueKind.Array } arr =>
                    arr.EnumerateArray().Select(e => e.TryGetDouble(out var d) ? d : 0).ToList(),
                _ => null,
            };
            if (list is { Count: > 0 })
            {
                StepDistances.Clear();
                foreach (var step in list)
                    StepDistances.Add(step);
            }
        }
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

    private Task SendJog(string axes, double speed) =>
        _grbl.SendAsync($"$J=G91 G0 {axes} F{Fmt(speed)}");

    private static string Fmt(double value) =>
        value.ToString("0.###", CultureInfo.InvariantCulture);
}
