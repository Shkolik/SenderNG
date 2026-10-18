/*
 * AppearanceLeafHeadlessTests.cs - headless UI tests for the Appearance leaf's
 * theme selector bindings
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

using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CNC.Core.Services;
using CNC.Core.ViewModels;
using SenderNG.Core.Localization;
using SenderNG.Core.Modules;
using SenderNG.Modules.Fixed;
using Xunit;

namespace SenderNG.Tests;

[Collection("LocalizationState")]
public class AppearanceLeafHeadlessTests
{
    // A private instance (not HeadlessTestCatalog.Locale): SetLocale calls here must
    // never race other test classes that read the shared static catalog.
    private static readonly LocalizationService Loc =
        LocalizationService.LoadFromDirectory(Path.Combine(AppContext.BaseDirectory, "Locale"));

    private static SettingsService MakeSettings()
    {
        var dir = Path.Combine(Path.GetTempPath(), "senderng-appsettings-ui-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return new SettingsService(dir);
    }

    [AvaloniaFact]
    public async Task Theme_selector_lists_three_options_and_applies_selection()
    {
        var prev = LocalizationService.Current;
        try
        {
            LocalizationService.Current = Loc;
            var settings = MakeSettings();
            var applied = new List<string>();
            var vm = new AppSettingsViewModel(settings, Array.Empty<IModule>(), Loc, applyTheme: applied.Add);
            var leaf = ((AppSettingsGroupItem)vm.Groups[0]).Children.OfType<AppearanceSettingsLeaf>().Single();
            vm.SelectedNode = leaf;

            var view = new AppSettingsView { DataContext = vm };
            var window = new Window { Width = 920, Height = 560, Content = view };
            window.Show();
            for (var i = 0; i < 4; i++)
                await Pump();

            var combo = window.GetVisualDescendants().OfType<ComboBox>()
                .Single(c => ReferenceEquals(c.ItemsSource, leaf.ThemeOptions));
            Assert.Equal(3, combo.ItemCount);
            Assert.True(combo.SelectedItem is ThemeOption { Code: "system" });

            combo.SelectedItem = leaf.ThemeOptions.Single(o => o.Code == "dark");
            for (var i = 0; i < 2; i++)
                await Pump();

            Assert.Equal(new[] { "dark" }, applied);
            Assert.Equal("dark", settings.Global.Theme);

            // Navigate away and back: the selection survives template teardown.
            vm.SelectedNode = ((AppSettingsGroupItem)vm.Groups[0]).Children.OfType<ProfileSettingsLeaf>().Single();
            for (var i = 0; i < 2; i++)
                await Pump();
            vm.SelectedNode = leaf;
            for (var i = 0; i < 4; i++)
                await Pump();
            var revived = window.GetVisualDescendants().OfType<ComboBox>()
                .Single(c => ReferenceEquals(c.ItemsSource, leaf.ThemeOptions));
            Assert.True(revived.SelectedItem is ThemeOption { Code: "dark" });

            window.Close();
        }
        finally
        {
            LocalizationService.Current = prev;
        }
    }

    /// <summary>Run queued dispatcher jobs (loc:Loc re-emissions land there) and yield
    /// so the headless SynchronizationContext runs a pending layout/render.</summary>
    private static async Task Pump()
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(16);
        await Task.Yield();
    }
}
