
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
using Microsoft.Extensions.DependencyInjection;
using SenderNG.ViewModels;
using SenderNG.Core.Modules;

namespace SenderNG.Core.Tests;

public class MainViewModelTests
{
    private static MachineProfile PortSettings(string port) =>
        new() { Connection = new MachineConnection { SerialPort = port } };

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
        var vm = new MainViewModel(new DemoGrblService(new MachineProfile()), settings, MakeProvider(settings));

        Assert.True(vm.IsDemo);
        Assert.IsType<DemoGrblService>(vm.Grbl);
    }

    [Fact]
    public void MainViewModel_seeds_modules_from_injected_modules()
    {
        var settings = MakeSettings();
        IModule[] modules = [new StubModule(), new StubModule()];
        var vm = new MainViewModel(new DemoGrblService(new MachineProfile()), settings, MakeProvider(settings), modules: modules);

        Assert.Equal(2, vm.Modules.Count);
        Assert.All(vm.Modules, m => Assert.IsType<StubModule>(m));
    }

    [Fact]
    public void SelectModuleCommand_builds_view_via_factory()
    {
        var settings = MakeSettings();
        IModule[] modules = [new StubModule()];
        Func<IModule, object> factory = m => new ViewStub();
        var vm = new MainViewModel(new DemoGrblService(new MachineProfile()), settings, MakeProvider(settings), modules: modules, viewFactory: factory);

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
        var vm = new MainViewModel(new DemoGrblService(new MachineProfile()), settings, MakeProvider(settings), viewFactory: _ => new ViewStub());

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
            Assert.Equal($"127.0.0.1:{port}", saved.Connection.LastNetworkEndpoint);
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
        var vm = new MainViewModel(new DemoGrblService(new MachineProfile()), settings, MakeProvider(settings));

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
        var vm = new MainViewModel(new DemoGrblService(new MachineProfile()), settings, MakeProvider(settings));

        vm.ConnectCommand.Execute(null);
        await Task.Yield();

        Assert.Contains("COM7", vm.Status);
    }

    [Fact]
    public async Task Successful_serial_connect_persists_globally()
    {
        var settings = MakeSettings();
        settings.Save(PortSettings("COM7"));
        var vm = new MainViewModel(new DemoGrblService(new MachineProfile()), settings, MakeProvider(settings));

        vm.ConnectCommand.Execute(null);
        await Task.Yield();

        var saved = new SettingsService(settings.SettingsDir).Load();
        Assert.Equal("COM7", saved.Connection.LastSerialPort);
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
        Assert.Empty(saved.Connection.LastSerialPort);
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
        settings.Load().Modules["macro"] = false;
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
        services.AddSingleton<SenderNG.Modules.Fixed.ProgramViewModel>();
        var provider = services.BuildServiceProvider();

        var program = provider.GetRequiredService<SenderNG.Modules.Fixed.ProgramViewModel>();
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

    private static ServiceProvider MakeProvider(SettingsService settings, bool withProgram = false,
        IDialogService? dialogService = null, IViewFactory? viewFactory = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(settings);
        services.AddSingleton(_ => new DemoGrblService(settings.Load()));
        services.AddSingleton<RealGrblService>();
        if (withProgram)
            services.AddSingleton<SenderNG.Modules.Fixed.ProgramViewModel>();
        if (dialogService is not null)
            services.AddSingleton<IDialogService>(dialogService);
        if (viewFactory is not null)
            services.AddSingleton<IViewFactory>(viewFactory);
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
    public async Task Start_and_Rewind_enabled_after_program_loads_when_connected()
    {
        var settings = MakeSettings();
        var provider = MakeProvider(settings, withProgram: true);
        var svc = new DemoGrblService(settings.Load());
        await svc.ConnectAsync("demo");
        var vm = new MainViewModel(svc, settings, provider);

        var program = provider.GetRequiredService<SenderNG.Modules.Fixed.ProgramViewModel>();
        program.LoadGCode("G0 X0" + "\nG0 Y0");

        Assert.True(vm.CycleStartCommand.CanExecute(null));
        Assert.True(vm.RewindCommand.CanExecute(null));
    }

    [Fact]
    public void Cycle_start_requires_a_connection()
    {
        var settings = MakeSettings();
        var provider = MakeProvider(settings, withProgram: true);
        var vm = new MainViewModel(new RecordingRealtimeGrbl(), settings, provider); // not connected

        var program = provider.GetRequiredService<SenderNG.Modules.Fixed.ProgramViewModel>();
        program.LoadGCode("G0 X0");

        Assert.False(vm.CycleStartCommand.CanExecute(null));
        Assert.True(vm.RewindCommand.CanExecute(null)); // Rewind is local-only
    }

    [Fact]
    public void Close_requires_a_loaded_program_and_unloads_it()
    {
        var settings = MakeSettings();
        var provider = MakeProvider(settings, withProgram: true);
        var vm = new MainViewModel(new RecordingRealtimeGrbl(), settings, provider);
        Assert.False(vm.CloseFileCommand.CanExecute(null));

        var program = provider.GetRequiredService<SenderNG.Modules.Fixed.ProgramViewModel>();
        program.LoadGCode("G0 X0" + "\nG0 Y0");
        Assert.True(vm.CloseFileCommand.CanExecute(null));

        vm.CloseFileCommand.Execute(null);

        Assert.Empty(program.ProgramRows);
        Assert.Empty(program.LimitRows);
        Assert.False(vm.CloseFileCommand.CanExecute(null)); // unload re-gates Close
        Assert.False(vm.RewindCommand.CanExecute(null));   // and the cycle bar
    }

    [Fact]
    public void Close_without_program_does_not_throw()
    {
        var settings = MakeSettings();
        var vm = new MainViewModel(new RecordingRealtimeGrbl(), settings, MakeProvider(settings));
        var before = vm.Status;

        vm.CloseFileCommand.Execute(null); // gated off; Execute is a defensive no-op

        Assert.Equal(before, vm.Status); // nothing loaded → nothing closed, no message
    }

    /// <summary>Dialog double: hands back a scripted save path and records the
    /// suggested file name and the Open filter list the VM passes.</summary>
    private sealed class StubFileDialogs : IFileDialogService
    {
        public string? SavePath { get; set; }
        public string? OpenPath { get; set; }
        public string? LastSuggestedName { get; private set; }
        public string[] LastFilters { get; private set; } = [];
        public Task<string?> OpenFileAsync(string title, params string[] filters)
        {
            LastFilters = filters;
            return Task.FromResult(OpenPath);
        }
        public Task<string?> SaveFileAsync(string title, string suggestedFileName, params string[] filters)
        {
            LastSuggestedName = suggestedFileName;
            return Task.FromResult(SavePath);
        }
        public Task ShowErrorAsync(string title, string message) => Task.CompletedTask;
    }

    private static async Task LoadViaRecentAsync(MainViewModel vm, string file)
    {
        vm.SelectRecentCommand.Execute(file); // async load — wait for it to land
        // "Loaded …" is the load's last assignment, so it is the completion
        // signal (CanExecute flips earlier, mid-continuation — racy to poll).
        for (var i = 0; i < 500 && !vm.Status.Contains("Loaded "); i++)
            await Task.Delay(10);
        Assert.True(vm.Status.Contains("Loaded "), "program failed to load — status: " + vm.Status);
    }

    [Fact]
    public async Task Save_prompts_with_current_file_name_and_writes_to_chosen_path()
    {
        var settings = MakeSettings();
        var provider = MakeProvider(settings, withProgram: true);
        var dialogs = new StubFileDialogs();
        var vm = new MainViewModel(new RecordingRealtimeGrbl(), settings, provider, fileDialogs: dialogs);
        Assert.False(vm.SaveFileCommand.CanExecute(null)); // no program yet

        var dir = Path.Combine(Path.GetTempPath(), "senderng-save-" + Guid.NewGuid().ToString("N"));
        var source = Path.Combine(dir, "part.nc");
        var target = Path.Combine(dir, "copy.nc");
        const string original = "G21\r\n; heading\r\nG0 X10 Y10\r\nM2\r\n";
        Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(source, original);
        try
        {
            await LoadViaRecentAsync(vm, source);
            Assert.Contains("Loaded part.nc", vm.Status);

            // Save As is the default: the picker pre-fills the loaded name, and
            // the source file is never written implicitly.
            dialogs.SavePath = target;
            vm.SaveFileCommand.Execute(null); // fire-and-forget async command

            for (var i = 0; i < 200 && !vm.Status.Contains("Saved copy.nc"); i++)
                await Task.Delay(10);
            Assert.Contains("Saved copy.nc", vm.Status);
            Assert.Equal("part.nc", dialogs.LastSuggestedName); // pre-filled current name
            Assert.Equal(original, await File.ReadAllTextAsync(target)); // written with loaded text
            Assert.Equal(original, await File.ReadAllTextAsync(source)); // source untouched
            Assert.Equal("copy.nc", vm.CurrentFileName); // picked path becomes the program's home
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task Save_accepting_the_prefilled_name_overwrites_the_source()
    {
        var settings = MakeSettings();
        var provider = MakeProvider(settings, withProgram: true);
        var dialogs = new StubFileDialogs();
        var vm = new MainViewModel(new RecordingRealtimeGrbl(), settings, provider, fileDialogs: dialogs);

        var dir = Path.Combine(Path.GetTempPath(), "senderng-save-" + Guid.NewGuid().ToString("N"));
        var file = Path.Combine(dir, "part.nc");
        const string original = "G21\r\n; heading\r\nG0 X10 Y10\r\nM2\r\n";
        Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(file, original);
        try
        {
            await LoadViaRecentAsync(vm, file);

            // External edit on disk; accepting the pre-filled name (same path)
            // is the one explicit click that overwrites — and it restores the
            // loaded text, line endings and all.
            await File.WriteAllTextAsync(file, "M2\n");
            dialogs.SavePath = file;
            vm.SaveFileCommand.Execute(null);

            for (var i = 0; i < 200 && !vm.Status.Contains("Saved part.nc"); i++)
                await Task.Delay(10);
            Assert.Contains("Saved part.nc", vm.Status);
            Assert.Equal(original, await File.ReadAllTextAsync(file));

            // Close unloads — Save re-disables (nothing left to save).
            vm.CloseFileCommand.Execute(null);
            Assert.False(vm.SaveFileCommand.CanExecute(null));
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
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

    [Fact]
    public void Transform_commands_require_a_loaded_program()
    {
        var settings = MakeSettings();
        var vm = new MainViewModel(new RecordingRealtimeGrbl(), settings, MakeProvider(settings));

        Assert.False(vm.RotateProgramCommand.CanExecute(null));
        Assert.False(vm.ArcsToLinesProgramCommand.CanExecute(null));
        Assert.False(vm.CompressProgramCommand.CanExecute(null));
    }

    [Fact]
    public void Transform_commands_enabled_after_program_loads()
    {
        var settings = MakeSettings();
        var provider = MakeProvider(settings, withProgram: true);
        var vm = new MainViewModel(new RecordingRealtimeGrbl(), settings, provider);

        var program = provider.GetRequiredService<SenderNG.Modules.Fixed.ProgramViewModel>();
        program.LoadGCode("G0 X0" + "\nG0 Y0");

        Assert.True(vm.RotateProgramCommand.CanExecute(null));
        Assert.True(vm.ArcsToLinesProgramCommand.CanExecute(null));
        Assert.True(vm.CompressProgramCommand.CanExecute(null));

        vm.CloseFileCommand.Execute(null);

        Assert.False(vm.RotateProgramCommand.CanExecute(null)); // unload re-gates transforms too
        Assert.False(vm.ArcsToLinesProgramCommand.CanExecute(null));
        Assert.False(vm.CompressProgramCommand.CanExecute(null));
    }

    [Fact]
    public void Compress_applies_and_reports_before_after_line_counts()
    {
        var settings = MakeSettings();
        var provider = MakeProvider(settings, withProgram: true);
        var vm = new MainViewModel(new RecordingRealtimeGrbl(), settings, provider);

        var program = provider.GetRequiredService<SenderNG.Modules.Fixed.ProgramViewModel>();
        program.LoadGCode("(header)\nG21\nG1 X0 Y0 F100\n; remark\nG1 X10 Y0\nM30\n");
        Assert.Equal(6, program.ProgramRows.Count);

        // StripAll mode comes from the profile's Program settings.
        var profile = settings.Load();
        var config = ProgramConfigStore.Load(profile);
        config.CompressCommentMode = CNC.GCode.GCodeCommentMode.StripAll;
        ProgramConfigStore.Save(profile, config);

        vm.CompressProgramCommand.Execute(null);

        // (header) and ; remark are gone — 6 → 4 rows; motion preserved.
        Assert.Contains("Compressed: 6 ", vm.Status);
        Assert.Equal(4, program.ProgramRows.Count);
        Assert.DoesNotContain(program.LoadedText!.Split('\n'), l => l.StartsWith("(", StringComparison.Ordinal));
        Assert.DoesNotContain(program.LoadedText.Split('\n'), l => l.StartsWith(";", StringComparison.Ordinal));
    }

    [Fact]
    public void Compress_without_program_does_not_throw()
    {
        var settings = MakeSettings();
        var vm = new MainViewModel(new RecordingRealtimeGrbl(), settings, MakeProvider(settings));
        var before = vm.Status;

        vm.CompressProgramCommand.Execute(null); // gated off; defensive no-op

        Assert.Equal(before, vm.Status);
    }

    // ---- Converter open flow ----

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

    private sealed class StubViewFactory : IViewFactory
    {
        public void Register<TViewModel, TView>() where TView : class { }
        public object Create(object viewModel) => new object();
    }

    /// <summary>Handler module under test (a real plugin, injected as the app
    /// would discover it via the Modules/ scan).</summary>
    private static readonly SenderNG.Modules.Excellon.ExcellonModule ExcellonModule = new();

    [Fact]
    public async Task Open_converter_file_shows_parameters_then_converts_and_loads()
    {
        var settings = MakeSettings();
        var dialogSvc = new StubDialogService();
        var provider = MakeProvider(settings, withProgram: true, dialogService: dialogSvc, viewFactory: new StubViewFactory());
        var dialogs = new StubFileDialogs();
        var vm = new MainViewModel(new RecordingRealtimeGrbl(), settings, provider,
            modules: [ExcellonModule], fileDialogs: dialogs, dialogs: dialogSvc);

        var dir = Path.Combine(Path.GetTempPath(), "senderng-convert-" + Guid.NewGuid().ToString("N"));
        var drl = Path.Combine(dir, "board.drl");
        const string drlText = "M48\nMETRIC\nT01C2.4\n%\nT01\nX1.5Y1.5\nM95\n";
        Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(drl, drlText);
        try
        {
            dialogs.OpenPath = drl;
            vm.OpenFileCommand.Execute(null);

            // Open → extension dispatch → module's parameters dialog, pre-filled
            // from the profile's stored values.
            for (var i = 0; i < 500 && dialogSvc.LastVm is not SenderNG.Modules.Shared.ConverterParametersViewModel; i++)
                await Task.Delay(10);
            var paramsVm = Assert.IsType<SenderNG.Modules.Shared.ConverterParametersViewModel>(dialogSvc.LastVm);
            Assert.Equal(1, paramsVm.ZRapids); // stored defaults

            paramsVm.OkCommand.Execute(null); // accept → convert → load

            for (var i = 0; i < 500 && !vm.Status.Contains("Converted board.drl"); i++)
                await Task.Delay(10);
            Assert.Contains("Converted board.drl", vm.Status);
            Assert.Equal("board.drl", vm.CurrentFileName);

            // The loaded program is the converted text (it passed the engine gate).
            var program = provider.GetRequiredService<SenderNG.Modules.Fixed.ProgramViewModel>();
            Assert.Contains("G81X1.5Y1.5Z-1.8R1", program.LoadedText);
            Assert.Contains("M30", program.LoadedText);

            // Accepted parameters persisted per module in the profile.
            Assert.Equal(1, SenderNG.Modules.Shared.ConverterSettingsStore.Load(settings.Load(), "excellon").ZRapids);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task Open_converter_file_cancel_loads_nothing()
    {
        var settings = MakeSettings();
        var dialogSvc = new StubDialogService();
        var provider = MakeProvider(settings, withProgram: true, dialogService: dialogSvc, viewFactory: new StubViewFactory());
        var dialogs = new StubFileDialogs();
        var vm = new MainViewModel(new RecordingRealtimeGrbl(), settings, provider,
            modules: [ExcellonModule], fileDialogs: dialogs, dialogs: dialogSvc);

        var dir = Path.Combine(Path.GetTempPath(), "senderng-convert-" + Guid.NewGuid().ToString("N"));
        var drl = Path.Combine(dir, "board.drl");
        Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(drl, "M48\nMETRIC\nT01C2.4\n%\nT01\nX1.5Y1.5\nM95\n");
        try
        {
            dialogs.OpenPath = drl;
            vm.OpenFileCommand.Execute(null);

            for (var i = 0; i < 500 && dialogSvc.LastVm is not SenderNG.Modules.Shared.ConverterParametersViewModel; i++)
                await Task.Delay(10);
            var paramsVm = Assert.IsType<SenderNG.Modules.Shared.ConverterParametersViewModel>(dialogSvc.LastVm);

            paramsVm.CancelCommand.Execute(null);
            await Task.Delay(100);

            Assert.Null(vm.CurrentFileName); // nothing loaded
            Assert.DoesNotContain("Converted", vm.Status);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    [Fact]
    public void Open_dialog_filter_resolves_handler_label_through_locale()
    {
        var settings = MakeSettings();
        var services = new ServiceCollection();
        services.AddSingleton(settings);
        services.AddSingleton(new SenderNG.Core.Localization.LocalizationService(
            JsonStream("""{ "strings": { "module.excellon.fileType": "Excellon files" } }"""),
            "xx",
            JsonStream("""{ "strings": { "module.excellon.fileType": "Файлы Excellon" } }""")));
        var provider = services.BuildServiceProvider();
        var dialogs = new StubFileDialogs();
        var vm = new MainViewModel(new RecordingRealtimeGrbl(), settings, provider,
            modules: [ExcellonModule], fileDialogs: dialogs);

        // The filter list is handed to the file dialog before the file is read,
        // so a nonexistent path still exercises the label resolution.
        dialogs.OpenPath = "C:\\no-such-dir\\board.drl";
        vm.OpenFileCommand.Execute(null);

        Assert.Contains(dialogs.LastFilters,
            f => f.StartsWith("Файлы Excellon (", StringComparison.Ordinal));
    }

    private static Stream JsonStream(string json) => new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));

    [Fact]
    public async Task Open_gcode_file_ignores_handler_modules()
    {
        var settings = MakeSettings();
        var provider = MakeProvider(settings, withProgram: true);
        var dialogs = new StubFileDialogs();
        var vm = new MainViewModel(new RecordingRealtimeGrbl(), settings, provider,
            modules: [ExcellonModule], fileDialogs: dialogs);

        var dir = Path.Combine(Path.GetTempPath(), "senderng-open-" + Guid.NewGuid().ToString("N"));
        var nc = Path.Combine(dir, "part.nc");
        Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(nc, "G21\nG0 X10 Y10\nM2\n");
        try
        {
            dialogs.OpenPath = nc;
            vm.OpenFileCommand.Execute(null);

            for (var i = 0; i < 500 && !vm.Status.Contains("Loaded part.nc"); i++)
                await Task.Delay(10);
            Assert.Contains("Loaded part.nc", vm.Status);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
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
        services.AddSingleton<SenderNG.Modules.Fixed.ProgramViewModel>();
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
        loaded.DemoAxes = 3;
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

    private sealed class SessionGrbl(BehaviorSubject<MachineState> state) : IGrblService
    {
        public bool IsConnected => true;
        public IObservable<MachineState> State => state;
        public Task ConnectAsync(string port) => Task.CompletedTask;
        public void Disconnect() { }
        // Never completes: the streaming session stays open (SendProgramLineAsync's
        // default awaits this task) so the test can inspect the cycle bar mid-run.
        public Task SendAsync(string line) =>
            new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously).Task;
        public void SendRealtime(byte code) { }
    }

    [Fact]
    public void Cycle_bar_gates_follow_the_session_state()
    {
        var settings = MakeSettings();
        var subject = new BehaviorSubject<MachineState>(new MachineState());
        var grbl = new SessionGrbl(subject);
        var services = new ServiceCollection();
        services.AddSingleton(settings);
        services.AddSingleton<IGrblService>(grbl);
        services.AddSingleton<SenderNG.Modules.Fixed.ProgramViewModel>();
        var provider = services.BuildServiceProvider();
        var vm = new MainViewModel(grbl, settings, provider);

        var program = provider.GetRequiredService<SenderNG.Modules.Fixed.ProgramViewModel>();
        program.LoadGCode("G0 X0");

        // Rewind is a local row reset: without an active session the machine
        // state never blocks it (Hold/Run from MDI are unrelated to the program).
        subject.OnNext(new MachineState { Status = "Hold" });
        Assert.True(vm.RewindCommand.CanExecute(null));
        Assert.True(vm.StopCommand.CanExecute(null)); // machine Held regardless of session
        Assert.False(vm.FeedHoldCommand.CanExecute(null)); // no session to pause
        subject.OnNext(new MachineState { Status = "Run" });
        Assert.True(vm.RewindCommand.CanExecute(null));
        Assert.True(vm.FeedHoldCommand.CanExecute(null)); // raw byte fallback for MDI runs
        subject.OnNext(new MachineState { Status = "Idle" });

        // Start: session opens and stays active (the stub never answers).
        Assert.True(vm.CycleStartCommand.CanExecute(null));
        vm.CycleStartCommand.Execute(null);

        Assert.False(vm.RewindCommand.CanExecute(null));     // session running
        Assert.False(vm.CycleStartCommand.CanExecute(null)); // already running
        Assert.True(vm.FeedHoldCommand.CanExecute(null));    // running -> Hold available
        Assert.False(vm.StopCommand.CanExecute(null));       // not Held

        subject.OnNext(new MachineState { Status = "Hold" }); // feed-held => session paused
        Assert.True(vm.CycleStartCommand.CanExecute(null)); // resume
        Assert.True(vm.StopCommand.CanExecute(null));       // Reset while Held (ends the session)
        Assert.False(vm.FeedHoldCommand.CanExecute(null));  // already paused

        subject.OnNext(new MachineState { Status = "Run" }); // resumed, moving
        Assert.False(vm.CycleStartCommand.CanExecute(null));
        Assert.True(vm.FeedHoldCommand.CanExecute(null));
        Assert.False(vm.StopCommand.CanExecute(null));
    }
}
