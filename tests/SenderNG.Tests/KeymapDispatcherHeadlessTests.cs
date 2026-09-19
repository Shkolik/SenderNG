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
using Avalonia.Media;
using Avalonia.Threading;
using CNC.Core.Services;
using SenderNG.Core.Modules;
using SenderNG.Core.Services;
using Xunit;

namespace SenderNG.Tests;

public class KeymapDispatcherHeadlessTests
{
    private int _down;
    private int _up;

    private HotkeyService MakeService()
    {
        var dir = Path.Combine(Path.GetTempPath(), "senderng-dispatch-" + Guid.NewGuid().ToString("N"));
        var service = new HotkeyService(new SettingsService(dir));
        service.RegisterAction(
            new CommandHotkeyAction("w", "W", null, () => _down++, onKeyUp: () => _up++),
            "Ctrl+W");
        return service;
    }

    [AvaloniaFact]
    public void Dispatcher_routes_keydown_and_keyup_for_modifier_chord()
    {
        var service = MakeService();
        var window = new Window();
        window.Show();
        using var dispatcher = new KeymapDispatcher(window, new KeymapController(service), _ => null);
        window.Content = new TextBox { DataContext = null };
        window.Focus();

        window.KeyPressQwerty(PhysicalKey.W, RawInputModifiers.Control);
        window.KeyReleaseQwerty(PhysicalKey.W, RawInputModifiers.Control);

        Assert.Equal(1, _down);
        Assert.Equal(1, _up);
    }

    [AvaloniaFact]
    public void Dispatcher_skips_a_focused_text_box_for_bare_gestures_but_not_chords()
    {
        var bare = 0;
        var dir = Path.Combine(Path.GetTempPath(), "senderng-dispatch-" + Guid.NewGuid().ToString("N"));
        var service = new HotkeyService(new SettingsService(dir));
        service.RegisterAction(new CommandHotkeyAction("space", "Space", null, () => bare++), "Space");
        service.RegisterAction(new CommandHotkeyAction("w", "W", null, () => _down++), "Ctrl+W");

        var window = new Window();
        window.Show();
        using var dispatcher = new KeymapDispatcher(window, new KeymapController(service), _ => null);
        var box = new TextBox();
        window.Content = box;
        box.Focus();

        window.KeyPressQwerty(PhysicalKey.Space, RawInputModifiers.None);
        window.KeyPressQwerty(PhysicalKey.W, RawInputModifiers.Control);

        Assert.Equal(0, bare);
        Assert.Equal(1, _down);
    }

    // Repro for the live symptom: Ctrl+Up/Down (and bare Up/Down) "intercepted by the
    // app menu". The menu's DefaultMenuInteractionHandler opens a focused top-level
    // submenu on Up/Down and ignores modifiers. The exchange between the arrows and the
    // menu is a focus problem, not a binding problem: clicking inert content leaves
    // keyboard focus on a menu item, and the tunnel dispatcher only intercepts what its
    // bound actions can execute. Click-to-focus (OnPointerPressed) pulls focus off the
    // menu onto the clicked control (or the window), so the arrows jog again; a
    // deliberately focused menu keeps its standard keyboard navigation because the
    // dispatcher lets Up/Down/Left/Right through untouched whenever focus is inside the
    // menu bar (tests below).

    [AvaloniaFact]
    public void Up_on_focused_top_level_item_goes_to_menu_when_action_executable()
    {
        var invoked = 0;
        var dir = Path.Combine(Path.GetTempPath(), "senderng-dispatch-" + Guid.NewGuid().ToString("N"));
        var service = new HotkeyService(new SettingsService(dir));
        service.RegisterAction(new CommandHotkeyAction("up", "Up", null, () => invoked++), "Ctrl+Up");

        var menu = new Menu();
        var file = new MenuItem { Header = "File" };
        file.Items.Add(new MenuItem { Header = "Open" });
        menu.Items.Add(file);

        var window = new Window { Content = menu };
        window.Show();
        using var dispatcher = new KeymapDispatcher(window, new KeymapController(service), _ => null);
        file.Focus();

        window.KeyPressQwerty(PhysicalKey.ArrowUp, RawInputModifiers.Control);

        Assert.Equal(0, invoked);
        Assert.True(file.IsSubMenuOpen);
    }

    [AvaloniaFact]
    public void Up_on_focused_top_level_item_goes_to_menu_when_action_not_executable()
    {
        var invoked = 0;
        var dir = Path.Combine(Path.GetTempPath(), "senderng-dispatch-" + Guid.NewGuid().ToString("N"));
        var service = new HotkeyService(new SettingsService(dir));
        service.RegisterAction(new CommandHotkeyAction("up", "Up", null, () => invoked++, canExecute: () => false), "Up");

        var menu = new Menu();
        var file = new MenuItem { Header = "File" };
        file.Items.Add(new MenuItem { Header = "Open" });
        menu.Items.Add(file);

        var window = new Window { Content = menu };
        window.Show();
        using var dispatcher = new KeymapDispatcher(window, new KeymapController(service), _ => null);
        file.Focus();

        window.KeyPressQwerty(PhysicalKey.ArrowUp, RawInputModifiers.None);

        Assert.Equal(0, invoked);
        Assert.True(file.IsSubMenuOpen);
    }

