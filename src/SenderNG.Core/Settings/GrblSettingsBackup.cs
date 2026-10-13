/*
 * GrblSettingsBackup.cs - a controller's grbl settings as a portable JSON file
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
using System.Text.Json;
using System.Text.Json.Serialization;
using CNC.Core.Services;

namespace SenderNG.Core.Settings;

/// <summary>
/// The controller a backup came from — the <c>$I</c> fingerprint
/// (<see cref="ControllerCapabilities.ControllerKey"/>) plus the handshake fields the
/// operator may want to read in the file. Informational on restore: a restore is
/// validated against the <em>board's</em> current metadata, never this.
/// </summary>
public sealed record GrblBackupController(string Key, string Firmware, string Version, int Build,
                                          int NumAxes, bool IsGrblHAL, bool MetadataFromController);

/// <summary>
/// One setting as captured. <see cref="Type"/> is the numeric
/// <see cref="GrblSettingType"/> value (the same external contract <c>$ES</c> uses),
/// not an enum name a rename could break. Metadata is informational — validation on
/// restore always uses the board's own current metadata.
/// </summary>
public sealed record GrblBackupSetting(int Id, string Value, string Name, string Unit,
                                       GrblSettingType Type, string Format, double? Min, double? Max,
                                       bool RebootRequired);

/// <summary>
/// Result of <see cref="GrblSettingsBackup.Parse"/>: the parsed backup, or the reason
/// the file is not a usable SenderNG grbl settings backup. Never both empty.
/// </summary>
public sealed record GrblSettingsBackupReadResult(GrblSettingsBackup? Backup, string? Error)
{
    public bool Ok => Backup is not null;
}

/// <summary>
/// A controller's grbl settings as a self-describing JSON file: the <c>$I</c>
/// controller fingerprint plus every id the board reported in <c>$$</c> (including
/// Password-type settings — fidelity over caution for a file the user controls) with
/// the metadata needed to read it. Pure: <see cref="Create"/> maps a loaded snapshot
/// and <see cref="Parse"/> never throws, so a corrupt or foreign file is a reported
/// error and never an exception on the UI path.
/// </summary>
public sealed class GrblSettingsBackup
{
    public const string Kind = "senderng-grbl-settings";
    public const int CurrentFormatVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public string CreatedUtc { get; init; } = "";
    public string AppVersion { get; init; } = "";
    public GrblBackupController Controller { get; init; } = new("unknown", "", "", 0, 3, false, false);
    public IReadOnlyList<GrblBackupSetting> Settings { get; init; } = [];

    /// <summary>
    /// Capture a loaded snapshot. Entries are ordered by id and carry the value the
    /// controller reported; the caller is responsible for having read the board fresh
    /// (a backup must never be built from unsaved editor state).
    /// </summary>
    public static GrblSettingsBackup Create(GrblSettingsSnapshot snapshot,
                                            ControllerCapabilities capabilities,
                                            string appVersion, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(capabilities);

        return new GrblSettingsBackup
        {
            CreatedUtc = FormatUtc(now),
            AppVersion = appVersion,
            Controller = new GrblBackupController(
                capabilities.ControllerKey,
                capabilities.Firmware,
                capabilities.Version,
                capabilities.Build,
                capabilities.NumAxes,
                capabilities.IsGrblHAL,
                snapshot.MetadataFromController),
            Settings = snapshot.Settings
                .OrderBy(static s => s.Id)
                .Select(static s => new GrblBackupSetting(
                    s.Id, s.Value, s.Name, s.Unit, s.DataType, s.Format, s.Min, s.Max, s.RebootRequired))
                .ToList(),
        };
    }

    public string ToJson() => JsonSerializer.Serialize(ToPayload(), JsonOptions);

    /// <summary>
    /// Read a backup file. Rejects malformed JSON, a foreign or missing kind, a newer
    /// format version, an empty setting list, and duplicate or negative ids. Never
    /// throws — every rejection is the returned error string.
    /// </summary>
    public static GrblSettingsBackupReadResult Parse(string json)
    {
        try
        {
            var payload = JsonSerializer.Deserialize<Payload>(json, JsonOptions);
            if (payload is null)
                return Fail("The file is empty.");

            if (!string.Equals(payload.Kind, Kind, StringComparison.Ordinal))
                return Fail($"kind '{payload.Kind}' is not '{Kind}'");

            if (payload.FormatVersion > CurrentFormatVersion)
                return Fail($"formatVersion {payload.FormatVersion} is newer than {CurrentFormatVersion}");

            if (payload.Controller is null)
                return Fail("no controller fingerprint");

            if (payload.Settings is not { Count: > 0 })
                return Fail("it holds no settings");

            if (payload.Settings.Any(static s => s is null || s.Id < 0))
                return Fail("it holds an invalid setting id");

            if (payload.Settings.Select(static s => s.Id).Distinct().Count() != payload.Settings.Count)
                return Fail("it holds duplicate setting ids");

            return new GrblSettingsBackupReadResult(
                new GrblSettingsBackup
                {
                    CreatedUtc = payload.CreatedUtc,
                    AppVersion = payload.AppVersion,
                    Controller = payload.Controller,
                    Settings = payload.Settings,
                },
                null);
        }
        catch (JsonException ex)
        {
            return Fail($"not valid JSON ({ex.Message})");
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or FormatException)
        {
            return Fail(ex.Message);
        }
    }

    private static GrblSettingsBackupReadResult Fail(string reason) =>
        new(null, $"Not a SenderNG grbl settings backup: {reason}.");

    private static string FormatUtc(DateTimeOffset now) =>
        now.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss'Z'", CultureInfo.InvariantCulture);

    private Payload ToPayload() => new(Kind, CurrentFormatVersion, CreatedUtc, AppVersion, Controller, Settings.ToList());

    /// <summary>Wire shape of the file — <c>kind</c>/<c>formatVersion</c> come from the
    /// consts rather than the instance, so they can never drift from what <see cref="Parse"/> checks.</summary>
    private sealed record Payload(string Kind, int FormatVersion, string CreatedUtc, string AppVersion,
                                  GrblBackupController Controller, List<GrblBackupSetting> Settings);
}
