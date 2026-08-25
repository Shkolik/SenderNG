using CNC.Core.Abstractions;
using CNC.Core.Services;
using System.Collections.ObjectModel;
using SenderNG.Avalonia.ViewModels;

namespace SenderNG.Avalonia.Modules.Fixed;

public class ConsoleViewModel : ViewModelBase
{
    private readonly IGrblService _grbl;

    public ObservableCollection<string> ConsoleLines { get; } = new();

    public ConsoleViewModel(IGrblService grbl)
    {
        _grbl = grbl ?? throw new ArgumentNullException(nameof(grbl));

        // Received lines from the controller: state reports like "< Idle|WPos X0 Y0 Z0"
        _grbl.State.Subscribe(s => UiThread.Post(() =>
        {
            if (s is null) return;
            var pos = string.Join(" ", s.Positions.Select(p => p.ToString("0.###")));
            Log($"< {s.Status}|WPos {pos}".TrimEnd());
        }));
    }

    public void Log(string line) => ConsoleLines.Add(line);
}
