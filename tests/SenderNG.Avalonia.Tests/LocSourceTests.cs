using System;
using System.Collections.Generic;
using System.Reactive.Linq;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using SenderNG.Avalonia.Localization;
using SenderNG.Core.Localization;
using Xunit;

namespace SenderNG.Avalonia.Tests;

public class LocSourceTests
{
    [AvaloniaFact]
    public async Task HotKey_emits_initial_then_reemits_on_locale_change()
    {
        var prev = LocalizationService.Current;
        try
        {
            LocalizationService.Current = HeadlessTestCatalog.Locale;
            var emitted = new List<string>();
            using var sub = LocSource.HotKey("grblSettings.title").Subscribe(emitted.Add);
            await Pump();
            Assert.Equal("GRBL settings", emitted[0]);

            if (HeadlessTestCatalog.Locale.AvailableLocales.Contains("ru"))
            {
                try
                {
                    HeadlessTestCatalog.Locale.SetLocale("ru");
                    await Pump();
                    Assert.Equal("Настройки GRBL", emitted[^1]);
                }
                finally
                {
                    HeadlessTestCatalog.Locale.SetLocale("en");
                    await Pump();
                }
            }
        }
        finally
        {
            LocalizationService.Current = prev;
        }
    }

    private static async Task Pump()
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(16);
        await Task.Yield();
    }
}