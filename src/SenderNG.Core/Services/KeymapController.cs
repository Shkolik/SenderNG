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

using SenderNG.Core.Modules;

namespace SenderNG.Core.Services;

/// <summary>
/// Framework-agnostic key dispatch policy: text-focus suppression, auto-repeat,
/// press/release tracking, and release-on-deactivation. The host adapter feeds it
/// canonical gestures built from real key events.
/// </summary>
public sealed class KeymapController
{
    private readonly HotkeyService _service;
    private readonly Action<Exception>? _onError;
    private readonly Dictionary<string, IHotkeyAction> _pressed = new(StringComparer.Ordinal);

    public KeymapController(HotkeyService service, Action<Exception>? onError = null)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _onError = onError;
    }

    public int PressedCount => _pressed.Count;

    /// <summary>Handle a KeyDown. Returns true when the event was consumed.</summary>
    public bool KeyDown(string gesture, bool textInputFocused, Func<string, bool> moduleHasFocus)
    {
        if (textInputFocused && !HotkeyGrammar.HasModifiers(gesture))
            return false;

        var action = _service.Resolve(gesture, moduleHasFocus);
        if (action is null || !SafeCanExecute(action))
            return false;

        if (_pressed.ContainsKey(gesture))
        {
            if (action.RepeatOnHold)
                Safe(action.OnKeyDown);
            return true;
        }

        Safe(action.OnKeyDown);
        _pressed[gesture] = action;
        return true;
    }

    /// <summary>Handle a KeyUp. Returns true when a pressed action was released.</summary>
    public bool KeyUp(string gesture)
    {
        if (!_pressed.Remove(gesture, out var action))
            return false;
        Safe(action.OnKeyUp);
        return true;
    }

    /// <summary>Release everything: a lost KeyUp (window/modal deactivation) must
    /// never leave a continuous jog running.</summary>
    public void WindowDeactivated()
    {
        foreach (var action in _pressed.Values)
            Safe(action.OnKeyUp);
        _pressed.Clear();
    }

    private bool SafeCanExecute(IHotkeyAction action)
    {
        try
        {
            return action.CanExecute();
        }
        catch (Exception ex)
        {
            _onError?.Invoke(ex);
            return false;
        }
    }

    private void Safe(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            _onError?.Invoke(ex);
        }
    }
}