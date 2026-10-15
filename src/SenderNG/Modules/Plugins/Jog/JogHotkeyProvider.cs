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
        var info = services.GetService<CNC.Core.GrblInfoState>() ?? CNC.Core.GrblInfo.Current;
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
        // The four plane-2 keys take their label from the live kinematics: the letter
        // they jog depends on the profile's plane-2 slot binding (and on whether the
        // machine has a second plane at all), so no static catalog value can be right
        // for every machine. IDs stay frozen — saved keymaps key on them. The label
        // literal passed below is the non-live fallback and is unused for these four.
        actions.Add(Direction("jog.z.plus", "Jog plane 2 horizontal +", () => Jog().SecondRightTag, liveLabel: true));
        actions.Add(Direction("jog.z.minus", "Jog plane 2 horizontal -", () => Jog().SecondLeftTag, liveLabel: true));
        actions.Add(Direction("jog.a.plus", "Jog plane 2 vertical +", () => Jog().SecondUpTag, liveLabel: true));
        actions.Add(Direction("jog.a.minus", "Jog plane 2 vertical -", () => Jog().SecondDownTag, liveLabel: true));

        actions.Add(Direction("jog.together.left", "Jog both left", () => Jog().LeftTag, allGantries: true));
        actions.Add(Direction("jog.together.right", "Jog both right", () => Jog().RightTag, allGantries: true));
        actions.Add(Direction("jog.together.up", "Jog both up", () => Jog().UpTag, allGantries: true));
        actions.Add(Direction("jog.together.down", "Jog both down", () => Jog().DownTag, allGantries: true));

        return actions;

        IHotkeyAction Direction(string id, string label, Func<string> tag, bool allGantries = false,
                                bool liveLabel = false)
        {
            Action onKeyDown = () =>
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
            };
            Func<bool> canExecute = () =>
            {
                var grbl = Grbl();
                if (!grbl.IsConnected || !TagDrivable(tag(), info))
                    return false;
                var jog = Jog();
                if (allGantries && !jog.Plane2Visible)
                    return false;
                return !jog.IsContinuousStep || jog.ContinuousAvailable;
            };
            Action onKeyUp = () => Jog().StopContinuousJog();

            return liveLabel
                ? new LiveLabelHotkeyAction(id, () => Jog().HotkeyLabel(id), onKeyDown, canExecute, onKeyUp)
                : new CommandHotkeyAction(id, label, $"module.jog.hotkey.{id}", onKeyDown, canExecute,
                                          onKeyUp, repeatOnHold: true);
        }
    }

    /// <summary>
    /// A jog direction hotkey whose <see cref="DisplayName"/> is computed per read
    /// from the live machine (the letter the key actually jogs), instead of a catalog
    /// value that can only be correct for one plane layout. <see cref="LocaleKey"/> is
    /// null so the App Settings editor renders <see cref="DisplayName"/>; the label
    /// itself localizes through <c>LocText</c> (and never leaks a raw key).
    /// </summary>
    private sealed class LiveLabelHotkeyAction : IHotkeyAction
    {
        private readonly Func<string> _label;
        private readonly Action _onKeyDown;
        private readonly Func<bool> _canExecute;
        private readonly Action _onKeyUp;

        public LiveLabelHotkeyAction(string actionId, Func<string> label, Action onKeyDown,
                                     Func<bool> canExecute, Action onKeyUp)
        {
            ActionId = actionId;
            _label = label;
            _onKeyDown = onKeyDown;
            _canExecute = canExecute;
            _onKeyUp = onKeyUp;
        }

        public string ActionId { get; }
        public string DisplayName => _label();
        public string? LocaleKey => null;
        public HotkeyScope Scope => HotkeyScope.Global;
        public string? ModuleId => null;
        public bool RepeatOnHold => true;
        public bool CanExecute() => _canExecute();
        public void OnKeyDown() => _onKeyDown();
        public void OnKeyUp() => _onKeyUp();
    }

    private static bool TagDrivable(string tag, CNC.Core.GrblInfoState info)
    {
        var letters = info.AxisLetters;
        var count = Math.Min(info.NumAxes, letters.Length);
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