/*
 * PlotterNexOverlayHeadlessTests.cs - headless proof that the 3D view's
 * top-left overlay toggle bar and the App Settings leaf are TWO VIEWS onto ONE
 * shared PlotterNexSettingsViewModel: a flip on either surface reflects
 * immediately on the other, through the same VM and its PropertyChanged —
 * no re-attach, no attach-time disk re-read, no second settings VM.
 */

using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CNC.Core.Services;
using SenderNG.Modules.Plotter.Nex;
using SenderNG.Modules.Plotter.Nex.Settings;
using Xunit;

namespace SenderNG.Tests;

public class PlotterNexOverlayHeadlessTests
{
    [AvaloniaFact]
    public async Task Overlay_bar_and_settings_leaf_share_one_settings_vm_live()
    {
        var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            "plotter-nex-overlay-" + Guid.NewGuid().ToString("N"));
        try
        {
            var settingsService = new SettingsService(dir);
            var mill = settingsService.Load();
            mill.MachineTypeId = MachineTypes.Mill;
            settingsService.Save(mill);

            // The ONE shared VM — the exact instance the module hands both the
            // App Settings leaf and this view's overlay bar.
            var sharedVm = new PlotterNexSettingsViewModel(settingsService, null);
            var view = new PlotterNexView { OverlaySettings = sharedVm };
            var window = new Window { Width = 920, Height = 560, Content = view };
            window.Show();
            for (var i = 0; i < 4; i++)
                await Pump();

            // The overlay toggles carry Path (icon) content, not text, so they
            // are addressed in document order: Grid, Origin, Envelope, Run State
            // (see PlotterNexView.axaml overlay bar).
            var toggles = window.GetVisualDescendants()
                .OfType<ToggleButton>()
                .Where(t => t.Classes.Contains("navBtn"))
                .ToList();
            Assert.Equal(4, toggles.Count);
            var grid = toggles[0];
            var origin = toggles[1];
            var envelope = toggles[2];
            Assert.True(grid.IsChecked == true);
            Assert.True(origin.IsChecked == true);

            // Leaf flip → overlay reflects immediately on the SAME VM.
            sharedVm.ShowOrigin = false;
            sharedVm.ShowGrid = false;
            for (var i = 0; i < 2; i++)
                await Pump();
            Assert.False(origin.IsChecked == true);
            Assert.False(grid.IsChecked == true);

            // Overlay flip → the leaf-bound surface reflects on the SAME VM.
            envelope.IsChecked = false;
            for (var i = 0; i < 2; i++)
                await Pump();
            Assert.False(sharedVm.ShowEnvelope);

            // Two-way sync keeps working after the overlay-driven round trip.
            sharedVm.ShowEnvelope = true;
            for (var i = 0; i < 2; i++)
                await Pump();
            Assert.True(envelope.IsChecked == true);
        }
        finally
        {
            if (System.IO.Directory.Exists(dir))
                System.IO.Directory.Delete(dir, recursive: true);
        }
    }

    private static async Task Pump()
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(16);
        await Task.Yield();
    }
}