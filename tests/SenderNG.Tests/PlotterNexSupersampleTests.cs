/*
 * PlotterNexSupersampleTests.cs - 2x supersample readback: the Supersample
 * setting default + persisted VM round-trip, and the panel/render size
 * decoupling contract (navigation px stay at panel size, the render target
 * scales by 2 so a 1px line becomes 2px of SMAA footprint).
 */

using CNC.Core.Services;
using SenderNG.Modules.Plotter.Nex.Rendering;
using SenderNG.Modules.Plotter.Nex.Settings;
using Xunit;

namespace SenderNG.Tests;

public class PlotterNexSupersampleTests
{
    [Fact]
    public void Supersample_is_on_by_default_and_round_trips_through_the_vm()
    {
        var dir = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "plotter-nex-supersample-" + Guid.NewGuid().ToString("N"));
        try
        {
            var settingsService = new SettingsService(dir);
            var mill = settingsService.Load();
            mill.MachineTypeId = MachineTypes.Mill;
            settingsService.Save(mill);

            var vm = new PlotterNexSettingsViewModel(settingsService, null);
            Assert.True(vm.Supersample, "supersample should default to ON (mainstream machines)");
            vm.Supersample = false;
            Assert.False(vm.Supersample);

            var reloaded = new PlotterNexSettingsViewModel(settingsService, null);
            Assert.False(reloaded.Supersample, "flip must persist through the profile save");
        }
        finally
        {
            if (System.IO.Directory.Exists(dir))
                System.IO.Directory.Delete(dir, recursive: true);
        }
    }

    [Theory]
    [InlineData(1293, 738, true, 1293u, 738u, 2586u, 1476u)]
    [InlineData(1293, 738, false, 1293u, 738u, 1293u, 738u)]
    [InlineData(1, 1, true, 1u, 1u, 2u, 2u)]
    [InlineData(7, 3, false, 7u, 3u, 7u, 3u)]
    public void RenderSize_keeps_panel_px_for_navigation_and_scales_only_the_rt(
        int panelW, int panelH, bool supersample,
        uint expPanelW, uint expPanelH, uint expRtW, uint expRtH)
    {
        var r = RenderSizing.Resolve((uint)panelW, (uint)panelH, supersample);
        Assert.Equal(expPanelW, r.PanelW);
        Assert.Equal(expPanelH, r.PanelH);
        Assert.Equal(expRtW, r.RtW);
        Assert.Equal(expRtH, r.RtH);
    }

    [Fact]
    public void Supersample_factor_is_two()
    {
        Assert.Equal(2, RenderSizing.SupersampleFactor);
    }
}