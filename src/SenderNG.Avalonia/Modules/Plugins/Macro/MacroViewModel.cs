using System.Collections.ObjectModel;
using System.Text.Json;
using CNC.Core.Abstractions;
using CNC.Core.Services;
using SenderNG.Core.Commands;
using SenderNG.Avalonia.ViewModels;

namespace SenderNG.Avalonia.Modules.Plugins;

public class MacroItem : ViewModelBase
{
    private string _name = string.Empty;
    public string Name
    {
        get => _name;
        set => SetField(ref _name, value);
    }

    private string _code = string.Empty;
    public string Code
    {
        get => _code;
        set => SetField(ref _code, value);
    }
}

public class MacroViewModel : ViewModelBase
{
    private readonly IGrblService _grbl;
    private readonly SettingsService _settingsService;
    private readonly AppSettings _appSettings;

    public ObservableCollection<MacroItem> Macros { get; } = new();

    private MacroItem? _selectedMacro;
    public MacroItem? SelectedMacro
    {
        get => _selectedMacro;
        set => SetField(ref _selectedMacro, value);
    }

    public DelegateCommand RunCommand { get; }
    public DelegateCommand AddMacroCommand { get; }
    public DelegateCommand RemoveMacroCommand { get; }

    public MacroViewModel(SettingsService settingsService, IGrblService grbl)
    {
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _grbl = grbl ?? throw new ArgumentNullException(nameof(grbl));
        _appSettings = _settingsService.Load();

        RunCommand = new DelegateCommand(_ => _ = RunAsync(SelectedMacro), _ => SelectedMacro is not null);
        AddMacroCommand = new DelegateCommand(_ => AddMacro());
        RemoveMacroCommand = new DelegateCommand(
            _ => RemoveSelected(), _ => SelectedMacro is not null);

        Load();
    }

    public async Task RunAsync(MacroItem? macro)
    {
        if (macro is null || string.IsNullOrWhiteSpace(macro.Code))
            return;

        foreach (var line in macro.Code.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries))
            await _grbl.SendAsync(line.Trim());
    }

    public void Save()
    {
        _appSettings.Modules["Macros"] = Macros
            .Select(m => new Dictionary<string, object> { ["Name"] = m.Name, ["Code"] = m.Code })
            .ToList();
        _settingsService.Save(_appSettings);
    }

    private void Load()
    {
        if (!_appSettings.Modules.TryGetValue("Macros", out var raw))
            return;

        var items = raw switch
        {
            List<object> typed => ReadTyped(typed),
            JsonElement { ValueKind: JsonValueKind.Array } el => ReadJson(el),
            _ => null,
        };

        if (items is null)
            return;

        foreach (var item in items)
            Macros.Add(item);
    }

    private static List<MacroItem>? ReadTyped(List<object> list) =>
        list.OfType<Dictionary<string, object>>()
            .Select(d => new MacroItem
            {
                Name = d.TryGetValue("Name", out var n) ? n?.ToString() ?? string.Empty : string.Empty,
                Code = d.TryGetValue("Code", out var c) ? c?.ToString() ?? string.Empty : string.Empty,
            })
            .ToList();

    private static List<MacroItem> ReadJson(JsonElement el) =>
        el.EnumerateArray()
            .Select(e => new MacroItem
            {
                Name = e.TryGetProperty("Name", out var n) ? n.GetString() ?? string.Empty : string.Empty,
                Code = e.TryGetProperty("Code", out var c) ? c.GetString() ?? string.Empty : string.Empty,
            })
            .ToList();

    private void AddMacro()
    {
        var macro = new MacroItem { Name = $"Macro {Macros.Count + 1}", Code = string.Empty };
        Macros.Add(macro);
        SelectedMacro = macro;
        Save();
    }

    private void RemoveSelected()
    {
        if (SelectedMacro is null)
            return;

        Macros.Remove(SelectedMacro);
        SelectedMacro = null;
        Save();
    }
}
