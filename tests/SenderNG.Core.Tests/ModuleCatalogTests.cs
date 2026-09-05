
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

using CNC.Core.Abstractions;
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
    public void ScanDirectory_discovers_plugin_modules_from_dll_folder()
    {
        // The plugin DLLs are staged into this project's OutputDir/Modules
        // (same Link trick the app csproj uses) — the real drop-in path.
        var dir = Path.Combine(AppContext.BaseDirectory, "Modules");
        Assert.True(Directory.Exists(dir), "plugin staging missing — rebuild the test project: " + dir);

        var sc = new ServiceCollection();
        ModuleCatalog.ScanDirectory(sc, dir);

        // LoadFrom context: the discovered types differ in identity from the
        // compile-time references, so match by full name, not type equality.
        Assert.Contains(sc, d => d.ServiceType.FullName == "SenderNG.Modules.Excellon.ExcellonModule");
        Assert.Contains(sc, d => d.ServiceType.FullName == "SenderNG.Modules.Hpgl.HpglModule");
        Assert.Contains(sc, d => d.ServiceType.FullName == "SenderNG.Modules.Dummy.DummyModule");

        using var sp = sc.BuildServiceProvider();
        var modules = sp.GetServices<IModule>().ToList();
        Assert.Contains(modules, m => m.Id == "excellon");
        Assert.Contains(modules, m => m.Id == "hpgl");
        Assert.Contains(modules, m => m.Id == "dummy");
    }

    [Fact]
    public void ScanDirectory_missing_folder_is_a_noop()
    {
        var sc = new ServiceCollection();
        ModuleCatalog.ScanDirectory(sc, Path.Combine(Path.GetTempPath(), "no-such-modules-" + Guid.NewGuid().ToString("N")));
        Assert.Empty(sc);
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

    [Fact]
    public void Settings_provider_defaults_to_profile_scope()
    {
        // docs/settings-migration.md §6.4: machine-coupled by default; only
        // modules that opt in (e.g. Console) persist to global.json.
        IModuleSettingsProvider provider = new FieldlessProvider();
        Assert.Equal(SettingsScope.Profile, provider.Scope);
    }

    private sealed class FieldlessProvider : IModuleSettingsProvider { }

    // ---- Id collisions: first wins, second rejected at discovery ----

    /// <summary>Two module classes claiming the same id — exactly one may register.</summary>
    public sealed class CollidingModuleA : IModule
    {
        public string Id => "collision-id";
        public string Name => "Colliding A";
        public int Order => 0;
        public IReadOnlyList<HotKey> HotKeys => Array.Empty<HotKey>();
        public void RegisterServices(IServiceCollection services) { }
        public object CreateViewModel(IServiceProvider serviceProvider) => new object();
        public object CreateView(object viewModel) => new object();
    }

    public sealed class CollidingModuleB : IModule
    {
        public string Id => "collision-id";
        public string Name => "Colliding B";
        public int Order => 0;
        public IReadOnlyList<HotKey> HotKeys => Array.Empty<HotKey>();
        public void RegisterServices(IServiceCollection services) { }
        public object CreateViewModel(IServiceProvider serviceProvider) => new object();
        public object CreateView(object viewModel) => new object();
    }

    [Fact]
    public void Scan_rejects_second_module_claiming_the_same_id()
    {
        // First wins, second rejected — the winner is the first of the two in
        // assembly metadata order (the same order ModuleCatalog scans).
        var expectedWinner = typeof(CollidingModuleA).Assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(IModule).IsAssignableFrom(t))
            .Select(t => (Type: t, Module: t.GetConstructor(Type.EmptyTypes) is null ? null : (IModule?)Activator.CreateInstance(t)))
            .Where(x => x.Module is not null && x.Module.Id == "collision-id")
            .First().Type;

        var sc = new ServiceCollection();
        ModuleCatalog.Scan(sc, typeof(CollidingModuleA).Assembly);

        Assert.Contains(sc, d => d.ServiceType == expectedWinner);
        var loser = expectedWinner == typeof(CollidingModuleA) ? typeof(CollidingModuleB) : typeof(CollidingModuleA);
        Assert.DoesNotContain(sc, d => d.ServiceType == loser);

        using var sp = sc.BuildServiceProvider();
        Assert.Single(sp.GetServices<IModule>(), m => m.Id == "collision-id");
    }

    // ---- Per-module error reporter (keyed by module Id) ----

    private sealed class FakeReporter : IErrorReporter
    {
        public readonly List<string> Errors = new();
        public readonly List<string> Statuses = new();
        public void Error(string text) => Errors.Add(text);
        public void Status(string text) => Statuses.Add(text);
    }

    private sealed class FakeReporterFactory : IErrorReporterFactory
    {
        public FakeReporter Reporter { get; } = new();
        public IErrorReporter Create(IModule module) => Reporter;
    }

    [Fact]
    public void Scan_registers_per_module_error_reporter_keyed_by_id()
    {
        var sc = new ServiceCollection();
        var factory = new FakeReporterFactory();
        sc.AddSingleton<IErrorReporterFactory>(factory);

        ModuleCatalog.Scan(sc, typeof(TestModule).Assembly);

        using var sp = sc.BuildServiceProvider();
        var reporter = sp.GetRequiredKeyedService<IErrorReporter>("Test");
        Assert.Same(factory.Reporter, reporter);

        reporter.Error("boom");
        reporter.Status("notice");
        Assert.Equal(["boom"], factory.Reporter.Errors);
        Assert.Equal(["notice"], factory.Reporter.Statuses);
    }
}
