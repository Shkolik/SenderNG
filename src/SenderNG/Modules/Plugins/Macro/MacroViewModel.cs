
/*

Copyright (c) 2018-2022 Io Engineering (Terje Io)

Copyright (c) 2026 Andrew Shkolik

All rights reserved.

Redistribution and use in source and binary forms, with or without modification,
are permitted provided that the following conditions are met:

· Redistributions of source code must retain the above copyright notice, this
list of conditions and the following disclaimer.

· Redistributions in binary form must reproduce the above copyright notice, this
list of conditions and the following disclaimer in the documentation and/or
other materials provided with the distribution.

· Neither the name of the copyright holder nor the names of its contributors may
be used to endorse or promote products derived from this software without
specific prior written permission.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND
ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT OWNER OR CONTRIBUTORS BE LIABLE FOR
ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
(INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON
ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
(INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.

*/

using System.Collections.ObjectModel;
using System.Text.Json;
using CNC.Core.Abstractions;
using CNC.Core.Services;
using SenderNG.Localization;
using SenderNG.Core.Commands;
using SenderNG.ViewModels;

namespace SenderNG.Modules.Plugins;

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
        set
        {
            if (SetField(ref _selectedMacro, value))
            {
                RunCommand.RaiseCanExecuteChanged();
                RemoveMacroCommand.RaiseCanExecuteChanged();
            }
        }
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
            // Shared in-memory graph stores the strongly-typed list written by Save().
            List<Dictionary<string, object>> typedDicts => ReadTyped([.. typedDicts]),
            List<object> typed => ReadTyped(typed),
            JsonElement { ValueKind: JsonValueKind.Array } el => ReadJson(el),
            _ => null,
        };

        if (items is null)
            return;

        foreach (var item in items)
            AddItem(item);
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
        var macro = new MacroItem {
            Name = LocText.Format("module.macro.newMacro", "Macro {0}", Macros.Count + 1),
            Code = string.Empty,
        };
        AddItem(macro);
        SelectedMacro = macro;
        Save();
    }

    private void AddItem(MacroItem item)
    {
        item.PropertyChanged += (_, _) => Save();
        Macros.Add(item);
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
