
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

using System.Reactive.Subjects;
using CNC.Core.Abstractions;
using CNC.Core.Services;
using SenderNG.Modules.Plugins;

namespace SenderNG.Core.Tests;

/// <summary>
/// Regression tests for the shared settings store: every view model must mutate
/// the same AppSettings graph, otherwise a later Save() from one VM resurrects a
/// stale snapshot and wipes sections saved by other VMs.
/// </summary>
public class SettingsSharingTests
{
    private static (string Dir, SettingsService Service) MakeSettings()
    {
        var dir = Path.Combine(Path.GetTempPath(), "senderng-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return (dir, new SettingsService(dir));
    }

    private sealed class RecordingGrbl : IGrblService
    {
        private readonly BehaviorSubject<MachineState> _subject = new(new MachineState(3));
        public List<string> Sent { get; } = [];
        public bool IsConnected => true;
        public IObservable<MachineState> State => _subject;
        public Task ConnectAsync(string port) => Task.CompletedTask;
        public void Disconnect() { }
        public void SendRealtime(byte code) => Sent.Add($"RT:{code:X2}");
        
public Task SendAsync(string line)
        {
            Sent.Add(line);
            return Task.CompletedTask;
        }
    }

    [Fact]
    public void Jog_save_after_macro_save_preserves_macros_on_disk()
    {
        var (dir, settings) = MakeSettings();
        var grbl = new RecordingGrbl();

        var macroVm = new MacroViewModel(settings, grbl);
        macroVm.AddMacroCommand.Execute(null); // auto-saves "Macro 1"

        var jogVm = new JogViewModel(grbl, settings);
        jogVm.IsSpeed2 = true; // 2000
        jogVm.Save(); // pre-fix this wrote a stale snapshot without Macros

        Assert.True(File.Exists(Path.Combine(dir, "settings.json")));
        var json = File.ReadAllText(Path.Combine(dir, "settings.json"));
        Assert.Contains("Macros", json);
        Assert.Contains("Macro 1", json);
        Assert.Contains("Jog", json);
        Assert.Contains("2000", json);
    }

    // NOTE: there is no "Offsets" settings-sharing test here anymore — the legacy
    // settings-backed Fixed offsets editor was removed; offsets now round-trip to
    // the controller through IGrblService, with no local settings persistence.

    [Fact]
    public void Macro_save_after_probe_profile_save_preserves_settings_sections()
    {
        var (dir, settings) = MakeSettings();
        var grbl = new RecordingGrbl();

        // Probing persists profiles to its own probing.json and must not touch
        // the shared settings.json graph (so a later macro save keeps both).
        var probeVm = new ProbeViewModel(grbl, settings);
        probeVm.NewProfileName = "p";
        probeVm.ProfileAddCommand.Execute(null);

        var macroVm = new MacroViewModel(settings, grbl);
        macroVm.AddMacroCommand.Execute(null);

        var json = File.ReadAllText(Path.Combine(dir, "settings.json"));
        Assert.Contains("Macros", json);
        Assert.True(File.Exists(Path.Combine(dir, "probing.json")));
    }
}
