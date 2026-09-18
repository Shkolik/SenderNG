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

    public KeymapDispatcher(Window window, KeymapController controller, Func<object, string?> moduleIdForView)
    {
        _window = window ?? throw new ArgumentNullException(nameof(window));
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        _moduleIdForView = moduleIdForView ?? throw new ArgumentNullException(nameof(moduleIdForView));

        _window.AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        _window.AddHandler(InputElement.KeyUpEvent, OnKeyUp, RoutingStrategies.Tunnel);
        _window.Deactivated += OnDeactivated;
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (!AvaloniaHotkeyInterop.TryGesture(e.Key, e.KeyModifiers, out var gesture) || gesture is null)
            return;

        var focused = _window.FocusManager?.GetFocusedElement();
        var textInput = IsTextInput(focused);
        var moduleId = ModuleIdFor(focused);
        if (_controller.KeyDown(gesture, textInput, id => string.Equals(id, moduleId, StringComparison.Ordinal)))
            e.Handled = true;
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

    public void Dispose()
    {
        _window.RemoveHandler(InputElement.KeyDownEvent, OnKeyDown);
        _window.RemoveHandler(InputElement.KeyUpEvent, OnKeyUp);
        _window.Deactivated -= OnDeactivated;
    }
}