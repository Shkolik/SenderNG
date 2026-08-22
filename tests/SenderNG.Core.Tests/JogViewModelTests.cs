using System.Reactive.Subjects;
using CNC.Core.Abstractions;
using CNC.Core.Services;
using SenderNG.Avalonia.Modules.Plugins;

namespace SenderNG.Core.Tests;

public class JogViewModelTests
{
    private static SettingsService MakeSettings(string mode = "Mill")
    {
        var dir = Path.Combine(Path.GetTempPath(), "senderng-tests-" + Guid.NewGuid().ToString("N"));
        var settings = new SettingsService(dir);
        var app = settings.Load();
        app.Mode = mode;
        settings.Save(app);
        return settings;
    }

    private sealed class RecordingGrbl(List<string> sent) : IGrblService
    {
        private readonly BehaviorSubject<MachineState> _subject = new(new MachineState(4));
        public bool IsConnected => true;
        public IObservable<MachineState> State => _subject;
        public Task ConnectAsync(string port) => Task.CompletedTask;
        public void Disconnect() { }
        public Task SendAsync(string line)
        {
            sent.Add(line);
            return Task.CompletedTask;
        }
    }

    [Fact]
    public void Default_steps_and_speeds_are_populated()
    {
        var vm = new JogViewModel(new RecordingGrbl([]), MakeSettings());

        Assert.Equal([0.1, 1, 10], vm.StepDistances);
        Assert.Equal(1000, vm.XySpeed);
        Assert.Equal(500, vm.ZSpeed);
        Assert.Equal(1, vm.SelectedStep);
    }

    [Fact]
    public void Mill_jog_XPlus_sends_single_axis_jog()
    {
        var sent = new List<string>();
        var vm = new JogViewModel(new RecordingGrbl(sent), MakeSettings());
        vm.SelectedStepIndex = 2; // 10

        vm.JogXPlusCommand.Execute(null);

        var line = Assert.Single(sent);
        Assert.Equal("$J=G91 G0 X10 F1000", line);
    }

    [Fact]
    public void Mill_diagonal_moves_XY_together()
    {
        var sent = new List<string>();
        var vm = new JogViewModel(new RecordingGrbl(sent), MakeSettings());
        vm.SelectedStepIndex = 1; // 1

        vm.JogXYSwNECommand.Execute(null); // X+ Y+

        var line = Assert.Single(sent);
        Assert.Contains("X1", line);
        Assert.Contains("Y1", line);
        Assert.StartsWith("$J=G91 G0", line);
        Assert.Contains("F1000", line);
    }

    [Fact]
    public void Mill_Z_jog_uses_Z_speed()
    {
        var sent = new List<string>();
        var vm = new JogViewModel(new RecordingGrbl(sent), MakeSettings());

        vm.JogZPlusCommand.Execute(null);

        var line = Assert.Single(sent);
        Assert.Equal("$J=G91 G0 Z1 F500", line);
    }

    [Fact]
    public async Task Speeds_are_loaded_from_settings()
    {
        var settings = MakeSettings();
        var first = new JogViewModel(new RecordingGrbl([]), settings);
        first.XySpeed = 2500;
        first.ZSpeed = 750;
        first.Save();

        var second = new JogViewModel(new RecordingGrbl([]), settings);

        Assert.Equal(2500, second.XySpeed);
        Assert.Equal(750, second.ZSpeed);
        await Task.CompletedTask;
    }

    [Fact]
    public void Foam_padlock_ZA_sync()
    {
        var sent = new List<string>();
        var vm = new JogViewModel(new RecordingGrbl(sent), MakeSettings("Foam"));
        vm.PadlockEngaged = true;

        vm.JogZPlusCommand.Execute(null);

        var line = Assert.Single(sent);
        Assert.Contains("Z1", line);
        Assert.Contains("A1", line);
    }

    [Fact]
    public void Foam_without_padlock_Z_moves_alone()
    {
        var sent = new List<string>();
        var vm = new JogViewModel(new RecordingGrbl(sent), MakeSettings("Foam"));
        vm.PadlockEngaged = false;

        vm.JogZMinusCommand.Execute(null);

        var line = Assert.Single(sent);
        Assert.Contains("Z-1", line);
        Assert.DoesNotContain("A", line);
    }

    [Fact]
    public void Foam_B_is_linear_and_independent_of_padlock()
    {
        var sent = new List<string>();
        var vm = new JogViewModel(new RecordingGrbl(sent), MakeSettings("Foam"));
        vm.PadlockEngaged = true;

        vm.JogBPlusCommand.Execute(null);

        var line = Assert.Single(sent);
        Assert.Contains("B1", line);
        Assert.DoesNotContain("A", line);
        Assert.DoesNotContain("Z", line);
    }

    [Fact]
    public void Foam_A_jog_does_not_drag_Z()
    {
        var sent = new List<string>();
        var vm = new JogViewModel(new RecordingGrbl(sent), MakeSettings("Foam"));
        vm.PadlockEngaged = true;

        vm.JogAMinusCommand.Execute(null);

        var line = Assert.Single(sent);
        Assert.Contains("A-1", line);
        Assert.DoesNotContain("Z", line);
    }

    [Fact]
    public void Diagonal_available_in_foam_mode_too()
    {
        var sent = new List<string>();
        var vm = new JogViewModel(new RecordingGrbl(sent), MakeSettings("Foam"));

        vm.JogXYNEswCommand.Execute(null); // X- Y-

        var line = Assert.Single(sent);
        Assert.Contains("X-1", line);
        Assert.Contains("Y-1", line);
    }
}
