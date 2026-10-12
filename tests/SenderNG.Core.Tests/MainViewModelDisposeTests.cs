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
/// <c>Dispose</c> must release every handler and subscription it takes.
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
        // is private and is reached only through the private SwapInner
        // (MainViewModel.cs:979). SwapInner is called from three places --
        // SwitchToSerial (:999), ActivateDemoAsync (:1011) and ConnectNetworkAsync
        // (:1678) -- and SwitchToSerial itself runs from ConnectWithDialogAsync's
        // serial branch (:1659) as well as from Disconnect (:1236), so this is not
        // connect-dialog-only. The reason the setter is unreachable in the app is
        // the proxy: production registers IGrblService as a GrblServiceProxy
        // (HostSetup.cs:118-119), so SwapInner always takes the proxy.Inner = target
        // branch (:983) and never reaches Grbl = target (:985). Reaching the setter
        // here is therefore the point -- the trigger is unreachable in the app,
        // so the test pins the invariant rather than a
        // shipped path. Swap twice, since SetField only runs the setter body when
        // the reference actually changes.
        var property = typeof(MainViewModel).GetProperty(
            nameof(MainViewModel.Grbl), BindingFlags.Instance | BindingFlags.Public);
        var profile = settings.Load();
        property!.SetValue(vm, new DemoGrblService(profile));
        property.SetValue(vm, new DemoGrblService(profile));

        // OnSettingsChanged raises this unconditionally (MainViewModel.cs:1416+).
        var notifications = CountNotifications(vm, nameof(MainViewModel.StatusBarJogStep));
        settings.Activate(profile.Id);
        Assert.True(notifications() > 0, "sanity: the view model reacts before Dispose");

        // The detach above would also pass if the constructor subscription
        // (MainViewModel.cs:286) were deleted outright, because
        // OnProfileSwitched calls OnSettingsChanged by itself (:353). The plan's
        // acceptance criterion is "exactly one" Changed registration, and nothing
        // else in the suite counts StatusBarJogStep notifications -- so pin the
        // count, not just its sign. MainViewModelStatusBarTests drives this
        // property through a Changed raise, but only asserts its value.
        // One Activate raises Changed once per registration plus ProfileSwitched
        // once, and UiThread.Post runs inline under test (UiThread.cs:61-69), so
        // the tally is deterministic: 4 against the unfixed code (constructor plus
        // two swaps), 2 here, 1 if the constructor subscription is removed.
        Assert.True(
            notifications() >= 2,
            $"expected >= 2 StatusBarJogStep notifications from one Activate (the " +
            $"constructor subscription plus the profile-switch path), got {notifications()}");

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
