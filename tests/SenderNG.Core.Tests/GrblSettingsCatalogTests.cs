
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

using System.Text;
using CNC.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using SenderNG.Core.Localization;
using SenderNG.Core.Modules;
using SenderNG.Core.Settings;

namespace SenderNG.Core.Tests;

public class GrblSettingsParserTests
{
    [Theory]
    [InlineData("$0=5.0", 0, "5.0")]
    [InlineData("$10=0.010", 10, "0.010")]
    [InlineData("$25=10", 25, "10")]
    public void Value_lines_parse_to_id_and_value(string line, int id, string value)
    {
        var parsed = GrblSettingsParser.ParseValueLine(line);
        Assert.NotNull(parsed);
        Assert.Equal(id, parsed.Value.Id);
        Assert.Equal(value, parsed.Value.Value);
    }

    [Theory]
    [InlineData("$=5")]
    [InlineData("$")]
    [InlineData("$$")]
    [InlineData("garbage")]
    [InlineData("")]
    [InlineData(null)]
    public void Malformed_value_lines_return_null(string? line) =>
        Assert.Null(GrblSettingsParser.ParseValueLine(line));

    [Fact]
    public void Setting_metadata_line_parses_all_fields()
    {
        // Captured from the RP2040 grblHAL board ($ES dump).
        var setting = GrblSettingsParser.ParseSettingLine(
            "[SETTING:0|35|Step pulse time|microseconds|6|#0.0|1.0||0|0]");

        Assert.NotNull(setting);
        Assert.Equal(0, setting.Id);
        Assert.Equal(35, setting.GroupId);
        Assert.Equal("Step pulse time", setting.Name);
        Assert.Equal("Step pulse time", setting.EnglishName);
        Assert.Equal("microseconds", setting.Unit);
        Assert.Equal(GrblSettingType.Float, setting.DataType);
        Assert.Equal("#0.0", setting.Format);
        Assert.Equal(1.0, setting.Min);
        Assert.Null(setting.Max);
        Assert.False(setting.RebootRequired);
        Assert.False(setting.AllowNull);
    }

    [Fact]
    public void Setting_metadata_with_axis_mask_type_parses_as_axismask()
    {
        var setting = GrblSettingsParser.ParseSettingLine("[SETTING:2|35|Step pulse invert||4||||0|0]");
        Assert.NotNull(setting);
        Assert.Equal(GrblSettingType.AxisMask, setting.DataType);
        Assert.Empty(setting.Unit);
        Assert.Null(setting.Min);
        Assert.Null(setting.Max);
    }

    [Fact]
    public void Setting_metadata_with_unknown_type_falls_back_to_text()
    {
        var setting = GrblSettingsParser.ParseSettingLine("[SETTING:9|1|Mystery|unit|42|fmt|1|2|0|0]");
        Assert.NotNull(setting);
        Assert.Equal(GrblSettingType.Text, setting.DataType);
        Assert.Equal("fmt", setting.Format);
        Assert.Equal(1.0, setting.Min);
        Assert.Equal(2.0, setting.Max);
    }

    [Fact]
    public void Group_line_parses_tree_relationship()
    {
        var group = GrblSettingsParser.ParseGroupLine("[SETTINGGROUP:6|0|Spindle]");
        Assert.NotNull(group);
        Assert.Equal(6, group.Id);
        Assert.Equal(0, group.ParentId);
        Assert.Equal("Spindle", group.Name);
    }

    [Fact]
    public void Description_line_converts_literal_newline_escapes()
    {
        var parsed = GrblSettingsParser.ParseDescriptionLine(
            "[SETTINGDESCR:100|Travel resolution in steps per millimeter.\\nNote: applies to X axis only.]");
        Assert.NotNull(parsed);
        Assert.Equal(100, parsed.Value.Id);
        Assert.Equal("Travel resolution in steps per millimeter.\nNote: applies to X axis only.", parsed.Value.Description);
    }

    [Fact]
    public void Description_line_without_escapes_parses_verbatim()
    {
        var parsed = GrblSettingsParser.ParseDescriptionLine(
            "[SETTINGDESCR:100|Travel resolution in steps per millimeter.]");
        Assert.NotNull(parsed);
        Assert.Equal("Travel resolution in steps per millimeter.", parsed.Value.Description);
    }

