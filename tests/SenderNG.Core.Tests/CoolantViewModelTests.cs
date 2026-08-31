using System.Reactive.Linq;
using System.Reactive.Subjects;
using CNC.Core.Abstractions;
using CNC.Core.Services;
using SenderNG.Modules.Plugins;

namespace SenderNG.Core.Tests;

public class CoolantViewModelTests
{
    private sealed class RecordingGrbl(List<string> sent) : IGrblService
    {
        private readonly BehaviorSubject<MachineState> _subject = new(new MachineState(3));
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
    public void Coolant_starts_off()
    {
        var vm = new CoolantViewModel(new RecordingGrbl([]));

        Assert.False(vm.IsFlood);
        Assert.False(vm.IsMist);
    }

    [Fact]
    public void Toggle_commands_send_realtime_override_bytes()
    {
        var sent = new List<string>();
        var vm = new CoolantViewModel(new RecordingGrbl(sent));

        vm.ToggleFloodCommand.Execute(null);
        vm.ToggleMistCommand.Execute(null);

        Assert.Equal(["RT:A0", "RT:A1"], sent);
    }

    [Fact]
    public void State_subscription_mirrors_machine_coolant()
    {
        var grbl = new RecordingGrbl([]);
        var vm = new CoolantViewModel(grbl);

        grbl.Push(new MachineState(3) { Flood = true });
        Assert.True(vm.IsFlood);
        Assert.False(vm.IsMist);

        grbl.Push(new MachineState(3) { Flood = true, Mist = true });
        Assert.True(vm.IsFlood);
        Assert.True(vm.IsMist);

        grbl.Push(new MachineState(3) { Mist = true });
        Assert.False(vm.IsFlood);
        Assert.True(vm.IsMist);
    }
}

public class CoolantCapabilityVisibilityTests
{
    /// <summary>Test double that overrides Capabilities to simulate classic grbl (no M7).</summary>
    private sealed class CapsGrbl(ControllerCapabilities caps) : IGrblService
    {
        private readonly System.Reactive.Subjects.BehaviorSubject<MachineState> _sub =
            new(new MachineState(3));
        public bool IsConnected => true;
        public System.IObservable<MachineState> State => _sub;
        public ControllerCapabilities Capabilities => caps;
        public Task ConnectAsync(string port) => Task.CompletedTask;
        public void Disconnect() { }
        public void SendRealtime(byte code) { }
        public Task SendAsync(string line) => Task.CompletedTask;
    }

    [Fact]
    public void Classic_grbl_without_m7_hides_mist_toggle()
    {
        var grbl = new CapsGrbl(new ControllerCapabilities { IsGrblHAL = false, CoolantMist = false });
        var vm = new CoolantViewModel(grbl);

        Assert.True(vm.HasFlood);
        Assert.False(vm.HasMist);
    }

    [Fact]
    public void GrblHAL_shows_both_coolant_toggles()
    {
        var grbl = new CapsGrbl(ControllerCapabilities.Full);
        var vm = new CoolantViewModel(grbl);

        Assert.True(vm.HasFlood);
        Assert.True(vm.HasMist);
    }
}

public class DemoGrblServiceCoolantTests
{
    [Fact]
    public async Task Realtime_overrides_toggle_flood_and_mist_independently()
    {
        var svc = new DemoGrblService(new AppSettings());
        await svc.ConnectAsync("demo");

        svc.SendRealtime(GrblRealtime.CoolantFloodToggle);
        var st = await svc.State.FirstAsync();
        Assert.True(st.Flood);
        Assert.False(st.Mist);

        // Mist on while flood stays on (M9's both-off problem avoided).
        svc.SendRealtime(GrblRealtime.CoolantMistToggle);
        st = await svc.State.FirstAsync();
        Assert.True(st.Flood);
        Assert.True(st.Mist);

        // Flood off alone.
        svc.SendRealtime(GrblRealtime.CoolantFloodToggle);
        st = await svc.State.FirstAsync();
        Assert.False(st.Flood);
        Assert.True(st.Mist);
    }
}
