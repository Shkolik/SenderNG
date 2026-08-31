using System.Collections.ObjectModel;
using System.Globalization;
using CNC.Core.Abstractions;
using CNC.Core.Services;
using SenderNG.Localization;
using SenderNG.ViewModels;
using SenderNG.Core.Commands;

namespace SenderNG.Modules.Plugins;

/// <summary>Bindable goto axis row: letter + editable machine coordinate.</summary>
public sealed class GotoAxisRow : ViewModelBase
{
    private string _value = "0";

    public GotoAxisRow(string letter, string? value = null)
    {
        Letter = letter;
        if (value is not null)
            _value = value;
    }

    public string Letter { get; }
    public string Value
    {
        get => _value;
        set => SetField(ref _value, value);
    }
}

/// <summary>
/// ioSender's Goto widget: G28/G30 "goto predefined position" buttons, G28.1/G30.1
/// "record current position as reference point", and a numeric machine-coordinate
/// goto (G53 G0 X.. Y.. Z..). All actions are gated on the machine being homed —
/// grbl itself refuses them while an alarm is active, and moving to a stored
/// reference before homing is meaningless.
/// The numeric goto shows as many axes as the user prefers (persisted setting,
/// 1..9) capped by the connected controller's reported axis count, so a 4-axis
/// preference shows 3 rows on a 3-axis controller and 4+ on a 5-axis one.
/// </summary>
public sealed class GotoViewModel : ViewModelBase
{
    private static readonly int[] AllAxisChoices =
        Enumerable.Range(1, CNC.Core.GrblInfo.AxisLetters.Length).ToArray();

    private readonly IGrblService _grbl;
    private readonly SettingsService _settingsService;
    private readonly AppSettings _appSettings;
    private readonly IDisposable _stateSubscription;
    private bool _isHomed;
    private int _availableAxes = 3;
    private string _status = string.Empty;
    private bool _saving;

    public GotoViewModel(IGrblService grbl, SettingsService settingsService)
    {
        _grbl = grbl ?? throw new ArgumentNullException(nameof(grbl));
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _appSettings = _settingsService.Load();

        Config = GotoConfigStore.Load(_appSettings);
        Config.PropertyChanged += OnConfigPropertyChanged;

        GotoHomeCommand = new DelegateCommand(_ => _ = SendAsync("G28", LocText.Resolve("module.goto.goingHome", "Going to home position")));
        GotoG30Command = new DelegateCommand(_ => _ = SendAsync("G30", LocText.Resolve("module.goto.goingRef2", "Going to second reference point")));
        SetHomeCommand = new DelegateCommand(_ => _ = SendAsync("G28.1", LocText.Resolve("module.goto.homeRecorded", "Home position recorded")));
        SetG30Command = new DelegateCommand(_ => _ = SendAsync("G30.1", LocText.Resolve("module.goto.ref2Recorded", "Second reference point recorded")));
        GoCommand = new DelegateCommand(_ => _ = GoAsync());

        _stateSubscription = grbl.State.Subscribe(s => UiThread.Post(() => ApplyState(s)));
        _settingsService.Changed += OnSettingsChanged;
        RefreshAxes();
    }

    public GotoConfig Config { get; private set; }

    public DelegateCommand GotoHomeCommand { get; }
    public DelegateCommand GotoG30Command { get; }
    public DelegateCommand SetHomeCommand { get; }
    public DelegateCommand SetG30Command { get; }
    public DelegateCommand GoCommand { get; }

    /// <summary>True once the controller reports a completed $H; cleared on any alarm or soft reset.</summary>
    public bool IsHomed
    {
        get => _isHomed;
        private set
        {
            if (SetField(ref _isHomed, value))
                Status = value ? string.Empty : LocText.Resolve("module.goto.homeFirst", "Home the machine first ($H)");
        }
    }

    public ObservableCollection<GotoAxisRow> GotoAxes { get; } = new();

    public IReadOnlyList<int> AxisChoices => AllAxisChoices;

    /// <summary>The user's preferred axis count (persisted), clamped to 1..9.</summary>
    public int SelectedAxisCount
    {
        get => Config.AxisCount;
        set => Config.AxisCount = Math.Clamp(value, 1, CNC.Core.GrblInfo.AxisLetters.Length);
    }

