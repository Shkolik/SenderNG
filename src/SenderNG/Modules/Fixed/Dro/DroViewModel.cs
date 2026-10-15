/*
 * DroViewModel.cs - DRO (digital readout) view model
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
using System.Windows.Input;
using CNC.Core.Abstractions;
using CNC.Core.Services;
using SenderNG.Core.Localization;
using SenderNG.Core.Services;
using SenderNG.Localization;
using SenderNG.ViewModels;
using SenderNG.Core.Commands;

namespace SenderNG.Modules.Fixed;

public sealed class AxisPosition : ViewModelBase
{
    private double _position;

    public AxisPosition(string letter, bool isRotary = false)
    {
        Letter = letter;
        IsRotary = isRotary;
    }

    public string Letter { get; }

    /// <summary>True when the profile declares this axis rotary. A physical axis-kind
    /// fact (never "index ≥ 3"), so the row can read in degrees.</summary>
    public bool IsRotary { get; }

    /// <summary>Read-out unit: degrees for a rotary axis, blank for a linear one —
    /// the compact rows stay uncluttered in the ordinary millimetre case.</summary>
    public string Unit => IsRotary ? "°" : string.Empty;

    public double Position
    {
        get => _position;
        set => SetField(ref _position, value);
    }

    /// <summary>Zero-button tooltip: "Zero axis" plus the bound gesture for the
    /// matching <c>dro.zero.</c> action (none → no brackets).</summary>
    public string Tip
    {
        get
        {
            var gesture = AppHotkeys.Current?.GestureFor($"dro.zero.{Letter.ToLowerInvariant()}");
            return HotkeyTipSource.Compose(LocText.Resolve("module.dro.zeroAxis", "Zero axis"), gesture);
        }
    }

    public void RefreshTip() => OnPropertyChanged(nameof(Tip));
}

public class DroViewModel : ViewModelBase
{
    private readonly IGrblService _grbl;
    private readonly IMachineKinematicsProvider? _kinematics;
    private string _status = string.Empty;
    private HotkeyService? _subscribedHotkeys;
    /// <summary>Signature of the undeclared-axes warning already surfaced, so a
    /// continuously refreshed read-out warns once per resolution instead of once per
    /// status poll (invariant 10).</summary>
    private string? _warnedUndeclaredAxes;

    public ObservableCollection<AxisPosition> Axes { get; } = new();

    public DroViewModel(IGrblService grbl, IMachineKinematicsProvider? kinematics = null)
    {
        ArgumentNullException.ThrowIfNull(grbl);
        _grbl = grbl;
        _kinematics = kinematics;
        grbl.State.Subscribe(s => UiThread.Post(() => ApplyState(s)));

        ZeroAxisCommand = new DelegateCommand(p => _ = ZeroAxisAsync(p as string));
        ZeroAllCommand = new DelegateCommand(_ => _ = ZeroAllAsync());

        if (LocalizationService.Current is { } loc)
            loc.Changed += OnLocaleChanged;
        AppHotkeys.Changed += OnAppHotkeysChanged;
        SubscribeToHotkeys(AppHotkeys.Current);
    }

    public ICommand ZeroAxisCommand { get; }

    public ICommand ZeroAllCommand { get; }

    public string Status
    {
        get => _status;
        private set => SetField(ref _status, value);
    }

    private async Task ZeroAxisAsync(string? letter)
    {
        if (string.IsNullOrEmpty(letter))
            return;
        try
        {
            // G10 L20 P0: make the current position read 0 in the ACTIVE work
            // coordinate system and persist it (iSender parity). Unlike G92 the
            // change is stored on the controller and shows up in Work Parameters.
            await _grbl.SendAsync($"G10 L20 P0 {letter}0");
            await RefreshWorkParametersAsync();
            Status = LocText.Format("module.dro.zeroedAxis", "{0} zeroed", letter);
        }
        catch (Exception ex)
        {
            Status = LocText.Format("module.dro.zeroAxisFailed", "Zero {0} failed: {1}", letter, ex.Message);
        }
    }

    private async Task ZeroAllAsync()
    {
        if (Axes.Count == 0)
            return;
        var words = string.Join(" ", Axes.Select(a => $"{a.Letter}0"));
        try
        {
            // G10 L20 P0: make the current position read 0 in the ACTIVE work
            // coordinate system and persist it (iSender parity) — G92 only left a
            // temporary shift that vanished on reset and never touched the offsets.
            await _grbl.SendAsync($"G90 G10 L20 P0 {words}");
            await RefreshWorkParametersAsync();
            Status = LocText.Resolve("module.dro.zeroed", "All axes zeroed");
        }
        catch (Exception ex)
        {
            Status = LocText.Format("module.dro.zeroAllFailed", "Zero all failed: {0}", ex.Message);
        }
    }

    private void RefreshTips()
    {
        foreach (var axis in Axes)
            axis.RefreshTip();
    }

    private void OnLocaleChanged(object? sender, EventArgs e) => UiThread.Post(RefreshTips);
    private void OnHotkeysChanged(object? sender, EventArgs e) => UiThread.Post(RefreshTips);
    /// <summary>Owns the single <see cref="HotkeyService.Changed"/> subscription, moving it
    /// to <paramref name="service"/> and releasing the previous one. The reference check
    /// makes the subscription idempotent, so no duplicate can accumulate.</summary>
    private void SubscribeToHotkeys(HotkeyService? service)
    {
        if (ReferenceEquals(_subscribedHotkeys, service))
            return;
        if (_subscribedHotkeys is not null)
            _subscribedHotkeys.Changed -= OnHotkeysChanged;
        _subscribedHotkeys = service;
        if (service is not null)
            service.Changed += OnHotkeysChanged;
    }

    private void OnAppHotkeysChanged(object? sender, EventArgs e)
    {
        SubscribeToHotkeys(AppHotkeys.Current);
        RefreshTips();
    }

    /// <summary>Pulls a $# re-read so the Work Parameters table reflects the
    /// zero immediately; non-fatal (the demo is already live; a failed read keeps
    /// the last snapshot).</summary>
    private async Task RefreshWorkParametersAsync()
    {
        try
        {
            await _grbl.RefreshWorkParametersAsync();
        }
        catch
        {
            // Non-fatal.
        }
    }

    private void ApplyState(MachineState state)
    {
        if (state is null)
            return;

        // One row per axis the CONTROLLER reports (invariant 10: an axis the profile
        // does not declare is still shown), labelled with the board's own letter and
        // marked rotary from the resolved axis kind — not from the index.
        var kinematics = _kinematics?.Current;
        var letters = kinematics?.BoardLetters ?? MachineType.ConventionalLetters;
        var count = Math.Min(state.NumAxes, letters.Length);
        if (!RowsMatch(letters, count, kinematics))
            RebuildRows(letters, count, kinematics);

        var positions = Math.Min(Axes.Count, state.Positions.Length);
        for (var i = 0; i < positions; i++)
            Axes[i].Position = state.Positions[i];

        WarnUndeclaredAxesOnce(kinematics);
    }

    private bool RowsMatch(string letters, int count, MachineKinematics? kinematics)
    {
        if (Axes.Count != count)
            return false;
        for (var i = 0; i < count; i++)
        {
            if (Axes[i].Letter != letters[i].ToString())
                return false;
            if (Axes[i].IsRotary != IsRotary(kinematics, i))
                return false;
        }
        return true;
    }

    private void RebuildRows(string letters, int count, MachineKinematics? kinematics)
    {
        Axes.Clear();
        for (var i = 0; i < count; i++)
            Axes.Add(new AxisPosition(letters[i].ToString(), IsRotary(kinematics, i)));
    }

    private static bool IsRotary(MachineKinematics? kinematics, int index) =>
        kinematics is not null && kinematics.RotaryAxes.Contains(index);

    /// <summary>
    /// Surfaces the resolver's superset-tolerance warning (invariant 10) once per
    /// resolution. The controller may report axes the profile never declared: they are
    /// shown as unknown linear axes above, and the operator hears about it exactly
    /// once — not on every status report this view consumes.
    /// </summary>
    private void WarnUndeclaredAxesOnce(MachineKinematics? kinematics)
    {
        var warning = kinematics?.Problems.FirstOrDefault(p =>
            p.Severity == KinematicsSeverity.Warning &&
            p.Code == KinematicsProblemCodes.ControllerUndeclaredAxes);
        if (warning is null)
        {
            _warnedUndeclaredAxes = null;
            return;
        }

        var signature = $"{kinematics!.ReportedAxes}|{warning.Message}";
        if (signature == _warnedUndeclaredAxes)
            return;
        _warnedUndeclaredAxes = signature;
        Status = warning.Message;
    }
}
