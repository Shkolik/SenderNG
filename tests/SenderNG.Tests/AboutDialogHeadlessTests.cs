
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

using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SenderNG.ViewModels;
using SenderNG.Views;
using SenderNG.Core.Localization;
using Xunit;

namespace SenderNG.Tests;

public class AboutDialogHeadlessTests
{
    [AvaloniaFact]
    public async Task About_dialog_shows_localized_app_info_and_libraries()
    {
        var prev = LocalizationService.Current;
        try
        {
            LocalizationService.Current = HeadlessTestCatalog.Locale;
            var vm = new AboutViewModel();
            var window = new AboutView { DataContext = vm };
            window.Show();
            for (var i = 0; i < 4; i++) await Pump();

            Assert.Equal("SenderNG", vm.AppName);
            Assert.StartsWith("Version: ", vm.Version);
            Assert.NotEmpty(vm.Libraries);
            Assert.Contains(vm.Libraries, l => l.Name == "Avalonia");

            // Both tabs render.
            var tabs = window.GetVisualDescendants().OfType<TabControl>().First();
            Assert.Equal(2, tabs.Items.Count);
            window.Close();
        }
        finally
        {
            LocalizationService.Current = prev;
        }
    }

    /// <summary>Drain the dispatcher so the window realizes and bindings settle.</summary>
    private static async Task Pump()
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(16);
        await Task.Yield();
    }
}
