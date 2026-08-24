using System.Collections.ObjectModel;
using System.Globalization;
using CNC.Core.Abstractions;
using CNC.Core.Services;
using SenderNG.Core.Commands;
using SenderNG.Avalonia.ViewModels;

namespace SenderNG.Avalonia.Modules.Plugins;

public sealed class LinearAxisRow
{
    public LinearAxisRow(string letter) => Letter = letter;
    public string Letter { get; }
    public string PlusTag => Letter + "+";
    public string MinusTag => Letter + "-";
}

public class JogViewModel : ViewModelBase
{
    private static readonly string[] Letters = "XYZABCUVW".Select(c => c.ToString()).ToArray();

    private readonly IGrblService _grbl;
    private readonly SettingsService _settingsService;
    private readonly AppSettings _appSettings;
    private int _numAxes = 2;
    private bool _suppressReload;

    public JogViewModel(IGrblService grbl, SettingsService settingsService)
    {
        _grbl = grbl ?? throw new ArgumentNullException(nameof(grbl));
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _appSettings = _settingsService.Load();

        Config = JogConfigStore.Load(_appSettings);
        RefreshLinearAxes();

        JogCommand = new DelegateCommand(p => _ = JogAsync(p as string));
        StopCommand = new DelegateCommand(_ => _ = _grbl.SendAsync("!"));

        _grbl.State.Subscribe(ApplyState);
        _settingsService.Changed += OnSettingsChanged;
    }

    public JogConfig Config { get; private set; }

    public ObservableCollection<LinearAxisRow> LinearAxisLetters { get; } = new();

    public bool IsFoam => _appSettings.Mode == AppSettings.FoamMode;
    public bool IsMill => !IsFoam;

    public bool ShowDiagonals => Config.Diagonals;

    // Session-only padlock: while engaged the XY cross jogs both gantries mirrored.
    private bool _together;
    public bool Together
    {
        get => _together;
        set
        {
            if (SetField(ref _together, value))
                OnPropertyChanged(nameof(ZaCrossEnabled));
        }
    }

    public bool ZaCrossEnabled => !Together;

    public DelegateCommand JogCommand { get; }
    public DelegateCommand StopCommand { get; }

    public double SelectedDistance =>
        Config.Distances[Math.Clamp(Config.DistanceIndex, 0, Config.Distances.Length - 1)];

    public double SelectedSpeed =>
        Config.Speeds[Math.Clamp(Config.SpeedIndex, 0, Config.Speeds.Length - 1)];

    public int SelectedDistanceIndex
    {
        get => Config.DistanceIndex;
        set
        {
            var v = Math.Clamp(value, 0, JogConfigStore.StepCount - 1);
            if (Config.DistanceIndex == v)
                return;
            Config.DistanceIndex = v;
            OnPropertyChanged();
            RaiseDistanceFlags();
            OnPropertyChanged(nameof(SelectedDistance));
            Save();
        }
    }

    public int SelectedSpeedIndex
    {
        get => Config.SpeedIndex;
        set
        {
            var v = Math.Clamp(value, 0, JogConfigStore.StepCount - 1);
            if (Config.SpeedIndex == v)
                return;
            Config.SpeedIndex = v;
            OnPropertyChanged();
            RaiseSpeedFlags();
            OnPropertyChanged(nameof(SelectedSpeed));
            Save();
        }
    }

    private bool GetDistanceFlag(int i) => Config.DistanceIndex == i;
    private void SetDistanceFlag(int i, bool value)
    {
        if (value)
            SelectedDistanceIndex = i;
        else
            OnPropertyChanged($"IsDistance{i}");
    }

    private bool GetSpeedFlag(int i) => Config.SpeedIndex == i;
    private void SetSpeedFlag(int i, bool value)
    {
        if (value)
            SelectedSpeedIndex = i;
        else
            OnPropertyChanged($"IsSpeed{i}");
    }

