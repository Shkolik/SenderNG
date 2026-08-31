
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
using CNC.Core.Abstractions;
using CNC.Core.Services;
using SenderNG.ViewModels;

namespace SenderNG.Modules.Plugins;

public sealed class MposAxis : ViewModelBase
{
    private double _position;

    public MposAxis(string letter) => Letter = letter;

    public string Letter { get; }

    public double Position
    {
        get => _position;
        set => SetField(ref _position, value);
    }
}

/// <summary>
/// Machine coordinate (MPos) readout widget. Read-only — machine coordinates
/// cannot be zeroed with G92 (that only shifts the work offset); use the
/// Offsets module to adjust the work coordinate system instead.
/// </summary>
public class MposViewModel : ViewModelBase
{
    private static readonly string[] Letters = ["X", "Y", "Z", "A", "B", "C", "U", "V", "W"];

    private readonly IGrblService _grbl;

    public ObservableCollection<MposAxis> Axes { get; } = new();

    public MposViewModel(IGrblService grbl)
    {
        ArgumentNullException.ThrowIfNull(grbl);
        _grbl = grbl;
        grbl.State.Subscribe(s => UiThread.Post(() => ApplyState(s)));
    }

    private void ApplyState(MachineState state)
    {
        if (state is null)
            return;

        while (Axes.Count > state.NumAxes)
            Axes.RemoveAt(Axes.Count - 1);

        while (Axes.Count < state.NumAxes && Axes.Count < Letters.Length)
            Axes.Add(new MposAxis(Letters[Axes.Count]));

        var machine = state.MachinePositions;
        var count = Math.Min(Axes.Count, machine?.Length ?? 0);
        for (var i = 0; i < count; i++)
            Axes[i].Position = machine![i];
    }
}
