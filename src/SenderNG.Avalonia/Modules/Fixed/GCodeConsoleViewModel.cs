using CNC.Core.Abstractions;
using System.Collections.ObjectModel;
using SenderNG.Avalonia.ViewModels;

namespace SenderNG.Avalonia.Modules.Fixed;

public class GCodeConsoleViewModel : ViewModelBase
{
    private readonly IGrblService _grbl;

    public ObservableCollection<string> ConsoleLines { get; } = new();

    public ObservableCollection<string> GCodeLines { get; } = new();

    public GCodeConsoleViewModel(IGrblService grbl)
    {
        _grbl = grbl ?? throw new ArgumentNullException(nameof(grbl));
    }

    public void Log(string line) => ConsoleLines.Add(line);

    public async Task SendAsync(string line)
    {
        Log($"> {line}");
        await _grbl.SendAsync(line);
    }

    public void LoadGCode(string text)
    {
        GCodeLines.Clear();
        if (string.IsNullOrWhiteSpace(text))
            return;

        foreach (var line in text.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries))
            GCodeLines.Add(line.TrimEnd('\r'));
    }
}
