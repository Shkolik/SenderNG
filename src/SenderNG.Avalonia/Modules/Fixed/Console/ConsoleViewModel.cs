/*
 * ConsoleViewModel.cs - Console view model
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
using System.Text.Json;
using CNC.Core.Abstractions;
using CNC.Core.Services;
using SenderNG.Avalonia.ViewModels;
using SenderNG.Core.Commands;

namespace SenderNG.Avalonia.Modules.Fixed;

/// <summary>Console display options, persisted under AppSettings.Modules["Console"].</summary>
public sealed class ConsoleOptions
{
    public bool Verbose { get; set; }
    public bool FilterRealtime { get; set; }
    public bool ShowAllRealtime { get; set; }
    public bool FilterOk { get; set; }
}

/// <summary>Loads/saves the console options section (defensive against Dictionary and JsonElement shapes).</summary>
public static class ConsoleSettingsStore
{
    public const string SectionKey = "Console";

    public static ConsoleOptions Load(AppSettings settings)
    {
        var options = new ConsoleOptions();
        if (settings.Modules.TryGetValue(SectionKey, out var raw) && AsObject(raw) is { } section)
        {
            options.Verbose = ReadBool(section, "Verbose") ?? options.Verbose;
            options.FilterRealtime = ReadBool(section, "FilterRealtime") ?? options.FilterRealtime;
            options.ShowAllRealtime = ReadBool(section, "ShowAllRealtime") ?? options.ShowAllRealtime;
            options.FilterOk = ReadBool(section, "FilterOk") ?? options.FilterOk;
        }
        return options;
    }

    public static void Save(AppSettings settings, ConsoleOptions options)
    {
        settings.Modules[SectionKey] = new Dictionary<string, object?>
        {
            ["Verbose"] = options.Verbose,
            ["FilterRealtime"] = options.FilterRealtime,
            ["ShowAllRealtime"] = options.ShowAllRealtime,
            ["FilterOk"] = options.FilterOk,
        };
    }

    private static Dictionary<string, object?>? AsObject(object? value) => value switch
    {
        // Nullable annotations are erased at runtime, so a Dictionary<string, object?>
        // matches this arm too.
        Dictionary<string, object> typed => typed.ToDictionary(kv => kv.Key, kv => (object?)kv.Value),
        JsonElement { ValueKind: JsonValueKind.Object } el => ReadObject(el),
        _ => null,
    };

    private static Dictionary<string, object?> ReadObject(JsonElement el)
    {
        var result = new Dictionary<string, object?>();
        foreach (var prop in el.EnumerateObject())
            result[prop.Name] = prop.Value;
        return result;
    }

    private static bool? ReadBool(IReadOnlyDictionary<string, object?> section, string key) =>
        section.TryGetValue(key, out var v)
            ? v switch
            {
                bool b => b,
                // Freshly deserialized settings.json exposes section values as JsonElement.
                JsonElement { ValueKind: JsonValueKind.True } => true,
                JsonElement { ValueKind: JsonValueKind.False } => false,
                _ => null,
            }
            : null;
}

/// <summary>
/// Console tab: shows the controller's actual raw output (ioSender console
/// semantics). Every line the transport reads is rendered exactly as sent —
/// status reports, "ok"/"error:nn" replies, $I identity/options, [PRB:...]
/// results and $$ dumps. Demo mode publishes no raw lines, so the console no
/// longer floods with synthesized status strings.
/// <para>
/// Filters mirror ioSender's ResponseLog options: without Verbose only
/// meaningful lines are shown (status reports, "$" dumps, "ok" and
/// [GC...]/enumeration replies are hidden); with Verbose everything appears,
/// subject to "Filter out realtime responses" and "Show all realtime
/// responses" (unchanged status reports are hidden unless shown). The log is
/// capped at 200 lines like ioSender.
/// </para>
/// </summary>
public class ConsoleViewModel : ViewModelBase
{
    private const int MaxLines = 200;

    private readonly SettingsService _settings;
    private readonly ConsoleOptions _options;
    private readonly IDisposable _linesSubscription;
    private string? _lastStatusLine;

    public ObservableCollection<string> ConsoleLines { get; } = new();

    /// <summary>
    /// The console log as one joined string, so the console renders as a single
    /// selectable/copyable text blob (ioSender binds a read-only TextBox to its
    /// ResponseLog via StringCollectionToTextConverter). Rebuilt on every line
    /// change; the log is capped at 200 lines so the join stays cheap.
    /// </summary>
    public string ConsoleText => string.Join(Environment.NewLine, ConsoleLines);

    public ConsoleViewModel(IGrblService grbl, SettingsService settings)
    {
        ArgumentNullException.ThrowIfNull(grbl);
        ArgumentNullException.ThrowIfNull(settings);
        _settings = settings;
        _options = ConsoleSettingsStore.Load(settings.Load());

        ClearCommand = new DelegateCommand(_ =>
        {
            ConsoleLines.Clear();
            OnPropertyChanged(nameof(ConsoleText));
        });

        // Raw controller output, before status parsing, so the console shows
        // exactly what the controller sent. UiThread.Post keeps the collection
        // mutations on the UI thread when lines arrive from the reader thread.
        _linesSubscription = grbl.ReceivedLines.Subscribe(line => UiThread.Post(() => OnLineReceived(line)));
    }

    public DelegateCommand ClearCommand { get; }

    /// <summary>Show everything, including status reports and "ok" replies (ioSender "Verbose").</summary>
    public bool Verbose
    {
        get => _options.Verbose;
        set { if (_options.Verbose != value) { _options.Verbose = value; OnPropertyChanged(); Save(); } }
    }

    /// <summary>Hide "<...>" status reports even in verbose mode.</summary>
    public bool FilterRealtime
    {
        get => _options.FilterRealtime;
        set { if (_options.FilterRealtime != value) { _options.FilterRealtime = value; OnPropertyChanged(); Save(); } }
    }

    /// <summary>In verbose mode, show status reports even when nothing changed.</summary>
    public bool ShowAllRealtime
    {
        get => _options.ShowAllRealtime;
        set { if (_options.ShowAllRealtime != value) { _options.ShowAllRealtime = value; OnPropertyChanged(); Save(); } }
    }

    /// <summary>Hide "ok" replies (ioSender ResponseLogFilterOk; not exposed in the console UI).</summary>
    public bool FilterOk
    {
        get => _options.FilterOk;
        set { if (_options.FilterOk != value) { _options.FilterOk = value; OnPropertyChanged(); Save(); } }
    }

    public void Log(string line)
    {
        ConsoleLines.Add(line);
        OnPropertyChanged(nameof(ConsoleText));
    }

    private void OnLineReceived(string data)
    {
        if (string.IsNullOrEmpty(data))
            return;

        var first = data[0];
        var isStatus = first == '<';
        var isError = data.StartsWith("error");
        // ioSender noise set hidden when not verbose: <status reports>, $
        // settings dumps, "ok", and [GC...]/enumeration replies.
        var isNoise = isStatus || first == '$' || first == 'o'
            || (first == '[' && (data.StartsWith("[GC") || IsEnumeration(data)));

        if (!Verbose && !isError && isNoise)
            return;

        if (isStatus && FilterRealtime)
            return;

        string? line = null;
        if (isError)
        {
            // ioSender semantics: show the raw reply with its dictionary
            // explanation appended, e.g. "error:2 - Missing the expected G-code
            // word value or numeric value format is not valid."
            line = GrblErrorCodes.Decorate(data);
        }
        else if (data == "ok")
        {
            line = FilterOk ? null : data;
        }
        else if (isStatus)
        {
            // Verbose: status reports are shown only when they changed, unless
            // "Show all realtime responses" is set (ioSender stateChanged logic).
            var changed = data != _lastStatusLine;
            _lastStatusLine = data;
            if (changed || ShowAllRealtime)
                line = data;
        }
        else
        {
            line = data; // meaningful replies: $I identity/options, [PRB:...], [VER:...], ...
        }

        if (line is null)
            return;

        Log(line);
        var trimmed = false;
        while (ConsoleLines.Count > MaxLines)
        {
            ConsoleLines.RemoveAt(0);
            trimmed = true;
        }
        if (trimmed)
            OnPropertyChanged(nameof(ConsoleText));
    }

    private void Save()
    {
        try
        {
            var app = _settings.Load();
            ConsoleSettingsStore.Save(app, _options);
            _settings.Save(app);
        }
        catch
        {
            // Best-effort persistence; console options still apply for the session.
        }
    }

    private static bool IsEnumeration(string data) =>
        data.StartsWith("[SETTING:") || data.StartsWith("[ERRORCODE:") ||
        data.StartsWith("[ALARMCODE:") || data.StartsWith("[SETTINGGROUP:") ||
        data.StartsWith("[SETTINGDESCR:");
}
