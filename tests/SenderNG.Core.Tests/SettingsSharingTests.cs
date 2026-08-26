using System.Reactive.Subjects;
using CNC.Core.Abstractions;
using CNC.Core.Services;
using SenderNG.Avalonia.Modules.Plugins;

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

    [Fact]
    public void Offsets_save_after_jog_save_preserves_jog_section()
    {
        var (dir, settings) = MakeSettings();

        var jogVm = new JogViewModel(new RecordingGrbl(), settings);
        jogVm.IsSpeed2 = true; // 2000
        jogVm.Save();

        var offsetsVm = new SenderNG.Avalonia.Modules.Fixed.OffsetsViewModel(settings);
        offsetsVm.Rows[0].X = 11.5;
        offsetsVm.SaveCommand.Execute(null);

        var json = File.ReadAllText(Path.Combine(dir, "settings.json"));
        Assert.Contains("Jog", json);
        Assert.Contains("2000", json);
        Assert.Contains("Offsets", json);
        Assert.Contains("G54", json);
    }

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
