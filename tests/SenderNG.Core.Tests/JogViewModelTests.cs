using System.Reactive.Subjects;
using CNC.Core.Abstractions;
using CNC.Core.Services;
using SenderNG.Modules.Plugins;

namespace SenderNG.Core.Tests;

public class JogViewModelTests
{
    internal static SettingsService MakeSettings(string mode = "Mill")
    {
        var dir = Path.Combine(Path.GetTempPath(), "senderng-tests-" + Guid.NewGuid().ToString("N"));
        var settings = new SettingsService(dir);
        var app = settings.Load();
        app.Mode = mode;
        settings.Save(app);
        return settings;
    }

    internal sealed class RecordingGrbl(List<string> sent, int numAxes = 4) : IGrblService
    {
        private readonly BehaviorSubject<MachineState> _subject = new(new MachineState(numAxes));
        public bool IsConnected => true;
        public IObservable<MachineState> State => _subject;
        public Task ConnectAsync(string port) => Task.CompletedTask;
        public void Disconnect() { }
        public void SendRealtime(byte code) => sent.Add($"RT:{code:X2}");
        
public Task SendAsync(string line)
        {
            sent.Add(line);
            return Task.CompletedTask;
        }
        public void Push(MachineState state) => _subject.OnNext(state);
    }

    [Fact]
    public void Default_distances_speeds_and_indexes_are_populated()
    {
        var vm = new JogViewModel(new RecordingGrbl([]), MakeSettings());

        Assert.Equal([0.1, 1.0, 10.0], vm.Config.Distances);
        Assert.Equal([500.0, 1000.0, 2000.0], vm.Config.Speeds);
        Assert.Equal(1, vm.SelectedDistanceIndex);
        Assert.Equal(1, vm.SelectedSpeedIndex);
        Assert.Equal(1.0, vm.SelectedDistance);
        Assert.Equal(1000.0, vm.SelectedSpeed);
    }

    [Fact]
    public void Mill_axis_jog_applies_selected_distance()
    {
        var sent = new List<string>();
        var vm = new JogViewModel(new RecordingGrbl(sent), MakeSettings());
        vm.IsDistance2 = true; // 10

        vm.JogCommand.Execute("X+");

        Assert.Equal(["$J=G91 X10 F1000"], sent);
    }

    [Fact]
    public void Mill_diagonal_sends_both_axes()
    {
        var sent = new List<string>();
        var vm = new JogViewModel(new RecordingGrbl(sent), MakeSettings());
        vm.IsSpeed2 = true; // 2000

        vm.JogCommand.Execute("X-,Y+");

        Assert.Equal(["$J=G91 X-1 Y1 F2000"], sent);
    }

    [Fact]
    public void Foam_together_jogs_both_gantries_mirrored()
    {
        var sent = new List<string>();
        var vm = new JogViewModel(new RecordingGrbl(sent), MakeSettings("Foam"));
        vm.Together = true;

        vm.JogCommand.Execute("X-,Y+|Z-,A+");

        Assert.Equal(["$J=G91 X-1 Y1 Z-1 A1 F1000"], sent);
    }

    [Fact]
    public void Foam_not_together_ignores_second_pair()
    {
        var sent = new List<string>();
        var vm = new JogViewModel(new RecordingGrbl(sent), MakeSettings());

        vm.JogCommand.Execute("X+,Y-|Z+,A-");

        Assert.Equal(["$J=G91 X1 Y-1 F1000"], sent);
    }

    [Fact]
    public void Together_lock_disables_ZA_cross()
    {
        var vm = new JogViewModel(new RecordingGrbl([]), MakeSettings("Foam"));

        Assert.True(vm.ZaCrossEnabled);
        vm.Together = true;
        Assert.False(vm.ZaCrossEnabled);
        vm.Together = false;
        Assert.True(vm.ZaCrossEnabled);
    }

    [Fact]
    public void Distance_selection_persists_and_reloads()
    {
        var settings = MakeSettings();
        var first = new JogViewModel(new RecordingGrbl([]), settings);
        first.IsDistance2 = true;
        first.IsSpeed0 = true;

        var second = new JogViewModel(new RecordingGrbl([]), settings);

        Assert.Equal(2, second.SelectedDistanceIndex);
        Assert.Equal(10.0, second.SelectedDistance);
        Assert.Equal(0, second.SelectedSpeedIndex);
        Assert.Equal(500.0, second.SelectedSpeed);
    }

