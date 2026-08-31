
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
using Xunit;

namespace SenderNG.Core.Tests;

public class ModuleCatalogTests
{
    private sealed class TestService { }

    public sealed class TestModule : IModule
    {
        public string Id => "Test";
        public string Name => "Test Module";
        public int Order => 0;
        public IReadOnlyList<HotKey> HotKeys => Array.Empty<HotKey>();

        public void RegisterServices(IServiceCollection services) =>
            services.AddSingleton<TestService>();

        public void RegisterSettings(AppSettings settings) { }

        public object CreateViewModel(IServiceProvider serviceProvider) => new object();
        public object CreateView(object viewModel) => new object();
    }

    [Fact]
    public void RegisterServices_registers_services()
    {
        var sc = new ServiceCollection();
        new TestModule().RegisterServices(sc);
        Assert.Single(sc);
    }

    [Fact]
    public void Scan_discovers_module_and_registers_it_as_singleton()
    {
        var sc = new ServiceCollection();
        ModuleCatalog.Scan(sc, typeof(TestModule).Assembly);

        var descriptor = Assert.Single(sc, sd => sd.ServiceType == typeof(TestModule));
        Assert.Equal(typeof(TestModule), descriptor.ImplementationType);
        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
    }

    [Fact]
    public void Scan_applies_module_service_registrations()
    {
        var sc = new ServiceCollection();
        ModuleCatalog.Scan(sc, typeof(TestModule).Assembly);

        using var sp = sc.BuildServiceProvider();
        Assert.NotNull(sp.GetService<TestService>());
    }

    [Fact]
    public void Scan_does_not_duplicate_already_registered_modules()
    {
        var sc = new ServiceCollection();
        ModuleCatalog.Scan(sc, typeof(TestModule).Assembly);
        ModuleCatalog.Scan(sc, typeof(TestModule).Assembly);

        Assert.Single(sc, sd => sd.ServiceType == typeof(TestModule));
    }

    [Fact]
    public void Scan_discovers_dummy_module_from_external_assembly()
    {
        // SenderNG.Modules.Dummy is a separate project (external plugin assembly);
        // the host passes it to Scan exactly like this.
        var sc = new ServiceCollection();
        ModuleCatalog.Scan(sc, typeof(SenderNG.Modules.Dummy.DummyModule).Assembly);

        using var sp = sc.BuildServiceProvider();
        var modules = sp.GetServices<IModule>().ToList();

        var dummy = Assert.Single(modules, m => m.Id == "dummy");
        Assert.IsType<SenderNG.Modules.Dummy.DummyModule>(dummy);
        Assert.Equal("Dummy", dummy.Name);
        Assert.Equal(100, dummy.Order);
        Assert.Equal(PanelHint.Flyout, dummy.Panel);
        Assert.True(dummy.IsVisibleByDefault);
    }

    [Fact]
    public void HotKey_record_holds_gesture_and_action()
    {
        var hk = new HotKey("Ctrl+O", "File.Open");
        Assert.Equal("Ctrl+O", hk.Gesture);
        Assert.Equal("File.Open", hk.ActionId);
    }
}