    [AvaloniaFact]
    public void Ctrl_Left_on_focused_top_level_item_is_not_consumed()
    {
        var invoked = 0;
        var dir = Path.Combine(Path.GetTempPath(), "senderng-dispatch-" + Guid.NewGuid().ToString("N"));
        var service = new HotkeyService(new SettingsService(dir));
        service.RegisterAction(new CommandHotkeyAction("left", "Left", null, () => invoked++), "Ctrl+Left");

        var menu = new Menu();
        var file = new MenuItem { Header = "File" };
        file.Items.Add(new MenuItem { Header = "Open" });
        menu.Items.Add(file);

        var window = new Window { Content = menu };
        window.Show();
        using var dispatcher = new KeymapDispatcher(window, new KeymapController(service), _ => null);
        file.Focus();

        window.KeyPressQwerty(PhysicalKey.ArrowLeft, RawInputModifiers.Control);

        // Left moves the selection between top-level items; it does not open a closed
        // menu. What matters here is that the app did NOT consume the arrow (invoked == 0).
        Assert.Equal(0, invoked);
        Assert.False(file.IsSubMenuOpen);
        Assert.False(menu.IsOpen);
    }

    // Inverse of the reported symptom: the user claims a Down arrow while focus is on
    // the center pane opens the menu. Avalonia's menu interaction handler only listens
    // on the Menu control itself, so a key whose source is NOT inside the menu cannot
    // reach it. This confirms the menu can only hijack arrows when one of its items
    // actually holds keyboard focus (e.g. a click that did not take focus).
    [AvaloniaFact]
    public void Bare_Down_from_focused_content_control_does_not_open_menu()
    {
        var menu = new Menu();
        var file = new MenuItem { Header = "File" };
        file.Items.Add(new MenuItem { Header = "Open" });
        menu.Items.Add(file);

        var button = new Button { Content = "Center" };

        var window = new Window
        {
            Content = new StackPanel
            {
                Children =
                {
                    menu,
                    button,
                },
            },
        };
        window.Show();
        using var dispatcher = new KeymapDispatcher(window, new KeymapController(new HotkeyService(
            new SettingsService(Path.Combine(Path.GetTempPath(), "senderng-dispatch-" + Guid.NewGuid().ToString("N"))))), _ => null);

        button.Focus();

        window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);

