/*
 * LocText.cs - code-side string localization helper for ViewModel-generated text
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

using SenderNG.Core.Localization;

namespace SenderNG.Localization;

/// <summary>
/// Code-side counterpart of the <c>{Loc key}</c> markup extension: ViewModels emit
/// user-facing status/result strings from places that can't declare markup, so this
/// resolves a key against <see cref="LocalizationService.Current"/> when set and
/// falls back to the supplied English text otherwise (headless/unset Current).
/// Unlike <c>T</c>, it never returns the raw key — the English literal stays in the
/// caller so strings read correctly even with no locale catalog loaded.
/// </summary>
public static class LocText
{
    /// <summary>Resolve <paramref name="key"/> to the active locale, else <paramref name="english"/>.</summary>
    public static string Resolve(string key, string english)
    {
        var loc = LocalizationService.Current;
        return loc is null ? english : loc.Resolve(key, english);
    }

    /// <summary>Localized format string (placeholders <c>{0}</c>…), English fallback.</summary>
    public static string Format(string key, string english, params object?[] args)
    {
        try
        {
            return string.Format(Resolve(key, english), args);
        }
        catch (FormatException)
        {
            return english;
        }
    }

    /// <summary>
    /// The active locale code, e.g. "en"/"ru" — the app language the user chose
    /// (AppSettings feeds this through <see cref="LocalizationService.SetLocale"/>).
    /// English when no catalog is loaded.
    /// </summary>
    public static string GetLanguage() => LocalizationService.Current?.Locale ?? "en";
}
