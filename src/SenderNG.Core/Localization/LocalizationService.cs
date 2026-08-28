/*
 * LocalizationService.cs - locale-aware string + grbl setting localization
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

using System.Globalization;
using System.Text.Json;
using SenderNG.Core.Settings;

namespace SenderNG.Core.Localization;

/// <summary>
/// Embedded catalog entry for one grbl setting: machine metadata (type, format,
/// min/max, group) plus the canonical English name/description. Serves two roles:
/// the structure + English strings fallback for classic grbl (no <c>$ES</c> dump),
/// and the English source for the id + en-name localization guard on grblHAL.
/// </summary>
public sealed record GrblSettingDefinition(
    int Id, int GroupId, string Name, string Unit, GrblSettingType Type,
    string Format, double? Min, double? Max, bool RebootRequired, bool AllowNull,
    string? Description);

/// <summary>Embedded catalog group node (grblHAL group tree, reused for classic grouping).</summary>
public sealed record GrblSettingGroupDefinition(int Id, int ParentId, string Name);

/// <summary>
/// Per-locale override for one grbl setting. <see cref="Name"/> / <see cref="Description"/>
/// are the localized strings; <see cref="EnglishName"/> is the canonical English name the
/// translation was authored against — the localization guard compares it with the
/// controller's own name before applying, so a driver-specific id reused with a
/// different meaning never receives a mismatched translation.
/// </summary>
public sealed record GrblSettingLocalization(int Id, string? Name, string? EnglishName, string? Description);

/// <summary>
/// Loads the locale JSON catalog (embedded <c>en.json</c> + optional per-locale
/// override) and resolves strings. Two services in one, deliberately:
///   • <see cref="T"/> — regular app strings, fallback chain: locale → English → key.
///   • grbl settings — <see cref="Settings"/> (structure + en strings, the classic
///     fallback) and <see cref="ApplySettingStrings"/> (guard-based localization for
///     settings whose metadata came from the controller).
/// JSON only — no csv/txt — so files are structured and machine-readable, and the
/// same pipeline feeds every future locale.
/// </summary>
public sealed class LocalizationService
{
    private const string EmbeddedEnJson = "SenderNG.Core.Localization.en.json";
    private static LocalizationService? _embedded;

    private readonly string _locale;
    private readonly IReadOnlyDictionary<string, string> _enStrings;
    private readonly IReadOnlyDictionary<string, string> _localeStrings;
    private readonly IReadOnlyDictionary<int, GrblSettingDefinition> _settings;
    private readonly IReadOnlyDictionary<int, GrblSettingGroupDefinition> _groups;
    private readonly IReadOnlyDictionary<int, GrblSettingLocalization> _overrides;

    /// <summary>Shared instance over the embedded en.json (English, no overrides).</summary>
    public static LocalizationService Embedded => _embedded ??= CreateEmbedded();

    /// <summary>Build from the embedded <c>en.json</c> only (English base).</summary>
    public static LocalizationService CreateEmbedded()
    {
        var stream = typeof(LocalizationService).Assembly.GetManifestResourceStream(EmbeddedEnJson)
            ?? throw new InvalidOperationException($"Embedded resource '{EmbeddedEnJson}' not found.");
        return new LocalizationService(stream);
    }

    /// <summary>
    /// Build from an en.json stream plus an optional locale override stream (a
    /// <c>&lt;locale&gt;.json</c> file with <c>strings</c> and <c>grblSettings</c>
    /// sections; each setting entry carries <c>name</c>, <c>en</c> and optional
    /// <c>description</c>).
    /// </summary>
    public LocalizationService(Stream enJson, string localeCode = "en", Stream? localeJson = null)
    {
        _locale = string.IsNullOrWhiteSpace(localeCode) ? "en" : localeCode;

        using (enJson)
        {
            var en = ReadCatalog(enJson);
            _enStrings = en.Strings;
            _settings = en.Settings;
            _groups = en.Groups;
        }

        _localeStrings = _enStrings;
        _overrides = new Dictionary<int, GrblSettingLocalization>();

        if (localeJson is not null && !string.Equals(_locale, "en", StringComparison.OrdinalIgnoreCase))
        {
            using (localeJson)
            {
                _overrides = ReadOverrides(localeJson, out var locStrings);
                if (locStrings.Count > 0)
                    _localeStrings = locStrings;
            }
        }
    }

    /// <summary>Current locale code ("en" unless a locale file was supplied).</summary>
    public string Locale => _locale;

    /// <summary>Embedded English grbl-settings catalog (structure + en strings).</summary>
    public IReadOnlyDictionary<int, GrblSettingDefinition> Settings => _settings;

    /// <summary>Embedded English group tree.</summary>
    public IReadOnlyDictionary<int, GrblSettingGroupDefinition> Groups => _groups;

    /// <summary>
    /// Resolve a regular app string: locale file → English → the key itself.
    /// </summary>
    public string T(string key)
    {
        if (_localeStrings.TryGetValue(key, out var localized))
            return localized;
        if (_enStrings.TryGetValue(key, out var english))
            return english;
        return key;
    }

    /// <summary>
    /// Applies the localized name/description to a setting whose metadata came from
    /// the controller (grblHAL <c>$ES</c>/<c>$SED</c>) or the embedded catalog.
    /// Guard rule: a per-locale override applies only when its canonical English name
    /// matches the setting's own English name (case-insensitive), or when either side
    /// has no English name (classic grbl, whose ids are spec-stable). A mismatch
    /// keeps the controller's string rather than misapplying a translation.
    /// </summary>
    public void ApplySettingStrings(GrblSetting setting)
    {
        if (string.Equals(_locale, "en", StringComparison.OrdinalIgnoreCase))
            return;

        if (!_overrides.TryGetValue(setting.Id, out var loc))
            return;

        var guardPass = string.IsNullOrEmpty(loc.EnglishName)
            || string.IsNullOrEmpty(setting.EnglishName)
            || string.Equals(loc.EnglishName, setting.EnglishName, StringComparison.OrdinalIgnoreCase);

        if (!guardPass)
            return;

        if (!string.IsNullOrEmpty(loc.Name))
            setting.Name = loc.Name;
        if (!string.IsNullOrEmpty(loc.Description))
            setting.Description = loc.Description;
    }

    private static (IReadOnlyDictionary<string, string> Strings,
        IReadOnlyDictionary<int, GrblSettingDefinition> Settings,
        IReadOnlyDictionary<int, GrblSettingGroupDefinition> Groups) ReadCatalog(Stream stream)
    {
        using var doc = JsonDocument.Parse(stream);

        var strings = new Dictionary<string, string>();
        if (doc.RootElement.TryGetProperty("strings", out var stringsEl) && stringsEl.ValueKind == JsonValueKind.Object)
            foreach (var p in stringsEl.EnumerateObject())
                strings[p.Name] = p.Value.GetString() ?? string.Empty;

        var settings = new Dictionary<int, GrblSettingDefinition>();
        if (doc.RootElement.TryGetProperty("grblSettings", out var settingsEl) && settingsEl.ValueKind == JsonValueKind.Object)
        {
            foreach (var p in settingsEl.EnumerateObject())
            {
                var e = p.Value;
                if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty("id", out var idEl) ||
                    !idEl.TryGetInt32(out var id))
                    continue;
                settings[id] = new GrblSettingDefinition(
                    id,
                    GetInt(e, "group", 0),
                    GetString(e, "name") ?? string.Empty,
                    GetString(e, "unit") ?? string.Empty,
                    ParseSettingType(GetString(e, "type")),
                    GetString(e, "format") ?? string.Empty,
                    GetDouble(e, "min"),
                    GetDouble(e, "max"),
                    GetBool(e, "reboot"),
                    GetBool(e, "allowNull"),
                    GetString(e, "description"));
            }
        }

        var groups = new Dictionary<int, GrblSettingGroupDefinition>();
        if (doc.RootElement.TryGetProperty("grblSettingGroups", out var groupsEl) && groupsEl.ValueKind == JsonValueKind.Object)
        {
            foreach (var p in groupsEl.EnumerateObject())
            {
                var e = p.Value;
                if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty("id", out var idEl) ||
                    !idEl.TryGetInt32(out var id))
                    continue;
                groups[id] = new GrblSettingGroupDefinition(id, GetInt(e, "parent", 0), GetString(e, "name") ?? string.Empty);
            }
        }

        return (strings, settings, groups);
    }

    /// <summary>
    /// Parses a locale override file: <c>strings</c> for regular strings, and
    /// <c>grblSettings.&lt;id&gt; = { name, en, description }</c> — <c>en</c> is the
    /// canonical English name the translation was authored against (the guard key).
    /// </summary>
    private static IReadOnlyDictionary<int, GrblSettingLocalization> ReadOverrides(Stream stream, out IReadOnlyDictionary<string, string> locStrings)
    {
        using var doc = JsonDocument.Parse(stream);

        var strings = new Dictionary<string, string>();
        if (doc.RootElement.TryGetProperty("strings", out var stringsEl) && stringsEl.ValueKind == JsonValueKind.Object)
            foreach (var p in stringsEl.EnumerateObject())
                strings[p.Name] = p.Value.GetString() ?? string.Empty;

        var overrides = new Dictionary<int, GrblSettingLocalization>();
        if (doc.RootElement.TryGetProperty("grblSettings", out var settingsEl) && settingsEl.ValueKind == JsonValueKind.Object)
        {
            foreach (var p in settingsEl.EnumerateObject())
            {
                var e = p.Value;
                if (e.ValueKind != JsonValueKind.Object || !int.TryParse(p.Name, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
                    continue;
                overrides[id] = new GrblSettingLocalization(
                    id,
                    GetString(e, "name"),
                    GetString(e, "en"),
                    GetString(e, "description"));
            }
        }

        locStrings = strings;
        return overrides;
    }

    /// <summary>
    /// Maps the JSON type name to the enum. "radio" is the catalog's name for
    /// <see cref="GrblSettingType.RadioButtons"/> — Enum.TryParse can't match it,
    /// so radio settings must map explicitly or they'd fall back to Text editors.
    /// </summary>
    private static GrblSettingType ParseSettingType(string? name) => name switch
    {
        "radio" => GrblSettingType.RadioButtons,
        _ => Enum.TryParse<GrblSettingType>(name, true, out var type) ? type : GrblSettingType.Text,
    };

    private static int GetInt(JsonElement e, string name, int fallback) =>
        e.TryGetProperty(name, out var v) && v.TryGetInt32(out var i) ? i : fallback;

    private static string? GetString(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static double? GetDouble(JsonElement e, string name)
    {
        if (!e.TryGetProperty(name, out var v) || v.ValueKind is not (JsonValueKind.Number or JsonValueKind.String))
            return null;
        return v.ValueKind == JsonValueKind.Number
            ? v.GetDouble()
            : double.TryParse(v.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : null;
    }

    private static bool GetBool(JsonElement e, string name, bool fallback = false)
    {
        if (!e.TryGetProperty(name, out var v) || v.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            return fallback;
        return v.GetBoolean();
    }
}
