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
using SenderNG.Core.Services;

namespace SenderNG.Localization;

public sealed class HotkeyTipExtension
{
    public HotkeyTipExtension() { }

    /// <summary>Positional form: <c>{loc:HotkeyTip module.jog.zero}</c>.</summary>
    public HotkeyTipExtension(string key) => Key = key;

    /// <summary>Positional form: <c>{loc:HotkeyTip app.cycle.startTip, cycle.start}</c>.</summary>
    public HotkeyTipExtension(string key, string actionId)
    {
        Key = key;
        ActionId = actionId;
    }

    public string Key { get; set; } = string.Empty;
    public string ActionId { get; set; } = string.Empty;
    public string DText { get; set; } = string.Empty;

    /// <summary>A binding that re-emits the tooltip text ("{tip} [{gesture}]") on
    /// locale change, remap, or the AppHotkeys handle appearing.</summary>
    public BindingBase ProvideValue(IServiceProvider? provider)
    {
        if (Design.IsDesignMode && !string.IsNullOrEmpty(DText))
            return new Binding { Source = DText };

        return HotkeyTipSource.Tip(Key, ActionId).ToBinding();
    }
}

/// <summary>Backing store for <see cref="HotkeyTipExtension"/>: subscribes to
/// <see cref="LocalizationService.Changed"/>, <see cref="AppHotkeys.Changed"/>
/// and the current service's <see cref="HotkeyService.Changed"/> and re-emits
/// <see cref="Compose"/>d text (raw key when no localization is wired yet).</summary>
public static class HotkeyTipSource
{
    public static string Compose(string tooltip, string? gesture)
        => gesture is { Length: > 0 } ? $"{tooltip} [{gesture}]" : tooltip;

    public static IObservable<string> Tip(string key, string actionId)
    {
        return Observable.Create<string>(observer =>
        {
            HotkeyService? tracked = null;

            void Emit()
            {
                var loc = LocalizationService.Current;
                var normal = loc?.T(key) ?? key;
                var gesture = AppHotkeys.Current?.GestureFor(actionId);
                var text = Compose(normal, gesture);
                // Emit on the UI thread: binding updates must not be pushed from
                // a background thread even if Changed is raised there.
                Dispatcher.UIThread.Post(() => observer.OnNext(text));
            }

            void OnAppHotkeysChanged(object? s, EventArgs e)
            {
                if (tracked is not null)
                    tracked.Changed -= OnHotkeysChanged;
                tracked = AppHotkeys.Current;
                if (tracked is not null)
                    tracked.Changed += OnHotkeysChanged;
                Emit();
            }

            void OnHotkeysChanged(object? s, EventArgs e) => Emit();
            void OnLocalizedChanged(object? s, EventArgs e) => Emit();

            if (LocalizationService.Current is { } loc)
                loc.Changed += OnLocalizedChanged;
            AppHotkeys.Changed += OnAppHotkeysChanged;
            tracked = AppHotkeys.Current;
            if (tracked is not null)
                tracked.Changed += OnHotkeysChanged;

            Emit();

            return () =>
            {
                if (LocalizationService.Current is { } loc)
                    loc.Changed -= OnLocalizedChanged;
                AppHotkeys.Changed -= OnAppHotkeysChanged;
                if (tracked is not null)
                    tracked.Changed -= OnHotkeysChanged;
            };
        });
    }
}