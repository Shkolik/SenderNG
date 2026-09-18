/*

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

using CNC.Core.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using SenderNG.Core.Modules;

namespace SenderNG.Modules.Fixed;

/// <summary>Builds the DRO module's per-axis zero hotkeys, gated on the controller
/// actually reporting the axis.</summary>
internal static class DroHotkeyProvider
{
    private static readonly (string Letter, string AxisId)[] Axes =
    [
        ("X", "x"), ("Y", "y"), ("Z", "z"), ("A", "a"), ("B", "b"), ("C", "c"),
    ];

    public static IReadOnlyList<IHotkeyAction> Create(IServiceProvider services)
    {
        DroViewModel Dro() => services.GetRequiredService<DroViewModel>();
        IGrblService Grbl() => services.GetRequiredService<IGrblService>();
        var actions = new List<IHotkeyAction>();

        foreach (var (letter, axisId) in Axes)
        {
            actions.Add(new CommandHotkeyAction(
                $"dro.zero.{axisId}", $"Zero {letter}", $"module.dro.hotkey.dro.zero.{axisId}",
                () => Dro().ZeroAxisCommand.Execute(letter),
                () => Grbl().IsConnected && AxisPresent(letter, Grbl())));
        }

        actions.Add(new CommandHotkeyAction(
            "dro.zero.all", "Zero all", "module.dro.hotkey.dro.zero.all",
            () => Dro().ZeroAllCommand.Execute(null),
            () => Grbl().IsConnected && Dro().Axes.Count > 0));

        return actions;
    }

    private static bool AxisPresent(string letter, IGrblService grbl)
    {
        var count = grbl.Capabilities.AxisLetters.Length > 0
            ? grbl.Capabilities.AxisLetters.Length
            : grbl.Capabilities.NumAxes;
        var index = "XYZABC".IndexOf(letter);
        return index >= 0 && index < count;
    }
}