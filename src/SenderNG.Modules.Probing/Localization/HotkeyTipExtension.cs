/*
 * HotkeyTipExtension.cs - {HotkeyTip key, actionId} markup extension for
 * tooltip "tip [gesture]" text on probe actions
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

using System.Reactive.Linq;
using Avalonia;
using Avalonia.Data;
using Avalonia.Controls;
using Avalonia.Threading;
using SenderNG.Core.Localization;
using SenderNG.Modules.Plugins;

namespace SenderNG.Modules.Probing.Localization;

/// <summary>
/// <c>{HotkeyTip key, actionId}</c> markup extension: "{localized tip} [{module
/// gesture}]". The probing plugin owns its module actions (their gestures come
/// from <see cref="ProbingHotkeyProvider"/>), so the gesture is resolved against
/// the module's declared default rather than the app-wide hotkey aggregator —
/// remaps applied in the app are composed by the app's tooltip affordances.
/// </summary>
public sealed class HotkeyTipExtension
{
    public HotkeyTipExtension() { }

    /// <summary>Positional form: <c>{loc:HotkeyTip module.probing.hotkey.probe.start}</c>.</summary>
    public HotkeyTipExtension(string key) => Key = key;

    /// <summary>Positional form: <c>{loc:HotkeyTip module.probing.hotkey.probe.start, probe.start}</c>.</summary>
    public HotkeyTipExtension(string key, string actionId)
    {
        Key = key;
        ActionId = actionId;
    }

    public string Key { get; set; } = string.Empty;
    public string ActionId { get; set; } = string.Empty;
    public string DText { get; set; } = string.Empty;

    /// <summary>A binding that re-emits the composed tooltip text on locale change.</summary>
    public BindingBase ProvideValue(IServiceProvider? provider)
    {
        if (Design.IsDesignMode && !string.IsNullOrEmpty(DText))
            return new Binding { Source = DText };

        return HotkeyTipSource.Tip(Key, ActionId).ToBinding();
    }
}

/// <summary>Backing store for <see cref="HotkeyTipExtension"/>: subscribes to
/// <see cref="LocalizationService.Changed"/> and re-emits
/// <see cref="Compose"/>d text (raw key when no localization is wired yet).</summary>
public static class HotkeyTipSource
{
    public static string Compose(string tooltip, string? gesture)
        => gesture is { Length: > 0 } ? $"{tooltip} [{gesture}]" : tooltip;

    public static IObservable<string> Tip(string key, string actionId)
    {
        return Observable.Create<string>(observer =>
        {
            void Emit()
            {
                var loc = LocalizationService.Current;
                var normal = loc?.T(key) ?? key;
                var gesture = ProbingHotkeyProvider.DefaultGestureFor(actionId);
                var text = Compose(normal, gesture);
                // Emit on the UI thread: binding updates must not be pushed from
                // a background thread even if Changed is raised there.
                Dispatcher.UIThread.Post(() => observer.OnNext(text));
            }

            void OnLocalizedChanged(object? s, EventArgs e) => Emit();

            if (LocalizationService.Current is { } l)
                l.Changed += OnLocalizedChanged;

            Emit();

            return () =>
            {
                if (LocalizationService.Current is { } l)
                    l.Changed -= OnLocalizedChanged;
            };
        });
    }
}