/*
 * SettingsSaveFailureTests.cs - settings writes must never escape a binding setter
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
using Microsoft.Extensions.DependencyInjection;
using SenderNG.Modules.Fixed;
using SenderNG.Modules.Plotter.GL.Settings;
using SenderNG.Modules.Plotter.Settings;
using SenderNG.Modules.Plugins;
using SenderNG.Modules.Shared;
using Xunit;

namespace SenderNG.Core.Tests;

public class SettingsSaveFailureTests
{
    private static (string Dir, SettingsService Settings) MakeSettings()
    {
        var dir = Path.Combine(Path.GetTempPath(), "senderng-savefail-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return (dir, new SettingsService(dir));
    }

    /// <summary>Replaces the settings directory with a FILE so the next
    /// Directory.CreateDirectory inside a save throws IOException.</summary>
    private static void Poison(string dir)
    {
        Directory.Delete(dir, recursive: true);
        File.WriteAllText(dir, "not a directory");
    }

    private static void Unpoison(string dir)
    {
        File.Delete(dir);
        Directory.CreateDirectory(dir);
    }

    [Fact]
    public void Jog_Save_survives_an_io_failure_and_keeps_working_after_recovery()
    {
        var (dir, settings) = MakeSettings();
        var vm = new JogViewModel(new JogViewModelTests.RecordingGrbl([]), settings);

        Poison(dir);
        vm.Save();  // must not throw
        vm.Save();  // second attempt must not be wedged by the _suppressReload/finally guard

        Unpoison(dir);
        var fired = false;
        settings.Changed += () => fired = true;
        vm.Save();
        Assert.True(fired); // the write path works again after recovery
    }

    [Fact]
    public void Plotter_Persist_survives_an_io_failure_and_keeps_working_after_recovery()
    {
        var (dir, settings) = MakeSettings();
        var vm = new PlotterSettingsViewModel(settings, null);

        Poison(dir);
        vm.ShowGrid = !vm.ShowGrid; // Persist()
        vm.ShowGrid = !vm.ShowGrid;

        Unpoison(dir);
        var fired = false;
        settings.Changed += () => fired = true;
        vm.ShowGrid = !vm.ShowGrid;
        Assert.True(fired);
    }

    [Fact]
    public void PlotterGl_Save_survives_an_io_failure()
    {
        var (_, settings) = MakeSettings();
        var vm = new PlotterGlSettingsViewModel(settings);
        Poison(settings.SettingsDir);
        vm.WireLength = 12345; // setter -> Save(); must not throw
    }

    [Fact]
    public void Macro_Save_survives_an_io_failure()
    {
        var (_, settings) = MakeSettings();
        var vm = new MacroViewModel(settings, new JogViewModelTests.RecordingGrbl([]));
        Poison(settings.SettingsDir);
        vm.Save(); // must not throw
    }

    [Fact]
    public void Outline_FeedRate_survives_an_io_failure()
    {
        var (_, settings) = MakeSettings();
        var vm = new OutlineViewModel(new JogViewModelTests.RecordingGrbl([]), settings,
            new ProgramViewModel());
        Poison(settings.SettingsDir);
        vm.FeedRate = vm.FeedRate + 1; // setter -> SaveFeedRate(); must not throw
    }

    [Fact]
    public void ConverterSettings_Save_survives_an_io_failure()
    {
        var (_, settings) = MakeSettings();
        var services = new ServiceCollection().AddSingleton(settings).BuildServiceProvider();
        var vm = new ConverterSettingsViewModel(services, "test-module", "Test");
        Poison(settings.SettingsDir);
        vm.ZHome = 42; // setter -> Save(); must not throw
    }
}
