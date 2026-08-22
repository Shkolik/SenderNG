using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using SenderNG.Core.Commands;
using SenderNG.Avalonia.ViewModels;

namespace SenderNG.Avalonia.Modules.Fixed;

public sealed class SettingRow
{
    public SettingRow(string key, string value, string comment)
    {
        Key = key;
        Value = value;
        Comment = comment;
    }

    public string Key { get; }
    public string Value { get; set; }
    public string Comment { get; }
}

public class GrblSettingsViewModel : ViewModelBase
{
    private static readonly Regex LinePattern =
        new(@"^\$(\d+)=([^\s(]+)\s*(?:\(([^)]*)\))?\s*$", RegexOptions.Compiled);

    private readonly Func<Task<IEnumerable<string>>> _provider;

    public ObservableCollection<SettingRow> Settings { get; } = new();

    private string _status = string.Empty;
    public string Status
    {
        get => _status;
        set => SetField(ref _status, value);
    }

    public DelegateCommand LoadCommand { get; }

    public GrblSettingsViewModel(Func<Task<IEnumerable<string>>> provider)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        LoadCommand = new DelegateCommand(_ => _ = LoadAsync());
    }

    public async Task LoadAsync()
    {
        try
        {
            var rows = new List<SettingRow>();
            foreach (var line in await _provider())
            {
                if (Parse(line) is { } row)
                    rows.Add(row);
            }

            Settings.Clear();
            foreach (var row in rows)
                Settings.Add(row);

            Status = $"Loaded {rows.Count} settings";
        }
        catch (Exception ex)
        {
            Status = $"Load failed: {ex.Message}";
        }
    }

    public static SettingRow? Parse(string line)
    {
        var match = LinePattern.Match(line?.Trim() ?? string.Empty);
        if (!match.Success)
            return null;

        return new SettingRow(
            match.Groups[1].Value,
            match.Groups[2].Value,
            match.Groups[3].Success ? match.Groups[3].Value : string.Empty);
    }
}
