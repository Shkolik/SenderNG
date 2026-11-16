
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
using SenderNG.Modules.Fixed;
using SenderNG.Core.Commands;
using SenderNG.Core.Services;
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

    private string _shortName = string.Empty;

    /// <summary>Optional toolbar label. Empty keeps the macro's "M1"…"M12" key label.</summary>
    public string ShortName
    {
        get => _shortName;
        set
        {
            if (!SetField(ref _shortName, value))
                return;
            OnPropertyChanged(nameof(HasShortName));
            OnPropertyChanged(nameof(ToolbarLabel));
        }
    }

    private string _code = string.Empty;
    public string Code
    {
        get => _code;
        set => SetField(ref _code, value);
    }

    private int _slot = -1;

    /// <summary>
    /// Zero-based F-key slot (0 = F1), or -1 when the macro has none. A slot is
    /// stable: deleting another macro never moves this one onto a different key.
    /// Persisted so the assignment survives restarts.
    /// </summary>
    public int Slot
    {
        get => _slot;
        internal set
        {
            if (!SetField(ref _slot, value))
                return;
            OnPropertyChanged(nameof(ShortcutKey));
            OnPropertyChanged(nameof(ShortcutLabel));
            OnPropertyChanged(nameof(ToolbarLabel));
            OnPropertyChanged(nameof(ToolbarIndex));
            OnPropertyChanged(nameof(HasShortcut));
        }
    }

    /// <summary>"F1"…"F12" for the macro's own slot, empty when it has none.</summary>
    public string ShortcutKey => Slot >= 0 && Slot < HotkeyGrammar.MacroShortcutGestures.Count
        ? HotkeyGrammar.MacroShortcutGestures[Slot]
        : string.Empty;

    /// <summary>Fallback toolbar label: "M1"…"M12", empty when the macro has no key.</summary>
    public string ShortcutLabel => ShortcutKey.Length == 0 ? string.Empty : "M" + ShortcutKey[1..];

    /// <summary>Toolbar label: the short name when the operator set one, else "M1"…"M12".</summary>
    public string ToolbarLabel => HasShortName ? ShortName.Trim() : ShortcutLabel;

    /// <summary>The small position number beside a short name: "1"…"12".</summary>
    public string ToolbarIndex => ShortcutKey.Length == 0 ? string.Empty : ShortcutKey[1..];

    public bool HasShortcut => Slot >= 0;

    public bool HasShortName => !string.IsNullOrWhiteSpace(ShortName);

    /// <summary>True when the stored profile carried a slot value (including -1,
    /// meaning "deliberately unbound"). Used only to tell a pre-slot profile — which
    /// needs the one-time migration — from a current one.</summary>
    internal bool HasStoredSlot { get; set; }
}

public class MacroViewModel : ViewModelBase
{
    /// <summary>Twelve F-key slots (F1–F12); a macro owns at most one and the rest
    /// stay unbound. The key table (<see cref="MacroShortcutKeys"/>) owns the keys.</summary>
    public static int ShortcutCount => MacroShortcutKeys.Count;

    private static readonly string[] BlockedStatuses = ["Run", "Hold", "Door", "Jog", "Home"];

    private readonly IGrblService _grbl;
    private readonly SettingsService _settingsService;
    private readonly ProgramViewModel? _program;
    private MachineProfile _profile;
    private bool _loading;

    public ObservableCollection<MacroItem> Macros { get; } = new();

    private readonly HashSet<MacroItem> _watched = new();

    /// <summary>The first twelve macros with code, in list order — the toolbar's source.
    /// An empty macro is not a runnable action, so it gets no button.</summary>
    public ObservableCollection<MacroItem> ToolbarMacros { get; } = new();

    private bool _hasToolbarMacros;
    public bool HasToolbarMacros
    {
        get => _hasToolbarMacros;
        private set => SetField(ref _hasToolbarMacros, value);
    }

    private bool _machineBusy;

    /// <summary>True while the controller reports motion or a hold (Run/Hold/Door/Jog/Home).</summary>
    public bool IsMachineBusy => _machineBusy;

    /// <summary>Every macro run path is gated on this: no macros while metal is moving.</summary>
    public bool CanRunMacros => !IsJobRunning && !IsMachineBusy;

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

