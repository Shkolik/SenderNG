/*
 * MainViewModelCenterTabsTests.cs - CenterTabs hands the TabControl the same
 * instances until the content really changes
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

using CNC.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using SenderNG.ViewModels;
using SenderNG.Core.Localization;
using SenderNG.Core.Modules;
using Xunit;

namespace SenderNG.Core.Tests;

/// <summary>
/// The checkpoint-3 zoom-jump root cause: the <c>CenterTabs</c> getter built
/// fresh <see cref="CenterTab"/> records on every read, so every
/// <c>OnPropertyChanged(nameof(CenterTabs))</c> (settings save, locale change,
/// module add) handed the TabControl a brand-new sequence - new containers, the
/// cached <c>PlotterGlView</c> re-parented, its GL scene torn down and rebuilt
/// against grid-default bounds. The caching contract these tests pin: an
/// unchanged read returns the SAME sequence instance (and the same record
/// objects), while a real change still returns - and then re-caches - an
/// updated one.
/// </summary>
public class MainViewModelCenterTabsTests
{
    private sealed class CenterStub(string id, int order = 0) : IModule
    {
        public string Id => id;
        public string Name => id;
        public int Order => order;
        public PanelHint Panel => PanelHint.Center;
        public IReadOnlyList<HotKey> HotKeys => [];
        public void RegisterServices(IServiceCollection services) { }
        public object CreateViewModel(IServiceProvider serviceProvider) => new object();
        public object CreateView(object viewModel) => new ViewStub();
    }

    private sealed class ViewStub;

    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "mvm-centertabs-" + Guid.NewGuid().ToString("N"));

    public MainViewModelCenterTabsTests() => Directory.CreateDirectory(_dir);

    /// <summary>Per-test settings store (xunit builds a fresh instance per
    /// test) plus a view model seeded with the given center modules. The
    /// localization instance is private when one is supplied: its Changed event
    /// must not reach other tests' view models.</summary>
    private (SettingsService Settings, MainViewModel Vm) MakeVm(
        IModule[] modules, LocalizationService? localization = null,
        Func<IModule, object>? viewFactory = null)
    {
        var settings = new SettingsService(_dir);
        var services = new ServiceCollection();
        services.AddSingleton(settings);
        services.AddSingleton(_ => new DemoGrblService(settings.Load()));
        services.AddSingleton<RealGrblService>();
        if (localization is not null)
            services.AddSingleton(localization);
        var vm = new MainViewModel(
            new DemoGrblService(settings.Load()), settings, services.BuildServiceProvider(),
            modules: modules, viewFactory: viewFactory);
        return (settings, vm);
    }

    [Fact]
    public void CenterTabs_reads_with_no_change_return_the_same_sequence_instance()
    {
        var (_, vm) = MakeVm(
        [
            new CenterStub("console", 20),
            new CenterStub("program", 10),
        ]);

        var first = vm.CenterTabs;
        var firstItems = first.ToArray();
        var second = vm.CenterTabs;

        // The enumerable instance itself is what the TabControl's ItemsSource
        // sees: a new one re-creates the containers and re-parents the view.
        Assert.Same(first, second);

        var secondItems = second.ToArray();
        Assert.Equal(firstItems.Length, secondItems.Length);
        for (var i = 0; i < firstItems.Length; i++)
            Assert.Same(firstItems[i], secondItems[i]);
    }

    [Fact]
    public void CenterTabs_view_instances_are_the_same_cached_objects()
    {
        var built = 0;
        var (_, vm) = MakeVm(
        [
            new CenterStub("console", 20),
            new CenterStub("program", 10),
        ],
        viewFactory: _ =>
        {
            built++;
            return new ViewStub();
        });

        var first = vm.CenterTabs.ToArray();
        var second = vm.CenterTabs.ToArray();

        Assert.All(first, t => Assert.IsType<ViewStub>(t.View));
        for (var i = 0; i < first.Length; i++)
            Assert.Same(first[i].View, second[i].View);
        // One view per module for the whole session - re-reading the sequence
        // never asks the factory for a replacement control.
        Assert.Equal(2, built);
    }

    [Fact]
    public void CenterTabs_module_visibility_change_updates_the_sequence()
    {
        var (settings, vm) = MakeVm(
        [
            new CenterStub("console", 20),
            new CenterStub("program", 10),
        ]);

        var before = vm.CenterTabs;
        Assert.Equal(2, before.Count());

        // App Settings does exactly this: write the map + Save, which raises
        // SettingsService.Changed -> OnPropertyChanged(nameof(CenterTabs)).
        settings.Load().Modules["program"] = false;
        settings.Save(settings.Load());

        var after = vm.CenterTabs;
        Assert.NotSame(before, after);
        Assert.Equal(["console"], after.Select(t => t.Module.Id).ToArray());
        // The replacement is cached like the one it replaced.
        Assert.Same(after, vm.CenterTabs);
    }

    [Fact]
    public void CenterTabs_locale_change_updates_the_names_and_re_caches()
    {
        var localization = LocalizationService.LoadFromDirectory(
            Path.Combine(AppContext.BaseDirectory, "Locale"));
        var (_, vm) = MakeVm([new CenterStub("console", 20)], localization);

        var before = vm.CenterTabs;
        Assert.Equal("Console", before.Single().Name);

        localization.SetLocale("ru");

        var after = vm.CenterTabs;
        Assert.NotSame(before, after);
        Assert.NotEqual(before.Single().Name, after.Single().Name);
        // The locale swap produced a new sequence, and THAT one is now cached
        // too - the change is absorbed once, not re-allocated on every read.
        Assert.Same(after, vm.CenterTabs);
    }
}
