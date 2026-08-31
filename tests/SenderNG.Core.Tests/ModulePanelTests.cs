
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
using SenderNG.Modules.Fixed;
using SenderNG.Modules.Plugins;
using SenderNG.Core.Modules;
using Xunit;

public class ModulePanelTests
{
    [Fact]
    public void New_PanelHint_property_exists()
    {
        var m = new DummyModule();
        Assert.Equal(PanelHint.Center, m.Panel);
        Assert.True(m.IsVisibleByDefault);
    }

    [Fact]
    public void DIM_fallback_gives_Center_and_visible_true_when_not_overridden()
    {
        IModule m = new BareModule();
        Assert.Equal(PanelHint.Center, m.Panel);
        Assert.True(m.IsVisibleByDefault);
    }

    [Fact]
    public void BuiltIn_modules_have_explicit_Panel_overrides_per_spec()
    {
        Assert.Equal(PanelHint.Left, new DroModule().Panel);
        Assert.Equal(10, new DroModule().Order);
        Assert.True(new DroModule().IsVisibleByDefault);

        Assert.Equal(PanelHint.Center, new ProgramModule().Panel);
        Assert.Equal(20, new ProgramModule().Order);

        Assert.Equal(PanelHint.Center, new ConsoleModule().Panel);
        Assert.Equal(21, new ConsoleModule().Order);

        Assert.Equal(PanelHint.Flyout, new SenderNG.Modules.Fixed.OffsetsModule().Panel);
        Assert.Equal(30, new SenderNG.Modules.Fixed.OffsetsModule().Order);

        // First menu-claiming module: opens as a modal dialog from Settings.
        Assert.Equal(PanelHint.Menu, new GrblSettingsModule().Panel);
        Assert.Equal(40, new GrblSettingsModule().Order);

        // Wide widget (foam/multiaxis) — claims the flyout drawer.
        Assert.Equal(PanelHint.Flyout, new JogModule().Panel);
        Assert.Equal(15, new JogModule().Order);

        Assert.Equal(PanelHint.Right, new CuttingModule().Panel);
        Assert.Equal(25, new CuttingModule().Order);

        Assert.Equal(PanelHint.Right, new CoolantModule().Panel);
        Assert.Equal(35, new CoolantModule().Order);

        Assert.Equal(PanelHint.Flyout, new MacroModule().Panel);
        Assert.Equal(45, new MacroModule().Order);

        Assert.Equal(PanelHint.Flyout, new ProbingModule().Panel);
        Assert.Equal(50, new ProbingModule().Order);

        Assert.Equal(PanelHint.Flyout, new MposModule().Panel);
        Assert.Equal(20, new MposModule().Order);

        Assert.Equal(PanelHint.Flyout, new OutlineModule().Panel);
        Assert.Equal(32, new OutlineModule().Order);
    }

    private class DummyModule : IModule
    {
        public string Id => "dummy";
        public string Name => "Dummy";
        public int Order => 0;
        public PanelHint Panel => PanelHint.Center;
        public bool IsVisibleByDefault => true;
        public IReadOnlyList<HotKey> HotKeys => [];
        public void RegisterServices(Microsoft.Extensions.DependencyInjection.IServiceCollection s) { }
        public void RegisterSettings(AppSettings s) { }
        public object CreateViewModel(System.IServiceProvider sp) => new object();
        public object CreateView(object vm) => new object();
    }

    private class BareModule : IModule
    {
        public string Id => "bare";
        public string Name => "Bare";
        public int Order => 99;
        public IReadOnlyList<HotKey> HotKeys => [];
        public void RegisterServices(Microsoft.Extensions.DependencyInjection.IServiceCollection s) { }
        public void RegisterSettings(AppSettings s) { }
        public object CreateViewModel(System.IServiceProvider sp) => new object();
        public object CreateView(object vm) => new object();
    }
}
