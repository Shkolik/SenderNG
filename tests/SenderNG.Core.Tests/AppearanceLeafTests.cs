/*
 * AppearanceLeafTests.cs - pins the appearance leaf's locale + theme behavior
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
using CNC.Core.ViewModels;
using SenderNG.Core.Modules;
using Xunit;

namespace SenderNG.Core.Tests;

public class AppearanceLeafTests
{
    private static (SettingsService Service, string Dir) MakeSettings()
    {
        var dir = Path.Combine(Path.GetTempPath(), "senderng-appearance-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return (new SettingsService(dir), dir);
    }

    private static AppearanceSettingsLeaf LeafOf(AppSettingsViewModel vm) =>
        ((AppSettingsGroupItem)vm.Groups[0]).Children.OfType<AppearanceSettingsLeaf>().Single();

    [Fact]
    public void Selecting_theme_applies_immediately_and_persists()
    {
        var (service, _) = MakeSettings();
        string? applied = null;
        var vm = new AppSettingsViewModel(service, Array.Empty<IModule>(), applyTheme: t => applied = t);
        var leaf = LeafOf(vm);

        leaf.SelectedTheme = leaf.ThemeOptions.Single(o => o.Code == ThemePreference.Dark);

        Assert.Equal("dark", applied);
        Assert.Equal("dark", service.Global.Theme);
    }

    [Fact]
    public void System_theme_persists_as_empty_string_and_applies_normally()
    {
        var (service, _) = MakeSettings();
        string? applied = "unset";
        var vm = new AppSettingsViewModel(service, Array.Empty<IModule>(), applyTheme: t => applied = t);
        var leaf = LeafOf(vm);

        leaf.SelectedTheme = leaf.ThemeOptions.Single(o => o.Code == ThemePreference.Light);
        leaf.SelectedTheme = leaf.ThemeOptions.Single(o => o.Code == ThemePreference.System);

        Assert.Equal(string.Empty, service.Global.Theme);
        Assert.Equal("system", applied);
    }

    [Fact]
    public void Stored_theme_selection_round_trips_into_the_leaf()
    {
        var (service, _) = MakeSettings();
        service.Global.Theme = ThemePreference.Light;
        service.SaveGlobal(service.Global);

        var reloaded = new SettingsService(service.SettingsDir);
        var vm = new AppSettingsViewModel(reloaded, Array.Empty<IModule>());
        Assert.Equal(ThemePreference.Light, LeafOf(vm).SelectedTheme!.Code);
    }

    [Fact]
    public void Unknown_stored_theme_falls_back_to_system_selection()
    {
        var (service, _) = MakeSettings();
        service.Global.Theme = "rainbow";
        var vm = new AppSettingsViewModel(service, Array.Empty<IModule>());
        Assert.Equal(ThemePreference.System, LeafOf(vm).SelectedTheme!.Code);
    }

    [Fact]
    public void Relocalize_updates_theme_labels()
    {
        var leaf = new AppearanceSettingsLeaf(() => "en", new[] { "en" }, _ => { },
            () => ThemePreference.System, _ => { }, key => key, "Appearance", "desc");

        leaf.RelocalizeTheme("Thema", key => "X-" + key);

        Assert.Equal("Thema", leaf.ThemeLabel);
        Assert.All(leaf.ThemeOptions, o => Assert.StartsWith("X-", o.Label));
    }
}
