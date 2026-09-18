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

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using CNC.Core.Services;
using SenderNG.Core.Modules;
using SenderNG.Core.Services;
using Xunit;

namespace SenderNG.Tests;

public class KeymapDispatcherHeadlessTests
{
    private int _down;
    private int _up;

    private HotkeyService MakeService()
    {
        var dir = Path.Combine(Path.GetTempPath(), "senderng-dispatch-" + Guid.NewGuid().ToString("N"));
        var service = new HotkeyService(new SettingsService(dir));
        service.RegisterAction(
            new CommandHotkeyAction("w", "W", null, () => _down++, onKeyUp: () => _up++),
            "Ctrl+W");
        return service;
    }

    [AvaloniaFact]
    public void Dispatcher_routes_keydown_and_keyup_for_modifier_chord()
    {
        var service = MakeService();
        var window = new Window();
        window.Show();
        using var dispatcher = new KeymapDispatcher(window, new KeymapController(service), _ => null);
        window.Content = new TextBox { DataContext = null };
        window.Focus();

        window.KeyPressQwerty(PhysicalKey.W, RawInputModifiers.Control);
        window.KeyReleaseQwerty(PhysicalKey.W, RawInputModifiers.Control);

        Assert.Equal(1, _down);
        Assert.Equal(1, _up);
    }

    [AvaloniaFact]
    public void Dispatcher_skips_a_focused_text_box_for_bare_gestures_but_not_chords()
    {
        var bare = 0;
        var dir = Path.Combine(Path.GetTempPath(), "senderng-dispatch-" + Guid.NewGuid().ToString("N"));
        var service = new HotkeyService(new SettingsService(dir));
        service.RegisterAction(new CommandHotkeyAction("space", "Space", null, () => bare++), "Space");
        service.RegisterAction(new CommandHotkeyAction("w", "W", null, () => _down++), "Ctrl+W");

        var window = new Window();
        window.Show();
        using var dispatcher = new KeymapDispatcher(window, new KeymapController(service), _ => null);
        var box = new TextBox();
        window.Content = box;
        box.Focus();

        window.KeyPressQwerty(PhysicalKey.Space, RawInputModifiers.None);
        window.KeyPressQwerty(PhysicalKey.W, RawInputModifiers.Control);

        Assert.Equal(0, bare);
        Assert.Equal(1, _down);
    }
}