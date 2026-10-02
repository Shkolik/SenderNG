using System.ComponentModel;
using System.Reactive.Subjects;
using CNC.Core.Abstractions;
using CNC.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using SenderNG.Core.Commands;
using SenderNG.Core.Localization;
using SenderNG.Core.Modules;
using SenderNG.Modules.Fixed;
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
        MakeHost(IGrblService? grbl = null)
    {
        var settings = new SettingsService(_dir);

        // Private localization instance: TestLocalization.Locale is shared, and its
        // Changed event has live subscribers from other tests' view models.
        var localization = LocalizationService.LoadFromDirectory(
            Path.Combine(AppContext.BaseDirectory, "Locale"));

        var services = new ServiceCollection();
        services.AddSingleton(settings);
        services.AddSingleton(_ => new DemoGrblService(settings.Load()));
        services.AddSingleton<RealGrblService>();
        services.AddSingleton(localization);
        var program = new ProgramViewModel(new DemoGrblService(settings.Load()));
        services.AddSingleton(program);

        var vm = new MainViewModel(
            grbl ?? new DemoGrblService(settings.Load()),
            settings,
            services.BuildServiceProvider());

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
        settings.Save(settings.Load());

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
    /// Minimal <see cref="IGrblService"/> with a pushable <c>State</c> and a real
    /// <c>ConnectionChanged</c> event (the interface's default is a no-op add/remove).
    /// <see cref="RaiseConnectionChanged"/> flips <see cref="IsConnected"/>, because
    /// the view model only notifies when the connection state actually CHANGES —
    /// a raise that reports the state the view model already has is silently
    /// swallowed by its set-field guard.
    /// </summary>
    private sealed class PushableGrbl : IGrblService
    {
        private readonly BehaviorSubject<MachineState> _state = new(new MachineState());
        private bool _connected;

        public event EventHandler? ConnectionChanged;

        public bool IsConnected => _connected;
        public IObservable<MachineState> State => _state;
        public Task ConnectAsync(string port) => Task.CompletedTask;
        public void Disconnect() => RaiseConnectionChanged();
        public Task SendAsync(string line) => Task.CompletedTask;
        public void SendRealtime(byte code) { }

        public void PushState(MachineState state) => _state.OnNext(state);
        public void RaiseConnectionChanged()
        {
            _connected = !_connected;
            ConnectionChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}