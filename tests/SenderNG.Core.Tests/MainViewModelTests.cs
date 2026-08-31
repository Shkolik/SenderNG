using System.Reactive.Subjects;
using CNC.Core.Abstractions;
using CNC.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using SenderNG.Avalonia.ViewModels;
using SenderNG.Core.Modules;

namespace SenderNG.Core.Tests;

public class MainViewModelTests
{
    private static AppSettings PortSettings(string port)
    {
        var s = new AppSettings();
        s.Current.Port = port;
        return s;
    }

    private sealed class StubModule : IModule
    {
        public string Id => "stub";
        public string Name => "Stub";
        public int Order => 0;
        public IReadOnlyList<HotKey> HotKeys { get; } = [];
        public void RegisterServices(IServiceCollection services) { }
        public void RegisterSettings(AppSettings settings) { }
        public object CreateViewModel(IServiceProvider serviceProvider) => new object();
        public object CreateView(object viewModel) => new object();
    }

    private static SettingsService MakeSettings()
    {
        var dir = Path.Combine(Path.GetTempPath(), "senderng-tests-" + Guid.NewGuid().ToString("N"));
        return new SettingsService(dir);
    }

    private static ServiceProvider MakeProvider(SettingsService settings)
    {
        var services = new ServiceCollection();
        services.AddSingleton(settings);
        services.AddSingleton(_ => new DemoGrblService(settings.Load()));
        services.AddSingleton<RealGrblService>();
        return services.BuildServiceProvider();
    }

    [Fact]
    public void MainViewModel_IsDemo_true_initially_when_demo_service_registered()
    {
        var settings = MakeSettings();
        var vm = new MainViewModel(new DemoGrblService(new AppSettings()), settings, MakeProvider(settings));

        Assert.True(vm.IsDemo);
        Assert.IsType<DemoGrblService>(vm.Grbl);
    }

    [Fact]
    public void MainViewModel_seeds_modules_from_injected_modules()
    {
        var settings = MakeSettings();
        IModule[] modules = [new StubModule(), new StubModule()];
        var vm = new MainViewModel(new DemoGrblService(new AppSettings()), settings, MakeProvider(settings), modules: modules);

        Assert.Equal(2, vm.Modules.Count);
        Assert.All(vm.Modules, m => Assert.IsType<StubModule>(m));
    }

    [Fact]
    public void MainViewModel_toggles_demo_and_swaps_service_reusing_di_singletons()
    {
        var settings = MakeSettings();
        var provider = MakeProvider(settings);
        var vm = new MainViewModel(provider.GetRequiredService<DemoGrblService>(), settings, provider);

        vm.DemoToggleCommand.Execute(null);

        Assert.False(vm.IsDemo);
        Assert.Same(provider.GetRequiredService<RealGrblService>(), vm.Grbl);

        vm.DemoToggleCommand.Execute(null);

        Assert.True(vm.IsDemo);
        Assert.Same(provider.GetRequiredService<DemoGrblService>(), vm.Grbl);
    }

    [Fact]
    public void MainViewModel_toggles_proxy_inner_and_keeps_proxy_instance()
    {
        var settings = MakeSettings();
        var provider = MakeProvider(settings);
        var proxy = new GrblServiceProxy(provider.GetRequiredService<DemoGrblService>());
        var vm = new MainViewModel(proxy, settings, provider);

        Assert.True(vm.IsDemo);
        Assert.Same(proxy, vm.Grbl);

        vm.DemoToggleCommand.Execute(null);

        Assert.False(vm.IsDemo);
        Assert.Same(provider.GetRequiredService<RealGrblService>(), proxy.Inner);

        vm.DemoToggleCommand.Execute(null);

        Assert.True(vm.IsDemo);
        Assert.Same(provider.GetRequiredService<DemoGrblService>(), proxy.Inner);
    }

