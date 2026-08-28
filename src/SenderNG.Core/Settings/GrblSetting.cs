/*
 * GrblSetting.cs - GRBL settings models and wire-line parser
 */

/*

Copyright (c) 2018-2022 Io Engineering (Terje Io)

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

namespace SenderNG.Core.Settings;

/// <summary>
/// Setting value types, mirroring grblHAL's <c>$ES</c> type field and ioSender's
/// <c>GrblSettingDetails.DataTypes</c>. The type drives which editor the settings
/// UI renders (bool → checkbox, bitfield → checkbox set, radio → option group,
/// float/integer → numeric box with min/max, text/ip4 → text field).
/// </summary>
public enum GrblSettingType
{
    Bool = 0,
    Bitfield = 1,
    XBitfield = 2,
    RadioButtons = 3,
    AxisMask = 4,
    Integer = 5,
    Float = 6,
    Text = 7,
    Password = 8,
    Ip4 = 9,
}

/// <summary>
/// A single grbl setting: metadata (from the controller <c>$ES</c> dump on grblHAL
/// with ENUMS, or from the embedded catalog for classic grbl) merged with its
/// current value (from <c>$$</c>). <see cref="Name"/> / <see cref="Description"/>
/// are the resolved display strings (hardware English, or localized by
/// <see cref="SenderNG.Core.Localization.LocalizationService"/>); <see cref="EnglishName"/>
/// is the canonical English name used as the localization guard key.
/// </summary>
public sealed class GrblSetting
{
    public int Id { get; init; }
    public int GroupId { get; init; }

    /// <summary>Resolved display name (hardware/catalog English or localized).</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Canonical English name — the guard key for id-keyed localization.</summary>
    public string? EnglishName { get; init; }

    public string Unit { get; init; } = string.Empty;
    public GrblSettingType DataType { get; init; } = GrblSettingType.Text;
    public string Format { get; init; } = string.Empty;
    public double? Min { get; init; }
    public double? Max { get; init; }
    public bool RebootRequired { get; init; }
    public bool AllowNull { get; init; }

    /// <summary>Current value from <c>$$</c>, as a raw string.</summary>
    public string Value { get; set; } = string.Empty;

    /// <summary>Resolved long description (from <c>$SED</c>, the catalog, or a locale file).</summary>
    public string? Description { get; set; }

    /// <summary>True when the user edited <see cref="Value"/> and it differs from the board.</summary>
    public bool IsDirty { get; set; }
}

/// <summary>
/// A settings group node in the grblHAL tree (<c>$EG</c> dump: id, parent id, name),
/// or a static group from the embedded catalog for classic grbl. <see cref="Children"/>
/// and <see cref="Settings"/> are populated when the tree is assembled.
/// </summary>
public sealed class GrblSettingGroup
{
    public int Id { get; init; }
    public int ParentId { get; init; }
    public string Name { get; set; } = string.Empty;
    public List<GrblSettingGroup> Children { get; } = new();
    public List<GrblSetting> Settings { get; } = new();
}

/// <summary>
/// Pure parser for the grbl settings wire lines:
///   <c>$0=5.0</c>                      → value
///   <c>[SETTING:id|group|name|unit|type|format|min|max|reboot|allowNull]</c>  → metadata
///   <c>[SETTINGGROUP:id|parent|name]</c> → group
///   <c>[SETTINGDESCR:id|description]</c> → description (firmware sends literal \n)
/// Returns null for unrecognized lines so a malformed reply never throws.
/// </summary>
public static class GrblSettingsParser
{
    /// <summary>Parse a <c>$N=value</c> line into (id, value).</summary>
    public static (int Id, string Value)? ParseValueLine(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return null;
        var trimmed = line.Trim();
        var eq = trimmed.IndexOf('=');
        if (eq <= 1 || trimmed[0] != '$' || !int.TryParse(trimmed[1..eq], NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
            return null;
        return (id, trimmed[(eq + 1)..].Trim());
    }

    /// <summary>Parse a <c>[SETTING:...]</c> metadata line. Unknown data types fall back to Text.</summary>
    public static GrblSetting? ParseSettingLine(string? line)
    {
        var fields = SplitBracket(line, "SETTING");
        if (fields is null || fields.Count < 10)
            return null;

        if (!int.TryParse(fields[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) ||
            !int.TryParse(fields[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var group))
            return null;

        var name = fields[2];
        var type = int.TryParse(fields[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out var t) && Enum.IsDefined(typeof(GrblSettingType), t)
            ? (GrblSettingType)t
            : GrblSettingType.Text;

        return new GrblSetting
        {
            Id = id,
            GroupId = group,
            Name = name,
            EnglishName = name,
            Unit = fields[3],
            DataType = type,
            Format = fields[5],
            Min = ParseOptionalDouble(fields[6]),
            Max = ParseOptionalDouble(fields[7]),
            RebootRequired = fields.Count > 8 && fields[8] == "1",
            AllowNull = fields.Count > 9 && fields[9] == "1",
        };
    }

    /// <summary>Parse a <c>[SETTINGGROUP:id|parent|name]</c> line.</summary>
    public static GrblSettingGroup? ParseGroupLine(string? line)
    {
        var fields = SplitBracket(line, "SETTINGGROUP");
        if (fields is null || fields.Count < 3)
            return null;
        if (!int.TryParse(fields[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) ||
            !int.TryParse(fields[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parent))
            return null;
        return new GrblSettingGroup { Id = id, ParentId = parent, Name = fields[2] };
    }

    /// <summary>Parse a <c>[SETTINGDESCR:id|description]</c> line; converts the firmware's literal \n to real newlines.</summary>
    public static (int Id, string Description)? ParseDescriptionLine(string? line)
    {
        var fields = SplitBracket(line, "SETTINGDESCR");
        if (fields is null || fields.Count < 2)
            return null;
        if (!int.TryParse(fields[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
            return null;
        return (id, fields[1].Replace("\\n", "\n"));
    }

    /// <summary>Splits a <c>[TAG:...]</c> line on '|', returning the fields after the tag, or null.</summary>
    private static List<string>? SplitBracket(string? line, string tag)
    {
        if (string.IsNullOrWhiteSpace(line))
            return null;
        var trimmed = line.Trim();
        var prefix = $"[{tag}:";
        if (!trimmed.StartsWith(prefix, StringComparison.Ordinal) || !trimmed.EndsWith(']'))
            return null;
        return trimmed[prefix.Length..^1].Split('|').ToList();
    }

    private static double? ParseOptionalDouble(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
    }
}
