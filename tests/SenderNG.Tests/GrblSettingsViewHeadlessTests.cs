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
using Avalonia.Media;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
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
        ThemeTestHost.EnsureMerged();
        base.OnFrameworkInitializationCompleted();
    }
}

[Collection("LocalizationState")] // sets LocalizationService.Current and switches the live locale
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

    /// <summary>Dialog double handing back one scripted path (see MainViewModelTests.StubFileDialogs).</summary>
    private sealed class StubDialogs : IFileDialogService
    {
        public string? SavePath { get; set; }
        public string? OpenPath { get; set; }
        public Task<string?> OpenFileAsync(string title, params string[] filters) => Task.FromResult(OpenPath);
        public Task<string?> SaveFileAsync(string title, string suggestedFileName, params string[] filters) =>
            Task.FromResult(SavePath);
        public Task ShowErrorAsync(string title, string message) => Task.CompletedTask;
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

    /// <summary>
    /// Yield so the headless SynchronizationContext runs a pending layout/render. `RunJobs` is
    /// load-bearing, not tidiness: a `{loc:Loc}` binding delivers its first value through
    /// `Dispatcher.UIThread.Post` (Localization/Loc.cs), so without draining the queue every
    /// localized Button.Content in this view reads back as null and a label assertion would look
    /// like "the markup extension does not work in this view" when it does.
    /// </summary>
    private static async Task Pump()
    {
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(16);
        Dispatcher.UIThread.RunJobs();
        await Task.Yield();
    }

    [AvaloniaFact]
    public async Task Backup_and_restore_buttons_render_and_the_strip_follows_the_plan()
    {
        // The buttons are labelled with {loc:Loc}, which resolves through
        // LocalizationService.Current and follows a live locale switch — provided the pump drains
        // the dispatcher (see Pump). Construct the view with the locale in place.
        var prevLocale = LocalizationService.Current;
        LocalizationService.Current = HeadlessTestCatalog.Locale;
        var window = default(Window);
        try
        {
            var provider = new StubProvider { ValueLines = ["$0=10"] };
            var grbl = new FakeGrbl();
            await grbl.ConnectAsync("stub");
            var dialogs = new StubDialogs();
            var vm = new GrblSettingsViewModel(provider, grbl, HeadlessTestCatalog.Locale, dialogs);
            vm.LoadAsync().GetAwaiter().GetResult();

            var view = new GrblSettingsView { DataContext = vm };
            window = new Window { Width = 660, Height = 520, Content = view };
            window.Show();
            for (var i = 0; i < 4; i++)
                await Pump();

            var buttons = window.GetVisualDescendants().OfType<Button>().ToList();

            // The transfer buttons render, carry localized labels, and are gated on the connection.
            var backupButton = buttons.SingleOrDefault(b => Equals(b.Content, "Back up"));
            var restoreButton = buttons.SingleOrDefault(b => Equals(b.Content, "Restore"));
            Assert.NotNull(backupButton);
            Assert.NotNull(restoreButton);
            Assert.True(backupButton.Bounds.Width > 0, "the Back up button was not laid out");
            Assert.True(restoreButton.Bounds.Width > 0, "the Restore button was not laid out");
            Assert.True(backupButton.IsEnabled && restoreButton.IsEnabled);
            Assert.True(vm.CanTransfer);
            // The confirm strip is bound to HasRestorePlan: collapsed while no plan waits.
            Assert.False(vm.HasRestorePlan);
            var strip = FindStrip(window);
            Assert.NotNull(strip);
            Assert.False(strip.IsVisible, "the confirm strip must be hidden before a plan exists");

            // Reading a backup builds the plan; the strip becomes visible and its buttons laid out.
            var path = Path.Combine(Path.GetTempPath(), "senderng-headless-restore-" + Guid.NewGuid().ToString("N") + ".json");
            await File.WriteAllTextAsync(path,
                "{\"kind\":\"senderng-grbl-settings\",\"formatVersion\":1,\"createdUtc\":\"2026-10-13T00:00:00Z\","
                + "\"appVersion\":\"test\",\"controller\":{\"key\":\"stub\",\"firmware\":\"demo\",\"version\":\"1\",\"build\":0,"
                + "\"numAxes\":3,\"isGrblHAL\":true,\"metadataFromController\":true},"
                + "\"settings\":[{\"id\":0,\"value\":\"11\",\"name\":\"n\",\"unit\":\"\",\"type\":6,\"format\":\"\","
                + "\"min\":null,\"max\":null,\"rebootRequired\":false}]}");
            dialogs.OpenPath = path;
            await vm.RestoreAsync();

            Assert.True(vm.HasRestorePlan);
            for (var i = 0; i < 4; i++)
                await Pump();

            strip = FindStrip(window);
            Assert.NotNull(strip);
            Assert.True(strip.IsVisible, "the confirm strip must show once a plan exists");
            // The strip's own controls exist in the tree with the localized labels. (Their
            // Bounds stay empty in the headless renderer for a strip that was measured while
            // collapsed; the binding and the labels are what this test can assert here.)
            Assert.Contains(window.GetVisualDescendants().OfType<Button>(), b => Equals(b.Content, "Apply restore"));
            Assert.Contains(window.GetVisualDescendants().OfType<Button>(), b => Equals(b.Content, "Cancel"));
            Assert.Contains(window.GetVisualDescendants().OfType<Button>(), b => Equals(b.Content, "Cancel"));

            // Cancelling discards the plan; the strip collapses again.
            vm.CancelRestoreCommand.Execute(null);
            for (var i = 0; i < 4; i++)
                await Pump();

            Assert.False(vm.HasRestorePlan);
            Assert.False(FindStrip(window)!.IsVisible, "the confirm strip must collapse after Cancel");

            // A {loc:Loc} label follows a LIVE locale switch. The VM-string variant this replaced did
            // not, and the difference is only observable with a dispatcher-draining pump.
            if (HeadlessTestCatalog.Locale.AvailableLocales.Contains("ru"))
            {
                HeadlessTestCatalog.Locale.SetLocale("ru");
                for (var i = 0; i < 4; i++)
                    await Pump();
                Assert.Contains(window.GetVisualDescendants().OfType<Button>(),
                    b => Equals(b.Content, "Резервная копия"));
            }
        }
        finally
        {
            if (HeadlessTestCatalog.Locale.AvailableLocales.Contains("ru"))
                HeadlessTestCatalog.Locale.SetLocale("en");
            LocalizationService.Current = prevLocale;
            window?.Close();
        }
    }

    [AvaloniaFact]
    public async Task Status_text_stays_inside_its_column_and_carries_the_full_text_as_a_tooltip()
    {
        // Regression: the status TextBlock used to sit in a horizontal StackPanel, which
        // measures its children with INFINITE width — TextTrimming therefore never bit, and the
        // status rendered underneath the transfer buttons. A status far longer than the 660px
        // footer can hold is forced here (a failed restore names the missing file).
        // The catalog must be in place: {loc:Loc} falls back to its raw KEY without it, which makes
        // the buttons wide enough to squeeze the status column to zero width.
        var prevLocale = LocalizationService.Current;
        LocalizationService.Current = HeadlessTestCatalog.Locale;
        var window = default(Window);
        try
        {
            var provider = new StubProvider { ValueLines = ["$0=10"] };
            var grbl = new FakeGrbl();
            await grbl.ConnectAsync("stub");
            var dialogs = new StubDialogs();
            var vm = new GrblSettingsViewModel(provider, grbl, HeadlessTestCatalog.Locale, dialogs);
            vm.LoadAsync().GetAwaiter().GetResult();

            var view = new GrblSettingsView { DataContext = vm };
            window = new Window { Width = 660, Height = 520, Content = view };
            window.Show();
            for (var i = 0; i < 4; i++)
                await Pump();

            dialogs.OpenPath = Path.Combine(
                Path.GetTempPath(), new string('x', 120), "does-not-exist.json");
            await vm.RestoreAsync();
            for (var i = 0; i < 8; i++)
                await Pump();

            Assert.Contains("Restore failed", vm.Status);
            Assert.True(vm.Status.Length > 80, "the status must be long enough to need trimming");

            var status = window.GetVisualDescendants().OfType<TextBlock>()
                .First(t => t.Text == vm.Status);
            // Identified by command, not by label: this test is about geometry, not localization.
            var backupButton = window.GetVisualDescendants().OfType<Button>()
                .First(b => ReferenceEquals(b.Command, vm.BackupCommand));

            // Trimming is configured, and the untrimmed text stays reachable in the tooltip.
            Assert.Equal(TextTrimming.CharacterEllipsis, status.TextTrimming);
            Assert.Equal(vm.Status, ToolTip.GetTip(status));

            // Bounded by its star column, so it can never reach the buttons to its right.
            Assert.True(status.Bounds.Width > 0,
                $"the status was not laid out (status={status.Bounds}, parent={(status.Parent as Visual)?.Bounds})");
            var statusRight = status.TranslatePoint(new Point(status.Bounds.Width, 0), window)!.Value.X;
            var buttonLeft = backupButton.TranslatePoint(new Point(0, 0), window)!.Value.X;
            Assert.True(statusRight <= buttonLeft + 1,
                $"the status runs under the buttons: status right={statusRight:F1}, Back up left={buttonLeft:F1}");
        }
        finally
        {
            LocalizationService.Current = prevLocale;
            window?.Close();
        }
    }

    private static Border? FindStrip(Window window) =>
        window.GetVisualDescendants().OfType<Border>().FirstOrDefault(b => b.Name == "RestoreStrip");
}