    [Fact]
    public void SelectModuleCommand_builds_view_via_factory()
    {
        var settings = MakeSettings();
        IModule[] modules = [new StubModule()];
        Func<IModule, object> factory = m => new ViewStub();
        var vm = new MainViewModel(new DemoGrblService(new AppSettings()), settings, MakeProvider(settings), modules: modules, viewFactory: factory);

        Assert.Null(vm.SelectedModule);
        Assert.Null(vm.CurrentModuleView);

        vm.SelectModuleCommand.Execute(modules[0]);

        Assert.Same(modules[0], vm.SelectedModule);
        Assert.IsType<ViewStub>(vm.CurrentModuleView);
    }

    [Fact]
    public void SelectModuleCommand_ignores_null_parameter()
    {
        var settings = MakeSettings();
        var vm = new MainViewModel(new DemoGrblService(new AppSettings()), settings, MakeProvider(settings), viewFactory: _ => new ViewStub());

        vm.SelectModuleCommand.Execute(null);

        Assert.Null(vm.SelectedModule);
        Assert.Null(vm.CurrentModuleView);
    }

    private sealed class ViewStub;

    [Fact]
    public void Startup_state_is_not_connected_with_clear_indicators()
    {
        var settings = MakeSettings();
        var vm = new MainViewModel(new RealGrblService(), settings, MakeProvider(settings));

        Assert.False(vm.IsDemo);       // the app no longer starts in demo mode
        Assert.False(vm.IsConnected);
        Assert.Equal("NOT CONNECTED", vm.MachineStateDisplay);
        Assert.Equal("Not connected", vm.ConnectionStatus);
        Assert.Contains("Not connected", vm.Status);
        Assert.Contains("Not connected", vm.Title);
    }

    [Fact]
    public void Startup_with_proxy_on_real_service_is_not_demo()
    {
        var settings = MakeSettings();
        var provider = MakeProvider(settings);
        var proxy = new GrblServiceProxy(provider.GetRequiredService<RealGrblService>());
        var vm = new MainViewModel(proxy, settings, provider);

        Assert.False(vm.IsDemo);
        Assert.False(vm.IsConnected);
        Assert.Same(proxy, vm.Grbl);
    }

    [Fact]
    public async Task Connect_dialog_demo_choice_activates_demo_mode()
    {
        var settings = MakeSettings();
        var provider = MakeProvider(settings);
        var dialogs = new StubDialogService();
        var vm = new MainViewModel(provider.GetRequiredService<DemoGrblService>(), settings, provider, dialogs: dialogs);

        Assert.False(vm.IsConnected);
        Assert.Equal("NOT CONNECTED", vm.MachineStateDisplay);

        var task = vm.ShowConnectDialogAsync();
        var connectVm = Assert.IsType<CNC.Core.ViewModels.ConnectViewModel>(dialogs.LastVm);
        connectVm.SelectedIndex = (int)CNC.Core.ViewModels.ConnectMode.Demo;
        connectVm.ConnectCommand.Execute(null);
        await task;

        Assert.True(vm.IsDemo);
        Assert.True(vm.IsConnected);
        Assert.Contains("Demo mode", vm.Status);
    }

    [Fact]
    public async Task Connect_dialog_cancel_leaves_app_disconnected()
    {
        var settings = MakeSettings();
        var provider = MakeProvider(settings);
        var dialogs = new StubDialogService();
        var vm = new MainViewModel(provider.GetRequiredService<RealGrblService>(), settings, provider, dialogs: dialogs);

        var task = vm.ShowConnectDialogAsync();
        var connectVm = Assert.IsType<CNC.Core.ViewModels.ConnectViewModel>(dialogs.LastVm);
        connectVm.CancelCommand.Execute(null);
        await task;

        Assert.False(vm.IsConnected);
        Assert.Equal("Connect cancelled", vm.Status);
    }