    public bool IsDistance0 { get => GetDistanceFlag(0); set => SetDistanceFlag(0, value); }
    public bool IsDistance1 { get => GetDistanceFlag(1); set => SetDistanceFlag(1, value); }
    public bool IsDistance2 { get => GetDistanceFlag(2); set => SetDistanceFlag(2, value); }
    public bool IsSpeed0 { get => GetSpeedFlag(0); set => SetSpeedFlag(0, value); }
    public bool IsSpeed1 { get => GetSpeedFlag(1); set => SetSpeedFlag(1, value); }
    public bool IsSpeed2 { get => GetSpeedFlag(2); set => SetSpeedFlag(2, value); }

    private void RaiseDistanceFlags()
    {
        for (var i = 0; i < JogConfigStore.StepCount; i++)
            OnPropertyChanged($"IsDistance{i}");
    }

    private void RaiseSpeedFlags()
    {
        for (var i = 0; i < JogConfigStore.StepCount; i++)
            OnPropertyChanged($"IsSpeed{i}");
    }

    private async Task JogAsync(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
            return;

        // Tag parts look like "X-,Y+" / "Z-,A+"; each word carries an axis and a
        // sign which we scale by the selected distance.
        var parts = tag.Split('|');
        IEnumerable<string> SelectWords(string part) => part
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(w => w.Trim())
            .Select(ToWord);

        string words;
        if (IsFoam && Together && parts.Length == 2)
        {
            // Mirrored gantries: send both pairs in one jog command.
            words = string.Join(" ", parts.SelectMany(SelectWords));
        }
        else
        {
            words = string.Join(" ", SelectWords(parts[0]));
        }

        await _grbl.SendAsync($"$J=G91 G0 {words} F{Fmt(SelectedSpeed)}");
    }

    private string ToWord(string token)
    {
        if (token.Length >= 2 && token[^1] is '+' or '-')
        {
            var sign = token[^1] == '+' ? 1 : -1;
            return $"{token[..^1]}{Fmt(sign * SelectedDistance)}";
        }
        return token;
    }

    public void Save()
    {
        _suppressReload = true;
        try
        {
            JogConfigStore.Save(_appSettings, Config);
            _settingsService.Save(_appSettings);
        }
        finally
        {
            _suppressReload = false;
        }
    }

    private void OnSettingsChanged()
    {
        if (_suppressReload)
            return;

        Config = JogConfigStore.Load(_appSettings);
        OnPropertyChanged(nameof(Config));
        OnPropertyChanged(nameof(IsFoam));
        OnPropertyChanged(nameof(IsMill));
        OnPropertyChanged(nameof(ShowDiagonals));
        OnPropertyChanged(nameof(SelectedDistanceIndex));
        OnPropertyChanged(nameof(SelectedSpeedIndex));
        OnPropertyChanged(nameof(SelectedDistance));
        OnPropertyChanged(nameof(SelectedSpeed));
        RaiseDistanceFlags();
        RaiseSpeedFlags();
        RefreshLinearAxes();
    }

    private void ApplyState(MachineState state)
    {
        if (state is null)
            return;

        var n = Math.Clamp(state.NumAxes, 2, Letters.Length);
        if (n == _numAxes)
            return;

        _numAxes = n;
        RefreshLinearAxes();
    }

    // Crossed axis count: Mill crosses the first 2 (X,Y), Foam the first 4 (X,Y,Z,A).
    // The rest render as linear +/- rows.
    private int CrossedAxes => IsFoam ? 4 : 2;

    private void RefreshLinearAxes()
    {
        var count = Math.Max(0, _numAxes - CrossedAxes);
        while (LinearAxisLetters.Count > count)
            LinearAxisLetters.RemoveAt(LinearAxisLetters.Count - 1);
        while (LinearAxisLetters.Count < count)
            LinearAxisLetters.Add(new LinearAxisRow(Letters[CrossedAxes + LinearAxisLetters.Count]));
    }

    private static string Fmt(double value) =>
        value.ToString("0.###", CultureInfo.InvariantCulture);
}
