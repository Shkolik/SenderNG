
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

using System.Reflection;
using System.Runtime.CompilerServices;
using CNC.Core.Services;
using CNC.Core.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using SenderNG.Core.Localization;
using SenderNG.Core.Modules;
using SenderNG.Core.Services;
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
        public object CreateViewModel(IServiceProvider sp) => new object();
        public object CreateView(object vm) => new object();
        public IReadOnlyDictionary<string, string> LocalizationKeys { get; } =
            new Dictionary<string, string> { ["module.jog.diagonal"] = "Diagonal jogging" };
        public IEnumerable<ModuleSettingBool> GetBoolSettingFields()
        {
            yield return new ModuleSettingBool("Diagonal jogging", () => true, _ => { }, key: "module.jog.diagonal");
        }
    }

    // A private instance (not TestLocalization.Locale): this fixture's SetLocale("ru")
    // calls must never race other test classes that read the shared English static.
    private static readonly LocalizationService Loc =
        LocalizationService.LoadFromDirectory(Path.Combine(AppContext.BaseDirectory, "Locale"));

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

    // Field-like auto-events expose their invocation list through a backing field of the
    // same name. Assert.NotNull rather than "return 0": if an event is ever rewritten with
    // explicit add/remove the field disappears, and a silent 0 would pass this test for the
    // wrong reason.
    private static int SubscriberCount(object source, string eventName)
    {
        var field = source.GetType().GetField(
            eventName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        Assert.NotNull(field);
        var handler = field!.GetValue(source) as Delegate;
        return handler?.GetInvocationList().Length ?? 0;
    }

    // Construct in a separate frame: with tiered compilation the tier-0 JIT keeps an
    // object built by `newobj` in the containing method's frame reported as a GC root,
    // so a VM built inline in the test below would keep the WeakReference alive no
    // matter what the production code does (a bare `new object()` reproduces this).
    // NoInlining is what makes the collectability assertion measure the leak, not the JIT.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static AppSettingsViewModel CreateDisposableVm(
        SettingsService settings, LocalizationService loc, HotkeyService hotkeys) =>
        new(settings, Modules, loc, null, hotkeys);

    [Fact]
    public void Dispose_releases_singleton_subscriptions_and_frees_the_view_model()
    {
        var (dir, settings) = MakeSettings();
        try
        {
            // A private instance, not the shared `Loc`: the baseline read below must be ours
            // alone, not a count that other test classes move by constructing their own VMs.
            var loc = LocalizationService.LoadFromDirectory(
                Path.Combine(AppContext.BaseDirectory, "Locale"));
            var hotkeys = new HotkeyService(settings);

            var locBaseline = SubscriberCount(loc, "Changed");
            var keyBaseline = SubscriberCount(hotkeys, "Changed");

            AppSettingsViewModel? last = null;
            for (var i = 0; i < 25; i++)
            {
                last = CreateDisposableVm(settings, loc, hotkeys);
                last.Dispose();
            }

            Assert.Equal(locBaseline, SubscriberCount(loc, "Changed"));
            Assert.Equal(keyBaseline, SubscriberCount(hotkeys, "Changed"));

            var weak = new WeakReference(last);
            last = null;
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            Assert.False(weak.IsAlive,
                "AppSettingsViewModel must be collectable after Dispose; a live instance means "
                + "LocalizationService.Changed still pins it.");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Dispose_is_idempotent()
    {
        var (dir, settings) = MakeSettings();
        try
        {
            var loc = LocalizationService.LoadFromDirectory(
                Path.Combine(AppContext.BaseDirectory, "Locale"));
            var hotkeys = new HotkeyService(settings);

            var vm = new AppSettingsViewModel(settings, Modules, loc, null, hotkeys);
            var locAfterCtor = SubscriberCount(loc, "Changed");
            var keyAfterCtor = SubscriberCount(hotkeys, "Changed");

            vm.Dispose();
            var locAfterDispose = SubscriberCount(loc, "Changed");
            var keyAfterDispose = SubscriberCount(hotkeys, "Changed");

            vm.Dispose();   // ReloadProfile and the shell's finally can both fire.

            // Not just "it didn't throw": the second call must move no count, and the first
            // must have moved them - otherwise this passes even if Dispose does nothing at all.
            Assert.Equal(locAfterDispose, SubscriberCount(loc, "Changed"));
            Assert.Equal(keyAfterDispose, SubscriberCount(hotkeys, "Changed"));
            Assert.NotEqual(locAfterCtor, locAfterDispose);
            Assert.NotEqual(keyAfterCtor, keyAfterDispose);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private static ModuleSettingsLeaf Leaf(AppSettingsViewModel vm, string id) =>
        vm.ModuleLeaves.First(e => e.Id == id);

    [Fact]
    public void Toggling_visibility_persists_to_the_active_profile()
    {
        var (dir, settings) = MakeSettings();
        var vm = CreateVm(settings);

        Leaf(vm, "jog").IsVisible = false;
        Leaf(vm, "console").IsVisible = true;

        // Fresh service instance proves values hit disk.
        var reloaded = new SettingsService(dir).Load();
        Assert.False(reloaded.Modules["jog"]);
        Assert.True(reloaded.Modules["console"]);
    }

    [Fact]
    public void Visibility_persists_per_profile_not_per_mode()
    {
        // The Mill/Foam mode selector is gone — a machine is a profile, and its
        // visibility map is the profile's Modules. Two profiles stay independent.
        var (dir, settings) = MakeSettings();
        var profile = settings.Load();
        var vm = CreateVm(settings);

        Leaf(vm, "jog").IsVisible = false;
        Leaf(vm, "console").IsVisible = true;

        var reloaded = new SettingsService(dir).Load();
        Assert.False(reloaded.Modules["jog"]);
        Assert.True(reloaded.Modules["console"]);
        Assert.Same(profile, settings.Load()); // still the same shared graph
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
    public void Non_hideable_module_without_settings_gets_no_leaf()
    {
        var (_, settings) = MakeSettings();
        var modules = new IModule[]
        {
            new StubModule("dro", "DRO", 10, canHide: false),
            new StubModule("jog", "Jog", 20),
        };

        var vm = new AppSettingsViewModel(settings, modules, Loc);

        // Fixed modules expose nothing but their always-on visibility flag, so they
        // are not listed; the hideable module keeps its visibility-toggle leaf.
        Assert.Equal(new[] { "jog" }, vm.ModuleLeaves.Select(e => e.Id).ToArray());
        // The tree's Modules group holds the same leaves in the same order.
        var modulesGroup = (AppSettingsGroupItem)vm.Groups[1];
        Assert.Equal(new[] { "jog" },
            modulesGroup.Children.OfType<ModuleSettingsLeaf>().Select(e => e.Id).ToArray());
    }

    [Fact]
    public void Settings_dialog_modules_get_no_leaf_when_not_hideable_and_without_surface()
    {
        // AppSettings/GrblSettings become CanHide=false and expose no settings
        // surface, so they must not appear in the Modules tree.
        var (_, settings) = MakeSettings();
        var modules = new IModule[]
        {
            new StubModule("app-settings", "App Settings", 41, canHide: false),
            new StubModule("grbl-settings", "GRBL Settings", 40, canHide: false),
            new StubModule("jog", "Jog", 20),
        };

        var vm = new AppSettingsViewModel(settings, modules, Loc);

        Assert.Equal(new[] { "jog" }, vm.ModuleLeaves.Select(e => e.Id).ToArray());
        var modulesGroup = (AppSettingsGroupItem)vm.Groups[1];
        Assert.Equal(new[] { "jog" },
            modulesGroup.Children.OfType<ModuleSettingsLeaf>().Select(e => e.Id).ToArray());
    }

    [Fact]
    public void Hideable_module_without_settings_still_gets_a_leaf()
    {
        // Plugins that expose nothing configurable still get a row: the leaf hosts
        // the hide/show toggle.
        var (_, settings) = MakeSettings();
        var vm = new AppSettingsViewModel(settings,
            new IModule[] { new StubModule("coolant", "Coolant", 50) }, Loc);

        var leaf = Assert.Single(vm.ModuleLeaves);
        Assert.Equal("coolant", leaf.Id);
        Assert.True(leaf.CanHide);
        Assert.True(leaf.IsVisible);
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
        Assert.Contains(global.Children, c => c is ProfileSettingsLeaf);
        Assert.Contains(global.Children, c => c is LanguageSettingsLeaf);
    }

    [Fact]
    public void First_leaf_is_selected_and_details_populate()
    {
        var (_, settings) = MakeSettings();
        var vm = CreateVm(settings);

        Assert.NotNull(vm.SelectedSetting);
        Assert.True(vm.HasSelection);
        Assert.True(vm.IsLanguageLeaf); // first leaf under Global is Language
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
                Assert.Equal("ru", settings.Global.Language);
                // The tree re-localizes immediately on the locale-changed event.
                Assert.Equal("Ручное перемещение", Leaf(vm, "jog").Name);
                Assert.Equal("Модули", ((AppSettingsGroupItem)vm.Groups[1]).Name);
                Assert.Equal("Язык", leaf.Name);
            }
            finally
            {
                // Loc is stateful within this fixture; restore English so later tests
                // in this class that read module names are unaffected by test order.
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
    public void Language_selector_ignores_null_pushed_by_combo_detach()
    {
        var (_, settings) = MakeSettings();
        var vm = CreateVm(settings);
        var leaf = (LanguageSettingsLeaf)((AppSettingsGroupItem)vm.Groups[0]).Children[1];
        Assert.Equal("en", leaf.SelectedLocale);

        // Avalonia detaches invisible controls from the visual tree and can push null
        // through a TwoWay SelectedItem on detach (leaf switch hides this pane). The
        // setter must ignore it: it must neither corrupt the current locale nor the
        // persisted global language.
        leaf.SelectedLocale = null!;
        Assert.Equal("en", leaf.SelectedLocale);
        Assert.Equal(string.Empty, settings.Global.Language);
        Assert.True(leaf.SelectedLocale is not null);
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
    public void Legacy_flat_settings_json_migrates_to_active_mill_profile()
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

        // Migration turns the legacy mode blocks into profiles; Load() returns
        // the active one (old Mode = Mill). SettingsMigrator tests cover the
        // two-profile split in detail.
        var loaded = new SettingsService(dir).Load();

        Assert.Equal("Mill", loaded.MachineTypeId);
        Assert.Equal(5, loaded.DemoAxes);
        Assert.Equal("COM7", loaded.Connection.SerialPort);
        Assert.False(loaded.Modules["jog"]);
    }

    [Fact]
    public void Every_settings_tree_node_exposes_IsExpanded_for_the_TreeViewItem_binding()
    {
        // Regression: the tree styles every TreeViewItem (groups AND leaves) with
        // {ReflectionBinding IsExpanded, Mode=TwoWay}. When a leaf type lacked the
        // accessor, Avalonia logged "Could not find a matching property accessor for
        // 'IsExpanded'" for each leaf the first time its group was opened.
        var (_, settings) = MakeSettings();
        var vm = CreateVm(settings);

        foreach (var group in vm.Groups)
        {
            var groupItem = AssertIsExpandedAccessor((AppSettingsGroupItem)group);
            foreach (var child in groupItem.Children)
                AssertIsExpandedAccessor((AppSettingsLeaf)child);
        }

        // Groups expand for real; leaves are inert no-ops (never expandable, but the
        // accessor keeps the two-way style binding valid without corrupting anything).
        foreach (var group in vm.Groups.Cast<AppSettingsGroupItem>())
        {
            group.IsExpanded = true;
            Assert.True(group.IsExpanded);
            foreach (var leaf in group.Children.Cast<AppSettingsLeaf>())
            {
                leaf.IsExpanded = true;
                Assert.False(leaf.IsExpanded);
            }
        }
    }

    private static T AssertIsExpandedAccessor<T>(T node) where T : class
    {
        var prop = node.GetType().GetProperty("IsExpanded");
        Assert.True(prop is { GetMethod.IsPublic: true, SetMethod.IsPublic: true },
            $"{node.GetType().Name} must expose a public IsExpanded accessor");
        return node;
    }
}