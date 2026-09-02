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
using SenderNG.Localization;
using SenderNG.ViewModels;
using SenderNG.Core.Commands;

namespace SenderNG.Modules.Fixed;

public sealed class AxisPosition : ViewModelBase
{
    private double _position;

    public AxisPosition(string letter) => Letter = letter;

    public string Letter { get; }

    public double Position
    {
        get => _position;
        set => SetField(ref _position, value);
    }
}

public class DroViewModel : ViewModelBase
{
    private static readonly string[] Letters = ["X", "Y", "Z", "A", "B", "C", "U", "V", "W"];

    private readonly IGrblService _grbl;
    private string _status = string.Empty;

    public ObservableCollection<AxisPosition> Axes { get; } = new();

    public DroViewModel(IGrblService grbl)
    {
        ArgumentNullException.ThrowIfNull(grbl);
        _grbl = grbl;
        grbl.State.Subscribe(s => UiThread.Post(() => ApplyState(s)));

        ZeroAxisCommand = new DelegateCommand(p => _ = ZeroAxisAsync(p as string));
        ZeroAllCommand = new DelegateCommand(_ => _ = ZeroAllAsync());
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

        while (Axes.Count > state.NumAxes)
            Axes.RemoveAt(Axes.Count - 1);

        while (Axes.Count < state.NumAxes && Axes.Count < Letters.Length)
            Axes.Add(new AxisPosition(Letters[Axes.Count]));

        var count = Math.Min(Axes.Count, state.Positions.Length);
        for (var i = 0; i < count; i++)
            Axes[i].Position = state.Positions[i];
    }
}
