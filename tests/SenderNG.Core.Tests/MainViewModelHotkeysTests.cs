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
using SenderNG.Core.Modules;
using SenderNG.Core.Services;
using SenderNG.ViewModels;

namespace SenderNG.Core.Tests;

public class MainViewModelHotkeysTests
{
    private sealed class StubHotkeyModule : IModule, IHotkeyActionProvider
    {
        public string Id => "stub";
        public string Name => "Stub";
        public int Order => 0;
        public IReadOnlyList<HotKey> HotKeys { get; } = [new("Ctrl+Shift+Z", "stub.go")];
        public void RegisterServices(IServiceCollection services) { }
        public object CreateViewModel(IServiceProvider serviceProvider) => new object();
        public object CreateView(object viewModel) => new object();
        public IReadOnlyList<IHotkeyAction> CreateHotkeyActions(IServiceProvider services)
            => [new CommandHotkeyAction("stub.go", "Stub go", "module.stub.hotkey.stub.go", () => { })];
    }

    private static (MainViewModel Vm, HotkeyService Hotkeys, SettingsService Settings) Make(params IModule[] modules)
    {
        var settings = new SettingsService(
            Path.Combine(Path.GetTempPath(), "senderng-mvm-hotkeys-" + Guid.NewGuid().ToString("N")));
        var services = new ServiceCollection();
        services.AddSingleton(settings);
        services.AddSingleton<HotkeyService>();
        services.AddSingleton(_ => new DemoGrblService(settings.Load()));
        services.AddSingleton<RealGrblService>();
        var provider = services.BuildServiceProvider();
        var vm = new MainViewModel(new DemoGrblService(settings.Load()), settings, provider, modules: modules);
        return (vm, provider.GetRequiredService<HotkeyService>(), settings);
    }

    [Fact]
    public void Builtin_actions_are_registered_with_parity_defaults()
    {
        var (_, hotkeys, _) = Make();

        Assert.Equal("Alt+R", hotkeys.GestureFor("cycle.start"));
        Assert.Equal("Space", hotkeys.GestureFor("cycle.hold"));
        Assert.Equal("Alt+S", hotkeys.GestureFor("cycle.stop"));
        Assert.Null(hotkeys.GestureFor("cycle.rewind"));
        Assert.Equal("Ctrl+H", hotkeys.GestureFor("home"));
        Assert.Equal("Ctrl+U", hotkeys.GestureFor("unlock"));
        Assert.Equal("Ctrl+Shift+R", hotkeys.GestureFor("reset"));
        Assert.Null(hotkeys.GestureFor("check"));
        Assert.Equal("Ctrl+M", hotkeys.GestureFor("mdi.focus"));
    }

    [Fact]
    public void Builtin_canexecute_rides_the_command()
    {
        var (_, hotkeys, _) = Make();
        var check = hotkeys.Find("check")!;
        Assert.False(check.Action.CanExecute());
    }

    [Fact]
    public void Module_provider_actions_link_their_declared_default()
    {
        var (_, hotkeys, _) = Make(new StubHotkeyModule());
        Assert.Equal("Ctrl+Shift+Z", hotkeys.GestureFor("stub.go"));
        Assert.Null(hotkeys.Find("stub.go")!.Action.ModuleId);
        Assert.Equal(HotkeyScope.Global, hotkeys.Find("stub.go")!.Action.Scope);
    }

    [Fact]
    public void MdiFocus_request_fires_event()
    {
        var (vm, hotkeys, _) = Make();
        var fired = 0;
        vm.FocusMdiRequested += (_, _) => fired++;
        hotkeys.Find("mdi.focus")!.Action.OnKeyDown();
        Assert.Equal(1, fired);
    }
}