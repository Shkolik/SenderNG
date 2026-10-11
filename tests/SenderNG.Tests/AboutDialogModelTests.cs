
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

using Microsoft.Extensions.DependencyInjection;
using SenderNG.Core.Localization;
using SenderNG.Core.Modules;
using SenderNG.ViewModels;
using Xunit;

namespace SenderNG.Tests;

[Collection("LocalizationState")]
public class AboutDialogModelTests
{
    [Fact]
    public void PluginDependency_OneLine_formats_name_version_license()
    {
        var dep = new PluginDependency("Avalonia", "12.0.0", "MIT");
        Assert.Equal("Avalonia v.12.0.0 · MIT", dep.OneLine);
    }

    [Fact]
    public void PluginDependency_OneLine_omits_blank_license()
    {
        var dep = new PluginDependency("Foo", "1.0", "");
        Assert.Equal("Foo v.1.0", dep.OneLine);
    }

    [Fact]
    public void ThirdPartyLibrary_VersionLicense_formats_version_and_license()
    {
        var lib = new ThirdPartyLibrary("Avalonia", "12.1.1", "MIT");
        Assert.Equal("v.12.1.1  license: MIT", lib.VersionLicense);
    }

    [Fact]
    public void ThirdPartyLibrary_VersionLicense_omits_null_license()
    {
        var lib = new ThirdPartyLibrary("Foo", "1.0");
        Assert.Equal("v.1.0", lib.VersionLicense);
    }

    [Fact]
    public void ThirdPartyLibrary_VersionLicense_omits_blank_license()
    {
        var lib = new ThirdPartyLibrary("Foo", "1.0", "");
        Assert.Equal("v.1.0", lib.VersionLicense);
    }

    [Fact]
    public void ModuleInfoItem_AuthorLicense_combines_author_and_license()
    {
        var item = new ModuleInfoItem("HPGL", "1.0.0", "Andrew Shkolik", "BSD-3-Clause",
            [new PluginDependency("Avalonia", "12.0.0", "MIT")]);
        Assert.Equal("Andrew Shkolik · BSD-3-Clause", item.AuthorLicense);
    }

    [Fact]
    public void ModuleInfoItem_HasDependencies_tracks_declared_dependencies()
    {
        Assert.True(new ModuleInfoItem("A", "1.0", "Author", "MIT",
            [new PluginDependency("Dep", "1.0", "MIT")]).HasDependencies);
        Assert.False(new ModuleInfoItem("B", "1.0", "Author", "MIT").HasDependencies);
        Assert.False(new ModuleInfoItem("C", "1.0", "Author", "MIT", []).HasDependencies);
    }

    [Fact]
    public void Bundled_plotter_modules_declare_third_party_dependencies()
    {
        var gl = new SenderNG.Modules.Plotter.GL.PlotterGlModule();

        Assert.True(gl is IPluginDependencies);
        Assert.Contains(gl.Dependencies, d => d.Name == "Silk.NET.OpenGL" && d.Version == "2.23.0");
        Assert.DoesNotContain(gl.Dependencies, d => d.Name == "Avalonia");
    }

    [Fact]
    public void Plotter_module_does_not_declare_dependencies()
    {
        var plotter = new SenderNG.Modules.Plotter.PlotterModule();
        Assert.False(plotter is IPluginDependencies);
    }

    [Fact]
    public void BuildCopyText_starts_with_version_header_and_active_language()
    {
        var prev = LocalizationService.Current;
        try
        {
            LocalizationService.Current = HeadlessTestCatalog.Locale;
            if (HeadlessTestCatalog.Locale.AvailableLocales.Contains("ru"))
                HeadlessTestCatalog.Locale.SetLocale("ru");
            var vm = new AboutViewModel();
            var firstLine = vm.BuildCopyText().Split('\n')[0];
            Assert.StartsWith("SenderNG Version: ", firstLine);
            if (HeadlessTestCatalog.Locale.Locale == "ru")
                Assert.EndsWith("(ru)", firstLine);
            else
                Assert.EndsWith("(en)", firstLine);
        }
        finally
        {
            HeadlessTestCatalog.Locale.SetLocale("en");
            LocalizationService.Current = prev;
        }
    }

    [Fact]
    public void BuildCopyText_builds_libraries_plugins_and_system_sections()
    {
        var vm = new AboutViewModel();
        var text = vm.BuildCopyText();
        Assert.Contains("\nLibraries:\n", text);
        Assert.Contains("\nPlugins:\n", text);
        Assert.Contains("\nSystem:\n", text);
        Assert.Contains("\nOS: ", text);
        Assert.Contains("OS architecture: ", text);
        Assert.Contains("Process architecture: ", text);
        Assert.Contains(".NET runtime: ", text);
        Assert.Contains("UI culture: ", text);
        Assert.Contains("Processors: ", text);
        Assert.EndsWith("\n", text);
    }

