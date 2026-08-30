using CNC.Core.Services;
using CNC.Core.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using SenderNG.Core.Localization;
using SenderNG.Core.Modules;
using Xunit;

namespace SenderNG.Core.Tests;

public class AppSettingsViewModelTests
{
    private sealed class StubModule : IModule
    {
        public StubModule(string id, string name, int order, bool visibleByDefault = true, bool canHide = true)
        {
            Id = id;
            Name = name;
            Order = order;
            IsVisibleByDefault = visibleByDefault;
            CanHide = canHide;
        }
        public string Id { get; }
        public string Name { get; }
        public string Description => $"Description of {Name}.";
        public int Order { get; }
        public bool IsVisibleByDefault { get; }
        public bool CanHide { get; }
        public IReadOnlyList<HotKey> HotKeys => [];
        public void RegisterServices(IServiceCollection services) { }
        public void RegisterSettings(AppSettings settings) { }
        public object CreateViewModel(IServiceProvider sp) => new object();
        public object CreateView(object vm) => new object();
    }

    private sealed class FieldStubModule : IModule, IModuleSettingsProvider
    {
        public FieldStubModule(string id, string name, int order, bool visibleByDefault = true, bool canHide = true)
        {
            Id = id;
            Name = name;
            Order = order;
            IsVisibleByDefault = visibleByDefault;
            CanHide = canHide;
        }
        public string Id { get; }
        public string Name { get; }
        public string Description => $"Description of {Name}.";
        public int Order { get; }
        public bool IsVisibleByDefault { get; }
        public bool CanHide { get; }
        public IReadOnlyList<HotKey> HotKeys => [];
        public void RegisterServices(IServiceCollection services) { }
        public void RegisterSettings(AppSettings settings) { }
        public object CreateViewModel(IServiceProvider sp) => new object();
        public object CreateView(object vm) => new object();
        public IEnumerable<ModuleSettingBool> GetBoolSettingFields()
        {
            yield return new ModuleSettingBool("Test bool", () => true, _ => { });
        }
    }

    private sealed class LocalizedFieldStubModule : IModule, IModuleSettingsProvider, ILocalizedModule
    {
        public string Id => "locf";
        public string Name => "Localized";
        public string Description => "Description.";
        public int Order => 60;
        public bool IsVisibleByDefault => true;
        public bool CanHide => true;
        public IReadOnlyList<HotKey> HotKeys => [];
        public void RegisterServices(IServiceCollection services) { }
        public void RegisterSettings(AppSettings settings) { }
        public object CreateViewModel(IServiceProvider sp) => new object();
        public object CreateView(object vm) => new object();
        public IReadOnlyDictionary<string, string> LocalizationKeys { get; } =
            new Dictionary<string, string> { ["module.jog.diagonal"] = "Diagonal jogging" };
        public IEnumerable<ModuleSettingBool> GetBoolSettingFields()
        {
            yield return new ModuleSettingBool("Diagonal jogging", () => true, _ => { }, key: "module.jog.diagonal");
        }
    }

    private static readonly LocalizationService Loc = TestLocalization.Locale;