        Assert.False(file.IsSubMenuOpen);
        Assert.False(menu.IsOpen);
    }

    // Click-to-focus in action: a click on inert (non-focusable) content pulls keyboard
    // focus off the lingering menu item, so the next Down arrow jogs instead of opening
    // the menu. This is the fix for the original repro ("click the center pane, then
    // press Down -- the menu opens").
    [AvaloniaFact]
    public void Click_on_nonfocusable_content_moves_focus_off_menu_and_Down_jogs()
    {
        var invoked = 0;
        var dir = Path.Combine(Path.GetTempPath(), "senderng-dispatch-" + Guid.NewGuid().ToString("N"));
        var service = new HotkeyService(new SettingsService(dir));
        service.RegisterAction(new CommandHotkeyAction("down", "Down", null, () => invoked++), "Down");
        service.RegisterAction(new CommandHotkeyAction("up", "Up", null, () => invoked++), "Ctrl+Up");

        var menu = new Menu();
        var file = new MenuItem { Header = "File" };
        file.Items.Add(new MenuItem { Header = "Open" });
        menu.Items.Add(file);

        var spot = new Border { Background = Brushes.Gray };
        var window = new Window
        {
            Width = 800,
            Height = 600,
            Content = new Grid
            {
                RowDefinitions = { new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Star) },
                Children = { menu, spot },
            },
        };
        Grid.SetRow(spot, 1);
        window.Show();
        Dispatcher.UIThread.RunJobs();
        using var dispatcher = new KeymapDispatcher(window, new KeymapController(service), _ => null);
        file.Focus();
        Assert.True(file.IsFocused);

        var point = spot.TranslatePoint(new Point(spot.Bounds.Width / 2, spot.Bounds.Height / 2), window)!.Value;
        window.MouseMove(point, RawInputModifiers.None);
        window.MouseDown(point, MouseButton.Left, RawInputModifiers.None);
        window.MouseUp(point, MouseButton.Left, RawInputModifiers.None);

        Assert.False(file.IsFocused);

        window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);

        Assert.Equal(1, invoked);
        Assert.False(file.IsSubMenuOpen);
        Assert.False(menu.IsOpen);

        // A Ctrl chord on the same menu-focused-forgotten state must jog too, now that
        // focus no longer sits on the menu item.
        window.KeyPressQwerty(PhysicalKey.ArrowUp, RawInputModifiers.Control);

        Assert.Equal(2, invoked);
        Assert.False(file.IsSubMenuOpen);
        Assert.False(menu.IsOpen);
    }

    // Click on a focusable control moves focus to exactly that control (kept as a guard
    // so the click-to-focus handler never relocates focus away from what the user aimed
    // at, and never refuses a valid focusable target).
    [AvaloniaFact]
    public void Click_on_focusable_control_moves_focus_there()
    {
        var menu = new Menu();
        var file = new MenuItem { Header = "File" };
        file.Items.Add(new MenuItem { Header = "Open" });
        menu.Items.Add(file);

        var button = new Button { Content = "Center" };
        var window = new Window
        {
            Width = 800,
            Height = 600,
            Content = new Grid
            {
                RowDefinitions = { new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Star) },
                Children = { menu, button },
            },
        };
        Grid.SetRow(button, 1);
        window.Show();
        Dispatcher.UIThread.RunJobs();
        using var dispatcher = new KeymapDispatcher(window, new KeymapController(new HotkeyService(
            new SettingsService(Path.Combine(Path.GetTempPath(), "senderng-dispatch-" + Guid.NewGuid().ToString("N"))))), _ => null);
        file.Focus();

        var point = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
        window.MouseMove(point, RawInputModifiers.None);
        window.MouseDown(point, MouseButton.Left, RawInputModifiers.None);

        Assert.True(button.IsFocused);
        Assert.False(file.IsFocused);
    }

    // The reported live bug: "Alt then Down opens the menu, but arrows don't navigate
    // it." Once a top-level submenu opens, focus moves with it onto the overlay popup
    // layer, OUTSIDE the menu bar's visual tree. A visual-only "inside the menu" check
    // stops matching there, so the next Down ran the (connected) jog action instead of
    // moving the submenu selection. The check must follow the LOGICAL tree, which
    // crosses the popup boundary. Down must navigate the submenu and never jog.
    [AvaloniaFact]
    public void Down_inside_open_submenu_navigates_the_submenu_instead_of_jogging()
    {
        var invoked = 0;
        var dir = Path.Combine(Path.GetTempPath(), "senderng-dispatch-" + Guid.NewGuid().ToString("N"));
        var service = new HotkeyService(new SettingsService(dir));
        service.RegisterAction(new CommandHotkeyAction("down", "Down", null, () => invoked++), "Down");

        var menu = new Menu();
        var file = new MenuItem { Header = "File" };
        var open = new MenuItem { Header = "Open" };
        var save = new MenuItem { Header = "Save" };
        file.Items.Add(open);
        file.Items.Add(save);
        menu.Items.Add(file);

        var window = new Window { Width = 800, Height = 600, Content = menu };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        using var dispatcher = new KeymapDispatcher(window, new KeymapController(service), _ => null);
        file.Focus();

        window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);
        Assert.True(file.IsSubMenuOpen);

        // Second Down must move the submenu selection, not run the bound jog.
        window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);

        Assert.Equal(0, invoked);
        Assert.True(save.IsFocused);
        Assert.False(open.IsFocused);
    }

    // The full user journey behind the report: click inert content (parking focus),
    // Alt opens/activates the menu bar, Down opens the first submenu, Down moves into
    // it, Escape closes the menu -- all without a bound arrow ever running.
    [AvaloniaFact]
    public void Alt_then_arrows_operate_the_menu_and_escape_closes_it()
    {
        var invoked = 0;
        var dir = Path.Combine(Path.GetTempPath(), "senderng-dispatch-" + Guid.NewGuid().ToString("N"));
        var service = new HotkeyService(new SettingsService(dir));
        service.RegisterAction(new CommandHotkeyAction("down", "Down", null, () => invoked++), "Down");

        var menu = new Menu();
        var file = new MenuItem { Header = "File" };
        var open = new MenuItem { Header = "Open" };
        var save = new MenuItem { Header = "Save" };
        file.Items.Add(open);
        file.Items.Add(save);
        menu.Items.Add(file);

        var spot = new Border { Background = Brushes.Gray };
        var window = new Window
        {
            Width = 800,
            Height = 600,
            Content = new Grid
            {
                RowDefinitions = { new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Star) },
                Children = { menu, spot },
            },
        };
        Grid.SetRow(spot, 1);
        window.Show();
        Dispatcher.UIThread.RunJobs();
        using var dispatcher = new KeymapDispatcher(window, new KeymapController(service), _ => null);

        var point = spot.TranslatePoint(new Point(spot.Bounds.Width / 2, spot.Bounds.Height / 2), window)!.Value;
        window.MouseMove(point, RawInputModifiers.None);
        window.MouseDown(point, MouseButton.Left, RawInputModifiers.None);

        window.KeyPressQwerty(PhysicalKey.AltLeft, RawInputModifiers.None);
        window.KeyReleaseQwerty(PhysicalKey.AltLeft, RawInputModifiers.None);
        Assert.True(menu.IsOpen);

        window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);
        Assert.True(file.IsSubMenuOpen);

        window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);
        Assert.True(save.IsFocused);

        window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        // First Escape closes the submenu back to the active menu bar...
        Assert.False(file.IsSubMenuOpen);
        Assert.True(menu.IsOpen);

        // ...and a second one deactivates/dismisses the bar.
        window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);

        Assert.Equal(0, invoked);
        Assert.False(menu.IsOpen);
    }
}