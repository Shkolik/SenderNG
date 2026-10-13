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

using System.Reactive.Linq;
using CNC.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using SenderNG.Core.Modules;
using SenderNG.Modules.Probing.Core;
using SenderNG.Modules.Probing.Converters;
using SenderNG.Modules.Plugins;

namespace SenderNG.Modules.Probing.Tests;

public class HeightMapSourceTests : IDisposable
{
    private readonly string _dir;
    private readonly SettingsService _settings;

    public HeightMapSourceTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "senderng-tests-" + Guid.NewGuid().ToString("N"));
        _settings = new SettingsService(_dir);
    }

    private ProbeViewModel CreateViewModel(IServiceProvider services)
        => new(new DemoGrblService(_settings.Load()), _settings, services);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }
    [Fact]
    public void ToHeightMapLayer_maps_grid_row_major()
    {
        // 3 columns x 3 rows over [0,2]x[0,2]; grid spacing 1 in each axis.
        var map = new HeightMap(1, 1, new Vec2(0, 0), new Vec2(2, 2));
        map.AddPoint(0, 0, 1);
        map.AddPoint(1, 0, 2);
        map.AddPoint(2, 0, 3);
        map.AddPoint(0, 1, 4);
        map.AddPoint(1, 1, 5);
        map.AddPoint(2, 1, 6);

        var layer = ProbingModule.ToHeightMapLayer(map);

        Assert.Equal(3, layer!.Columns);
        Assert.Equal(3, layer.Rows);
        Assert.Equal(0d, layer.OriginX);
        Assert.Equal(0d, layer.OriginY);
        Assert.Equal(1d, layer.CellX);
        Assert.Equal(1d, layer.CellY);

        // Row-major Z: [row0: 1,2,3, row1: 4,5,6].
        Assert.Equal(1d, layer[0, 0]);
        Assert.Equal(6d, layer[1, 2]);
        Assert.Equal(2d, layer[0, 1]);
    }

    [Fact]
    public void ToHeightMapLayer_marks_unprobed_cells_nan()
    {
        var map = new HeightMap(1, 1, new Vec2(0, 0), new Vec2(2, 2));
        map.AddPoint(1, 0, 5); // one probe only

        var layer = ProbingModule.ToHeightMapLayer(map)!;

        Assert.True(double.IsNaN(layer[0, 0]));
        Assert.Equal(5d, layer[0, 1]);
    }

    [Fact]
    public void ToHeightMapLayer_carries_zoffset()
    {
        var map = new HeightMap(1, 1, new Vec2(0, 0), new Vec2(1, 1)) { ZOffset = 0.5 };
        map.AddPoint(0, 0, 1);

        Assert.Equal(0.5, ProbingModule.ToHeightMapLayer(map)!.ZOffset);
    }

    [Fact]
    public async Task Preview3D_invokes_renderer_ShowAsync()
    {
        var stub = new StubRendererModule();
        var services = new ServiceCollection();
        services.AddSingleton<IHeightMap3DRenderer>(stub);
        var provider = services.BuildServiceProvider();

        var demo = new DemoGrblService(_settings.Load());
        await demo.ConnectAsync("demo");
        var vm = new ProbeViewModel(demo, _settings, provider);

        // Drive a completed map through the VM (same path the apply test uses).
        vm.SelectedTool = ProbingType.HeightMap;
        await vm.StartAsync();
        Assert.True(vm.HasHeightMap, $"Height map probe failed: {vm.LastResult}");

        vm.Preview3DCommand.Execute(null);

        Assert.Equal(1, stub.ShowCount);
    }

    [Fact]
    public void ProbingModule_publishes_snapshot_when_vm_reports_completion()
    {
        var module = new ProbingModule();

        HeightMapLayer? seen = null;
        using var sub = module.Snapshot.Subscribe(layer => seen = layer);

        var map = new HeightMap(1, 1, new Vec2(0, 0), new Vec2(1, 1));
        map.AddPoint(0, 0, (double)1);
        module.PublishHeightMap(ProbingModule.ToHeightMapLayer(map));

        Assert.NotNull(seen);
        Assert.Equal(1d, seen![0, 0]);
    }

    [Fact]
    public void Snapshot_replays_latest_map_to_a_late_subscriber()
    {
        var module = new ProbingModule();

        // Publish BEFORE anyone subscribes — the dialog's fresh view model
        // subscribes only when it opens.
        var map = new HeightMap(1, 1, new Vec2(0, 0), new Vec2(1, 1));
        map.AddPoint(0, 0, 7);
        module.PublishHeightMap(ProbingModule.ToHeightMapLayer(map));

        var received = false;
        HeightMapLayer? seen = null;
        using var sub = module.Snapshot.Subscribe(layer =>
        {
            received = true;
            seen = layer;
        });

        Assert.True(received, "a late subscriber must receive the current map immediately");
        Assert.NotNull(seen);
        Assert.Equal(7d, seen![0, 0]);
    }

    [Fact]
    public void Snapshot_replays_null_to_a_late_subscriber_after_clear()
    {
        var module = new ProbingModule();

        var map = new HeightMap(1, 1, new Vec2(0, 0), new Vec2(1, 1));
        map.AddPoint(0, 0, 7);
        module.PublishHeightMap(ProbingModule.ToHeightMapLayer(map));
        module.PublishHeightMap(null);

        var received = false;
        HeightMapLayer? latest = null;
        using var sub = module.Snapshot.Subscribe(layer =>
        {
            received = true;
            latest = layer;
        });

        Assert.True(received, "a late subscriber must still receive the current (cleared) state");
        Assert.Null(latest);
    }

    [Fact]
    public void Has3DRenderer_reflects_registered_renderer_module()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IHeightMap3DRenderer>(new StubRendererModule());
        var provider = services.BuildServiceProvider();

        var vm = CreateViewModel(provider);

        Assert.True(vm.Has3DRenderer);
        vm.SelectedTool = ProbingType.HeightMap;
        Assert.True(vm.IsPreview3DVisible); // height-map tab active + renderer present
        vm.SelectedTool = ProbingType.ToolLength;
        Assert.False(vm.IsPreview3DVisible);
    }

    [Fact]
    public void Has3DRenderer_is_false_without_a_renderer_module()
    {
        var plain = CreateViewModel(new ServiceCollection().BuildServiceProvider());

        Assert.False(plain.Has3DRenderer);
        plain.SelectedTool = ProbingType.HeightMap;
        Assert.False(plain.IsPreview3DVisible);
    }

    /// <summary>Throw-away height-map renderer registered by service contract
    /// (how the future renderer plugin registers itself).</summary>
    internal sealed class StubRendererModule : IModule, IHeightMap3DRenderer
    {
        public int ShowCount { get; private set; }

        public Task ShowAsync()
        {
            ShowCount++;
            return Task.CompletedTask;
        }

        public string Id => "stub-renderer";
        public string Name => "Stub renderer";
        public string Description => "Test renderer stub.";
        public int Order => 0;
        public IReadOnlyList<HotKey> HotKeys => [];
        public void RegisterServices(IServiceCollection services) { }
        public object CreateViewModel(IServiceProvider serviceProvider) => new();
        public object CreateView(object viewModel) => new();
    }
}