
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
using System.Globalization;
using CNC.Core.Abstractions;
using CNC.Core.Services;
using SenderNG.Localization;
using SenderNG.ViewModels;
using SenderNG.Core.Commands;

namespace SenderNG.Modules.Plugins;

/// <summary>Bindable goto axis row: letter + editable machine coordinate.</summary>
public sealed class GotoAxisRow : ViewModelBase
{
    private string _value = "0";

    public GotoAxisRow(string letter, string? value = null)
    {
        Letter = letter;
        if (value is not null)
            _value = value;
    }

    public string Letter { get; }
    public string Value
    {
        get => _value;
        set => SetField(ref _value, value);
    }
}

/// <summary>
/// ioSender's Goto widget: G28/G30 "goto predefined position" buttons, G28.1/G30.1
/// "record current position as reference point", and a numeric machine-coordinate
/// goto (G53 G0 X.. Y.. Z..). All actions are gated on the machine being homed —
/// grbl itself refuses them while an alarm is active, and moving to a stored
/// reference before homing is meaningless.
/// The numeric goto shows as many axes as the connected controller reports
/// (one row per axis, like the DRO and mPos read-outs).
/// </summary>
public sealed class GotoViewModel : ViewModelBase
{
    private readonly IGrblService _grbl;
    private readonly CNC.Core.GrblInfoState _info;
    private readonly IDisposable _stateSubscription;
    private bool _isHomed;
    private int _availableAxes = 3;
    private string _status = string.Empty;

    public GotoViewModel(IGrblService grbl, CNC.Core.GrblInfoState? info = null)
    {
        _grbl = grbl ?? throw new ArgumentNullException(nameof(grbl));
        _info = info ?? CNC.Core.GrblInfo.Current;

        GotoHomeCommand = new DelegateCommand(_ => _ = SendAsync("G28", LocText.Resolve("module.goto.goingHome", "Going to home position")));
        GotoG30Command = new DelegateCommand(_ => _ = SendAsync("G30", LocText.Resolve("module.goto.goingRef2", "Going to second reference point")));
        SetHomeCommand = new DelegateCommand(_ => _ = SendAsync("G28.1", LocText.Resolve("module.goto.homeRecorded", "Home position recorded")));
        SetG30Command = new DelegateCommand(_ => _ = SendAsync("G30.1", LocText.Resolve("module.goto.ref2Recorded", "Second reference point recorded")));
        GoCommand = new DelegateCommand(_ => _ = GoAsync());

        _stateSubscription = grbl.State.Subscribe(s => UiThread.Post(() => ApplyState(s)));
        RefreshAxes();
    }

    public DelegateCommand GotoHomeCommand { get; }
    public DelegateCommand GotoG30Command { get; }
    public DelegateCommand SetHomeCommand { get; }
    public DelegateCommand SetG30Command { get; }
    public DelegateCommand GoCommand { get; }

    /// <summary>True once the controller reports a completed $H; cleared on any alarm or soft reset.</summary>
    public bool IsHomed
    {
        get => _isHomed;
        private set
        {
            if (SetField(ref _isHomed, value))
                Status = value ? string.Empty : LocText.Resolve("module.goto.homeFirst", "Home the machine first ($H)");
        }
    }

    public ObservableCollection<GotoAxisRow> GotoAxes { get; } = new();

    public string Status
    {
        get => _status;
        private set => SetField(ref _status, value);
    }

    private void ApplyState(MachineState state)
    {
        if (state is null)
            return;
        IsHomed = state.IsHomed;
        var available = Math.Clamp(state.NumAxes, 1, _info.AxisLetters.Length);
        if (available != _availableAxes)
        {
            _availableAxes = available;
            RefreshAxes();
        }
    }

    /// <summary>Rebuilds the visible rows to the controller's axis count; existing
    /// row values are preserved by letter.</summary>
    private void RefreshAxes()
    {
        var count = Math.Clamp(_availableAxes, 1, _info.AxisLetters.Length);
        var existing = GotoAxes.ToDictionary(row => row.Letter, row => row.Value);
        GotoAxes.Clear();
        for (var i = 0; i < count; i++)
        {
            var letter = _info.AxisLetters[i].ToString();
            GotoAxes.Add(new GotoAxisRow(letter, existing.TryGetValue(letter, out var value) ? value : "0"));
        }
    }

    private async Task GoAsync()
    {
        if (!IsHomed)
        {
            Status = LocText.Resolve("module.goto.homeFirst", "Home the machine first ($H)");
            return;
        }

        var words = new List<string>();
        var invalid = new List<string>();
        foreach (var row in GotoAxes)
            TryAxis(words, invalid, row);

        if (invalid.Count > 0)
        {
            Status = LocText.Format("module.goto.invalidValue", "Invalid value: {0}", string.Join(", ", invalid));
            return;
        }
        if (words.Count == 0)
        {
            Status = LocText.Resolve("module.goto.enterCoordinate", "Enter at least one coordinate");
            return;
        }

        await SendAsync($"G53 G0 {string.Join(" ", words)}", LocText.Resolve("module.goto.goingPosition", "Going to position"));
    }

    private static void TryAxis(List<string> words, List<string> invalid, GotoAxisRow row)
    {
        if (string.IsNullOrWhiteSpace(row.Value))
            return;
        if (double.TryParse(row.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
            words.Add($"{row.Letter}{parsed.ToString("0.###", CultureInfo.InvariantCulture)}");
        else
            invalid.Add($"{row.Letter}=\"{row.Value}\"");
    }

    private async Task SendAsync(string command, string success)
    {
        if (!IsHomed)
        {
            Status = LocText.Resolve("module.goto.homeFirst", "Home the machine first ($H)");
            return;
        }
        try
        {
            await _grbl.SendAsync(command);
            Status = success;
        }
        catch (Exception ex)
        {
            Status = LocText.Format("module.goto.commandFailed", "{0} failed: {1}", command, ex.Message);
        }
    }
}
