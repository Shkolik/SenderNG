
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

using CNC.Core.Services;
using SenderNG.Core.Settings;

namespace SenderNG.Core.Tests;

public class GrblSettingsBackupTests
{
    private static ControllerCapabilities Capabilities() => new()
    {
        IsKnown = true,
        Firmware = "grblHAL",
        Version = "1.1f",
        Build = 20240101,
        RawOptions = "VNMSL+,100,1024,4,32",
        RawAxes = "4:XYZA",
        NumAxes = 4,
        IsGrblHAL = true,
    };

    private static GrblSettingsSnapshot Snapshot() => new(
        [],
        [
            new GrblSetting
            {
                Id = 110, Name = "X-axis maximum rate", Unit = "mm/min",
                DataType = GrblSettingType.Float, Format = "#0.0", Value = "800.000",
            },
            new GrblSetting
            {
                Id = 0, Name = "Step pulse time", Unit = "us",
                DataType = GrblSettingType.Integer, Format = "#0", Min = 1, Value = "10",
                RebootRequired = true,
            },
            new GrblSetting
            {
                Id = 30, Name = "Spindle max", Unit = "rpm",
                DataType = GrblSettingType.Integer, Format = "#0", Max = 10000, Value = "12000",
            },
        ],
        MetadataFromController: true);

    private static GrblSettingsBackup CreateBackup() => GrblSettingsBackup.Create(
        Snapshot(), Capabilities(), "1.2.3", new DateTimeOffset(2026, 10, 13, 1, 31, 0, TimeSpan.Zero));

    /// <summary>Minimal well-formed backup JSON whose <c>settings</c> array is supplied verbatim.</summary>
    private static string BackupJson(string settingsArray, string kind = GrblSettingsBackup.Kind,
        int formatVersion = GrblSettingsBackup.CurrentFormatVersion) =>
        "{"
        + $"\"kind\":\"{kind}\",\"formatVersion\":{formatVersion},"
        + "\"createdUtc\":\"2026-10-13T01:31:00Z\",\"appVersion\":\"1.2.3\","
        + "\"controller\":{\"key\":\"k\",\"firmware\":\"grblHAL\",\"version\":\"1.1f\",\"build\":1,"
        + "\"numAxes\":3,\"isGrblHAL\":true,\"metadataFromController\":true},"
        + $"\"settings\":{settingsArray}"
        + "}";

    private static string Element(int id, string value) =>
        $"{{\"id\":{id},\"value\":\"{value}\",\"name\":\"N{id}\",\"unit\":\"mm\",\"type\":6,"
        + "\"format\":\"#0.0\",\"min\":null,\"max\":null,\"rebootRequired\":false}";

    [Fact]
    public void Create_captures_every_setting_with_values_and_metadata_in_id_order()
    {
        var backup = CreateBackup();

        Assert.Equal(new[] { 0, 30, 110 }, backup.Settings.Select(static s => s.Id));
        Assert.Equal("1.2.3", backup.AppVersion);
        Assert.Equal("2026-10-13T01:31:00Z", backup.CreatedUtc);

        var x = backup.Settings.Single(static s => s.Id == 110);
        Assert.Equal("800.000", x.Value);
        Assert.Equal("X-axis maximum rate", x.Name);
        Assert.Equal("mm/min", x.Unit);
        Assert.Equal(GrblSettingType.Float, x.Type);
        Assert.Equal("#0.0", x.Format);
        Assert.Null(x.Min);
        Assert.Null(x.Max);
        Assert.False(x.RebootRequired);

        var step = backup.Settings.Single(static s => s.Id == 0);
        Assert.Equal("10", step.Value);
        Assert.Equal(GrblSettingType.Integer, step.Type);
        Assert.Equal(1d, step.Min!.Value);
        Assert.Null(step.Max);
        Assert.True(step.RebootRequired);

        var spindle = backup.Settings.Single(static s => s.Id == 30);
        Assert.Equal("12000", spindle.Value);
        Assert.Null(spindle.Min);
        Assert.Equal(10000d, spindle.Max!.Value);
    }

    [Fact]
    public void Create_captures_the_controller_fingerprint()
    {
        var backup = CreateBackup();

        Assert.Equal("grblHAL", backup.Controller.Firmware);
        Assert.Equal("1.1f", backup.Controller.Version);
        Assert.Equal(20240101, backup.Controller.Build);
        Assert.Equal(4, backup.Controller.NumAxes);
        Assert.True(backup.Controller.IsGrblHAL);
        Assert.True(backup.Controller.MetadataFromController);
        Assert.Equal(Capabilities().ControllerKey, backup.Controller.Key);
        Assert.Equal("grblHAL|1.1f.20240101|VNMSL+,100,1024,4,32|4:XYZA", backup.Controller.Key);
    }

    [Fact]
    public void Json_round_trips_through_Parse()
    {
        var original = CreateBackup();

        var result = GrblSettingsBackup.Parse(original.ToJson());

        Assert.True(result.Ok, result.Error);
        var parsed = result.Backup!;
        Assert.Equal(original.CreatedUtc, parsed.CreatedUtc);
        Assert.Equal(original.AppVersion, parsed.AppVersion);
        Assert.Equal(original.Controller, parsed.Controller);
        Assert.Equal(original.Settings, parsed.Settings);
    }

    [Fact]
    public void Parse_rejects_malformed_json()
    {
        var result = GrblSettingsBackup.Parse("{ not json");

        Assert.False(result.Ok);
        Assert.Contains("JSON", result.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_rejects_a_foreign_kind()
    {
        var result = GrblSettingsBackup.Parse(BackupJson($"[{Element(110, "800.000")}]", kind: "something-else"));

        Assert.False(result.Ok);
        Assert.Contains("kind", result.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_rejects_a_newer_format_version()
    {
        var result = GrblSettingsBackup.Parse(
            BackupJson($"[{Element(110, "800.000")}]", formatVersion: GrblSettingsBackup.CurrentFormatVersion + 1));

        Assert.False(result.Ok);
        Assert.Contains("formatVersion", result.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_rejects_an_empty_setting_list()
    {
        var result = GrblSettingsBackup.Parse(BackupJson("[]"));

        Assert.False(result.Ok);
        Assert.Contains("settings", result.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_rejects_duplicate_ids()
    {
        var result = GrblSettingsBackup.Parse(BackupJson($"[{Element(110, "1")},{Element(110, "2")}]"));

        Assert.False(result.Ok);
        Assert.Contains("duplicate", result.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_rejects_a_negative_id()
    {
        var result = GrblSettingsBackup.Parse(BackupJson($"[{Element(-1, "1")}]"));

        Assert.False(result.Ok);
        Assert.Contains("id", result.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{ \"settings\": 1 }")]
    public void Parse_never_throws_for_hostile_input(string json)
    {
        var result = GrblSettingsBackup.Parse(json);

        Assert.False(result.Ok);
        Assert.NotNull(result.Error);
    }
}
