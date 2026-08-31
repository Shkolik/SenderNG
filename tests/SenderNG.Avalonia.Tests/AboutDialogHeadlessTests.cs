using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SenderNG.Avalonia.ViewModels;
using SenderNG.Avalonia.Views;
using SenderNG.Core.Localization;
using Xunit;

namespace SenderNG.Avalonia.Tests;

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