    /// <summary>Toolbar button command: parameter is the <see cref="MacroItem"/>; never asks.</summary>
    public DelegateCommand RunToolbarCommand { get; }

    public MacroViewModel(SettingsService settingsService, IGrblService grbl, ProgramViewModel? program = null)
    {
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _grbl = grbl ?? throw new ArgumentNullException(nameof(grbl));
        _program = program;
        _profile = _settingsService.Load();

        RunCommand = new DelegateCommand(_ => _ = RunAsync(SelectedMacro),
            _ => SelectedMacro is not null && CanRunMacros);
        AddMacroCommand = new DelegateCommand(_ => AddMacro());
        RemoveMacroCommand = new DelegateCommand(
            _ => RemoveSelected(), _ => SelectedMacro is not null);
        RunToolbarCommand = new DelegateCommand(
            p => _ = RunAsync(p as MacroItem),
            p => p is MacroItem macro && !string.IsNullOrWhiteSpace(macro.Code) && CanRunMacros);

        // Macros are raw G-code: closed while a program session streams, so a macro
        // line cannot be queued between program blocks (ioSender's
        // MacroExecuteControl refuses while IsJobRunning).
        if (_program is not null)
            _program.PropertyChanged += OnProgramPropertyChanged;

        // A macro's F-key is its own slot, assigned once and kept: deleting a
        // different macro must not move it onto another key. New macros take the
        // lowest free slot; a pre-slot profile is migrated in AssignLoadedSlots.
        Macros.CollectionChanged += OnMacrosCollectionChanged;

        // A macro that lands between motion blocks is a crash the operator cannot
        // predict: follow the controller status and close every run path while the
        // machine moves or holds.
        _grbl.State.Subscribe(s => UiThread.Post(() => ApplyState(s)));

        Load();
        // Macros are machine routines: follow profile switches (reload the list
        // from the newly active machine, keep saving to its file).
        _settingsService.Changed += OnSettingsChanged;
    }

    /// <summary>True while a program session streams or is paused.</summary>
    public bool IsJobRunning => _program?.IsRunning == true;

    private string _status = string.Empty;

    /// <summary>Last refusal message (a run blocked by a program session).</summary>
    public string Status
    {
        get => _status;
        private set => SetField(ref _status, value);
    }