    [Fact]
    public void Configs_are_mode_aware()
    {
        var settings = MakeSettings("Mill");
        var millVm = new JogViewModel(new RecordingGrbl([]), settings);
        millVm.Config.Distances[0] = 0.05;
        millVm.Save();

        settings.Load().Mode = AppSettings.FoamMode;
        var foamVm = new JogViewModel(new RecordingGrbl([]), settings);

        Assert.Equal(0.1, foamVm.Config.Distances[0]); // Foam untouched
        var millView = new AppSettings { Mode = AppSettings.MillMode, Modules = settings.Load().Modules };
        Assert.Equal(0.05, JogConfigStore.Load(millView).Distances[0]); // Mill custom
    }

    [Fact]
    public void Linear_rows_track_machine_num_axes()
    {
        var grbl = new RecordingGrbl([], numAxes: 4);
        var vm = new JogViewModel(grbl, MakeSettings());

        Assert.Equal(["Z", "A"], vm.LinearAxisLetters.Select(r => r.Letter));

        grbl.Push(new MachineState(5));
        Assert.Equal(["Z", "A", "B"], vm.LinearAxisLetters.Select(r => r.Letter));
    }

    [Fact]
    public void Row_tags_combine_letter_and_sign()
    {
        var row = new LinearAxisRow("B");
        Assert.Equal("B+", row.PlusTag);
        Assert.Equal("B-", row.MinusTag);
    }

    [Fact]
    public void Stop_command_sends_jog_cancel_realtime_byte()
    {
        // Jog cancel (0x85) is the firmware's dedicated stop-jog command on classic
        // grbl 1.1, the mega5x fork and grblHAL: it returns to IDLE, while a feed
        // hold ('!') would leave the machine in HOLD.
        var sent = new List<string>();
        var vm = new JogViewModel(new RecordingGrbl(sent), MakeSettings());

        vm.StopCommand.Execute(null);

        Assert.Equal(["RT:85"], sent);
    }
}

public class JogLinearAxesTests
{
    [Fact]
    public void Mill_5axes_linear_rows_start_at_Z()
    {
        var vm = new JogViewModel(new JogViewModelTests.RecordingGrbl([], numAxes: 5), JogViewModelTests.MakeSettings());
        Assert.Equal(["Z", "A", "B"], vm.LinearAxisLetters.Select(a => a.Letter));
    }

    [Fact]
    public void Foam_5axes_linear_rows_start_at_B()
    {
        var vm = new JogViewModel(new JogViewModelTests.RecordingGrbl([], numAxes: 5), JogViewModelTests.MakeSettings("Foam"));
        var letters = vm.LinearAxisLetters.Select(a => a.Letter).ToList();
        Assert.Single(letters);
        Assert.Equal("B", letters[0]);
    }

    [Fact]
    public void Linear_rows_refresh_on_mode_switch()
    {
        var settings = JogViewModelTests.MakeSettings("Mill");
        var vm = new JogViewModel(new JogViewModelTests.RecordingGrbl([], numAxes: 5), settings);
        Assert.Equal(3, vm.LinearAxisLetters.Count);

        var app = settings.Load();
        app.Mode = AppSettings.FoamMode;
        settings.Save(app);

        Assert.Single(vm.LinearAxisLetters);
        Assert.Equal("B", vm.LinearAxisLetters[0].Letter);
    }
}

public class JogProxyInitialStateTests
{
    [Fact]
    public void Foam_axes_populate_immediately_through_proxy()
    {
        // Regression: through the proxy, the jog widget must see the machine's
        // axis count right away (foam 6 axes => crossed X/Y/Z/A + linear B/C),
        // not only after the first state emission.
        var proxy = new GrblServiceProxy(new JogViewModelTests.RecordingGrbl([], numAxes: 6));
        var vm = new JogViewModel(proxy, JogViewModelTests.MakeSettings("Foam"));

        Assert.Equal(["B", "C"], vm.LinearAxisLetters.Select(r => r.Letter));
    }
}
