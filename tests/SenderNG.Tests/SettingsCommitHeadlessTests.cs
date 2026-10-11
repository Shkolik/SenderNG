/*
 * SettingsCommitHeadlessTests.cs - settings TextBoxes commit on lost focus or Enter, not per keystroke
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
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CNC.Core.Abstractions;
using CNC.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using SenderNG.Core.Localization;
using SenderNG.Modules.Plotter.GL.Settings;
using SenderNG.Modules.Plotter.Nex.Settings;
using SenderNG.Modules.Plotter.Settings;
using SenderNG.Modules.Plugins;
using SenderNG.Modules.Shared;
using SenderNG.Services;
using Xunit;

namespace SenderNG.Tests;

public class SettingsCommitHeadlessTests
{
    private static SettingsService MakeSettings()
    {
        var dir = Path.Combine(Path.GetTempPath(), "senderng-commit-hl-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return new SettingsService(dir);
    }

    /// <summary>Like <see cref="MakeSettings"/> but seeded to the two-plane Foam
    /// machine type (same Load/modify/Save shape as JogViewModelTests.MakeSettings).
    /// The Nex/GL WireLength row is visible only when the profile has more than
    /// one plane (ShowWireLength => Plaines.Count > 1), and a hidden box cannot
    /// take focus, so blur would raise no LostFocus.</summary>
    private static SettingsService MakeFoamSettings()
    {
        var settings = MakeSettings();
        var profile = settings.Load();
        profile.MachineTypeId = MachineTypes.Foam;
        settings.Save(profile);
        return settings;
    }

    /// <summary>Run queued dispatcher jobs and yield so the headless
    /// SynchronizationContext runs a pending layout/render.</summary>
    private static async Task Pump()
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(16);
        await Task.Yield();
    }

    /// <summary>Blur = focus any other focusable control (or the window). Focus
    /// on another control raises LostFocus on the TextBox.</summary>
    private static void Blur(Window window, Control box)
    {
        var target = window.GetVisualDescendants().OfType<Control>()
            .FirstOrDefault(c => c is { Focusable: true, IsVisible: true, IsEnabled: true }
                && !ReferenceEquals(c, box));
        (target ?? window).Focus();
    }

    private static void RaiseEnter(TextBox box) => box.RaiseEvent(new KeyEventArgs
    {
        RoutedEvent = InputElement.KeyDownEvent,
        Key = Key.Enter,
    });

    [AvaloniaFact]
    public async Task Jog_distance_typing_reaches_the_vm_only_after_a_commit()
    {
        var settings = MakeSettings();
        var vm = new JogViewModel(
            new DemoGrblService(new MachineProfile(), localization: HeadlessTestCatalog.Locale),
            settings);
        var view = new JogSettingsView { DataContext = vm };
        var window = new Window { Content = view };
        window.Show();
        await Pump();

        var saves = 0;
        settings.Changed += () => saves++;

        // Distance1's box is the view's first TextBox (JogSettingsView.axaml:69).
        var box = window.GetVisualDescendants().OfType<TextBox>().First();
        box.Focus();
        var before = vm.Config.Distance1;
        box.Text = "4321";
        await Pump();
        Assert.Equal(0, saves);            // RED today: PropertyChanged pushes per keystroke
        Assert.Equal(before, vm.Config.Distance1);

        Blur(window, box);
        await Pump();
        Assert.Equal(1, saves);
        Assert.Equal(4321d, vm.Config.Distance1);
    }

    [AvaloniaFact]
    public async Task Jog_distance_enter_commits_while_the_box_keeps_focus()
    {
        var settings = MakeSettings();
        var vm = new JogViewModel(
            new DemoGrblService(new MachineProfile(), localization: HeadlessTestCatalog.Locale),
            settings);
        var view = new JogSettingsView { DataContext = vm };
        var window = new Window { Content = view };
        window.Show();
        await Pump();

        var saves = 0;
        settings.Changed += () => saves++;

        var box = window.GetVisualDescendants().OfType<TextBox>().First();
        box.Focus();
        box.Text = "555";
        await Pump();
        Assert.Equal(0, saves);

        RaiseEnter(box);
        await Pump();
        Assert.Equal(1, saves);
        Assert.Equal(555d, vm.Config.Distance1);
        Assert.Same(box, window.FocusManager?.GetFocusedElement());
    }

    [AvaloniaFact]
    public async Task Jog_garbage_text_never_reaches_the_vm_and_saves_nothing()
    {
        var settings = MakeSettings();
        var vm = new JogViewModel(
            new DemoGrblService(new MachineProfile(), localization: HeadlessTestCatalog.Locale),
            settings);
        var view = new JogSettingsView { DataContext = vm };
        var window = new Window { Content = view };
        window.Show();
        await Pump();

        var saves = 0;
        settings.Changed += () => saves++;

        var box = window.GetVisualDescendants().OfType<TextBox>().First();
        box.Focus();
        var before = vm.Config.Distance1;
        box.Text = "abc";
        await Pump();
        Blur(window, box);
        await Pump();

        Assert.Equal(0, saves);            // NumericText returned DoNothing: no setter, no write
        Assert.Equal(before, vm.Config.Distance1);
    }

    [AvaloniaFact]
    public async Task Macro_name_typing_reaches_the_vm_only_after_a_commit()
    {
        var settings = MakeSettings();
        var vm = new MacroViewModel(settings,
            new DemoGrblService(new MachineProfile(), localization: HeadlessTestCatalog.Locale));
        vm.AddMacroCommand.Execute(null); // AddMacro sets SelectedMacro (MacroViewModel.cs:180)
        var view = new MacroView { DataContext = vm };
        var window = new Window { Content = view };
        window.Show();
        await Pump();

        var saves = 0;
        settings.Changed += () => saves++;

        // The Name box precedes the Code box (MacroView.axaml:53 < :57).
        var box = window.GetVisualDescendants().OfType<TextBox>().First();
        box.Focus();
        box.Text = "Renamed";
        await Pump();
        Assert.Equal(0, saves);
        Assert.Equal("Macro 1", vm.SelectedMacro!.Name);

        Blur(window, box);
        await Pump();
        Assert.Equal(1, saves);
        Assert.Equal("Renamed", vm.SelectedMacro!.Name);
    }

    [AvaloniaFact]
    public async Task Nex_wire_length_reaches_the_vm_only_after_a_commit()
    {
        var settings = MakeFoamSettings();
        var vm = new PlotterNexSettingsViewModel(settings, null);
        var view = new PlotterNexSettingsView { DataContext = vm };
        var window = new Window { Content = view };
        window.Show();
        await Pump();

        var saves = 0;
        settings.Changed += () => saves++;

        // WireLength's box is the view's first TextBox (PlotterNexSettingsView.axaml:46;
        // the origin-row boxes follow at :106+).
        var box = window.GetVisualDescendants().OfType<TextBox>().First();
        box.Focus();
        var before = vm.WireLength;
        box.Text = "4321";
        await Pump();
        Assert.Equal(0, saves);
        Assert.Equal(before, vm.WireLength);

        Blur(window, box);
        await Pump();
        Assert.Equal(1, saves);
        Assert.Equal(4321d, vm.WireLength);
    }

    [AvaloniaFact]
    public async Task Nex_wire_length_enter_commits_with_the_plugins_own_handler()
    {
        var settings = MakeSettings();
        var vm = new PlotterNexSettingsViewModel(settings, null);
        var view = new PlotterNexSettingsView { DataContext = vm };
        var window = new Window { Content = view };
        window.Show();
        await Pump();

        var saves = 0;
        settings.Changed += () => saves++;

        var box = window.GetVisualDescendants().OfType<TextBox>().First();
        box.Focus();
        box.Text = "777";
        await Pump();
        Assert.Equal(0, saves);

        RaiseEnter(box);
        await Pump();
        Assert.Equal(1, saves);
        Assert.Equal(777d, vm.WireLength);
    }

    [AvaloniaFact]
    public async Task Gl_wire_length_reaches_the_vm_only_after_a_commit()
    {
        var settings = MakeFoamSettings();
        var vm = new PlotterGlSettingsViewModel(settings);
        var view = new PlotterGlSettingsView { DataContext = vm };
        var window = new Window { Content = view };
        window.Show();
        await Pump();

        var saves = 0;
        settings.Changed += () => saves++;

        var box = window.GetVisualDescendants().OfType<TextBox>().First(); // WireLength (:46)
        box.Focus();
        var before = vm.WireLength;
        box.Text = "4321";
        await Pump();
        Assert.Equal(0, saves);
        Assert.Equal(before, vm.WireLength);

        Blur(window, box);
        await Pump();
        Assert.Equal(1, saves);
        Assert.Equal(4321d, vm.WireLength);
    }

    [AvaloniaFact]
    public async Task Plotter_origin_row_reaches_the_service_only_after_a_commit()
    {
        var settings = MakeSettings();
        var vm = new PlotterSettingsViewModel(settings, null);
        var view = new PlotterSettingsView { DataContext = vm };
        var window = new Window { Content = view };
        window.Show();
        await Pump();

        var saves = 0;
        settings.Changed += () => saves++;

        // The origin-row Value box: its DataContext is the nested PlotterOriginRow
        // (x:DataType at PlotterSettingsView.axaml:65). A bare First() would land
        // on the slot ComboBox's template TextBox instead.
        var box = window.GetVisualDescendants().OfType<TextBox>()
            .First(b => b.DataContext is PlotterSettingsViewModel.PlotterOriginRow);
        box.Focus();
        box.Text = "4321";
        await Pump();
        Assert.Equal(0, saves);

        Blur(window, box);
        await Pump();
        Assert.Equal(1, saves); // setter -> ApplyRow -> Persist -> Changed
    }

    [AvaloniaFact]
    public async Task Converter_settings_ZHome_reaches_the_vm_only_after_a_commit()
    {
        var settings = MakeSettings();
        var services = new ServiceCollection()
            .AddSingleton(settings)
            .BuildServiceProvider();
        var vm = new ConverterSettingsViewModel(services, "test-module", "Test");
        var view = new ConverterSettingsView { DataContext = vm };
        var window = new Window { Content = view };
        window.Show();
        await Pump();

        var saves = 0;
        settings.Changed += () => saves++;

        var box = window.GetVisualDescendants().OfType<TextBox>().First(); // ZHome (:56)
        box.Focus();
        var before = vm.ZHome;
        box.Text = "4321";
        await Pump();
        Assert.Equal(0, saves);
        Assert.Equal(before, vm.ZHome);

        Blur(window, box);
        await Pump();
        Assert.Equal(1, saves);
        Assert.Equal(4321d, vm.ZHome);
    }

    [AvaloniaFact]
    public async Task Closing_flushes_a_focused_boxes_pending_text_into_the_source()
    {
        var settings = MakeSettings();
        var vm = new JogViewModel(
            new DemoGrblService(new MachineProfile(), localization: HeadlessTestCatalog.Locale),
            settings);
        var view = new JogSettingsView { DataContext = vm };
        var window = new Window { Content = view };
        window.Show();
        await Pump();

        var saves = 0;
        settings.Changed += () => saves++;

        var box = window.GetVisualDescendants().OfType<TextBox>().First();
        box.Focus();
        box.Text = "999";
        await Pump();
        Assert.Equal(0, saves);

        BindingCommit.CommitFocused(window); // what Window.Closing invokes at both sites
        await Pump();
        Assert.Equal(1, saves);
        Assert.Equal(999d, vm.Config.Distance1);
    }
}
