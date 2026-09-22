
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

using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;
using SenderNG.Core.Localization;
using SenderNG.Core.Modules;
using SenderNG.ViewModels;
using SenderNG.Views;
using Xunit;

namespace SenderNG.Tests;

public class AboutDialogHeadlessTests
{
    [AvaloniaFact]
    public async Task About_dialog_shows_localized_app_info_and_libraries()
    {
        var prev = LocalizationService.Current;
        try
        {
            LocalizationService.Current = HeadlessTestCatalog.Locale;
            var vm = new AboutViewModel();
            var window = new AboutView { DataContext = vm };
            window.Show();
            for (var i = 0; i < 4; i++) await Pump();

            Assert.Equal("SenderNG", vm.AppName);
            Assert.StartsWith("Version: ", vm.Version);
            Assert.NotEmpty(vm.Libraries);
            Assert.Contains(vm.Libraries, l => l.Name == "Avalonia");

            // All three tabs render (About / Libraries / Modules).
            var tabs = window.GetVisualDescendants().OfType<TabControl>().First();
            Assert.Equal(3, tabs.Items.Count);
            window.Close();
        }
        finally
        {
            LocalizationService.Current = prev;
        }
    }

    /// <summary>Drain the dispatcher so the window realizes and bindings settle.</summary>
    private static async Task Pump()
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(16);
        await Task.Yield();
    }

    [AvaloniaFact]
    public async Task Modules_tab_renders_two_lines_and_dependency_chips()
    {
        var prev = LocalizationService.Current;
        try
        {
            LocalizationService.Current = HeadlessTestCatalog.Locale;
            var vm = new AboutViewModel(new IModule[] { new TestPlugin() });
            var window = new AboutView { DataContext = vm };
            window.Show();
            for (var i = 0; i < 4; i++) await Pump();

            var tabControl = window.GetVisualDescendants().OfType<TabControl>().First();
            var modulesTab = (TabItem)tabControl.Items![2]!;
            modulesTab.IsSelected = true;
            for (var i = 0; i < 4; i++) await Pump();

            var card = tabControl.GetVisualDescendants().OfType<StackPanel>().First();
            Assert.NotNull(card);
            // Caption + chips render only when the plugin declared dependencies.
            const string depCaption = "Dependencies";
            const string chipText = "SixLabors.ImageSharp v.3.1.12 · Six Labors Split License";
            var caption = tabControl.GetVisualDescendants()
                .OfType<TextBlock>()
                .FirstOrDefault(t => t.Text == depCaption);
            Assert.NotNull(caption);
            // The chip strip rendered one dependency (TextBlock matching OneLine).
            var chip = tabControl.GetVisualDescendants()
                .OfType<TextBlock>()
                .FirstOrDefault(t => t.Text == chipText);
            Assert.NotNull(chip);
            window.Close();
        }
        finally
        {
            LocalizationService.Current = prev;
        }
    }

    [AvaloniaFact]
    public async Task About_dialog_copy_and_close_buttons_wire_the_commands()
    {
        var prev = LocalizationService.Current;
        try
        {
            LocalizationService.Current = HeadlessTestCatalog.Locale;
            var vm = new AboutViewModel(new IModule[] { new TestPlugin() });
            string? copied = null;
            vm.CopyRequested += (_, t) => copied = t;
            var window = new AboutView { DataContext = vm };
            window.Show();
            for (var i = 0; i < 4; i++) await Pump();

            var buttons = window.GetVisualDescendants().OfType<Button>().ToArray();
            var copyButton = buttons.FirstOrDefault(b => ReferenceEquals(b.Command, vm.CopyInfoCmd));
            Assert.NotNull(copyButton);
            var closeButton = buttons.FirstOrDefault(b => ReferenceEquals(b.Command, vm.CloseCmd));
            Assert.NotNull(closeButton);

            copyButton.Command!.Execute(copyButton.CommandParameter);
            for (var i = 0; i < 4; i++) await Pump();
            Assert.Equal(vm.BuildCopyText(), copied);
            window.Close();
        }
        finally
        {
            LocalizationService.Current = prev;
        }
    }

    [AvaloniaFact]
    public async Task About_dialog_close_command_closes_the_window()
    {
        var prev = LocalizationService.Current;
        try
        {
            LocalizationService.Current = HeadlessTestCatalog.Locale;
            var vm = new AboutViewModel();
            var window = new AboutView { DataContext = vm };
            var closed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            window.Closed += (_, _) => closed.TrySetResult(true);
            window.Show();
            for (var i = 0; i < 4; i++) await Pump();

            var closeButton = window.GetVisualDescendants()
                .OfType<Button>()
                .FirstOrDefault(b => ReferenceEquals(b.Command, vm.CloseCmd));
            Assert.NotNull(closeButton);
            closeButton.Command!.Execute(closeButton.CommandParameter);
            await closed.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(closed.Task.IsCompletedSuccessfully);
        }
        finally
        {
            LocalizationService.Current = prev;
        }
    }

    private sealed class TestPlugin : IModule, IPluginInfo, IPluginDependencies
    {
        public string Id => "test";
        public string Name => "Test Plugin";
        public string Description => string.Empty;
        public int Order => 1;
        public IReadOnlyList<HotKey> HotKeys { get; } = [];
        public void RegisterServices(IServiceCollection services) { }
        public object CreateViewModel(IServiceProvider _) => new object();
        public object CreateView(object _) => new object();
        public string Version => "1.0.0";
        public string Author => "Author";
        public string License => "MIT";
        public IReadOnlyList<PluginDependency> Dependencies { get; } =
            [new PluginDependency("SixLabors.ImageSharp", "3.1.12", "Six Labors Split License")];
    }
}