    [Fact]
    public async Task Connect_dialog_network_choice_connects_over_tcp()
    {
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
            var settings = MakeSettings();
            var provider = MakeProvider(settings);
            var dialogs = new StubDialogService();
            var vm = new MainViewModel(provider.GetRequiredService<RealGrblService>(), settings, provider, dialogs: dialogs);

            var task = vm.ShowConnectDialogAsync();
            var connectVm = Assert.IsType<CNC.Core.ViewModels.ConnectViewModel>(dialogs.LastVm);
            connectVm.SelectedIndex = (int)CNC.Core.ViewModels.ConnectMode.Network;
            connectVm.NetworkEndpoint = $"127.0.0.1:{port}";
            connectVm.ConnectCommand.Execute(null);
            await task;

            Assert.False(vm.IsDemo);
            Assert.True(vm.IsConnected);
            Assert.Contains($"Connected: 127.0.0.1:{port}", vm.Status);
            // Only successful connects are persisted, into the global settings.
            var saved = new SettingsService(settings.SettingsDir).Load();
            Assert.Equal($"127.0.0.1:{port}", saved.LastNetworkEndpoint);
        }
        finally
        {
            listener.Stop();
        }
    }

    private sealed class StubDialogService : IDialogService
    {
        public object? LastVm { get; private set; }

        public Task<bool?> ShowDialogAsync(string title, object viewModel, (double Width, double Height)? preferredSize = null)
        {
            LastVm = viewModel;
            var tcs = new TaskCompletionSource<bool?>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (viewModel is IClosableDialog closable)
                closable.CloseRequested += (_, r) => tcs.TrySetResult(r);
            return tcs.Task;
        }
    }

    [Fact]
    public async Task ConnectCommand_sets_status_connected_on_success()
    {
        var settings = MakeSettings();
        var vm = new MainViewModel(new DemoGrblService(new AppSettings()), settings, MakeProvider(settings));

        vm.ConnectCommand.Execute(null);
        await Task.Yield();

        Assert.Contains("Connected", vm.Status);
    }

    [Fact]
    public async Task Connect_without_dialog_connects_to_persisted_port()
    {
        // With no dialog service (unit tests) Connect falls back to the persisted
        // port directly; port selection moved into the ConnectViewModel dialog.
        var settings = MakeSettings();
        settings.Save(PortSettings("COM7"));
        var vm = new MainViewModel(new DemoGrblService(new AppSettings()), settings, MakeProvider(settings));

        vm.ConnectCommand.Execute(null);
        await Task.Yield();

        Assert.Contains("COM7", vm.Status);
    }

    [Fact]
    public async Task Successful_serial_connect_persists_globally()
    {
        var settings = MakeSettings();
        settings.Save(PortSettings("COM7"));
        var vm = new MainViewModel(new DemoGrblService(new AppSettings()), settings, MakeProvider(settings));

        vm.ConnectCommand.Execute(null);
        await Task.Yield();

        var saved = new SettingsService(settings.SettingsDir).Load();
        Assert.Equal("COM7", saved.LastSerialPort);
    }

    [Fact]
    public async Task Failed_connect_does_not_persist()
    {
        var settings = MakeSettings();
        settings.Save(PortSettings("nonexistent-port-xyz"));
        var vm = new MainViewModel(new RealGrblService(), settings, MakeProvider(settings));

        vm.ConnectCommand.Execute(null);
        await Task.Yield();

        var saved = new SettingsService(settings.SettingsDir).Load();
        Assert.Empty(saved.LastSerialPort);
        Assert.StartsWith("Connection failed", vm.Status);
    }

    [Fact]
    public async Task ConnectCommand_sets_status_failed_and_does_not_throw_on_bad_port()
    {
        var settings = MakeSettings();
        settings.Save(PortSettings("nonexistent-port-xyz"));
        var vm = new MainViewModel(new RealGrblService(), settings, MakeProvider(settings));

        vm.ConnectCommand.Execute(null);
        await Task.Yield();

        Assert.StartsWith("Connection failed", vm.Status);
    }
}

