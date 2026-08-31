using System.Collections.ObjectModel;
using CNC.Core.Abstractions;
using CNC.Core.Services;
using SenderNG.ViewModels;

namespace SenderNG.Modules.Plugins;

public sealed class MposAxis : ViewModelBase
{
    private double _position;

    public MposAxis(string letter) => Letter = letter;

    public string Letter { get; }

    public double Position
    {
        get => _position;
        set => SetField(ref _position, value);
    }
}

/// <summary>
/// Machine coordinate (MPos) readout widget. Read-only — machine coordinates
/// cannot be zeroed with G92 (that only shifts the work offset); use the
/// Offsets module to adjust the work coordinate system instead.
/// </summary>
public class MposViewModel : ViewModelBase
{
    private static readonly string[] Letters = ["X", "Y", "Z", "A", "B", "C", "U", "V", "W"];

    private readonly IGrblService _grbl;

    public ObservableCollection<MposAxis> Axes { get; } = new();

    public MposViewModel(IGrblService grbl)
    {
        ArgumentNullException.ThrowIfNull(grbl);
        _grbl = grbl;
        grbl.State.Subscribe(s => UiThread.Post(() => ApplyState(s)));
    }

    private void ApplyState(MachineState state)
    {
        if (state is null)
            return;

        while (Axes.Count > state.NumAxes)
            Axes.RemoveAt(Axes.Count - 1);

        while (Axes.Count < state.NumAxes && Axes.Count < Letters.Length)
            Axes.Add(new MposAxis(Letters[Axes.Count]));

        var machine = state.MachinePositions;
        var count = Math.Min(Axes.Count, machine?.Length ?? 0);
        for (var i = 0; i < count; i++)
            Axes[i].Position = machine![i];
    }
}
