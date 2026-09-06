/*
 * Loc.cs - {Loc key} markup extension for live module/settings localization
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

namespace SenderNG.Localization;

/// <summary>
/// <c>{Loc key}</c> markup extension. Resolves <paramref name="Key"/> against
/// <see cref="LocalizationService.Current"/> and returns a <see cref="BindingBase"/>
/// wrapping an observable so Avalonia re-binds on every locale change (null/absent
/// Current falls back to raw key). Used by module settings views (Jog, Goto, ...)
/// whose texts live in Locale JSON files.
/// </summary>
public sealed class LocExtension
{
    public LocExtension() { }

    /// <summary>Positional form: <c>{loc:Loc module.jog.diagonal}</c>.</summary>
    public LocExtension(string key) => Key = key;

    public string Key { get; set; } = string.Empty;

    public string DText { get; set; } = string.Empty;

    /// <summary>A binding that re-emits the resolved string on every locale change.</summary>
    public BindingBase ProvideValue(IServiceProvider? provider)
    {
        if (Design.IsDesignMode && !string.IsNullOrEmpty(DText))
        {
            return new Binding { Source = DText };
        }

        return LocSource.HotKey(Key).ToBinding();
    }
}

/// <summary>
/// Backing store for <see cref="LocExtension"/>: subscribes to
/// <see cref="LocalizationService.Changed"/> and re-emits the resolved string (or the
/// raw key when no service/Current is set yet).
/// </summary>
public static class LocSource
{
    public static IObservable<string> HotKey(string key)
    {
        var loc = LocalizationService.Current;
        if (loc is null)
            return Observable.Return(key);

        return Observable.Create<string>(observer =>
        {
            // Emit on the UI thread: binding updates must not be pushed from a
            // background thread even if Changed is raised there.
            void Emit(string value) => Dispatcher.UIThread.Post(() => observer.OnNext(value));

            void Handler(object? s, EventArgs e) => Emit(loc.T(key));
            Emit(loc.T(key));
            loc.Changed += Handler;
            return () => loc.Changed -= Handler;
        });
    }
}