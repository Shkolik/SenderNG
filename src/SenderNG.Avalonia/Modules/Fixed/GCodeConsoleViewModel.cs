using CNC.Core.Abstractions;
using System.Collections.ObjectModel;
using SenderNG.Avalonia.ViewModels;

namespace SenderNG.Avalonia.Modules.Fixed;

public record GCodeProgramRow(int Line, string Status, string Data);

public class GCodeConsoleViewModel : ViewModelBase
{
    private readonly IGrblService _grbl;

    public ObservableCollection<string> ConsoleLines { get; } = new();

    public ObservableCollection<string> GCodeLines { get; } = new();

    public ObservableCollection<GCodeProgramRow> ProgramRows { get; } = new();

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
        ProgramRows.Clear();
        if (string.IsNullOrWhiteSpace(text))
            return;

        int n = 1;
        foreach (var line in text.Split(["\r\n", "\n"], StringSplitOptions.None))
        {
            var data = line.TrimEnd('\r');
            GCodeLines.Add(data);
            // Status: · idle, ✓ done, ✕ error — placeholder · for now
            ProgramRows.Add(new GCodeProgramRow(n++, "·", data));
        }
    }
}
