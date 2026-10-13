using System.ComponentModel;
using System.Reactive.Subjects;
using SenderNG.Core.Modules;
using SenderNG.Modules.HeightMapViewer;

namespace SenderNG.Modules.HeightMapViewer.Tests;

public class HeightMapViewerViewModelTests
{
    private sealed class FakeSource : IHeightMapSource
    {
        public Subject<HeightMapLayer?> Bus { get; } = new();

        public IObservable<HeightMapLayer?> Snapshot => Bus;
    }

    private static HeightMapLayer Map(params double[] z)
        => new("heightmap", 0, 0, 1, 1, z.Length, 1, z, 0);

    [Fact]
    public void Null_source_is_empty()
    {
        using var vm = new HeightMapViewerViewModel(null, null);

        Assert.True(vm.IsEmpty);
        Assert.Null(vm.Layer);
    }

    [Fact]
    public void Null_snapshot_leaves_view_empty()
    {
        var source = new FakeSource();
        using var vm = new HeightMapViewerViewModel(source, null);

        source.Bus.OnNext(null);

        Assert.True(vm.IsEmpty);
        Assert.Null(vm.Layer);
    }

    [Fact]
    public void Layer_emission_populates_the_view()
    {
        var source = new FakeSource();
        using var vm = new HeightMapViewerViewModel(source, null);
        var layer = Map(1, 2, 3, 4);

        source.Bus.OnNext(layer);

        Assert.False(vm.IsEmpty);
        Assert.Same(layer, vm.Layer);
    }

    [Fact]
    public void Live_update_replaces_the_layer()
    {
        var source = new FakeSource();
        using var vm = new HeightMapViewerViewModel(source, null);
        var first = Map(1, 2, 3, 4);
        var second = Map(5, 6, 7, 8);

        source.Bus.OnNext(first);
        source.Bus.OnNext(second);

        Assert.False(vm.IsEmpty);
        Assert.Same(second, vm.Layer);
    }

    [Fact]
    public void Clearing_returns_to_the_empty_state()
    {
        var source = new FakeSource();
        using var vm = new HeightMapViewerViewModel(source, null);

        source.Bus.OnNext(Map(1, 2, 3, 4));
        source.Bus.OnNext(null);

        Assert.True(vm.IsEmpty);
        Assert.Null(vm.Layer);
    }

    [Fact]
    public void Emission_raises_property_changed_for_is_empty_and_layer()
    {
        var source = new FakeSource();
        using var vm = new HeightMapViewerViewModel(source, null);
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        source.Bus.OnNext(Map(1, 2, 3, 4));

        Assert.Contains(nameof(HeightMapViewerViewModel.IsEmpty), raised);
        Assert.Contains(nameof(HeightMapViewerViewModel.Layer), raised);
    }

    [Fact]
    public void Dispose_unsubscribes_from_the_source()
    {
        var source = new FakeSource();
        var vm = new HeightMapViewerViewModel(source, null);
        vm.Dispose();

        source.Bus.OnNext(Map(1, 2, 3, 4));

        Assert.True(vm.IsEmpty);
        Assert.Null(vm.Layer);
    }

    [Fact]
    public void Empty_text_is_never_blank_without_localization()
    {
        using var vm = new HeightMapViewerViewModel(null, null);

        Assert.False(string.IsNullOrWhiteSpace(vm.EmptyText));
    }
}
