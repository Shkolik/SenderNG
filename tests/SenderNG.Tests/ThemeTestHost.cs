/*
 * ThemeTestHost.cs - loads the Sender theme dictionary for headless tests
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
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Styling;
using System.Runtime.CompilerServices;

namespace SenderNG.Tests;

/// <summary>Loads the app's theme dictionary straight from the compiled
/// avares resource and exposes per-variant key lookups for tests.</summary>
internal static class ThemeTestHost
{
    public static ResourceDictionary LoadSenderTheme() =>
        (ResourceDictionary)AvaloniaXamlLoader.Load(
            new Uri("avares://SenderNG/Themes/SenderTheme.axaml"));

    public static IResourceDictionary DictionaryFor(ResourceDictionary theme, ThemeVariant variant) =>
        (IResourceDictionary)theme.ThemeDictionaries[variant]!;

    public static ISolidColorBrush Brush(ResourceDictionary theme, ThemeVariant variant, string key) =>
        (ISolidColorBrush)DictionaryFor(theme, variant)[key]!;

    public static Color ColorOf(ResourceDictionary theme, ThemeVariant variant, string key) =>
        Brush(theme, variant, key).Color;

    private static readonly ConditionalWeakTable<Application, ResourceDictionary> MergedThemes = new();

    /// <summary>Merge SenderTheme into Application.Current.Resources the same way
    /// App.axaml does. Idempotent per application: the headless test host hands each
    /// test a fresh Application, so a process-wide flag alone is not enough.</summary>
    public static void EnsureMerged()
    {
        var app = Application.Current!;
        if (MergedThemes.TryGetValue(app, out _))
            return;
        var theme = LoadSenderTheme();
        app.Resources.MergedDictionaries.Add(theme);
        MergedThemes.Add(app, theme);
    }
}
