using System.Collections.ObjectModel;
using System.Windows.Input;
using CNC.Core.Abstractions;
using CNC.Core.Services;
using SenderNG.Avalonia.ViewModels;
using SenderNG.Core.Commands;

namespace SenderNG.Avalonia.Modules.Fixed;

public sealed class AxisPosition : ViewModelBase
{
    private double _position;

    public AxisPosition(string letter) => Letter = letter;

    public string Letter { get; }

    public double Position
    {
        get => _position;
        set => SetField(ref _position, value);
    }
}

public class DroViewModel : ViewModelBase
{
    private static readonly string[] Letters = ["X", "Y", "Z", "A", "B", "C", "U", "V", "W"];

    private readonly IGrblService _grbl;
    private string _status = string.Empty;

    public ObservableCollection<AxisPosition> Axes { get; } = new();

    public DroViewModel(IGrblService grbl)
    {
        ArgumentNullException.ThrowIfNull(grbl);
        _grbl = grbl;
        grbl.State.Subscribe(s => UiThread.Post(() => ApplyState(s)));

        ZeroAxisCommand = new DelegateCommand(p => _ = ZeroAxisAsync(p as string));
        ZeroAllCommand = new DelegateCommand(_ => _ = ZeroAllAsync());
    }

    public ICommand ZeroAxisCommand { get; }

    public ICommand ZeroAllCommand { get; }

    public string Status
    {
        get => _status;
        private set => SetField(ref _status, value);
    }

    private async Task ZeroAxisAsync(string? letter)
    {
        if (string.IsNullOrEmpty(letter))
            return;
        try
        {
            await _grbl.SendAsync($"G92 {letter}0");
            Status = $"{letter} zeroed";
        }
        catch (Exception ex)
        {
            Status = $"Zero {letter} failed: {ex.Message}";
        }
    }

    private async Task ZeroAllAsync()
    {
        if (Axes.Count == 0)
            return;
        var words = string.Join(" ", Axes.Select(a => $"{a.Letter}0"));
        try
        {
            await _grbl.SendAsync($"G92 {words}");
            Status = "All axes zeroed";
        }
        catch (Exception ex)
        {
            Status = $"Zero all failed: {ex.Message}";
        }
    }

    private void ApplyState(MachineState state)
    {
        if (state is null)
            return;

        while (Axes.Count > state.NumAxes)
            Axes.RemoveAt(Axes.Count - 1);

        while (Axes.Count < state.NumAxes && Axes.Count < Letters.Length)
            Axes.Add(new AxisPosition(Letters[Axes.Count]));

        var count = Math.Min(Axes.Count, state.Positions.Length);
        for (var i = 0; i < count; i++)
            Axes[i].Position = state.Positions[i];
    }
}
