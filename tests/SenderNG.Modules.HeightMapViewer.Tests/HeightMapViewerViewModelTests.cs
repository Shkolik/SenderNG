/*
 * HeightMapViewerViewModelTests.cs - headless tests for the viewer view model
 */

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

using System.ComponentModel;
using System.Reactive.Subjects;
using SenderNG.Core.Localization;
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

    [Fact]
    public void Locale_change_raises_property_changed_for_empty_text()
    {
        var (loc, dir) = NewLocalization();
        try
        {
            using var vm = new HeightMapViewerViewModel(null, loc);
            var raised = new List<string?>();
            vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

            loc.SetLocale("ru");

            Assert.Contains(nameof(HeightMapViewerViewModel.EmptyText), raised);
            Assert.Equal("RU empty", vm.EmptyText);
        }
        finally
        {
            Cleanup(dir);
        }
    }

    [Fact]
    public void Dispose_unsubscribes_from_locale_changes()
    {
        var (loc, dir) = NewLocalization();
        try
        {
            var vm = new HeightMapViewerViewModel(null, loc);
            var raised = new List<string?>();
            vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

            vm.Dispose();
            loc.SetLocale("ru");

            Assert.DoesNotContain(nameof(HeightMapViewerViewModel.EmptyText), raised);
        }
        finally
        {
            Cleanup(dir);
        }
    }

    private static (LocalizationService loc, string dir) NewLocalization()
    {
        var dir = Path.Combine(Path.GetTempPath(), "hmv-locale-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "en.json"), """{"strings":{}}""");
        File.WriteAllText(
            Path.Combine(dir, "ru.json"),
            """{"strings":{"module.heightmap-viewer.empty":"RU empty"}}""");
        return (LocalizationService.LoadFromDirectory(dir, "en"), dir);
    }

    private static void Cleanup(string dir)
    {
        try { Directory.Delete(dir, recursive: true); } catch { }
    }
}
