/*
 * GrblSettingsViewHeadlessTests.cs - headless UI tests for the GRBL settings view
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

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Themes.Fluent;
using Avalonia.VisualTree;
using CNC.Core.Abstractions;
using CNC.Core.Services;
using SenderNG.Modules.Fixed;
using SenderNG.Core.Localization;
using Xunit;

[assembly: AvaloniaTestApplication(typeof(SenderNG.Tests.UiHeadlessApp))]

namespace SenderNG.Tests;

public sealed class UiHeadlessApp : Application
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<UiHeadlessApp>()
            // Real software rendering (Skia) instead of the managed headless
            // drawing stub: needed so CaptureRenderedFrame() works (plotter
            // smoke tests assert actual pixels). Frame capture throws
            // NotSupportedException without .UseSkia() + UseHeadlessDrawing=false.
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });

    public override void OnFrameworkInitializationCompleted()
    {
        Styles.Add(new FluentTheme());
        base.OnFrameworkInitializationCompleted();
    }
}

public class GrblSettingsViewHeadlessTests
{
    private sealed class StubProvider : IGrblSettingsProvider
    {
        public IReadOnlyList<string> ValueLines { get; init; } = [];
        public Task<IReadOnlyList<string>> GetSettingLinesAsync() => Task.FromResult(ValueLines);
        public Task<IReadOnlyList<string>> GetSettingDetailLinesAsync() => Task.FromResult<IReadOnlyList<string>>([]);
        public Task<IReadOnlyList<string>> GetSettingGroupLinesAsync() => Task.FromResult<IReadOnlyList<string>>([]);
        public Task<string?> GetSettingDescriptionAsync(int id) => Task.FromResult<string?>(null);
    }

    private sealed class FakeGrbl : IGrblService
    {
        public ControllerCapabilities Capabilities { get; init; } = new() { NumAxes = 3 };
        public bool IsConnected { get; private set; }
        public IObservable<MachineState> State { get; } = new System.Reactive.Subjects.Subject<MachineState>();
        public Task ConnectAsync(string port) { IsConnected = true; return Task.CompletedTask; }
        public void Disconnect() => IsConnected = false;
        public void SendRealtime(byte code) { }
        public Task SendAsync(string line) => Task.CompletedTask;
    }

    private static GrblSettingItem Find(IEnumerable<object> nodes, int id)
    {
        foreach (var node in Flatten(nodes))
            if (node.Id == id)
                return node;
        throw new InvalidOperationException($"setting not found: ${id}");
    }

    private static IEnumerable<GrblSettingItem> Flatten(IEnumerable<object> nodes)
    {
        foreach (var node in nodes)
        {
            switch (node)
            {
                case GrblSettingItem s:
                    yield return s;
                    break;
                case GrblSettingGroupItem g:
                    foreach (var x in Flatten(g.Children))
                        yield return x;
                    break;
            }
        }
    }

    [AvaloniaFact]
    public async Task Switching_bitmask_selection_while_open_stays_clean()
    {
        var provider = new StubProvider
        {
            ValueLines = ["$44=4", "$14=72", "$4=15", "$10=511", "$370=9", "$45=3"],
        };
        var grbl = new GrblServiceProxy(
            new DemoGrblService(new MachineProfile(), localization: HeadlessTestCatalog.Locale));
        var vm = new GrblSettingsViewModel(provider, grbl, HeadlessTestCatalog.Locale);
        vm.LoadAsync().GetAwaiter().GetResult();

        var view = new GrblSettingsView { DataContext = vm };
        var window = new Window { Width = 920, Height = 560, Content = view };

        // The pane must be visible on the FIRST layout (hidden-at-show panes don't
        // realize in this headless build), so select $44 before showing, then switch.
        vm.SelectedNode = Find(vm.Groups, 44);
        window.Show();
        for (var i = 0; i < 4; i++)
            await Pump();

        // Select + switch bitmasks with the detail pane already open.
        foreach (var id in new[] { 44, 14, 4, 10, 370, 45 })
        {
            var item = Find(vm.Groups, id);
            vm.SelectedNode = item;
            for (var i = 0; i < 4; i++)
                await Pump();

            var boxes = window.GetVisualDescendants().OfType<CheckBox>().ToList();
            Assert.True(boxes.Count >= 1, $"after selecting {item.Key} realized {boxes.Count} checkboxes");
            Assert.False(item.IsDirty, $"{item.Key} marked dirty by selecting it");
            Assert.Equal(item.OriginalValue, item.Value);
        }

        window.Close();
    }

    [AvaloniaFact]
    public async Task Switching_radio_to_bitmask_does_not_corrupt_the_bitmask()
    {
        // Regression for the recycled-ComboBox bug: the single shared ComboBox fired
        // SelectionChanged with a STALE index (from the previously selected radio)
        // targeting the just-selected bitmask, calling ApplyRadio() and corrupting its
        // value + marking it dirty. Only radio settings may write via the combo, and the
        // write is deferred until the bindings settle into a no-op.
        var provider = new StubProvider { ValueLines = ["$32=2", "$44=0"] };
        var grbl = new GrblServiceProxy(
            new DemoGrblService(new MachineProfile(), localization: HeadlessTestCatalog.Locale));
        var vm = new GrblSettingsViewModel(provider, grbl, HeadlessTestCatalog.Locale);
        vm.LoadAsync().GetAwaiter().GetResult();

        var view = new GrblSettingsView { DataContext = vm };
        var window = new Window { Width = 920, Height = 560, Content = view };

        var radio = Find(vm.Groups, 32);
        Assert.True(radio.IsRadio);
        vm.SelectedNode = radio;
        window.Show();
        for (var i = 0; i < 4; i++)
            await Pump();

        var mask = Find(vm.Groups, 44);
        Assert.True(mask.IsBits);
        vm.SelectedNode = mask; // the combo is recycled here: it must NOT write its stale index
        for (var i = 0; i < 4; i++)
            await Pump();

        Assert.Equal("0", mask.Value);
        Assert.False(mask.IsDirty);
        window.Close();
    }

    /// <summary>Yield so the headless SynchronizationContext runs a pending layout/render.</summary>
    private static async Task Pump()
    {
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(16);
        await Task.Yield();
    }
}