    private static (string Dir, SettingsService Service) MakeSettings()
    {
        var dir = Path.Combine(Path.GetTempPath(), "senderng-appsettings-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return (dir, new SettingsService(dir));
    }

    private static readonly IModule[] Modules =
    [
        new StubModule("dro", "DRO", 10),
        new StubModule("jog", "Jog", 20),
        new StubModule("console", "Console", 30, visibleByDefault: false),
    ];

    private static AppSettingsViewModel CreateVm(SettingsService settings) =>
        new(settings, Modules, Loc);

    private static ModuleSettingsLeaf Leaf(AppSettingsViewModel vm, string id) =>
        vm.ModuleLeaves.First(e => e.Id == id);

    private static ModeSettingsLeaf ModeLeaf(AppSettingsViewModel vm) =>
        (ModeSettingsLeaf)((AppSettingsGroupItem)vm.Groups[0]).Children[0];

    private static void SetFoam(SettingsService settings, bool foam)
    {
        settings.Load().Modes["Mill"] = new ModeSettings { ModulesVisibility = new Dictionary<string, bool> { ["jog"] = foam } };
        settings.Load().Modes["Foam"] = new ModeSettings { ModulesVisibility = new Dictionary<string, bool> { ["jog"] = !foam } };
    }

    [Fact]
    public void Toggling_visibility_persists_to_current_mode()
    {
        var (dir, settings) = MakeSettings();
        var vm = CreateVm(settings);

        Leaf(vm, "jog").IsVisible = false;
        Leaf(vm, "console").IsVisible = true;

        // Fresh service instance proves values hit disk.
        var reloaded = new SettingsService(dir).Load();
        Assert.False(reloaded.Current.ModulesVisibility["jog"]);
        Assert.True(reloaded.Current.ModulesVisibility["console"]);
    }

    [Fact]
    public void Mode_switch_reflects_that_mode_visibility()
    {
        var (_, settings) = MakeSettings();
        SetFoam(settings, foam: true); // Mill: jog=true, Foam: jog=false

        var vm = CreateVm(settings);
        var jog = Leaf(vm, "jog");

        Assert.True(jog.IsVisible); // Mill map says jog = true
        ModeLeaf(vm).IsFoam = true;

        Assert.Equal("Foam", settings.Load().Mode);
        Assert.False(jog.IsVisible); // Foam map says jog = false

        ModeLeaf(vm).IsMill = true;
        Assert.True(jog.IsVisible);
    }

    [Fact]
    public void Close_command_requests_close_with_true()
    {
        var (_, settings) = MakeSettings();
        var vm = CreateVm(settings);
        bool? result = null;
        vm.CloseRequested += (_, r) => result = r;

        vm.CloseCommand.Execute(null);

        Assert.True(result ?? false);
    }

    [Fact]
    public void Module_leaves_ordered_by_module_order()
    {
        var (_, settings) = MakeSettings();
        var vm = CreateVm(settings);

        Assert.Equal(new[] { "dro", "jog", "console" }, vm.ModuleLeaves.Select(e => e.Id).ToArray());
        // The tree's Modules group holds the same leaves in the same order.
        var modulesGroup = (AppSettingsGroupItem)vm.Groups[1];
        Assert.Equal(new[] { "dro", "jog", "console" },
            modulesGroup.Children.OfType<ModuleSettingsLeaf>().Select(e => e.Id).ToArray());
    }

    [Fact]
    public void Fixed_modules_without_settings_are_still_listed()
    {
        var (_, settings) = MakeSettings();
        var modules = new IModule[]
        {
            new StubModule("dro", "DRO", 10, canHide: false),
            new StubModule("jog", "Jog", 20),
        };

        var vm = new AppSettingsViewModel(settings, modules, Loc);

        // Every module gets a leaf so its description/docs are reachable.
        Assert.Equal(new[] { "dro", "jog" }, vm.ModuleLeaves.Select(e => e.Id).ToArray());
        var dro = Leaf(vm, "dro");
        Assert.True(dro.IsVisible);          // fixed modules stay pinned visible
        Assert.False(dro.CanHide);           // no Enabled checkbox
        Assert.True(dro.ShowAlwaysVisibleCaption);
    }

    [Fact]
    public void Fixed_module_with_a_field_pins_visibility_true()
    {
        var (_, settings) = MakeSettings();
        // A fixed module is only listed when it actually has settings to show.
        var modules = new IModule[]
        {
            new FieldStubModule("jog", "Jog", 20, canHide: false),
        };

        var vm = new AppSettingsViewModel(settings, modules, Loc);

        Assert.True(vm.ModuleLeaves.Single().IsVisible);
        Assert.False(vm.ModuleLeaves.Single().CanHide);
    }

    [Fact]
    public void Tree_has_global_and_modules_groups()
    {
        var (_, settings) = MakeSettings();
        var vm = CreateVm(settings);

        Assert.Equal(2, vm.Groups.Count);
        var global = (AppSettingsGroupItem)vm.Groups[0];
        var modules = (AppSettingsGroupItem)vm.Groups[1];

        Assert.Equal("Global", global.Name);
        Assert.Equal("Modules", modules.Name);
        Assert.Contains(global.Children, c => c is ModeSettingsLeaf);
        Assert.Contains(global.Children, c => c is LanguageSettingsLeaf);
    }

    [Fact]
    public void First_leaf_is_selected_and_details_populate()
    {
        var (_, settings) = MakeSettings();
        var vm = CreateVm(settings);

        Assert.NotNull(vm.SelectedSetting);
        Assert.True(vm.HasSelection);
        Assert.True(vm.IsModeLeaf);
    }

    [Fact]
    public void Module_names_and_descriptions_localize_from_the_locale_file()
    {
        var (_, settings) = MakeSettings();
        var vm = CreateVm(settings);
        var dro = Leaf(vm, "dro");
        var jog = Leaf(vm, "jog");

        // English (base) -> locale keys exist so the tree resolves from the file.
        Assert.Equal("Jog", jog.Name);
        Assert.Contains("jog", jog.Description, StringComparison.OrdinalIgnoreCase);
        Assert.True(dro.HasDescription);
    }

    [Fact]
    public void Language_change_localizes_module_names_and_descriptions()
    {
        var (_, settings) = MakeSettings();
        var vm = CreateVm(settings);

        var leaf = (LanguageSettingsLeaf)((AppSettingsGroupItem)vm.Groups[0]).Children[1];
        Assert.Contains("en", leaf.AvailableLocales);

        if (leaf.AvailableLocales.Contains("ru"))
        {
            try
            {
                leaf.SelectedLocale = "ru";
                Assert.Equal("ru", settings.Load().Language);
                // The tree re-localizes immediately on the locale-changed event.
                Assert.Equal("Джог", Leaf(vm, "jog").Name);
                Assert.Equal("Модули", ((AppSettingsGroupItem)vm.Groups[1]).Name);
                Assert.Equal("Режим", ((ModeSettingsLeaf)((AppSettingsGroupItem)vm.Groups[0]).Children[0]).Name);
            }
            finally
            {
                // The shared static Loc is deliberately stateful; restore English so
                // other tests that read module names are unaffected by order.
                Loc.SetLocale("en");
            }
        }
    }

    [Fact]
    public void Generic_field_label_resolves_from_its_localization_key()
    {
        var (_, settings) = MakeSettings();
        var vm = new AppSettingsViewModel(settings, new IModule[] { new LocalizedFieldStubModule() }, Loc);
        var moduleLeaf = Leaf(vm, "locf");

        // en.json holds the key, so the leaf's field label comes from the locale file.
        var boolField = Assert.Single(moduleLeaf.BoolFields);
        Assert.Equal("Diagonal jogging", boolField.Label);
        Assert.Equal("module.jog.diagonal", boolField.Key);
    }

    [Fact]
    public void Generic_field_label_relocalizes_on_language_change()
    {
        var (_, settings) = MakeSettings();
        var vm = new AppSettingsViewModel(settings, new IModule[] { new LocalizedFieldStubModule() }, Loc);
        var moduleLeaf = Leaf(vm, "locf");
        var boolField = Assert.Single(moduleLeaf.BoolFields);

        if (Loc.AvailableLocales.Contains("ru"))
        {
            try
            {
                Loc.SetLocale("ru");
                // locale-changed triggers RefreshFields, re-resolving each keyed label.
                Assert.Equal("Диагональные перемещения", boolField.Label);
            }
            finally
            {
                Loc.SetLocale("en");
            }
        }
    }

    [Fact]
    public void Language_selector_retains_selection_across_reselect_and_reopen()
    {
        var (_, settings) = MakeSettings();
        var vm = CreateVm(settings);
        var leaf = (LanguageSettingsLeaf)((AppSettingsGroupItem)vm.Groups[0]).Children[1];

        if (leaf.AvailableLocales.Contains("ru"))
        {
            try
            {
                leaf.SelectedLocale = "ru";
                // Getter is backed by the VM's live current locale, not a stale copy:
                // re-reading (as when the user navigates away and back) still shows "ru".
                Assert.Equal("ru", leaf.SelectedLocale);
                // And a freshly opened dialog (new VM over the same settings) starts on "ru".
                var reopened = new AppSettingsViewModel(settings, Modules, Loc);
                var reopenedLeaf = (LanguageSettingsLeaf)((AppSettingsGroupItem)reopened.Groups[0]).Children[1];
                Assert.Equal("ru", reopenedLeaf.SelectedLocale);
            }
            finally
            {
                Loc.SetLocale("en");
            }
        }
    }

    [Fact]
    public void Plugin_locale_registration_serves_its_own_strings()
    {
        var dir = Path.Combine(Path.GetTempPath(), "senderng-plugin-locale-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "en.json"),
            """{ "strings": { "module.dummy.name": "Dummy", "module.dummy.tagline": "Dummy plugin works!" } }""");
        File.WriteAllText(Path.Combine(dir, "ru.json"),
            """{ "strings": { "module.dummy.name": "Заглушка", "module.dummy.tagline": "Плагин «Заглушка» работает!" } }""");

        var loc = LocalizationService.LoadFromDirectory(Path.Combine(AppContext.BaseDirectory, "Locale"));
        loc.RegisterModuleLocales(dir);

        try
        {
            Assert.Contains("ru", loc.AvailableLocales);
            Assert.Equal("Dummy", loc.T("module.dummy.name"));
            Assert.Equal("Dummy plugin works!", loc.T("module.dummy.tagline"));
            loc.SetLocale("ru");
            Assert.Equal("Заглушка", loc.T("module.dummy.name"));
            Assert.Equal("Плагин «Заглушка» работает!", loc.T("module.dummy.tagline"));
            // Base catalogs take precedence: a plugin may not override an app string.
            Assert.NotEqual("Заглушка", loc.T("appSettings.enabled"));
        }
        finally
        {
            loc.SetLocale("en");
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Plugin_module_name_resolves_from_its_own_locale_first()
    {
        var dir = Path.Combine(Path.GetTempPath(), "senderng-plugin-name-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "en.json"),
            """{ "strings": { "module.dummy.name": "Dummy plugin" } }""");
        File.WriteAllText(Path.Combine(dir, "ru.json"),
            """{ "strings": { "module.dummy.name": "Заглушка" } }""");

        var loc = LocalizationService.LoadFromDirectory(Path.Combine(AppContext.BaseDirectory, "Locale"));
        loc.RegisterModuleLocales(dir);
        var module = new StubModule("dummy", "Dummy", 10);

        try
        {
            // The plugin's own en.json provides the name before the locale switch.
            Assert.Equal("Dummy plugin", ModuleNames.Localized(module, loc));
            loc.SetLocale("ru");
            // And its ru.json wins after the switch — proving plugin-first resolution.
            Assert.Equal("Заглушка", ModuleNames.Localized(module, loc));
        }
        finally
        {
            loc.SetLocale("en");
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Grbl_errors_localize_through_the_service()
    {
        try
        {
            Assert.Contains("integer value", Loc.TError(23)); // English fallback
            if (Loc.AvailableLocales.Contains("ru"))
            {
                Loc.SetLocale("ru");
                Assert.Contains("целочисленного значения", Loc.TError(23));
                Assert.Contains("Цель джога", Loc.TError(15));
                Assert.Contains("критическое событие", Loc.TError(79));
                Assert.Contains("возврат в ноль", Loc.TErrorHint(15));
                Assert.Contains("error:23 -", Loc.DecorateError("error:23"));
                Assert.Null(Loc.TError(9999)); // unknown code stays unknown
            }
        }
        finally
        {
            Loc.SetLocale("en");
        }
    }

    [Fact]
    public void Legacy_flat_settings_json_migrates_to_mill_mode()
    {
        var dir = Path.Combine(Path.GetTempPath(), "senderng-legacy-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "settings.json"),
            """
            {
              "Mode": "Mill",
              "DemoAxes": 5,
              "Port": "COM7",
              "ModulesVisibility": { "jog": false }
            }
            """);

        var loaded = new SettingsService(dir).Load();

        Assert.Equal(2, loaded.Modes.Count);
        Assert.Contains(AppSettings.MillMode, loaded.Modes.Keys);
        Assert.Contains(AppSettings.FoamMode, loaded.Modes.Keys);
        var mill = loaded.Modes[AppSettings.MillMode];
        Assert.Equal(5, mill.DemoAxes);
        Assert.Equal("COM7", mill.Port);
        Assert.False(mill.ModulesVisibility["jog"]);
    }
}