using System.Reflection;
using System.ComponentModel;
using System.Reactive.Subjects;
using CNC.Core.Abstractions;
using CNC.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using SenderNG.Core.Commands;
using SenderNG.Core.Localization;
using SenderNG.Core.Modules;
using SenderNG.Modules.Fixed;
using SenderNG.Services;
using SenderNG.ViewModels;
using Xunit;

namespace SenderNG.Core.Tests;

/// <summary>
/// §2.3: <c>Dispose</c> must release every handler and subscription it takes.
/// These tests build a view model directly so disposal can be driven by hand.
/// </summary>
[Collection("LegacyStatics")]
public class MainViewModelDisposeTests
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "mvm-dispose-" + Guid.NewGuid().ToString("N"));

    public MainViewModelDisposeTests() => Directory.CreateDirectory(_dir);

    // Instance, not static: the settings directory is per-test (xunit builds a
    // fresh instance per test), so each view model gets its own profile store.
    private (SettingsService, LocalizationService, ProgramViewModel, MainViewModel)
        MakeHost(IGrblService? grbl = null, StatusSink? statusSink = null)
    {
        var settings = new SettingsService(_dir);

        // Private localization instance: TestLocalization.Locale is shared, and its
        // Changed event has live subscribers from other tests' view models.
        var localization = LocalizationService.LoadFromDirectory(
            Path.Combine(AppContext.BaseDirectory, "Locale"));

        // One IGrblService for the whole host, as in production. Registering a
        // second instance in the container while handing the first to the
        // constructor would bind the program view model (resolved internally)
        // to a service the test never pushes into.
        var active = grbl ?? new DemoGrblService(settings.Load());

        var services = new ServiceCollection();
        services.AddSingleton(settings);
        services.AddSingleton(_ => active);
        services.AddSingleton<RealGrblService>();
        services.AddSingleton(localization);
        var program = new ProgramViewModel(active);
        services.AddSingleton(program);

        var vm = new MainViewModel(
            active,
            settings,
            services.BuildServiceProvider(),
            statusSink: statusSink);

        return (settings, localization, program, vm);
    }

    [Fact]
    public void Dispose_clears_the_work_parameters_provider()
    {
        var (_, _, _, vm) = MakeHost();
        Assert.NotNull(CNC.Core.Machine.WorkParametersProvider);

        vm.Dispose();

        Assert.Null(CNC.Core.Machine.WorkParametersProvider);
    }

    [Fact]
    public void Dispose_keeps_a_provider_installed_by_another_view_model()
    {
        var (_, _, _, first) = MakeHost();
        var firstProvider = CNC.Core.Machine.WorkParametersProvider;
        var (_, _, _, second) = MakeHost();
        Assert.NotSame(firstProvider, CNC.Core.Machine.WorkParametersProvider); // sanity: the static moved

        // The ownership guard: the first view model no longer owns the static,
        // so its teardown must leave whoever installed it in place.
        first.Dispose();

        // Not an identity check on purpose: other test classes construct view
        // models in parallel and each one rewrites this static, so which provider
        // is installed is not stable here. The guard only has to guarantee that
        // THIS teardown never blanks it.
        Assert.NotNull(CNC.Core.Machine.WorkParametersProvider);

        second.Dispose();
    }

    [Fact]
    public void Dispose_is_idempotent()
    {
        var (_, _, _, vm) = MakeHost();

        Action act = () => { vm.Dispose(); vm.Dispose(); };

        Assert.Null(Record.Exception(act));
    }

    [Fact]
    public void Dispose_detaches_the_localization_handler()
    {
        var (_, localization, _, vm) = MakeHost();
        var notifications = CountNotifications(vm);
        localization.SetLocale("ru");
        Assert.True(notifications() > 0, "sanity: the VM reacts before Dispose");

        vm.Dispose();
        var baseline = notifications();
        localization.SetLocale("en");

        Assert.Equal(baseline, notifications());
    }

    [Fact]
    public void Dispose_detaches_the_settings_and_profile_handlers()
    {
        var (settings, _, _, vm) = MakeHost();
        var notifications = CountNotifications(vm);
        settings.Activate(settings.Load().Id);   // raises Changed + ProfileSwitched
        Assert.True(notifications() > 0, "sanity: the VM reacts before Dispose");

        vm.Dispose();
        var baseline = notifications();
        // Activate, not Save: only Activate raises ProfileSwitched as well as
        // Changed, so this covers BOTH detaches. Save would exercise only the
        // Changed one and leave the ProfileSwitched detach untested.
        settings.Activate(settings.Load().Id);   // raises Changed + ProfileSwitched

        Assert.Equal(baseline, notifications());
    }

    [Fact]
    public void Dispose_detaches_the_modules_collection_handler()
    {
        var (_, _, _, vm) = MakeHost();
        var notifications = CountNotifications(vm);
        vm.Modules.Add(new StubModule());
        Assert.True(notifications() > 0, "sanity: the VM reacts before Dispose");

        vm.Dispose();
        var baseline = notifications();
        vm.Modules.Add(new StubModule());

        Assert.Equal(baseline, notifications());
    }

    [Fact]
    public void Dispose_detaches_the_program_handlers()
    {
        var (_, _, program, vm) = MakeHost();
        var commandRaised = 0;
        ((DelegateCommand)vm.CycleStartCommand).CanExecuteChanged += (_, _) => commandRaised++;
        var rowNotifications = CountNotifications(vm, nameof(MainViewModel.StatusBarBlocks));
        program.LoadGCode("G0 X0\nM30");
        Assert.True(commandRaised > 0, "sanity: the VM reacts before Dispose");
        Assert.True(rowNotifications() > 0, "sanity: RowsChanged reached the VM");

        vm.Dispose();
        var commandBaseline = commandRaised;
        var rowBaseline = rowNotifications();
        program.LoadGCode("G0 X1\nG0 X2\nM30");

        Assert.Equal(commandBaseline, commandRaised);
        Assert.Equal(rowBaseline, rowNotifications());
    }

    [Fact]
    public void Dispose_disposes_the_state_subscription()
    {
        var grbl = new PushableGrbl();
        var (_, _, _, vm) = MakeHost(grbl);
        var notifications = CountNotifications(vm);
        grbl.PushState(new MachineState { Status = "Run" });
        Assert.True(notifications() > 0, "sanity: the VM reacts before Dispose");

        vm.Dispose();
        var baseline = notifications();
        grbl.PushState(new MachineState { Status = "Idle" });

        Assert.Equal(baseline, notifications());
    }

    [Fact]
    public void Dispose_disposes_the_lines_subscription()
    {
        var grbl = new PushableGrbl();
        var (_, _, _, vm) = MakeHost(grbl);
        // Status raises "Message", and its setter drops a repeat of the same
        // text, so each push uses a different error code.
        var notifications = CountNotifications(vm, nameof(MainViewModel.Message));
        grbl.PushLine("error:79");
        Assert.True(notifications() > 0, "sanity: the VM reacts before Dispose");

        vm.Dispose();
        var baseline = notifications();
        grbl.PushLine("error:78");

        Assert.Equal(baseline, notifications());
    }

    [Fact]
    public void Dispose_disposes_the_work_parameters_subscription()
    {
        var grbl = new PushableGrbl();
        var (_, _, _, vm) = MakeHost(grbl);
        // The snapshot is only published through this delegate, so hold the
        // pre-teardown reference: a dead subscription cannot move it.
        var provider = CNC.Core.Machine.WorkParametersProvider;
        Assert.NotNull(provider);
        var before = WorkParameters.CreateDefault(3);
        grbl.PushWorkParameters(before);
        Assert.Same(before, provider());

        vm.Dispose();
        var after = WorkParameters.CreateDefault(3);
        grbl.PushWorkParameters(after);

        Assert.Same(before, provider());
    }

    [Fact]
    public void Dispose_detaches_the_connection_changed_handler()
    {
        var grbl = new PushableGrbl();
        var (_, _, _, vm) = MakeHost(grbl);
        var notifications = CountNotifications(vm);
        grbl.RaiseConnectionChanged();
        Assert.True(notifications() > 0, "sanity: the VM reacts before Dispose");

        vm.Dispose();
        var baseline = notifications();
        grbl.RaiseConnectionChanged();

        Assert.Equal(baseline, notifications());
    }

    [Fact]
    public void Dispose_releases_the_status_sink()
    {
        var sink = new StatusSink();
        var (_, _, _, vm) = MakeHost(statusSink: sink);
        var notifications = CountNotifications(vm, nameof(MainViewModel.Message));
        sink.Set("reported while the view model is alive");
        Assert.True(notifications() > 0, "sanity: the sink reaches the VM before Dispose");

        vm.Dispose();
        var baseline = notifications();
        sink.Set("reported after teardown");

        Assert.Null(sink.Sink);
        Assert.Equal(baseline, notifications());
    }

    /// <summary>A non-proxy <c>Grbl</c> swap used to subscribe
    /// <c>SettingsService.Changed</c> a second time. <c>Dispose</c> removes one
    /// registration, so the survivor outlived the view model.</summary>
    [Fact]
    public void Dispose_releases_the_settings_subscription_after_a_grbl_swap()
    {
        var (settings, _, _, vm) = MakeHost();

        // Reflection, because there is no public path to this setter: Grbl's setter
        // is private and is only reached through the private SwapInner, which in
        // turn runs only from the connect-dialog flow (MainViewModel.cs:980-986,
        // :1012, :1679). Reaching it here is the point -- the audit records this
        // trigger as unreachable in the app, so the test pins the invariant rather
        // than a shipped path. Swap twice, since SetField only runs the setter body
        // when the reference actually changes.
        var property = typeof(MainViewModel).GetProperty(
            nameof(MainViewModel.Grbl), BindingFlags.Instance | BindingFlags.Public);
        var profile = settings.Load();
        property!.SetValue(vm, new DemoGrblService(profile));
        property.SetValue(vm, new DemoGrblService(profile));

        // OnSettingsChanged raises this unconditionally (MainViewModel.cs:1416+).
        var notifications = CountNotifications(vm, nameof(MainViewModel.StatusBarJogStep));
        settings.Activate(profile.Id);
        Assert.True(notifications() > 0, "sanity: the view model reacts before Dispose");

        vm.Dispose();
        var baseline = notifications();
        settings.Activate(profile.Id);
        settings.Activate(profile.Id);

        Assert.Equal(baseline, notifications());
    }

    /// <summary>Counts every (or one named) <see cref="INotifyPropertyChanged"/> notification.</summary>
    private static Func<int> CountNotifications(
        MainViewModel vm, string? propertyName = null)
    {
        var count = 0;
        vm.PropertyChanged += (_, e) =>
        {
            if (propertyName is null || e.PropertyName == propertyName)
                count++;
        };
        return () => count;
    }

    private sealed class StubModule : IModule
    {
        public string Id => "stub";
        public string Name => "Stub";
        public int Order => 0;
        public IReadOnlyList<HotKey> HotKeys { get; } = [];
        public void RegisterServices(IServiceCollection services) { }
        public object CreateViewModel(IServiceProvider serviceProvider) => new object();
        public object CreateView(object viewModel) => new object();
    }

    /// <summary>
    /// Minimal <see cref="IGrblService"/> with a pushable <c>State</c>,
    /// <c>ReceivedLines</c> and <c>WorkParametersChanges</c>, and a real
    /// <c>ConnectionChanged</c> event (the interface's default is a no-op add/remove).
    /// The two line/parameter streams are overridden because the interface ships
    /// them as <c>Observable.Empty</c> defaults, and disposing a subscription to
    /// an empty stream is unobservable.
    /// <see cref="RaiseConnectionChanged"/> flips <see cref="IsConnected"/>, because
    /// the view model only notifies when the connection state actually CHANGES —
    /// a raise that reports the state the view model already has is silently
    /// swallowed by its set-field guard.
    /// </summary>
    private sealed class PushableGrbl : IGrblService
    {
        private readonly BehaviorSubject<MachineState> _state = new(new MachineState());
        private readonly Subject<string> _lines = new();
        private readonly Subject<WorkParameters> _workParameters = new();
        private bool _connected;

        public event EventHandler? ConnectionChanged;

        public bool IsConnected => _connected;
        public IObservable<MachineState> State => _state;
        public IObservable<string> ReceivedLines => _lines;
        public IObservable<WorkParameters> WorkParametersChanges => _workParameters;
        public Task ConnectAsync(string port) => Task.CompletedTask;
        public void Disconnect() => RaiseConnectionChanged();
        public Task SendAsync(string line) => Task.CompletedTask;
        public void SendRealtime(byte code) { }

        public void PushState(MachineState state) => _state.OnNext(state);
        public void PushLine(string line) => _lines.OnNext(line);
        public void PushWorkParameters(WorkParameters parameters) => _workParameters.OnNext(parameters);
        public void RaiseConnectionChanged()
        {
            _connected = !_connected;
            ConnectionChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}