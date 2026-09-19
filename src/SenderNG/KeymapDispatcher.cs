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
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using SenderNG.Core.Services;

namespace SenderNG;

/// <summary>Tunnel key pump on the MainWindow: builds canonical gestures and drives
/// the Core <see cref="KeymapController"/>; owns focus/scoping and deactivation release.</summary>
public sealed class KeymapDispatcher : IDisposable
{
    private readonly Window _window;
    private readonly KeymapController _controller;
    private readonly Func<object, string?> _moduleIdForView;

    // An invisible keyboard parking spot, appended to the content root. Clicking an
    // inert area (no focusable ancestor at the pointer) focuses it, so focus never
    // lingers on the menu bar. The window itself must NOT be Focusable: a focusable
    // top-level confuses the menu's keyboard handling, so the window cannot be the
    // parking target.
    private readonly Control _parking;

    public KeymapDispatcher(Window window, KeymapController controller, Func<object, string?> moduleIdForView)
    {
        _window = window ?? throw new ArgumentNullException(nameof(window));
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        _moduleIdForView = moduleIdForView ?? throw new ArgumentNullException(nameof(moduleIdForView));

        _parking = new Control
        {
            Focusable = true,
            IsTabStop = false,
            IsHitTestVisible = false,
            Width = 0,
            Height = 0,
        };

        // Attach the parking spot only when the content root is a Panel (the MainWindow's
        // root grid is). Single-control content is left untouched -- wrapping it would
        // detach/reparent the focused element and throws for already-attached controls.
        if (window.Content is Panel panel)
            panel.Children.Add(_parking);

        _window.AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        _window.AddHandler(InputElement.KeyUpEvent, OnKeyUp, RoutingStrategies.Tunnel);
        _window.AddHandler(InputElement.PointerPressedEvent, OnPointerPressed, RoutingStrategies.Bubble);
        _window.Deactivated += OnDeactivated;
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (!AvaloniaHotkeyInterop.TryGesture(e.Key, e.KeyModifiers, out var gesture) || gesture is null)
            return;

        var focused = _window.FocusManager?.GetFocusedElement();

        // The main menu owns the raw arrows while it holds keyboard focus: the user is
        // deliberately operating the menu (see OnPointerPressed -- content clicks park
        // focus on the clicked control or the parking control, so this branch only fires
        // for real menu use or Tab navigation). Let Up/Down/Left/Right reach the menu's
        // handler even when a jog action could execute, so a focused menu keeps its
        // standard keyboard navigation (open a top-level item, move between items).
        if (e.Key is Key.Up or Key.Down or Key.Left or Key.Right && IsInsideMainMenuBar(focused))
            return;

        var textInput = IsTextInput(focused);
        var moduleId = ModuleIdFor(focused);
        if (_controller.KeyDown(gesture, textInput, id => string.Equals(id, moduleId, StringComparison.Ordinal)))
            e.Handled = true;
    }

    // Click-to-focus: Avalonia leaves keyboard focus on the last focused element when a
    // pointer press lands on a non-focusable spot (blank panel, empty scroll area, tab
    // content). With the arrows bound as jog keys that lingers as an invisible "menu bar
    // focused" state, and the menu's handler then opens on Up/Down (it ignores modifiers).
    // A left press on any non-menu, non-editable area moves keyboard focus to the clicked
    // control (nearest focusable ancestor) or the parking control, so the arrows go to
    // the app.
    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(_window).Properties.IsLeftButtonPressed)
            return;
        if (e.Source is not Visual source)
            return;
        if (IsTextInput(source) || IsInsideMainMenuBar(source))
            return;

        var target = FocusableAncestor(source);
        if (target is not null)
            target.Focus();
        else
            _parking.Focus();
    }

    private static InputElement? FocusableAncestor(Visual visual)
    {
        for (Visual? v = visual; v is not null; v = v.GetVisualParent())
            if (v is InputElement element && element.Focusable)
                return element;
        return null;
    }

    private void OnKeyUp(object? sender, KeyEventArgs e)
    {
        if (!AvaloniaHotkeyInterop.TryGesture(e.Key, e.KeyModifiers, out var gesture) || gesture is null)
            return;
        if (_controller.KeyUp(gesture))
            e.Handled = true;
    }

    private void OnDeactivated(object? sender, EventArgs e) => _controller.WindowDeactivated();

    private string? ModuleIdFor(object? focused)
    {
        if (focused is not Visual visual)
            return null;
        for (Visual? v = visual; v is not null; v = v.GetVisualParent())
            if (_moduleIdForView(v) is { } id)
                return id;
        return null;
    }

    private static bool IsTextInput(object? focused)
    {
        if (focused is not Visual visual)
            return false;
        for (Visual? v = visual; v is not null; v = v.GetVisualParent())
            if (v is TextBox)
                return true;
        return false;
    }

    // Menu-owned focus: the focused element is a Menu/MenuItem in the LOGICAL tree.
    // Logical ancestry is used because an open submenu popup runs on the overlay
    // layer -- its items are visual children of the popup root, not of the Menu bar,
    // but they stay in the Menu's logical tree. This covers menu-bar items and open
    // sub/popup menus alike, so their arrows keep reaching the menu handlers.
    private static bool IsInsideMainMenuBar(object? focused)
    {
        if (focused is not ILogical logical)
            return false;
        foreach (var l in LogicalExtensions.GetSelfAndLogicalAncestors(logical))
            if (l is Menu or MenuItem)
                return true;
        return false;
    }

    public void Dispose()
    {
        _window.RemoveHandler(InputElement.KeyDownEvent, OnKeyDown);
        _window.RemoveHandler(InputElement.KeyUpEvent, OnKeyUp);
        _window.RemoveHandler(InputElement.PointerPressedEvent, OnPointerPressed);
        _window.Deactivated -= OnDeactivated;
    }
}