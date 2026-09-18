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

namespace SenderNG.Core.Modules;

/// <summary>Where a hotkey is live: always (Global) or only while a module's view has focus.</summary>
public enum HotkeyScope
{
    Global,
    Module,
}

/// <summary>A remappable action a hotkey can trigger.</summary>
public interface IHotkeyAction
{
    /// <summary>Stable key for the keymap and locale lookup.</summary>
    string ActionId { get; }

    /// <summary>Literal display name; fallback when no locale key resolves.</summary>
    string DisplayName { get; }

    /// <summary>"app.hotkeys.&lt;id&gt;" or "module.&lt;m&gt;.hotkey.&lt;id&gt;"; null = literal only.</summary>
    string? LocaleKey { get; }

    HotkeyScope Scope { get; }

    /// <summary>Non-null iff <see cref="Scope"/> is <see cref="HotkeyScope.Module"/>.</summary>
    string? ModuleId { get; }

    /// <summary>Step-jog style: re-dispatch on auto-repeat KeyDown while held.</summary>
    bool RepeatOnHold { get; }

    bool CanExecute();
    void OnKeyDown();

    /// <summary>No-op for momentary actions.</summary>
    void OnKeyUp();
}

/// <summary>Modules implement this to contribute runtime hotkey handlers.</summary>
public interface IHotkeyActionProvider
{
    IReadOnlyList<IHotkeyAction> CreateHotkeyActions(IServiceProvider services);
}

/// <summary>Concrete <see cref="IHotkeyAction"/> over delegates.</summary>
public sealed class CommandHotkeyAction : IHotkeyAction
{
    private readonly Action _onKeyDown;
    private readonly Action? _onKeyUp;
    private readonly Func<bool>? _canExecute;

    public CommandHotkeyAction(
        string actionId,
        string displayName,
        string? localeKey,
        Action onKeyDown,
        Func<bool>? canExecute = null,
        Action? onKeyUp = null,
        HotkeyScope scope = HotkeyScope.Global,
        string? moduleId = null,
        bool repeatOnHold = false)
    {
        ActionId = actionId ?? throw new ArgumentNullException(nameof(actionId));
        DisplayName = string.IsNullOrWhiteSpace(displayName) ? actionId : displayName;
        LocaleKey = localeKey;
        _onKeyDown = onKeyDown ?? throw new ArgumentNullException(nameof(onKeyDown));
        _canExecute = canExecute;
        _onKeyUp = onKeyUp;
        Scope = scope;
        ModuleId = moduleId;
        RepeatOnHold = repeatOnHold;
    }

    public string ActionId { get; }
    public string DisplayName { get; }
    public string? LocaleKey { get; }
    public HotkeyScope Scope { get; }
    public string? ModuleId { get; }
    public bool RepeatOnHold { get; }

    public bool CanExecute() => _canExecute?.Invoke() ?? true;
    public void OnKeyDown() => _onKeyDown();
    public void OnKeyUp() => _onKeyUp?.Invoke();
}