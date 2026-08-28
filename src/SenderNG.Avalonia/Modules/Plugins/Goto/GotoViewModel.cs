using System.Globalization;
using CNC.Core.Abstractions;
using CNC.Core.Services;
using SenderNG.Avalonia.ViewModels;
using SenderNG.Core.Commands;

namespace SenderNG.Avalonia.Modules.Plugins;

/// <summary>
/// ioSender's Goto widget: G28/G30 "goto predefined position" buttons, G28.1/G30.1
/// "record current position as reference point", and a numeric machine-coordinate
/// goto (G53 G0 X.. Y.. Z..). All actions are gated on the machine being homed —
/// grbl itself refuses them while an alarm is active, and moving to a stored
/// reference before homing is meaningless.
/// </summary>
public sealed class GotoViewModel : ViewModelBase
{
    private readonly IGrblService _grbl;
    private readonly IDisposable _stateSubscription;
    private bool _isHomed;
    private string _x = "0";
    private string _y = "0";
    private string _z = "0";
    private string _status = string.Empty;

    public GotoViewModel(IGrblService grbl)
    {
        _grbl = grbl ?? throw new ArgumentNullException(nameof(grbl));
        GotoHomeCommand = new DelegateCommand(_ => _ = SendAsync("G28", "Going to home position"));
        GotoG30Command = new DelegateCommand(_ => _ = SendAsync("G30", "Going to second reference point"));
        SetHomeCommand = new DelegateCommand(_ => _ = SendAsync("G28.1", "Home position recorded"));
        SetG30Command = new DelegateCommand(_ => _ = SendAsync("G30.1", "Second reference point recorded"));
        GoCommand = new DelegateCommand(_ => _ = GoAsync());
        _stateSubscription = grbl.State.Subscribe(s => UiThread.Post(() => ApplyState(s)));
    }

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
                Status = value ? string.Empty : "Home the machine first ($H)";
        }
    }

    public string X { get => _x; set => SetField(ref _x, value); }
    public string Y { get => _y; set => SetField(ref _y, value); }
    public string Z { get => _z; set => SetField(ref _z, value); }

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
    }

    private async Task GoAsync()
    {
        if (!IsHomed)
        {
            Status = "Home the machine first ($H)";
            return;
        }

        var words = new List<string>();
        var invalid = new List<string>();
        TryAxis(words, invalid, 'X', X);
        TryAxis(words, invalid, 'Y', Y);
        TryAxis(words, invalid, 'Z', Z);

        if (invalid.Count > 0)
        {
            Status = $"Invalid value: {string.Join(", ", invalid)}";
            return;
        }
        if (words.Count == 0)
        {
            Status = "Enter at least one coordinate";
            return;
        }

        await SendAsync($"G53 G0 {string.Join(" ", words)}", "Going to position");
    }

    private void TryAxis(List<string> words, List<string> invalid, char letter, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;
        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
            words.Add($"{letter}{parsed.ToString("0.###", CultureInfo.InvariantCulture)}");
        else
            invalid.Add($"{letter}=\"{value}\"");
    }

    private async Task SendAsync(string command, string success)
    {
        if (!IsHomed)
        {
            Status = "Home the machine first ($H)";
            return;
        }
        try
        {
            await _grbl.SendAsync(command);
            Status = success;
        }
        catch (Exception ex)
        {
            Status = $"{command} failed: {ex.Message}";
        }
    }
}