    [Fact]
    public void Malformed_bracket_lines_return_null()
    {
        Assert.Null(GrblSettingsParser.ParseSettingLine("SETTING:0|1|x"));
        Assert.Null(GrblSettingsParser.ParseSettingLine("[SETTING:0|1]"));
        Assert.Null(GrblSettingsParser.ParseGroupLine("[OTHER:0|0|Root]"));
        Assert.Null(GrblSettingsParser.ParseDescriptionLine("[SETTINGDESCR:abc|x]"));
        Assert.Null(GrblSettingsParser.ParseSettingLine(null));
    }
}

public class LocalizationServiceTests
{
    private static Stream Json(string json) =>
        new MemoryStream(Encoding.UTF8.GetBytes(json));

    private static Stream EnglishJson() =>
        File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Locale", "en.json"));

    [Fact]
    public void English_catalog_loads_settings_groups_and_strings()
    {
        var service = TestLocalization.Locale;

        Assert.Equal("en", service.Locale);
        Assert.Equal(96, service.Settings.Count);
        Assert.Equal(18, service.Groups.Count);
        // Root group + General + axis groups are all present.
        Assert.Contains(service.Groups.Values, g => g.Id == 0 && g.Name == "Root");
        Assert.Contains(service.Groups.Values, g => g.Id == 43 && g.ParentId == 42 && g.Name == "X-axis");
    }

    [Fact]
    public void Unknown_string_key_falls_back_to_the_key_itself()
    {
        var service = TestLocalization.Locale;
        Assert.Equal("app.nonexistent", service.T("app.nonexistent"));
    }

    [Fact]
    public void English_string_is_served_from_the_english_catalog()
    {
        var service = TestLocalization.Locale;
        Assert.Equal("GRBL settings", service.T("grblSettings.title"));
    }

    [Fact]
    public void Locale_string_overrides_english_when_present()
    {
        using var en = EnglishJson();
        var service = new LocalizationService(
            en,
            "es",
            Json("""{ "strings": { "grblSettings.title": "Configuración GRBL" } }"""));

        Assert.Equal("es", service.Locale);
        Assert.Equal("Configuración GRBL", service.T("grblSettings.title"));
        // Unknown keys still fall back through English to the key itself.
        Assert.Equal("app.nonexistent", service.T("app.nonexistent"));
    }

    [Fact]
    public void Locale_without_strings_uses_english_as_fallback()
    {
        using var en = EnglishJson();
        var service = new LocalizationService(en, "es", Json("""{ "strings": {} }"""));
        Assert.Equal("GRBL settings", service.T("grblSettings.title"));
    }

    [Fact]
    public void Russian_locale_overrides_strings_and_setting_names_when_en_matches()
    {
        // Load the real ru.json shipped with the app (copied to OutputDir/Locale),
        // exercising the full LoadFromDirectory override path end-to-end.
        var ruDir = Path.Combine(AppContext.BaseDirectory, "Locale");
        var service = LocalizationService.LoadFromDirectory(ruDir, "ru");

        Assert.Equal("ru", service.Locale);
        // App string override.
        Assert.Equal("Настройки GRBL", service.T("grblSettings.title"));
        // Placeholder overrides with a non-ASCII character.
        Assert.Equal("Поиск\u2026", service.T("grblSettings.searchPlaceholder"));

        // Setting name override applies while the id + English-name guard matches.
        var setting = new GrblSetting { Id = 0, EnglishName = "Step pulse time", Name = "Step pulse time" };
        service.ApplySettingStrings(setting);
        Assert.Equal("Время импульса шага", setting.Name);
        Assert.False(string.IsNullOrEmpty(setting.Description));

        // Unknown keys still fall back through English to the key.
        Assert.Equal("app.nonexistent", service.T("app.nonexistent"));
    }

    [Fact]
    public void Setting_localization_applies_when_english_names_match()
    {
        using var en = EnglishJson();
        var service = new LocalizationService(
            en,
            "es",
            Json("""{ "grblSettings": { "1": { "name": "Retardo de reposo", "en": "Step idle delay" } } }"""));

        var setting = new GrblSetting { Id = 1, EnglishName = "Step idle delay", Name = "Step idle delay" };
        service.ApplySettingStrings(setting);
        Assert.Equal("Retardo de reposo", setting.Name);
    }

