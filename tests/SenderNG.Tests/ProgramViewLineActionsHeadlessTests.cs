/*
 * ProgramViewLineActionsHeadlessTests.cs - row selection and the line-action menu
 */

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

using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using CNC.Core.Abstractions;
using CNC.Core.Services;
using SenderNG.Core.Localization;
using SenderNG.Core.Modules;
using SenderNG.Modules.Fixed;
using Xunit;

namespace SenderNG.Tests;

[Collection("LocalizationState")]
public class ProgramViewLineActionsHeadlessTests
{
    private static readonly LocalizationService Loc =
        LocalizationService.LoadFromDirectory(Path.Combine(AppContext.BaseDirectory, "Locale"));

    [AvaloniaFact]
    public async Task Grid_selection_drives_the_row_action_menu()
    {
        var previous = LocalizationService.Current;
        try
        {
            LocalizationService.Current = Loc;
            // A static idle controller, not the demo: the demo's poll timer fires
            // on its own thread, and this headless host never initializes the
            // UI-thread bridge the app installs at startup.
            var vm = new ProgramViewModel(new IdleGrblService());
            vm.LoadGCode("G21\nG0 X10\nM5");

            var view = new ProgramView { DataContext = vm };
            var window = new Window { Width = 640, Height = 420, Content = view };
            window.Show();
            for (var i = 0; i < 4; i++)
                await Pump();

            var list = view.FindControl<ListBox>("ProgramList");
            Assert.NotNull(list);
            Assert.Equal(SelectionMode.Multiple, list!.SelectionMode);

            var menu = list.ContextMenu;
            Assert.NotNull(menu);
            menu!.Open(list);
            for (var i = 0; i < 4; i++)
                await Pump();

            var items = menu.Items.OfType<MenuItem>().ToList();
            Assert.Equal(2, items.Count);
            Assert.Equal(vm.SendSelectionMenuText, items[0].Header as string);
            Assert.Equal(vm.RunFromLineMenuText, items[1].Header as string);
            // Nothing selected: both actions are refused.
            Assert.False(items[0].IsEnabled);
            Assert.False(items[1].IsEnabled);
            menu.Close();
            for (var i = 0; i < 4; i++)
                await Pump();

            // Selecting a row reaches the view-model and re-labels the menu.
            list.Selection.Select(1);
            for (var i = 0; i < 4; i++)
                await Pump();
            Assert.Equal([1], vm.SelectedRows);
            Assert.Equal("Send selected lines (1)", vm.SendSelectionMenuText);
            Assert.Equal("Run from line 2 to end", vm.RunFromLineMenuText);

            menu.Open(list);
            for (var i = 0; i < 4; i++)
                await Pump();
            Assert.Equal("Send selected lines (1)", items[0].Header as string);
            Assert.Equal("Run from line 2 to end", items[1].Header as string);
            Assert.True(items[0].IsEnabled);
            Assert.True(items[1].IsEnabled);
            menu.Close();

            // Starting the send drops the selection deliberately: the status updates
            // replace row records, which would erode a live selection row by row
            // (sent rows unhighlight, comment rows keep their highlight).
            var send = vm.SendSelectionAsync();
            Assert.False(vm.HasSelection);
            Assert.Empty(list.Selection.SelectedIndexes);

            // This host never installs the app's UI-thread bridge (App calls
            // UiThread.Initialize at startup), so streamer progress must not be
            // driven here — stop the session and let it wind down.
            vm.OnHardReset();
            await send;
            for (var i = 0; i < 4; i++)
                await Pump();

            // A reload replaces the rows: the stale index selection must be dropped.
            vm.LoadGCode("G21\nG0 Y10\nM5");
            for (var i = 0; i < 4; i++)
                await Pump();
            Assert.Empty(vm.SelectedRows);
            Assert.Empty(list.Selection.SelectedIndexes);

            window.Close();
        }
        finally
        {
            LocalizationService.Current = previous;
        }
    }

    [AvaloniaFact]
    public void Module_declares_the_two_module_scoped_hotkeys()
    {
        var module = new ProgramModule();

        Assert.Equal(["Ctrl+Enter", "Ctrl+Shift+Enter"], module.HotKeys.Select(h => h.Gesture));
        Assert.Equal(["program.sendSelection", "program.runFromLine"], module.HotKeys.Select(h => h.ActionId));

        var vm = new ProgramViewModel();
        var actions = module.CreateHotkeyActions(new SingleServiceProvider(vm));

        Assert.Equal(2, actions.Count);
        Assert.All(actions, a => Assert.Equal(HotkeyScope.Module, a.Scope));
        Assert.All(actions, a => Assert.Equal("program", a.ModuleId));
        // Gated through the view-model's commands: no connection/selection, no fire.
        Assert.False(actions[0].CanExecute());
        Assert.False(actions[1].CanExecute());
    }

    /// <summary>Run queued dispatcher jobs and yield so the headless host runs a
    /// pending layout/render (the pattern the other view tests use).</summary>
    private static async Task Pump()
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(16);
        await Task.Yield();
    }

    private sealed class SingleServiceProvider : IServiceProvider
    {
        private readonly object _service;
        public SingleServiceProvider(object service) => _service = service;
        public object? GetService(Type serviceType) =>
            serviceType.IsInstanceOfType(_service) ? _service : null;
    }

    /// <summary>Connected, always idle: enough to exercise the action gates without
    /// a background publisher touching UI objects.</summary>
    private sealed class IdleGrblService : IGrblService
    {
        private readonly System.Reactive.Subjects.BehaviorSubject<MachineState> _state =
            new(new MachineState { Status = "Idle" });

        public bool IsConnected => true;
        public IObservable<MachineState> State => _state;
        public Task ConnectAsync(string port) => Task.CompletedTask;
        public void Disconnect() { }
        public Task SendAsync(string line) => Task.CompletedTask;
        public void SendRealtime(byte code) { }
    }
}
