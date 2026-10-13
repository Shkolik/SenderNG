/*
 * HeightMapViewerSettingsViewModelTests.cs - headless tests for the settings VM
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
using SenderNG.Core.Localization;
using SenderNG.Modules.Common.Render;
using SenderNG.Modules.HeightMapViewer.Settings;

namespace SenderNG.Modules.HeightMapViewer.Tests;

public class HeightMapViewerSettingsViewModelTests
{
    [Fact]
    public void Defaults_mirror_the_settings()
    {
        var (service, dir) = NewService();
        try
        {
            var vm = new HeightMapViewerSettingsViewModel(service);

            Assert.True(vm.Supersample);
            Assert.Equal(MsaaMode.None, vm.MsaaMode);
            Assert.Equal(0, vm.MsaaIndex);
            Assert.Equal(["Off", "2x", "4x", "8x"], vm.MsaaChoices);
            Assert.False(string.IsNullOrWhiteSpace(vm.SupersampleLabel));
            Assert.False(string.IsNullOrWhiteSpace(vm.MsaaLabel));
        }
        finally
        {
            Cleanup(dir);
        }
    }

    [Fact]
    public void Supersample_persists()
    {
        var (service, dir) = NewService();
        try
        {
            var vm = new HeightMapViewerSettingsViewModel(service);

            vm.Supersample = false;

            var persisted = HeightMapViewerConfig.Load(new SettingsService(dir).Load());
            Assert.False(persisted.Supersample);
        }
        finally
        {
            Cleanup(dir);
        }
    }

    [Fact]
    public void MsaaIndex_sets_and_persists_the_mode()
    {
        var (service, dir) = NewService();
        try
        {
            var vm = new HeightMapViewerSettingsViewModel(service);

            vm.MsaaIndex = 2;

            Assert.Equal(MsaaMode.X4, vm.MsaaMode);
            var persisted = HeightMapViewerConfig.Load(new SettingsService(dir).Load());
            Assert.Equal(MsaaMode.X4, persisted.MsaaMode);
        }
        finally
        {
            Cleanup(dir);
        }
    }

    [Fact]
    public void MsaaIndex_ignores_out_of_range_writes()
    {
        var (service, dir) = NewService();
        try
        {
            var vm = new HeightMapViewerSettingsViewModel(service);

            vm.MsaaIndex = -1;
            Assert.Equal(MsaaMode.None, vm.MsaaMode);

            vm.MsaaIndex = 99;
            Assert.Equal(MsaaMode.None, vm.MsaaMode);
        }
        finally
        {
            Cleanup(dir);
        }
    }

    [Fact]
    public void Labels_relocalize_on_a_locale_switch()
    {
        var (service, dir) = NewService();
        var locDir = Path.Combine(Path.GetTempPath(), "hmvvm-locale-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(locDir);
        File.WriteAllText(Path.Combine(locDir, "en.json"), """{"strings":{}}""");
        File.WriteAllText(
            Path.Combine(locDir, "ru.json"),
            """{"strings":{"module.heightmap-viewer.settings.supersample":"RU ss","module.heightmap-viewer.settings.msaa":"RU msaa"}}""");
        try
        {
            var loc = LocalizationService.LoadFromDirectory(locDir, "en");
            var vm = new HeightMapViewerSettingsViewModel(service, loc);

            loc.SetLocale("ru");

            Assert.Equal("RU ss", vm.SupersampleLabel);
            Assert.Equal("RU msaa", vm.MsaaLabel);
        }
        finally
        {
            Cleanup(dir);
            Cleanup(locDir);
        }
    }

    private static (SettingsService service, string dir) NewService()
    {
        var dir = Path.Combine(Path.GetTempPath(), "hmvvm-" + Guid.NewGuid().ToString("N"));
        return (new SettingsService(dir), dir);
    }

    private static void Cleanup(string dir)
    {
        try { Directory.Delete(dir, recursive: true); } catch { }
    }
}