    private void OnProgramPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(ProgramViewModel.IsRunning) or nameof(ProgramViewModel.IsPaused)))
            return;
        OnPropertyChanged(nameof(IsJobRunning));
        OnPropertyChanged(nameof(CanRunMacros));
        RunCommand.RaiseCanExecuteChanged();
        RunToolbarCommand.RaiseCanExecuteChanged();
    }

    private void ApplyState(MachineState state)
    {
        if (state is null)
            return;

        var busy = IsBlockedStatus(state.Status);
        if (busy == _machineBusy)
            return;

        _machineBusy = busy;
        OnPropertyChanged(nameof(IsMachineBusy));
        OnPropertyChanged(nameof(CanRunMacros));
        RunCommand.RaiseCanExecuteChanged();
        RunToolbarCommand.RaiseCanExecuteChanged();
    }

    /// <summary>
    /// True when the reported state is one that must keep macros out: motion or a
    /// hold. grbl/grblHAL qualify some states ("Hold:0", "Hold:1", "Door:1"), so
    /// only the name before ':' is compared — an exact match would leave a held or
    /// open-door machine looking idle and let a macro queue lines into the hold.
    /// </summary>
    private static bool IsBlockedStatus(string? status)
    {
        if (string.IsNullOrEmpty(status))
            return false;

        var colon = status.IndexOf(':');
        var name = colon < 0 ? status : status[..colon];
        return BlockedStatuses.Contains(name, StringComparer.OrdinalIgnoreCase);
    }

    private void OnMacrosCollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        // A slot is assigned when a macro arrives (new macro or profile load) and
        // then never changes. A removal only frees its slot; the survivors keep
        // theirs, so deleting a macro cannot silently re-key another one.
        if (!_loading
            && e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Add
            && e.NewItems is not null)
            foreach (MacroItem macro in e.NewItems)
                AssignSlot(macro);

        RefreshToolbarMacros();
        RewatchMacros();
        RunToolbarCommand.RaiseCanExecuteChanged();
    }

    /// <summary>
    /// Keeps <paramref name="macro"/>'s loaded slot when it is valid and free, and
    /// otherwise gives it the lowest free one. Repairs duplicate or out-of-range
    /// slots from a hand-edited settings file; existing macros are never touched.
    /// </summary>
    private void AssignSlot(MacroItem macro)
    {
        var used = new HashSet<int>();
        foreach (var other in Macros)
            if (!ReferenceEquals(other, macro) && other.Slot >= 0 && other.Slot < ShortcutCount)
                used.Add(other.Slot);

        if (macro.Slot >= 0 && macro.Slot < ShortcutCount && !used.Contains(macro.Slot))
            return;

        var slot = 0;
        while (slot < ShortcutCount && used.Contains(slot))
            slot++;

        macro.Slot = slot < ShortcutCount ? slot : -1;
    }

    /// <summary>
    /// One-time slot assignment after a profile load. Stored slots win; a profile
    /// from before slots existed keeps the key its auto-generated name implies
    /// ("Macro 5" → F5), which repairs a list that already lost a macro instead of
    /// shifting it; renamed macros and the rest keep list order. A stored -1 means
    /// "deliberately unbound" and is left alone.
    /// </summary>
    private void AssignLoadedSlots()
    {
        var used = new HashSet<int>();

        // Stored, valid and unique slots win. Duplicate or out-of-range values are
        // hand-edited garbage: treat them as if the macro had no stored slot.
        foreach (var macro in Macros)
        {
            if (macro.Slot >= 0 && macro.Slot < ShortcutCount && used.Add(macro.Slot))
                continue;

            if (macro.Slot >= 0)
                macro.HasStoredSlot = false;
            macro.Slot = -1;
        }

        // Pre-slot profiles: the default name already carries the intended key.
        foreach (var macro in Macros)
            if (!macro.HasStoredSlot && macro.Slot < 0
                && TrySlotFromName(macro.Name, out var slot) && used.Add(slot))
                macro.Slot = slot;

        // Anything still unbound (renamed macro) gets the lowest free slot.
        foreach (var macro in Macros)
        {
            if (macro.Slot >= 0 || macro.HasStoredSlot)
                continue;   // stored -1: the list already had more than twelve macros

            var slot = 0;
            while (slot < ShortcutCount && used.Contains(slot))
                slot++;

            macro.Slot = slot < ShortcutCount ? slot : -1;
            if (macro.Slot >= 0)
                used.Add(macro.Slot);
        }

        RefreshToolbarMacros();
        RunToolbarCommand.RaiseCanExecuteChanged();
    }

    /// <summary>Slot implied by the localized "new macro" name ("Macro 5" → F5).
    /// English is tried too: the operator may have switched languages since the
    /// list was created.</summary>
    private static bool TrySlotFromName(string name, out int slot)
        => TrySlotFromTemplate(name, LocText.Resolve("module.macro.newMacro", "Macro {0}"), out slot)
           || TrySlotFromTemplate(name, "Macro {0}", out slot);

    private static bool TrySlotFromTemplate(string name, string template, out int slot)
    {
        slot = -1;
        const string placeholder = "{0}";
        var marker = template.IndexOf(placeholder, StringComparison.Ordinal);
        if (marker < 0)
            return false;

        var prefix = template[..marker];
        var suffix = template[(marker + placeholder.Length)..];
        if (!name.StartsWith(prefix, StringComparison.Ordinal) || !name.EndsWith(suffix, StringComparison.Ordinal))
            return false;

        var numberText = name[prefix.Length..(name.Length - suffix.Length)];
        if (!int.TryParse(numberText, out var number) || number < 1 || number > ShortcutCount)
            return false;

        slot = number - 1;
        return true;
    }

    /// <summary>Re-watch so a macro whose code an operator just typed enables its
    /// button without waiting for the next machine-state change.</summary>
    private void RewatchMacros()
    {
        foreach (var watched in _watched)
            watched.PropertyChanged -= OnMacroPropertyChanged;
        _watched.Clear();
        foreach (var macro in Macros)
        {
            _watched.Add(macro);
            macro.PropertyChanged += OnMacroPropertyChanged;
        }
    }

    /// <summary>Toolbar contents: the macros that own a shortcut slot and actually
    /// have code, in slot order. An empty macro keeps its slot (its F-key is still
    /// claimed) but gets no button; a deleted macro leaves its slot empty until a
    /// new macro takes it. Updated in place: a Clear()/Add() pass would recreate
    /// every button (and drop focus/hover) when a single macro's code changed.</summary>
    private void RefreshToolbarMacros()
    {
        var desired = Macros
            .Where(macro => macro.Slot >= 0 && !string.IsNullOrWhiteSpace(macro.Code))
            .OrderBy(macro => macro.Slot)
            .ToList();

        for (var i = ToolbarMacros.Count - 1; i >= 0; i--)
            if (!desired.Contains(ToolbarMacros[i]))
                ToolbarMacros.RemoveAt(i);

        for (var i = 0; i < desired.Count; i++)
        {
            var macro = desired[i];
            if (i < ToolbarMacros.Count && ReferenceEquals(ToolbarMacros[i], macro))
                continue;

            var existing = ToolbarMacros.IndexOf(macro);
            if (existing >= 0)
                ToolbarMacros.Move(existing, i);
            else
                ToolbarMacros.Insert(i, macro);
        }

        HasToolbarMacros = ToolbarMacros.Count > 0;
    }

    private void OnMacroPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MacroItem.Code))
            return;
        RefreshToolbarMacros();
        RunToolbarCommand.RaiseCanExecuteChanged();
    }

    private void OnSettingsChanged()
    {
        var active = _settingsService.Load();
        if (ReferenceEquals(active, _profile))
            return;
        _profile = active;
        // The old selection belongs to the previous machine's list; leaving it set
        // would let the editor show (and run) a macro that is no longer loaded.
        SelectedMacro = null;
        Macros.Clear();
        Load();
    }

    public async Task RunAsync(MacroItem? macro)
    {
        if (macro is null)
            return;
        // An empty macro is not a runnable action. The panel's status line is the
        // only feedback surface an F-key press has, so don't fail silently.
        if (string.IsNullOrWhiteSpace(macro.Code))
        {
            Refuse("module.macro.empty", "This macro has no code");
            return;
        }
        // Belt-and-braces: RunCommand.CanExecute is closed during a session, but
        // Execute() bypasses it and RunAsync is public.
        if (IsJobRunning)
        {
            Refuse("module.macro.programRunning", "Stop the running program first");
            return;
        }
        // Motion or a hold: a macro line queued between blocks is unpredictable.
        if (IsMachineBusy)
        {
            Refuse("module.macro.machineBusy", "Wait for the machine to stop");
            return;
        }

        Status = string.Empty;   // the run supersedes the last refusal
        foreach (var line in macro.Code.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries))
            await _grbl.SendAsync(line.Trim());
    }

    /// <summary>
    /// Runs the macro that owns shortcut slot <paramref name="slot"/> (0 = F1) and
    /// reports a refusal in <see cref="Status"/> — an F-key press has no other
    /// feedback surface. A slot no macro owns is not claimed at all.
    /// </summary>
    public async Task<bool> RunFromShortcutAsync(int slot)
    {
        var macro = Macros.FirstOrDefault(m => m.Slot == slot);
        if (macro is null)
            return false;
        if (IsJobRunning)
        {
            Refuse("module.macro.programRunning", "Stop the running program first");
            return false;
        }
        if (IsMachineBusy)
        {
            Refuse("module.macro.machineBusy", "Wait for the machine to stop");
            return false;
        }
        if (string.IsNullOrWhiteSpace(macro.Code))
        {
            Refuse("module.macro.empty", "This macro has no code");
            return false;
        }

        await RunAsync(macro);
        return true;
    }

    /// <summary>Show why a run was refused (the panel's status line).</summary>
    private void Refuse(string key, string fallback) => Status = LocText.Resolve(key, fallback);

    public void Save()
    {
        try
        {
            _profile.ModuleSettings["Macros"] = Macros
                .Select(m => new Dictionary<string, object>
                {
                    ["Name"] = m.Name,
                    ["Code"] = m.Code,
                    ["ShortName"] = m.ShortName,
                    // The slot is part of the macro's identity: without it the F-key
                    // would be reassigned by list position after a delete/restart.
                    ["Slot"] = m.Slot,
                })
                .ToList();
            _settingsService.Save(_profile);
        }
        catch
        {
            // Best-effort persistence; settings still apply for the session.
        }
    }

    private void Load()
    {
        if (!_profile.ModuleSettings.TryGetValue("Macros", out var raw))
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

        _loading = true;
        try
        {
            foreach (var item in items)
                AddItem(item);
        }
        finally
        {
            _loading = false;
        }

        AssignLoadedSlots();
    }

    private static List<MacroItem>? ReadTyped(List<object> list) =>
        list.OfType<Dictionary<string, object>>()
            .Select(d => new MacroItem
            {
                Name = d.TryGetValue("Name", out var n) ? n?.ToString() ?? string.Empty : string.Empty,
                Code = d.TryGetValue("Code", out var c) ? c?.ToString() ?? string.Empty : string.Empty,
                // Optional and newer than the schema: older profiles simply lack it.
                ShortName = d.TryGetValue("ShortName", out var s) ? s?.ToString() ?? string.Empty : string.Empty,
                Slot = d.TryGetValue("Slot", out var sl) ? ReadSlot(sl) : -1,
                HasStoredSlot = d.ContainsKey("Slot"),
            })
            .ToList();

    private static List<MacroItem> ReadJson(JsonElement el) =>
        el.EnumerateArray()
            .Select(e => new MacroItem
            {
                Name = e.TryGetProperty("Name", out var n) ? n.GetString() ?? string.Empty : string.Empty,
                Code = e.TryGetProperty("Code", out var c) ? c.GetString() ?? string.Empty : string.Empty,
                ShortName = e.TryGetProperty("ShortName", out var s) ? s.GetString() ?? string.Empty : string.Empty,
                Slot = e.TryGetProperty("Slot", out var sl) && sl.ValueKind == JsonValueKind.Number
                    ? sl.GetInt32()
                    : -1,
                HasStoredSlot = e.TryGetProperty("Slot", out var storedSlot)
                    && storedSlot.ValueKind == JsonValueKind.Number,
            })
            .ToList();

    /// <summary>Slot value from the shared in-memory graph (Save writes an int);
    /// unknown shapes mean "no slot" and are assigned on load.</summary>
    private static int ReadSlot(object? value) => value switch
    {
        int i => i,
        long l => (int)l,
        JsonElement { ValueKind: JsonValueKind.Number } e => e.GetInt32(),
        _ => -1,
    };

    private void AddMacro()
    {
        // Name the macro after the key it will own, so the default name and the
        // toolbar label agree (a freed slot is filled first).
        var slot = NextFreeSlot();
        var macro = new MacroItem {
            Name = LocText.Format("module.macro.newMacro", "Macro {0}",
                slot >= 0 ? slot + 1 : Macros.Count + 1),
            Code = string.Empty,
        };
        AddItem(macro);
        SelectedMacro = macro;
        Save();
    }

    /// <summary>Lowest slot not owned by a macro, or -1 when all twelve are taken.</summary>
    private int NextFreeSlot()
    {
        var used = new HashSet<int>();
        foreach (var macro in Macros)
            if (macro.Slot >= 0 && macro.Slot < ShortcutCount)
                used.Add(macro.Slot);

        var slot = 0;
        while (slot < ShortcutCount && used.Contains(slot))
            slot++;
        return slot < ShortcutCount ? slot : -1;
    }

    private void AddItem(MacroItem item)
    {
        item.PropertyChanged += OnMacroItemPropertyChanged;
        Macros.Add(item);
    }

    /// <summary>
    /// Persist only the stored fields. <c>Slot</c>/<c>ShortcutKey</c> are assignment
    /// state maintained by <see cref="AssignSlot"/> / <see cref="AssignLoadedSlots"/>,
    /// not an edit: assigning keys during a bulk reload must not write the profile
    /// once per macro. One edit still costs exactly one write of the whole list.
    /// </summary>
    private void OnMacroItemPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MacroItem.Name) or nameof(MacroItem.Code) or nameof(MacroItem.ShortName))
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