public class MainViewModelLiveVisibilityTests
{
    private sealed class FlyoutStub(string id) : IModule
    {
        public string Id => id;
        public string Name => id;
        public int Order => 0;
        public PanelHint Panel => PanelHint.Flyout;
        public IReadOnlyList<HotKey> HotKeys { get; } = [];
        public void RegisterServices(IServiceCollection services) { }
        public void RegisterSettings(AppSettings settings) { }
        public object CreateViewModel(IServiceProvider serviceProvider) => new object();
        public object CreateView(object viewModel) => new object();
    }

    [Fact]
    public void Saving_hidden_module_removes_flyout_button_immediately()
    {
        var dir = Path.Combine(Path.GetTempPath(), "senderng-tests-" + Guid.NewGuid().ToString("N"));
        var settings = new SettingsService(dir);
        var services = new ServiceCollection();
        services.AddSingleton(settings);
        services.AddSingleton(_ => new DemoGrblService(settings.Load()));
        services.AddSingleton<RealGrblService>();
        var vm = new MainViewModel(
            new DemoGrblService(settings.Load()), settings, services.BuildServiceProvider(),
            modules: new IModule[] { new FlyoutStub("macro"), new FlyoutStub("dummy") });

        Assert.Contains(vm.FlyoutModules, m => m.Module.Id == "macro");

        // App Settings dialog does exactly this: write map + Save.
        settings.Load().Current.ModulesVisibility["macro"] = false;
        settings.Save(settings.Load());

        Assert.DoesNotContain(vm.FlyoutModules, m => m.Module.Id == "macro");
    }
}

public class MainViewModelCycleControlTests
{
    private sealed class RecordingRealtimeGrbl : IGrblService
    {
        public List<byte> Realtime { get; } = [];
        public bool IsConnected => false;
        public IObservable<MachineState> State => System.Reactive.Linq.Observable.Empty<MachineState>();
        public Task ConnectAsync(string port) => Task.CompletedTask;
        public void Disconnect() { }
        public Task SendAsync(string line) => Task.CompletedTask;
        public void SendRealtime(byte code) => Realtime.Add(code);
    }

    private static SettingsService MakeSettings()
    {
        var dir = Path.Combine(Path.GetTempPath(), "senderng-tests-" + Guid.NewGuid().ToString("N"));
        return new SettingsService(dir);
    }

    private static ServiceProvider MakeProvider(SettingsService settings)
    {
        var services = new ServiceCollection();
        services.AddSingleton(settings);
        services.AddSingleton(_ => new DemoGrblService(settings.Load()));
        services.AddSingleton<RealGrblService>();
        return services.BuildServiceProvider();
    }

    [Fact]
    public void Cycle_controls_send_realtime_bytes()
    {
        var settings = MakeSettings();
        var grbl = new RecordingRealtimeGrbl();
        var vm = new MainViewModel(grbl, settings, MakeProvider(settings));

        vm.CycleStartCommand.Execute(null);
        vm.FeedHoldCommand.Execute(null);
        vm.StopCommand.Execute(null);

        Assert.Equal([GrblRealtime.CycleStart, GrblRealtime.FeedHold, GrblRealtime.Reset], grbl.Realtime);
    }

    [Fact]
    public void Rewind_command_resets_loaded_program_rows()
    {
        var settings = MakeSettings();
        var services = new ServiceCollection();
        services.AddSingleton(settings);
        services.AddSingleton(_ => new DemoGrblService(settings.Load()));
        services.AddSingleton<RealGrblService>();
        services.AddSingleton<SenderNG.Avalonia.Modules.Fixed.ProgramViewModel>();
        var provider = services.BuildServiceProvider();

        var program = provider.GetRequiredService<SenderNG.Avalonia.Modules.Fixed.ProgramViewModel>();
        program.LoadGCode("G0 X0\nG0 Y0\nM30");
        program.ProgramRows[0] = program.ProgramRows[0] with { Status = "\u2713" };

        var vm = new MainViewModel(new DemoGrblService(settings.Load()), settings, provider);
        vm.RewindCommand.Execute(null);

        Assert.All(program.ProgramRows, r => Assert.Equal("\u00b7", r.Status));
        Assert.Equal("Program rewound", vm.Status);
    }