    /// <summary>Controller-reported axis count, clamped to the known axis set.</summary>
    public int AvailableAxisCount => _availableAxes;

    /// <summary>Rows to show: the preference capped by what the controller reports.</summary>
    public int AxisCount => Math.Min(SelectedAxisCount, AvailableAxisCount);

    public string Status
    {
        get => _status;
        private set => SetField(ref _status, value);
    }

    private void ApplyState(MachineState state)
    {
        if (state is null)
            return;
        IsHomed = state.IsHomed;
        var available = Math.Clamp(state.NumAxes, 1, CNC.Core.GrblInfo.AxisLetters.Length);
        if (available != _availableAxes)
        {
            _availableAxes = available;
            OnPropertyChanged(nameof(AvailableAxisCount));
            OnPropertyChanged(nameof(AxisCount));
            RefreshAxes();
        }
    }

    private void OnConfigPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (_saving || e.PropertyName != nameof(GotoConfig.AxisCount))
            return;

        Save();
        OnPropertyChanged(nameof(AxisCount));
        RefreshAxes();
    }

    private void OnSettingsChanged()
    {
        if (_saving)
            return;
        Config = GotoConfigStore.Load(_appSettings);
        Config.PropertyChanged += OnConfigPropertyChanged;
        OnPropertyChanged(nameof(Config));
        OnPropertyChanged(nameof(SelectedAxisCount));
        OnPropertyChanged(nameof(AxisCount));
        RefreshAxes();
    }

    public void Save()
    {
        _saving = true;
        try
        {
            GotoConfigStore.Save(_appSettings, Config);
            _settingsService.Save(_appSettings);
        }
        finally
        {
            _saving = false;
        }
    }

    /// <summary>Rebuilds the visible rows; existing row values are preserved by letter.</summary>
    private void RefreshAxes()
    {
        var count = Math.Clamp(AxisCount, 1, CNC.Core.GrblInfo.AxisLetters.Length);
        var existing = GotoAxes.ToDictionary(row => row.Letter, row => row.Value);
        GotoAxes.Clear();
        for (var i = 0; i < count; i++)
        {
            var letter = CNC.Core.GrblInfo.AxisLetters[i].ToString();
            GotoAxes.Add(new GotoAxisRow(letter, existing.TryGetValue(letter, out var value) ? value : "0"));
        }
    }

    private async Task GoAsync()
    {
        if (!IsHomed)
        {
            Status = LocText.Resolve("module.goto.homeFirst", "Home the machine first ($H)");
            return;
        }

        var words = new List<string>();
        var invalid = new List<string>();
        foreach (var row in GotoAxes)
            TryAxis(words, invalid, row);

        if (invalid.Count > 0)
        {
            Status = LocText.Format("module.goto.invalidValue", "Invalid value: {0}", string.Join(", ", invalid));
            return;
        }
        if (words.Count == 0)
        {
            Status = LocText.Resolve("module.goto.enterCoordinate", "Enter at least one coordinate");
            return;
        }

        await SendAsync($"G53 G0 {string.Join(" ", words)}", LocText.Resolve("module.goto.goingPosition", "Going to position"));
    }

    private static void TryAxis(List<string> words, List<string> invalid, GotoAxisRow row)
    {
        if (string.IsNullOrWhiteSpace(row.Value))
            return;
        if (double.TryParse(row.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
            words.Add($"{row.Letter}{parsed.ToString("0.###", CultureInfo.InvariantCulture)}");
        else
            invalid.Add($"{row.Letter}=\"{row.Value}\"");
    }

    private async Task SendAsync(string command, string success)
    {
        if (!IsHomed)
        {
            Status = LocText.Resolve("module.goto.homeFirst", "Home the machine first ($H)");
            return;
        }
        try
        {
            await _grbl.SendAsync(command);
            Status = success;
        }
        catch (Exception ex)
        {
            Status = LocText.Format("module.goto.commandFailed", "{0} failed: {1}", command, ex.Message);
        }
    }
}
