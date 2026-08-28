/*
 * GrblSettingsViewModel.cs - GRBL settings view model
 */

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
    private readonly Func<string, Task>? _send;
    private readonly Dictionary<string, string> _originals = new();

    public ObservableCollection<SettingRow> Settings { get; } = new();

    private string _status = string.Empty;
    public string Status
    {
        get => _status;
        set => SetField(ref _status, value);
    }

    public DelegateCommand LoadCommand { get; }

    public DelegateCommand SaveCommand { get; }

    public GrblSettingsViewModel(Func<Task<IEnumerable<string>>> provider, Func<string, Task>? send = null)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _send = send;
        LoadCommand = new DelegateCommand(_ => _ = LoadAsync());
        SaveCommand = new DelegateCommand(_ => _ = SaveAsync(), _ => _send is not null);
        // Fill on open; failures surface via Status.
        _ = LoadAsync();
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

            _originals.Clear();
            foreach (var row in rows)
                _originals[row.Key] = row.Value;

            Status = $"Loaded {rows.Count} settings";
        }
        catch (Exception ex)
        {
            Status = $"Load failed: {ex.Message}";
        }
    }

    public async Task SaveAsync()
    {
        if (_send is null)
            return;
        try
        {
            var changed = 0;
            foreach (var row in Settings)
            {
                if (!_originals.TryGetValue(row.Key, out var original) || original == row.Value)
                    continue;
                await _send($"${row.Key}={row.Value}");
                _originals[row.Key] = row.Value;
                changed++;
            }
            Status = changed == 0 ? "No changes" : $"Saved {changed} setting(s)";
        }
        catch (Exception ex)
        {
            Status = $"Save failed: {ex.Message}";
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