    [Fact]
    public void Setting_localization_is_rejected_when_english_names_differ()
    {
        using var en = EnglishJson();
        // A driver-specific id reused with a different meaning: translation was
        // authored against "Step idle delay" but this controller's id=1 says "no".
        var service = new LocalizationService(
            en,
            "es",
            Json("""{ "grblSettings": { "1": { "name": "Retardo de reposo", "en": "Step idle delay" } } }"""));

        var setting = new GrblSetting { Id = 1, EnglishName = "no", Name = "no" };
        service.ApplySettingStrings(setting);
        Assert.Equal("no", setting.Name);
    }

    [Fact]
    public void Setting_localization_without_en_key_trusts_the_id()
    {
        using var en = EnglishJson();
        // No "en" guard key: the translation is authored for a spec-stable id
        // (classic grbl path) — the guard passes unconditionally.
        var service = new LocalizationService(
            en,
            "es",
            Json("""{ "grblSettings": { "1": { "name": "Retardo de reposo" } } }"""));

        var setting = new GrblSetting { Id = 1, EnglishName = "Step idle delay", Name = "Step idle delay" };
        service.ApplySettingStrings(setting);
        Assert.Equal("Retardo de reposo", setting.Name);
    }

    [Fact]
    public void Setting_localization_is_skipped_for_english_locale()
    {
        var service = TestLocalization.Locale;
        var setting = new GrblSetting { Id = 1, EnglishName = "Step idle delay", Name = "Step idle delay" };
        service.ApplySettingStrings(setting);
        Assert.Equal("Step idle delay", setting.Name);
    }

    [Fact]
    public void Resolve_returns_fallback_when_key_is_missing()
    {
        var service = TestLocalization.Locale;
        Assert.Equal("Fallback text", service.Resolve("module.nope.missing", "Fallback text"));
    }

    [Fact]
    public void Resolve_returns_localized_value_when_present()
    {
        using var en = EnglishJson();
        var service = new LocalizationService(
            en,
            "es",
            Json("""{ "strings": { "grblSettings.title": "Configuración GRBL" } }"""));

        Assert.Equal("Configuración GRBL", service.Resolve("grblSettings.title", "GRBL settings"));
    }

    [Fact]
    public void Resolve_returns_english_when_only_in_english_catalog()
    {
        var service = TestLocalization.Locale;
        Assert.Equal("GRBL settings", service.Resolve("grblSettings.title", "GRBL settings"));
    }

    [Fact]
    public void English_catalog_definition_matches_captured_hardware_metadata()
    {
        var service = TestLocalization.Locale;
        var s100 = service.Settings[100];
        Assert.Equal("X-axis travel resolution", s100.Name);
        Assert.Equal(GrblSettingType.Float, s100.Type);
        Assert.Equal(43, s100.GroupId);

        var s1 = service.Settings[1];
        Assert.Equal("Step idle delay", s1.Name);
        Assert.Equal(65535.0, s1.Max);
    }

    [Fact]
    public void ModuleNames_Localized_resolves_from_the_base_catalog()
    {
        // "module.jog.name" lives in the app's en.json/ru.json.
        Assert.Equal("Jog", ModuleNames.Localized(new StubNamedModule("jog", "Jog"), TestLocalization.Locale));
    }

    [Fact]
    public void ModuleNames_Localized_falls_back_to_module_name_when_key_missing()
    {
        var module = new StubNamedModule("custom", "My Module");
        Assert.Equal("My Module", ModuleNames.Localized(module, TestLocalization.Locale));
    }

    [Fact]
    public void Shell_keys_resolve_in_english_and_russian()
    {
        // Use a private instance (not the shared TestLocalization.Locale) so the
        // SetLocale("ru") switch below can't race other tests reading that static.
        var service = LocalizationService.LoadFromDirectory(Path.Combine(AppContext.BaseDirectory, "Locale"));
        Assert.Equal("_File", service.T("app.menu.file"));
        Assert.Equal("Cycle Start", service.T("app.cycle.start"));
        Assert.Equal("State", service.T("app.state.label"));
        Assert.Equal("About SenderNG", service.T("about.title"));

        try
        {
            service.SetLocale("ru");
            Assert.Equal("_Файл", service.T("app.menu.file"));
            Assert.Equal("Пуск цикла", service.T("app.cycle.start"));
            Assert.Equal("Состояние", service.T("app.state.label"));
        }
        finally
        {
            service.SetLocale("en");
        }
    }

