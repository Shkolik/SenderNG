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
using CNC.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using SenderNG.Core.Modules;

namespace SenderNG.Modules.Plugins;

/// <summary>Builds the Jog module's hotkey actions, resolving the shared
/// <see cref="JogViewModel"/> lazily so startup never forces the widget to build.</summary>
internal static class JogHotkeyProvider
{
    public static IReadOnlyList<IHotkeyAction> Create(IServiceProvider services)
    {
        JogViewModel Jog() => services.GetRequiredService<JogViewModel>();
        IGrblService Grbl() => services.GetRequiredService<IGrblService>();
        var actions = new List<IHotkeyAction>();

        for (var i = 0; i < 4; i++)
        {
            var slot = i;
            actions.Add(new CommandHotkeyAction(
                $"jog.step.{slot}", $"Step {slot + 1}", $"module.jog.hotkey.jog.step.{slot}",
                () => Jog().SelectedDistanceIndex = slot));
        }

        for (var i = 0; i < 3; i++)
        {
            var slot = i;
            actions.Add(new CommandHotkeyAction(
                $"jog.feed.{slot}", $"Feed {slot + 1}", $"module.jog.hotkey.jog.feed.{slot}",
                () => Jog().SelectedSpeedIndex = slot));
        }

        actions.Add(new CommandHotkeyAction("jog.step.inc", "Step +", "module.jog.hotkey.jog.step.inc",
            () => { var j = Jog(); j.SelectedDistanceIndex = j.SelectedDistanceIndex + 1; }));
        actions.Add(new CommandHotkeyAction("jog.step.dec", "Step -", "module.jog.hotkey.jog.step.dec",
            () => { var j = Jog(); j.SelectedDistanceIndex = j.SelectedDistanceIndex - 1; }));
        actions.Add(new CommandHotkeyAction("jog.feed.inc", "Feed +", "module.jog.hotkey.jog.feed.inc",
            () => { var j = Jog(); j.SelectedSpeedIndex = j.SelectedSpeedIndex + 1; }));
        actions.Add(new CommandHotkeyAction("jog.feed.dec", "Feed -", "module.jog.hotkey.jog.feed.dec",
            () => { var j = Jog(); j.SelectedSpeedIndex = j.SelectedSpeedIndex - 1; }));

        actions.Add(Direction("jog.left", "Jog left", () => Jog().LeftTag));
        actions.Add(Direction("jog.right", "Jog right", () => Jog().RightTag));
        actions.Add(Direction("jog.up", "Jog up", () => Jog().UpTag));
        actions.Add(Direction("jog.down", "Jog down", () => Jog().DownTag));
        actions.Add(Direction("jog.z.plus", "Jog Z+", () => Jog().SecondRightTag));
        actions.Add(Direction("jog.z.minus", "Jog Z-", () => Jog().SecondLeftTag));
        actions.Add(Direction("jog.a.plus", "Jog A+", () => Jog().SecondUpTag));
        actions.Add(Direction("jog.a.minus", "Jog A-", () => Jog().SecondDownTag));

        actions.Add(Direction("jog.together.left", "Jog both left", () => Jog().LeftTag, allGantries: true));
        actions.Add(Direction("jog.together.right", "Jog both right", () => Jog().RightTag, allGantries: true));
        actions.Add(Direction("jog.together.up", "Jog both up", () => Jog().UpTag, allGantries: true));
        actions.Add(Direction("jog.together.down", "Jog both down", () => Jog().DownTag, allGantries: true));

        return actions;

        IHotkeyAction Direction(string id, string label, Func<string> tag, bool allGantries = false)
            => new CommandHotkeyAction(
                id, label, $"module.jog.hotkey.{id}",
                onKeyDown: () =>
                {
                    var jog = Jog();
                    var current = tag();
                    if (allGantries)
                    {
                        if (jog.IsContinuousStep)
                            jog.StartContinuousJog(current, allGantries: true);
                        else
                            jog.JogTogether(current);
                    }
                    else
                    {
                        if (jog.IsContinuousStep)
                            jog.StartContinuousJog(current);
                        else
                            jog.JogCommand.Execute(current);
                    }
                },
                canExecute: () =>
                {
                    var grbl = Grbl();
                    if (!grbl.IsConnected || !TagDrivable(tag()))
                        return false;
                    var jog = Jog();
                    if (allGantries && !jog.Plane2Visible)
                        return false;
                    return !jog.IsContinuousStep || jog.ContinuousAvailable;
                },
                onKeyUp: () => Jog().StopContinuousJog(),
                repeatOnHold: true);
    }

    private static bool TagDrivable(string tag)
    {
        var letters = CNC.Core.GrblInfo.AxisLetters;
        var count = Math.Min(CNC.Core.GrblInfo.NumAxes, letters.Length);
        foreach (var raw in tag.Split([',', '|'], StringSplitOptions.RemoveEmptyEntries))
        {
            var token = raw.Trim();
            var index = token.Length > 0 ? letters.IndexOf(token[0]) : -1;
            if (index < 0 || index >= count)
                return false;
        }
        return true;
    }
}