    [Fact]
    public void Rewind_command_without_program_reports_status()
    {
        var settings = MakeSettings();
        var vm = new MainViewModel(new RecordingRealtimeGrbl(), settings, MakeProvider(settings));

        vm.RewindCommand.Execute(null);

        Assert.Equal("Rewind: no program loaded", vm.Status);
    }
}

public class MainViewModelCycleEnablementTests
{
    private sealed class RecordingRealtimeGrbl : IGrblService
    {
        public bool IsConnected => false;
        public IObservable<MachineState> State => System.Reactive.Linq.Observable.Empty<MachineState>();
        public Task ConnectAsync(string port) => Task.CompletedTask;
        public void Disconnect() { }
        public Task SendAsync(string line) => Task.CompletedTask;
        public void SendRealtime(byte code) { }
    }

    private static SettingsService MakeSettings()
    {
        var dir = Path.Combine(Path.GetTempPath(), "senderng-tests-" + Guid.NewGuid().ToString("N"));
        return new SettingsService(dir);
    }

    private static ServiceProvider MakeProvider(SettingsService settings, bool withProgram = false)
    {
        var services = new ServiceCollection();
        services.AddSingleton(settings);
        services.AddSingleton(_ => new DemoGrblService(settings.Load()));
        services.AddSingleton<RealGrblService>();
        if (withProgram)
            services.AddSingleton<SenderNG.Avalonia.Modules.Fixed.ProgramViewModel>();
        return services.BuildServiceProvider();
    }

    [Fact]
    public void Start_and_Rewind_require_loaded_program()
    {
        var settings = MakeSettings();
        var vm = new MainViewModel(new RecordingRealtimeGrbl(), settings, MakeProvider(settings));

        Assert.False(vm.CycleStartCommand.CanExecute(null));
        Assert.False(vm.RewindCommand.CanExecute(null));
    }

    [Fact]
    public void Start_and_Rewind_enabled_after_program_loads()
    {
        var settings = MakeSettings();
        var provider = MakeProvider(settings, withProgram: true);
        var vm = new MainViewModel(new RecordingRealtimeGrbl(), settings, provider);

        var program = provider.GetRequiredService<SenderNG.Avalonia.Modules.Fixed.ProgramViewModel>();
        program.LoadGCode("G0 X0" + "\nG0 Y0");

        Assert.True(vm.CycleStartCommand.CanExecute(null));
        Assert.True(vm.RewindCommand.CanExecute(null));
    }

    [Fact]
    public async Task Stop_enabled_only_in_hold_and_Hold_only_in_run()
    {
        var settings = MakeSettings();
        var svc = new DemoGrblService(settings.Load());
        var vm = new MainViewModel(svc, settings, MakeProvider(settings));
        await svc.ConnectAsync("demo");

        // Idle: neither makes sense.
        Assert.False(vm.FeedHoldCommand.CanExecute(null));
        Assert.False(vm.StopCommand.CanExecute(null));

        svc.SendRealtime(GrblRealtime.FeedHold); // -> Hold
        Assert.True(vm.StopCommand.CanExecute(null));
        Assert.False(vm.FeedHoldCommand.CanExecute(null));

        svc.SendRealtime(GrblRealtime.CycleStart); // -> Run
        Assert.True(vm.FeedHoldCommand.CanExecute(null));
        Assert.False(vm.StopCommand.CanExecute(null));

        svc.SendRealtime(GrblRealtime.Reset); // -> Idle
        Assert.False(vm.FeedHoldCommand.CanExecute(null));
        Assert.False(vm.StopCommand.CanExecute(null));
    }

