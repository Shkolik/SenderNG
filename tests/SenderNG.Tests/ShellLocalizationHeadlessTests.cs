
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
using CNC.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using SenderNG;
using SenderNG.Core.Localization;
using Xunit;

namespace SenderNG.Tests;

public class ShellLocalizationHeadlessTests
{
    [AvaloniaFact]
    public async Task Shell_localizes_live_on_locale_switch()
    {
        var prev = LocalizationService.Current;
        try
        {
            var locale = HeadlessTestCatalog.Locale;
            LocalizationService.Current = locale;

            var window = BuildMainWindow(locale);
            for (var i = 0; i < 4; i++) await Pump();
            var englishTitle = window.Title;

            // A static {loc:Loc} string (State-area Home button) and the VM-computed title.
            var homeButton = window.GetVisualDescendants().OfType<Button>()
                .First(b => b.Content is string s && s == "Home");
            Assert.Equal("Home", homeButton.Content);
            Assert.Contains("SenderNG", englishTitle);

            if (locale.AvailableLocales.Contains("ru"))
            {
                try
                {
                    locale.SetLocale("ru");
                    for (var i = 0; i < 4; i++) await Pump();
                    Assert.Equal("Домой", homeButton.Content);
                    Assert.NotEqual(englishTitle, window.Title);
                }
                finally
                {
                    locale.SetLocale("en");
                    for (var i = 0; i < 4; i++) await Pump();
                }
            }

            window.Close();
        }
        finally
        {
            LocalizationService.Current = prev;
        }
    }

    private static Window BuildMainWindow(LocalizationService locale)
    {
        var dir = Path.Combine(Path.GetTempPath(), "senderng-headless-" + Guid.NewGuid().ToString("N"));
        var settings = new SettingsService(dir);
        var services = new ServiceCollection();
        services.AddSingleton(settings);
        services.AddSingleton(locale);
        services.AddSingleton(_ => new DemoGrblService(settings.Load(), localization: locale));
        services.AddSingleton<RealGrblService>();
        var provider = services.BuildServiceProvider();

        var vm = new SenderNG.ViewModels.MainViewModel(
            provider.GetRequiredService<DemoGrblService>(),
            settings,
            provider);        var window = new MainWindow { DataContext = vm };
        window.Show();
        return window;
    }

    /// <summary>Drain the dispatcher so locale-change notifications and re-renders land.</summary>
    private static async Task Pump()
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(16);
        await Task.Yield();
    }
}