    [Fact]
    public void BuildCopyText_libraries_match_the_tab_rows()
    {
        var vm = new AboutViewModel();
        var text = vm.BuildCopyText();
        foreach (var lib in vm.Libraries)
            Assert.Contains($"{lib.Name} {lib.VersionLicense}", text);
    }

    [Fact]
    public void BuildCopyText_plugins_show_info_and_indented_dependencies()
    {
        var vm = new AboutViewModel(new IModule[] { new CopyTestPlugin(), new CopyNoDepsTestPlugin() });
        var text = vm.BuildCopyText();
        Assert.Contains("Copy Test v.2.0.0 — Tester · MIT", text);
        Assert.Contains("    Sample.Dependency v.1.2.3 · MIT", text);
        Assert.Contains("No Deps v.1.0.0 — Tester · BSD-3-Clause", text);
    }

    [Fact]
    public void BuildCopyText_plugin_without_dependencies_has_no_indented_lines()
    {
        var vm = new AboutViewModel(new IModule[] { new CopyNoDepsTestPlugin() });
        var text = vm.BuildCopyText();
        var plugins = text[(text.IndexOf("Plugins:", StringComparison.Ordinal) + 8)..text.IndexOf("\nSystem:", StringComparison.Ordinal)];
        Assert.DoesNotContain("\n    ", plugins);
    }

    [Fact]
    public void CopyInfoCmd_raises_CopyRequested_with_build_copy_text()
    {
        var vm = new AboutViewModel();
        string? received = null;
        vm.CopyRequested += (_, t) => received = t;
        vm.CopyInfoCmd.Execute(null);
        Assert.Equal(vm.BuildCopyText(), received);
    }

    [Fact]
    public void CloseCmd_raises_CloseRequested_with_true()
    {
        var vm = new AboutViewModel();
        bool? result = null;
        vm.CloseRequested += (_, r) => result = r;
        vm.CloseCmd.Execute(null);
        Assert.True(result);
    }

    [Fact]
    public async Task Copy_info_second_copy_within_window_is_not_reverted_by_first_timer()
    {
        var prev = LocalizationService.Current;
        try
        {
            LocalizationService.Current = null; // English fallback — deterministic
            var vm = new AboutViewModel();
            Assert.Equal("Copy info", vm.CopyButtonText);

            vm.SetCopyResult(true); // first copy starts timer #1 (reverts at 2000 ms)
            Assert.Equal("Copied", vm.CopyButtonText);
            await Task.Delay(1100, TestContext.Current.CancellationToken); // still inside timer #1's window

            vm.SetCopyResult(true); // second copy re-bases the revert to timer #2 (2000 ms from now)
            Assert.Equal("Copied", vm.CopyButtonText);
            await Task.Delay(1400, TestContext.Current.CancellationToken); // timer #1's deadline passed, timer #2's has not

            // Discriminating: only the generation guard keeps "Copied" here — without it timer #1 already reverted.
            Assert.Equal("Copied", vm.CopyButtonText);

            await Task.Delay(1100, TestContext.Current.CancellationToken); // past timer #2's deadline

            Assert.Equal("Copy info", vm.CopyButtonText); // final revert from timer #2
        }
        finally
        {
            LocalizationService.Current = prev;
        }
    }

    [Fact]
    public void Copy_feedback_does_not_flip_label_on_failure()
    {
        var prev = LocalizationService.Current;
        try
        {
            LocalizationService.Current = null;
            var vm = new AboutViewModel();
            vm.SetCopyResult(false);
            Assert.Equal("Copy info", vm.CopyButtonText);
        }
        finally
        {
            LocalizationService.Current = prev;
        }
    }

    private sealed class CopyTestPlugin : IModule, IPluginInfo, IPluginDependencies
    {
        public string Id => "copy-test";
        public string Name => "Copy Test";
        public string Description => string.Empty;
        public int Order => 1;
        public IReadOnlyList<HotKey> HotKeys { get; } = [];
        public void RegisterServices(IServiceCollection services) { }
        public object CreateViewModel(IServiceProvider _) => new object();
        public object CreateView(object _) => new object();
        public string Version => "2.0.0";
        public string Author => "Tester";
        public string License => "MIT";
        public IReadOnlyList<PluginDependency> Dependencies { get; } =
            [new PluginDependency("Sample.Dependency", "1.2.3", "MIT")];
    }

    private sealed class CopyNoDepsTestPlugin : IModule, IPluginInfo
    {
        public string Id => "copy-no-deps";
        public string Name => "No Deps";
        public string Description => string.Empty;
        public int Order => 2;
        public IReadOnlyList<HotKey> HotKeys { get; } = [];
        public void RegisterServices(IServiceCollection services) { }
        public object CreateViewModel(IServiceProvider _) => new object();
        public object CreateView(object _) => new object();
        public string Version => "1.0.0";
        public string Author => "Tester";
        public string License => "BSD-3-Clause";
    }
}
