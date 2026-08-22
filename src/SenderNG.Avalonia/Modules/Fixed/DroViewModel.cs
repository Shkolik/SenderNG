using System.Collections.ObjectModel;
using CNC.Core.Abstractions;
using CNC.Core.Services;
using SenderNG.Avalonia.ViewModels;

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

    public ObservableCollection<AxisPosition> Axes { get; } = new();

    public DroViewModel(IGrblService grbl)
    {
        ArgumentNullException.ThrowIfNull(grbl);
        grbl.State.Subscribe(ApplyState);
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
