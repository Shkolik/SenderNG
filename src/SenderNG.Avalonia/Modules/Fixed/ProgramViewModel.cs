using System.Collections.ObjectModel;
using CNC.Core.Services;
using SenderNG.Avalonia.ViewModels;

namespace SenderNG.Avalonia.Modules.Fixed;

public record GCodeProgramRow(int Line, string Status, string Data);

public record ProgramLimitRow(string Letter, double Min, double Max);

public class ProgramViewModel : ViewModelBase
{
    public ObservableCollection<GCodeProgramRow> ProgramRows { get; } = new();

    public ObservableCollection<ProgramLimitRow> LimitRows { get; } = new();

    public bool HasLimits => LimitRows.Count > 0;

    public void LoadGCode(string? text)
    {
        ProgramRows.Clear();
        LimitRows.Clear();
        OnPropertyChanged(nameof(HasLimits));
        if (string.IsNullOrWhiteSpace(text))
            return;

        int n = 1;
        foreach (var line in text.Split(["\r\n", "\n"], StringSplitOptions.None))
        {
            var data = line.TrimEnd('\r');
            // Status: · idle, ✓ done, ✕ error — placeholder · for now
            ProgramRows.Add(new GCodeProgramRow(n++, "·", data));
        }

        var limits = GCodeLimitsCalculator.Compute(text);
        if (limits is not null)
        {
            foreach (var l in limits)
                LimitRows.Add(new ProgramLimitRow(l.Letter.ToString(), l.Min, l.Max));
            OnPropertyChanged(nameof(HasLimits));
        }
    }

    /// <summary>Reset the loaded program to the start: all rows return to the un-sent state.</summary>
    public void Rewind()
    {
        for (var i = 0; i < ProgramRows.Count; i++)
            ProgramRows[i] = ProgramRows[i] with { Status = "·" };
    }
}
