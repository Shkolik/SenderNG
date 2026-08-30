using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CNC.Core.Abstractions;
using CNC.Core.Services;
using SenderNG.Avalonia.Modules.Fixed;
using SenderNG.Core.Localization;
using SenderNG.Core.Settings;
using Xunit;

namespace SenderNG.Avalonia.Tests;

public class LocExtensionHeadlessTests
{
    private sealed class StubProvider : IGrblSettingsProvider
    {
        public IReadOnlyList<string> ValueLines { get; init; } = [];
        public Task<IReadOnlyList<string>> GetSettingLinesAsync() => Task.FromResult(ValueLines);
        public Task<IReadOnlyList<string>> GetSettingDetailLinesAsync() => Task.FromResult<IReadOnlyList<string>>([]);
        public Task<IReadOnlyList<string>> GetSettingGroupLinesAsync() => Task.FromResult<IReadOnlyList<string>>([]);
        public Task<string?> GetSettingDescriptionAsync(int id) => Task.FromResult<string?>(null);
    }

    [AvaloniaFact]
    public async Task Loc_extension_resolves_and_relocalizes_in_a_view()
    {
        var prev = LocalizationService.Current;
        try
        {
            LocalizationService.Current = HeadlessTestCatalog.Locale;
            var provider = new StubProvider();
            var grbl = new GrblServiceProxy(
                new DemoGrblService(new AppSettings(), localization: HeadlessTestCatalog.Locale));
            var vm = new GrblSettingsViewModel(provider, grbl, HeadlessTestCatalog.Locale);
            vm.LoadAsync().GetAwaiter().GetResult();

            var view = new GrblSettingsView { DataContext = vm };
            var window = new Window { Width = 920, Height = 560, Content = view };
            window.Show();
            for (var i = 0; i < 4; i++)
                await Pump();

            var search = window.GetVisualDescendants().OfType<TextBox>()
                .First(t => t.PlaceholderText is not null);
            Assert.Equal("Search settings...", search.PlaceholderText);

            if (HeadlessTestCatalog.Locale.AvailableLocales.Contains("ru"))
            {
                try
                {
                    HeadlessTestCatalog.Locale.SetLocale("ru");
                    for (var i = 0; i < 4; i++)
                        await Pump();
                    Assert.Equal("Поиск настроек...", search.PlaceholderText);
                }
                finally
                {
                    HeadlessTestCatalog.Locale.SetLocale("en");
                    for (var i = 0; i < 4; i++)
                        await Pump();
                }
            }

            window.Close();
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