    [Fact]
    public async Task State_indicator_tracks_machine_state()
    {
        var settings = MakeSettings();
        var svc = new DemoGrblService(settings.Load());
        var vm = new MainViewModel(svc, settings, MakeProvider(settings));
        await svc.ConnectAsync("demo");

        Assert.Equal("Idle", vm.MachineState);

        svc.SendRealtime(GrblRealtime.FeedHold);
        Assert.Equal("Hold", vm.MachineState);

        svc.SendRealtime(GrblRealtime.CycleStart);
        Assert.Equal("Run", vm.MachineState);
    }
}

public class MainViewModelStatusButtonsTests
{
    private sealed class RecordingGrbl : IGrblService
    {
        public List<string> Lines { get; } = [];
        public List<byte> Realtime { get; } = [];
        public bool IsConnected => true;
        public IObservable<MachineState> State => System.Reactive.Linq.Observable.Empty<MachineState>();
        public Task ConnectAsync(string port) => Task.CompletedTask;
        public void Disconnect() { }
        public Task SendAsync(string line) { Lines.Add(line); return Task.CompletedTask; }
        public void SendRealtime(byte code) => Realtime.Add(code);
    }

    private static SettingsService MakeSettings()
    {
        var dir = Path.Combine(Path.GetTempPath(), "senderng-tests-" + Guid.NewGuid().ToString("N"));
        return new SettingsService(dir);
    }

    private static ServiceProvider MakeProvider(SettingsService settings)
    {
        var services = new ServiceCollection();
        services.AddSingleton(settings);
        services.AddSingleton(_ => new DemoGrblService(settings.Load()));
        services.AddSingleton<RealGrblService>();
        services.AddSingleton<SenderNG.Avalonia.Modules.Fixed.ProgramViewModel>();
        return services.BuildServiceProvider();
    }

    [Fact]
    public void Home_unlock_reset_send_expected_commands()
    {
        var settings = MakeSettings();
        var grbl = new RecordingGrbl();
        var vm = new MainViewModel(grbl, settings, MakeProvider(settings));

        vm.HomeCommand.Execute(null);
        vm.UnlockCommand.Execute(null);
        vm.ResetCommand.Execute(null);

        Assert.Equal(["$H", "$X"], grbl.Lines);
        Assert.Equal([GrblRealtime.Reset], grbl.Realtime);
    }

    [Fact]
    public void Check_command_sends_dollar_C()
    {
        var settings = MakeSettings();
        var grbl = new RecordingGrbl();
        var vm = new MainViewModel(grbl, settings, MakeProvider(settings));

        vm.CheckCommand.Execute(null);

        Assert.Equal(["$C"], grbl.Lines);
    }

    [Fact]
    public void Signal_strip_gates_optional_signals_on_controller_report()
    {
        // A controller that reports NO optional signals (classic grbl, $I without
        // [SIGNALS:]/[OPT:]+) shows only axis limits + the always-on H/S/R/P set —
        // mirroring ioSender, which hides door/e-stop/motor LEDs it can't drive.
        var settings = MakeSettings();
        var loaded = settings.Load();
        loaded.Current.DemoAxes = 3;
        settings.Save(loaded);
        var grbl = new RecordingGrbl(); // Capabilities = Unknown -> OptionalSignals = Off
        var vm = new MainViewModel(grbl, settings, MakeProvider(settings));

        // 3 axis/limit LEDs + always-on H/S/R/P; no optional LEDs.
        Assert.Equal(["X", "Y", "Z", "H", "S", "R", "P"],
            vm.SignalIndicators.Select(s => s.Letter));
        Assert.DoesNotContain(vm.SignalIndicators, s => s.Flag == CNC.Core.Signals.SafetyDoor);
        Assert.DoesNotContain(vm.SignalIndicators, s => s.Flag == CNC.Core.Signals.EStop);
        Assert.DoesNotContain(vm.SignalIndicators, s => s.Flag == CNC.Core.Signals.MotorFault);

        // The grouped collections split axes (left) from controls (right).
        Assert.Equal(["X", "Y", "Z"], vm.AxisSignalIndicators.Select(s => s.Letter));
        Assert.Equal(["H", "S", "R", "P"], vm.ControlSignalIndicators.Select(s => s.Letter));
    }

