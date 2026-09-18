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
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using CNC.Core.Services;
using SenderNG.Core.Localization;
using SenderNG.Core.Modules;
using SenderNG.Core.Services;
using SenderNG.Localization;
using Xunit;

namespace SenderNG.Tests;

public class HotkeyTipHeadlessTests
{
    private static HotkeyService MakeHotkeys()
    {
        var dir = Path.Combine(Path.GetTempPath(), "senderng-tooltip-" + Guid.NewGuid().ToString("N"));
        var service = new HotkeyService(new SettingsService(dir));
        service.RegisterAction(new CommandHotkeyAction("cycle.start", "Cycle start", null, () => { }), "Alt+R");
        return service;
    }

    [AvaloniaFact]
    public void Tooltip_shows_gesture_and_tracks_remap()
    {
        var previousLoc = LocalizationService.Current;
        var previousHotkeys = AppHotkeys.Current;
        try
        {
            LocalizationService.Current =
                LocalizationService.LoadFromDirectory(Path.Combine(AppContext.BaseDirectory, "Locale"));
            var service = MakeHotkeys();
            AppHotkeys.Current = service;

            var text = new TextBlock();
            text.Bind(TextBlock.TextProperty,
                new HotkeyTipExtension("app.cycle.startTip", "cycle.start").ProvideValue(null));
            var window = new Window { Content = text };
            window.Show();
            Dispatcher.UIThread.RunJobs();

            Assert.Equal("Cycle Start [Alt+R]", text.Text);

            service.SetGesture("cycle.start", "Ctrl+Shift+K");
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("Cycle Start [Ctrl+Shift+K]", text.Text);

            service.ClearGesture("cycle.start");
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("Cycle Start", text.Text);
        }
        finally
        {
            LocalizationService.Current = previousLoc;
            AppHotkeys.Current = previousHotkeys;
        }
    }
}