    private sealed record StubNamedModule(string Id, string Name) : IModule
    {
        public string Description => "desc";
        public int Order => 1;
        public PanelHint Panel => PanelHint.Flyout;
        public bool IsVisibleByDefault => true;
        public bool CanHide => true;
        public IReadOnlyList<HotKey> HotKeys => [];
        public void RegisterServices(IServiceCollection services) { }
        public void RegisterSettings(AppSettings settings) { }
        public object CreateViewModel(IServiceProvider sp) => new object();
        public object CreateView(object vm) => new object();
    }
}

public class DemoCatalogRoundTripTests
{
    [Fact]
    public async Task Demo_serves_es_lines_that_parse_back_to_the_catalog()
    {
        var service = new DemoGrblService(new AppSettings(), localization: TestLocalization.Locale);
        var lines = await service.GetSettingDetailLinesAsync();

        Assert.Equal(96, lines.Count);
        var parsed = lines.Select(GrblSettingsParser.ParseSettingLine).ToList();
        Assert.DoesNotContain(parsed, static s => s is null);
        Assert.Equal(0, parsed[0]!.Id);
        Assert.Equal("Step pulse time", parsed[0]!.Name);
        Assert.Equal(GrblSettingType.Float, parsed[0]!.DataType);
    }

    [Fact]
    public async Task Demo_serves_eg_lines_that_parse_back_to_the_group_tree()
    {
        var service = new DemoGrblService(new AppSettings(), localization: TestLocalization.Locale);
        var lines = await service.GetSettingGroupLinesAsync();

        Assert.Equal(18, lines.Count);
        var parsed = lines.Select(GrblSettingsParser.ParseGroupLine).ToList();
        Assert.DoesNotContain(parsed, static g => g is null);
        Assert.Contains(parsed, static g => g!.Id == 0 && g.ParentId == 0 && g.Name == "Root");
        Assert.Contains(parsed, static g => g!.Id == 43 && g.ParentId == 42 && g.Name == "X-axis");
    }

    [Fact]
    public async Task Demo_serves_sed_description_with_literal_newline_escapes()
    {
        var service = new DemoGrblService(new AppSettings(), localization: TestLocalization.Locale);
        var line = await service.GetSettingDescriptionAsync(100);

        Assert.NotNull(line);
        Assert.StartsWith("[SETTINGDESCR:100|", line);
        var parsed = GrblSettingsParser.ParseDescriptionLine(line);
        Assert.NotNull(parsed);
        Assert.Equal(100, parsed.Value.Id);
        Assert.False(string.IsNullOrWhiteSpace(parsed.Value.Description));
    }

    [Fact]
    public async Task Demo_serves_null_for_unknown_setting_description()
    {
        var service = new DemoGrblService(new AppSettings(), localization: TestLocalization.Locale);
        Assert.Null(await service.GetSettingDescriptionAsync(999_999));
    }
}

public class GrblSettingsCapabilityFlagTests
{
    [Fact]
    public void GrblHAL_newopt_enums_and_sed_set_the_capability_flags()
    {
        // Captured from the RP2040 grblHAL board ($I dump): NEWOPT arrives
        // alongside the VER/FIRMWARE lines that mark the reply as grblHAL.
        var caps = ControllerInfoParser.ParseInfo(
            ["[VER:1.1f.20260827:]", "[NEWOPT:ENUMS,RT+,ES,MPG,REBOOT,TC,SED,RTC]", "[FIRMWARE:grblHAL]"]);
        Assert.True(caps.HasSettingEnums);
        Assert.True(caps.HasSettingDescriptions);
    }

    [Fact]
    public void Classic_grbl_has_no_settings_capabilities()
    {
        var caps = ControllerInfoParser.ParseInfo(["Grbl 1.1h ['$' for help]"]);
        Assert.False(caps.HasSettingEnums);
        Assert.False(caps.HasSettingDescriptions);
    }

    [Fact]
    public void Mega5x_1_2i_fork_has_no_settings_capabilities()
    {
        var caps = ControllerInfoParser.ParseInfo(
            ["[VER:1.2i.20260521:]", "[AXS:5:XYZAB]", "[OPT:VNMGH,35,255,48]"]);
        Assert.False(caps.HasSettingEnums);
        Assert.False(caps.HasSettingDescriptions);
    }

    [Fact]
    public void Demo_reports_full_settings_capabilities()
    {
        Assert.True(ControllerCapabilities.Full.HasSettingEnums);
        Assert.True(ControllerCapabilities.Full.HasSettingDescriptions);
    }
}