    [Fact]
    public void Signal_strip_shows_optional_signals_the_controller_reports()
    {
        // A grblHAL board reporting door + e-stop in $I gains those LEDs.
        var settings = MakeSettings();
        var subject = new System.Reactive.Subjects.BehaviorSubject<MachineState>(
            new MachineState(3));
        var grbl = new BehaviorGrbl(subject);
        var vm = new MainViewModel(grbl, settings, MakeProvider(settings));

        subject.OnNext(new MachineState(3)
        {
            OptionalSignals = CNC.Core.Signals.SafetyDoor | CNC.Core.Signals.EStop,
            Signals = CNC.Core.Signals.SafetyDoor | CNC.Core.Signals.Probe,
        });

        var door = vm.SignalIndicators.Single(s => s.Flag == CNC.Core.Signals.SafetyDoor);
        var estop = vm.SignalIndicators.Single(s => s.Flag == CNC.Core.Signals.EStop);
        var probe = vm.SignalIndicators.Single(s => s.Flag == CNC.Core.Signals.Probe);
        var motorFault = vm.SignalIndicators.SingleOrDefault(s => s.Flag == CNC.Core.Signals.MotorFault);
        Assert.True(door.IsActive);
        Assert.True(probe.IsActive);
        Assert.False(estop.IsActive); // present (reported) but not tripped
        Assert.Null(motorFault);      // never reported -> LED not rendered

        // Reported optional signals land in the control group, not the axis group.
        Assert.All(vm.AxisSignalIndicators, s => Assert.Contains(s.Flag, new[]
            { CNC.Core.Signals.LimitX, CNC.Core.Signals.LimitY, CNC.Core.Signals.LimitZ }));
        Assert.Contains(vm.ControlSignalIndicators, s => s.Flag == CNC.Core.Signals.SafetyDoor);
    }

    private sealed class BehaviorGrbl(BehaviorSubject<MachineState> state) : IGrblService
    {
        public bool IsConnected => true;
        public IObservable<MachineState> State => state;
        public Task ConnectAsync(string port) => Task.CompletedTask;
        public void Disconnect() { }
        public Task SendAsync(string line) => Task.CompletedTask;
        public void SendRealtime(byte code) { }
    }

    [Fact]
    public async Task Rewind_enabled_only_when_idle()
    {
        var settings = MakeSettings();
        var provider = MakeProvider(settings);
        var svc = new DemoGrblService(settings.Load());
        var vm = new MainViewModel(svc, settings, provider);
        await svc.ConnectAsync("demo");

        var program = provider.GetRequiredService<SenderNG.Avalonia.Modules.Fixed.ProgramViewModel>();
        program.LoadGCode("G0 X0");

        // Idle: rewind makes sense (ioSender enables it when streaming is idle).
        Assert.True(vm.RewindCommand.CanExecute(null));

        svc.SendRealtime(GrblRealtime.FeedHold); // -> Hold
        Assert.False(vm.RewindCommand.CanExecute(null));

        svc.SendRealtime(GrblRealtime.CycleStart); // -> Run
        Assert.False(vm.RewindCommand.CanExecute(null));

        svc.SendRealtime(GrblRealtime.Reset); // -> Idle
        Assert.True(vm.RewindCommand.CanExecute(null));
    }
}
