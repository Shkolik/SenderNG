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

using CNC.Core.Services;
using SenderNG.Core.Modules;
using SenderNG.Core.Services;

namespace SenderNG.Core.Tests;

public class KeymapControllerTests
{
    private static SettingsService MakeSettings()
        => new(Path.Combine(Path.GetTempPath(), "senderng-keymap-" + Guid.NewGuid().ToString("N")));

    private static (KeymapController Controller, HotkeyService Service) Make(params IHotkeyAction[] actions)
    {
        var service = new HotkeyService(MakeSettings());
        foreach (var action in actions)
            service.RegisterAction(action, action.ActionId switch
            {
                "cycle.hold" => "Space",
                _ => "Ctrl+A",
            });
        return (new KeymapController(service), service);
    }

    private static readonly Func<string, bool> NoFocus = _ => false;

    [Fact]
    public void KeyDown_dispatches_and_marks_handled()
    {
        var down = 0;
        var action = new CommandHotkeyAction("a", "A", null, () => down++);
        var (controller, _) = Make(action);

        Assert.True(controller.KeyDown("Ctrl+A", false, NoFocus));
        Assert.Equal(1, down);
        Assert.Equal(1, controller.PressedCount);
    }

    [Fact]
    public void KeyDown_unknown_gesture_and_disabled_action_pass_through()
    {
        var action = new CommandHotkeyAction("a", "A", null, () => { }, () => false);
        var (controller, _) = Make(action);

        Assert.False(controller.KeyDown("Ctrl+Q", false, NoFocus));
        Assert.False(controller.KeyDown("Ctrl+A", false, NoFocus));
        Assert.Equal(0, controller.PressedCount);
    }

    [Fact]
    public void AutoRepeat_only_redispatches_when_repeat_on_hold()
    {
        var repeating = 0;
        var once = 0;
        var repeatAction = new CommandHotkeyAction("r", "R", null, () => repeating++, repeatOnHold: true);
        var onceAction = new CommandHotkeyAction("o", "O", null, () => once++);
        var service = new HotkeyService(MakeSettings());
        service.RegisterAction(repeatAction, "Ctrl+A");
        service.RegisterAction(onceAction, "Ctrl+B");
        var controller = new KeymapController(service);

        controller.KeyDown("Ctrl+A", false, NoFocus);
        controller.KeyDown("Ctrl+A", false, NoFocus);
        controller.KeyDown("Ctrl+B", false, NoFocus);
        controller.KeyDown("Ctrl+B", false, NoFocus);

        Assert.Equal(2, repeating);
        Assert.Equal(1, once);
    }

    [Fact]
    public void KeyUp_fires_once_for_the_pressed_action()
    {
        var up = 0;
        var action = new CommandHotkeyAction("a", "A", null, () => { }, onKeyUp: () => up++);
        var (controller, _) = Make(action);

        controller.KeyDown("Ctrl+A", false, NoFocus);
        Assert.True(controller.KeyUp("Ctrl+A"));
        Assert.Equal(1, up);
        Assert.False(controller.KeyUp("Ctrl+A"));
    }

    [Fact]
    public void KeyUp_after_module_loses_focus_still_releases_original_action()
    {
        var up = 0;
        var action = new CommandHotkeyAction("probe.stop", "Stop", null, () => { }, onKeyUp: () => up++,
            scope: HotkeyScope.Module, moduleId: "probing");
        var (controller, _) = Make(action);

        controller.KeyDown("Ctrl+A", false, _ => true);
        Assert.True(controller.KeyUp("Ctrl+A"));
        Assert.Equal(1, up);
    }

    [Fact]
    public void Text_focus_suppresses_bare_gestures_only()
    {
        var bare = 0;
        var chord = 0;
        var space = new CommandHotkeyAction("cycle.hold", "Hold", null, () => bare++);
        var chorded = new CommandHotkeyAction("c", "C", null, () => chord++);
        var service = new HotkeyService(MakeSettings());
        service.RegisterAction(space, "Space");
        service.RegisterAction(chorded, "Ctrl+C");
        var controller = new KeymapController(service);

        Assert.False(controller.KeyDown("Space", true, NoFocus));
        Assert.Equal(0, bare);
        Assert.True(controller.KeyDown("Ctrl+C", true, NoFocus));
        Assert.Equal(1, chord);
    }

    [Fact]
    public void WindowDeactivated_releases_every_pressed_action()
    {
        var up = 0;
        var action = new CommandHotkeyAction("a", "A", null, () => { }, onKeyUp: () => up++);
        var (controller, _) = Make(action);

        controller.KeyDown("Ctrl+A", false, NoFocus);
        controller.WindowDeactivated();

        Assert.Equal(1, up);
        Assert.Equal(0, controller.PressedCount);
    }

    [Fact]
    public void Throwing_action_is_reported_not_propagated()
    {
        Exception? captured = null;
        var action = new CommandHotkeyAction("a", "A", null, () => throw new InvalidOperationException("boom"));
        var service = new HotkeyService(MakeSettings());
        service.RegisterAction(action, "Ctrl+A");
        var controller = new KeymapController(service, ex => captured = ex);

        Assert.True(controller.KeyDown("Ctrl+A", false, NoFocus));
        Assert.IsType<InvalidOperationException>(captured);
    }
}