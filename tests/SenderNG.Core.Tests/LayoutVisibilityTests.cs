
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
using SenderNG.Core.Modules;
using Xunit;

namespace SenderNG.Core.Tests;

public class LayoutVisibilityTests
{
    private sealed class StubModule : IModule
    {
        public StubModule(string id, PanelHint panel, int order, bool visibleByDefault = true)
        {
            Id = id;
            Panel = panel;
            Order = order;
            IsVisibleByDefault = visibleByDefault;
        }
        public string Id { get; }
        public string Name => Id;
        public int Order { get; }
        public PanelHint Panel { get; }
        public bool IsVisibleByDefault { get; }
        public IReadOnlyList<HotKey> HotKeys => [];
        public void RegisterServices(IServiceCollection services) { }
        public object CreateViewModel(IServiceProvider sp) => new object();
        public object CreateView(object vm) => new object();
    }

    private static SettingsService MakeSettings(MachineProfile? seed = null)
    {
        var dir = Path.Combine(Path.GetTempPath(), "senderng-layout-" + Guid.NewGuid().ToString("N"));
        var svc = new SettingsService(dir);
        if (seed is not null) svc.Save(seed);
        return svc;
    }

    private static ServiceProvider MakeProvider(SettingsService settings)
    {
        var sc = new ServiceCollection();
        sc.AddSingleton(settings);
        sc.AddSingleton(_ => new DemoGrblService(settings.Load()));
        sc.AddSingleton<RealGrblService>();
        return sc.BuildServiceProvider();
    }

    private static MainViewModel CreateVMWithVisibility(MachineProfile appSettings, IEnumerable<IModule>? modules = null)
    {
        var settings = MakeSettings(appSettings);
        var provider = MakeProvider(settings);
        var mods = modules ?? new IModule[]
        {
            new StubModule("dro", PanelHint.Left, 10),
            new StubModule("gcode-console", PanelHint.Center, 20),
            new StubModule("offsets", PanelHint.Left, 30),
            new StubModule("grbl-settings", PanelHint.Right, 40),
            new StubModule("jog", PanelHint.Right, 15),
        };
        return new MainViewModel(new DemoGrblService(appSettings), settings, provider, modules: mods);
    }

    [Fact]
    public void CenterModules_excludes_Hidden()
    {
        var appSettings = new MachineProfile();
        appSettings.Modules["gcode-console"] = false;
        var vm = CreateVMWithVisibility(appSettings);
        Assert.DoesNotContain(vm.CenterModules, m => m.Id == "gcode-console");
    }

    [Fact]
    public void CenterModules_includes_Visible()
    {
        var appSettings = new MachineProfile();
        var vm = CreateVMWithVisibility(appSettings);
        Assert.Contains(vm.CenterModules, m => m.Id == "gcode-console");
    }

    [Fact]
    public void LeftModules_ordered_by_Order()
    {
        var appSettings = new MachineProfile();
        var mods = new IModule[]
        {
            new StubModule("offsets", PanelHint.Left, 30),
            new StubModule("dro", PanelHint.Left, 10),
        };
        var vm = CreateVMWithVisibility(appSettings, mods);
        Assert.Equal(new[] { "dro", "offsets" }, vm.LeftModules.Select(m => m.Module.Id).ToArray());
    }

    [Fact]
    public void RightModules_excludes_Hidden()
    {
        var appSettings = new MachineProfile();
        appSettings.Modules["jog"] = false;
        var vm = CreateVMWithVisibility(appSettings);
        Assert.DoesNotContain(vm.RightModules, m => m.Id == "jog");
        Assert.Contains(vm.RightModules, m => m.Id == "grbl-settings");
    }

    [Fact]
    public void FlyoutModules_empty_when_none_claimed()
    {
        var appSettings = new MachineProfile();
        var vm = CreateVMWithVisibility(appSettings);
        Assert.Empty(vm.FlyoutModules);
    }

    [Fact]
    public void FlyoutModules_contains_Flyout_module()
    {
        var appSettings = new MachineProfile();
        var mods = new IModule[] { new StubModule("macro", PanelHint.Flyout, 45) };
        var vm = CreateVMWithVisibility(appSettings, mods);
        Assert.Contains(vm.FlyoutModules, m => m.Module.Id == "macro");
    }

    [Fact]
    public void IsModuleVisible_falls_back_to_IsVisibleByDefault_when_not_in_map()
    {
        var appSettings = new MachineProfile();
        var mods = new IModule[] { new StubModule("hidden-by-default", PanelHint.Left, 99, visibleByDefault: false) };
        var vm = CreateVMWithVisibility(appSettings, mods);
        var m = mods[0];
        Assert.False(vm.IsModuleVisible(m));
        Assert.DoesNotContain(vm.LeftModules, x => x.Module.Id == "hidden-by-default");
    }

    [Fact]
    public void IsModuleVisible_respects_ModulesVisibility_override()
    {
        var appSettings = new MachineProfile();
        appSettings.Modules["hidden-by-default"] = true;
        var mods = new IModule[] { new StubModule("hidden-by-default", PanelHint.Left, 99, visibleByDefault: false) };
        var vm = CreateVMWithVisibility(appSettings, mods);
        Assert.True(vm.IsModuleVisible(mods[0]));
        Assert.Contains(vm.LeftModules, x => x.Module.Id == "hidden-by-default");
    }
}
