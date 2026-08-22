using System.Collections.ObjectModel;
using SenderNG.Avalonia.ViewModels;

namespace SenderNG.Avalonia.Modules.Fixed;

public record GCodeProgramRow(int Line, string Status, string Data);

public class ProgramViewModel : ViewModelBase
{
    public ObservableCollection<GCodeProgramRow> ProgramRows { get; } = new();

    public void LoadGCode(string? text)
    {
        ProgramRows.Clear();
        if (string.IsNullOrWhiteSpace(text))
            return;

        int n = 1;
        foreach (var line in text.Split(["\r\n", "\n"], StringSplitOptions.None))
        {
            var data = line.TrimEnd('\r');
            // Status: · idle, ✓ done, ✕ error — placeholder · for now
            ProgramRows.Add(new GCodeProgramRow(n++, "·", data));
        }
    }
}
