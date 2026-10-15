
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
using CNC.Core.Abstractions;
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

    /// <summary>Dialog double: hands back a scripted path and records the suggested name
    /// and the filter lists the backup leaf passes (pattern: MainViewModelTests.StubFileDialogs).</summary>
    private sealed class RecordingFileDialogs : IFileDialogService
    {
        public string? SavePath { get; set; }
        public string? OpenPath { get; set; }
        public string? LastSuggestedName { get; private set; }
        public string[] LastOpenFilters { get; private set; } = [];
        public string[] LastSaveFilters { get; private set; } = [];
        public int OpenCalls { get; private set; }
        public int SaveCalls { get; private set; }

        public Task<string?> OpenFileAsync(string title, params string[] filters)
        {
            OpenCalls++;
            LastOpenFilters = filters;
            return Task.FromResult(OpenPath);
        }

        public Task<string?> SaveFileAsync(string title, string suggestedFileName, params string[] filters)
        {
            SaveCalls++;
            LastSuggestedName = suggestedFileName;
            LastSaveFilters = filters;
            return Task.FromResult(SavePath);
        }

        public Task ShowErrorAsync(string title, string message) => Task.CompletedTask;
    }

    /// <summary>A live settings fixture: global.json, two profiles and one companion.</summary>
    private static (string Dir, SettingsService Service, RecordingFileDialogs Dialogs) MakeBackupFixture()
    {
        var (dir, settings) = MakeSettings();
        settings.Load();                                    // global.json + the default profile
        settings.CreateProfile("foam", MachineTypes.Foam);  // a second profile file
        File.WriteAllText(
            Path.Combine(ProfileSettingsPaths.ProfilesDir(dir), MachineProfile.DefaultId + ".probing.json"), "{}");
        return (dir, settings, new RecordingFileDialogs());
    }

    private static AppSettingsViewModel CreateBackupVm(SettingsService settings, RecordingFileDialogs dialogs,
        Func<bool>? gate = null) =>
        new(settings, Modules, Loc, gate, null, dialogs);

    private static BackupSettingsLeaf BackupLeafOf(AppSettingsViewModel vm) =>
        ((AppSettingsGroupItem)vm.Groups[0]).Children.OfType<BackupSettingsLeaf>().Single();

    private static string TempBackupPath() =>
        Path.Combine(Path.GetTempPath(), "senderng-backup-" + Guid.NewGuid().ToString("N") + AppSettingsBundle.Extension);

    /// <summary>Relative path + verbatim text of every file under the settings dir, so a
    /// refusal can be shown to have left the directory byte-identical.</summary>
    private static string DiskState(string dir) =>
        string.Join("\n", Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)
            .OrderBy(p => p, StringComparer.Ordinal)
            .Select(p => Path.GetRelativePath(dir, p) + "=" + File.ReadAllText(p)));

    /// <summary>A readable archive written from <paramref name="files"/> (any payload text).</summary>
    private static string WriteArchive(string path, params (string Name, string Text)[] files)
    {
        new AppSettingsBundle
        {
            Manifest = new AppSettingsBundleManifest(
                AppSettingsBundle.Kind, AppSettingsBundle.CurrentFormatVersion, "2026-10-13T12:00:00Z", "1.0", files.Length),
            Files = files.ToDictionary(f => f.Name, f => f.Text, StringComparer.Ordinal),
        }.WriteTo(path, DateTimeOffset.UtcNow);
        return path;
    }

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

    // The reference must never become a local of the frame that reads IsAlive: that
    // frame keeps a stale strong pointer to whatever it has held, even after
    // `last = null` (a bare `new object()` in this pattern stays alive too), so the
    // collectability check fails no matter what the production code does. Create,
    // dispose and capture the WeakReference inside this callee; only the
    // WeakReference crosses back. NoInlining keeps the whole transaction out of the
    // test frame, so the assertion measures the leak, not the frame's stack slots.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference CreateDisposeAndTrack(
        SettingsService settings, LocalizationService loc, HotkeyService hotkeys, bool dispose)
    {
        var vm = new AppSettingsViewModel(settings, Modules, loc, null, hotkeys);
        if (dispose)
            vm.Dispose();
        return new WeakReference(vm);
    }

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

            var tracked = new List<WeakReference>(25);
            for (var i = 0; i < 25; i++)
                tracked.Add(CreateDisposeAndTrack(settings, loc, hotkeys, dispose: true));

            Assert.Equal(locBaseline, SubscriberCount(loc, "Changed"));
            Assert.Equal(keyBaseline, SubscriberCount(hotkeys, "Changed"));

            // Control: tracking must notice a real pin, or the check below passes
            // vacuously. An undisposed VM is rooted by LocalizationService.Changed.
            var pinned = CreateDisposeAndTrack(settings, loc, hotkeys, dispose: false);

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            Assert.True(pinned.IsAlive,
                "control: an undisposed VM must stay rooted by LocalizationService.Changed");
            Assert.All(tracked, weak => Assert.False(weak.IsAlive,
                "AppSettingsViewModel must be collectable after Dispose; a live instance means "
                + "a singleton service still pins it."));
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

    /* -----------------------------------------------------------------------
     * Backup & restore leaf
     * --------------------------------------------------------------------- */

    [Fact]
    public void The_backup_leaf_is_the_last_child_of_the_global_group()
    {
        var (dir, settings) = MakeSettings();
        try
        {
            var vm = CreateVm(settings);
            var global = (AppSettingsGroupItem)vm.Groups[0];

            // The positional assumptions existing tests rely on are load-bearing.
            Assert.IsType<ProfileSettingsLeaf>(global.Children[0]);
            Assert.IsType<LanguageSettingsLeaf>(global.Children[1]);
            var leaf = Assert.IsType<BackupSettingsLeaf>(global.Children[global.Children.Count - 1]);
            Assert.Equal("backup", leaf.Id);

            // With the optional Hotkeys leaf present the backup leaf is still appended last.
            var hotkeys = new HotkeyService(settings);
            var withHotkeys = new AppSettingsViewModel(settings, Modules, Loc, null, hotkeys);
            try
            {
                var hotkeysGlobal = (AppSettingsGroupItem)withHotkeys.Groups[0];
                Assert.IsType<LanguageSettingsLeaf>(hotkeysGlobal.Children[1]);
                var last = Assert.IsType<BackupSettingsLeaf>(hotkeysGlobal.Children[hotkeysGlobal.Children.Count - 1]);
                Assert.Equal(3, hotkeysGlobal.Children.IndexOf(last));
            }
            finally
            {
                withHotkeys.Dispose();
            }
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void The_leaf_reports_the_settings_folder_and_file_counts()
    {
        var (dir, settings, dialogs) = MakeBackupFixture();
        try
        {
            var leaf = BackupLeafOf(CreateBackupVm(settings, dialogs));

            Assert.Equal(dir, leaf.SettingsFolder);
            Assert.Equal(Loc.T("appSettings.backup.folder").Replace("{0}", dir), leaf.FolderText);
            Assert.Equal(
                Loc.T("appSettings.backup.summary").Replace("{0}", "4").Replace("{1}", "2"),
                leaf.Summary);

            // The counts come from the disk, not the in-memory graph: a profile file dropped
            // in behind the service's back shows up without the registry ever knowing.
            File.WriteAllText(Path.Combine(ProfileSettingsPaths.ProfilesDir(dir), "ghost.json"), "{}");
            Assert.DoesNotContain("ghost", settings.Global.Profiles.Keys);

            leaf.Refresh();

            Assert.Equal(
                Loc.T("appSettings.backup.summary").Replace("{0}", "5").Replace("{1}", "3"),
                leaf.Summary);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Backup_writes_a_zip_into_the_chosen_path()
    {
        var (dir, settings, dialogs) = MakeBackupFixture();
        var target = TempBackupPath();
        try
        {
            var leaf = BackupLeafOf(CreateBackupVm(settings, dialogs));
            dialogs.SavePath = target;

            await leaf.BackupAsync();

            Assert.True(File.Exists(target), leaf.Status);
            Assert.EndsWith(AppSettingsBundle.Extension, target);
            Assert.NotNull(dialogs.LastSuggestedName);
            Assert.EndsWith(AppSettingsBundle.Extension, dialogs.LastSuggestedName!);
            Assert.Contains(AppSettingsBundle.Extension, string.Join(";", dialogs.LastSaveFilters));

            // The archive reads back as a SenderNG app-settings bundle holding the live file set.
            var read = AppSettingsBundle.ReadFrom(target);
            Assert.True(read.Ok, read.Error);
            Assert.Equal(
                new[] { "global.json", "profiles/default.json", "profiles/default.probing.json", "profiles/foam.json" },
                read.Bundle!.Files.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray());
            Assert.Equal(File.ReadAllText(Path.Combine(dir, "global.json")), read.Bundle.Files["global.json"]);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
            if (File.Exists(target))
                File.Delete(target);
        }
    }

    [Fact]
    public async Task Backup_reports_the_file_count_and_destination()
    {
        var (dir, settings, dialogs) = MakeBackupFixture();
        var target = TempBackupPath();
        try
        {
            var leaf = BackupLeafOf(CreateBackupVm(settings, dialogs));
            dialogs.SavePath = target;

            await leaf.BackupAsync();

            Assert.True(leaf.HasStatus);
            Assert.Equal(
                Loc.T("appSettings.backup.saved").Replace("{0}", "4").Replace("{1}", target),
                leaf.Status);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
            if (File.Exists(target))
                File.Delete(target);
        }
    }

    [Fact]
    public async Task A_cancelled_backup_writes_nothing()
    {
        var (dir, settings, dialogs) = MakeBackupFixture();
        try
        {
            var leaf = BackupLeafOf(CreateBackupVm(settings, dialogs));
            var before = DiskState(dir);
            dialogs.SavePath = null;

            await leaf.BackupAsync();

            Assert.Equal(1, dialogs.SaveCalls);
            Assert.Equal(before, DiskState(dir));
            Assert.False(leaf.HasStatus); // a cancel is not a state change
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Restore_shows_a_confirm_step_before_writing_anything()
    {
        var (dir, settings, dialogs) = MakeBackupFixture();
        var target = TempBackupPath();
        try
        {
            var archived = AppSettingsBundle.Capture(dir, "test", DateTimeOffset.UtcNow);
            archived.WriteTo(target, DateTimeOffset.UtcNow);
            var leaf = BackupLeafOf(CreateBackupVm(settings, dialogs));
            // The live files diverge from the archive after the backup was taken.
            File.WriteAllText(Path.Combine(dir, "global.json"), "stale");
            var before = DiskState(dir);
            dialogs.OpenPath = target;

            await leaf.RestoreAsync();

            Assert.True(leaf.HasRestorePlan);
            Assert.Contains(archived.Files.Count.ToString(), leaf.PlanSummary);
            Assert.Equal(Loc.T("appSettings.backup.restoreNow"), leaf.RestoreNowText);
            Assert.Equal(Loc.T("appSettings.backup.restoreNow"), leaf.RestoreButtonText);
            Assert.Contains(AppSettingsBundle.Extension, string.Join(";", dialogs.LastOpenFilters));
            Assert.Equal(before, DiskState(dir)); // nothing is written before the confirm
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
            if (File.Exists(target))
                File.Delete(target);
        }
    }

    [Fact]
    public async Task ApplyRestore_writes_the_archive_and_marks_the_service_restore_pending()
    {
        var (dir, settings, dialogs) = MakeBackupFixture();
        var target = TempBackupPath();
        try
        {
            var archived = AppSettingsBundle.Capture(dir, "test", DateTimeOffset.UtcNow);
            archived.WriteTo(target, DateTimeOffset.UtcNow);
            var leaf = BackupLeafOf(CreateBackupVm(settings, dialogs));
            File.WriteAllText(Path.Combine(dir, "global.json"), "stale");
            File.WriteAllText(Path.Combine(ProfileSettingsPaths.ProfilesDir(dir), "default.json"), "stale");
            dialogs.OpenPath = target;

            await leaf.RestoreAsync();
            await leaf.ApplyRestoreAsync();

            Assert.True(settings.RestorePending);
            Assert.False(leaf.HasRestorePlan);
            foreach (var (key, text) in archived.Files)
                Assert.Equal(text, File.ReadAllText(Path.Combine(dir, key.Replace('/', Path.DirectorySeparatorChar))));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
            if (File.Exists(target))
                File.Delete(target);
        }
    }

    [Fact]
    public async Task ApplyRestore_reports_the_restart_requirement()
    {
        var (dir, settings, dialogs) = MakeBackupFixture();
        var target = TempBackupPath();
        try
        {
            var archived = AppSettingsBundle.Capture(dir, "test", DateTimeOffset.UtcNow);
            archived.WriteTo(target, DateTimeOffset.UtcNow);
            var leaf = BackupLeafOf(CreateBackupVm(settings, dialogs));
            dialogs.OpenPath = target;

            await leaf.RestoreAsync();
            await leaf.ApplyRestoreAsync();

            Assert.Equal(
                Loc.T("appSettings.backup.restored").Replace("{0}", archived.Files.Count.ToString()),
                leaf.Status);
            Assert.Contains("Restart", leaf.Status, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
            if (File.Exists(target))
                File.Delete(target);
        }
    }

    [Fact]
    public async Task ApplyRestore_leaves_bystander_files_alone()
    {
        var (dir, settings, dialogs) = MakeBackupFixture();
        var target = TempBackupPath();
        try
        {
            AppSettingsBundle.Capture(dir, "test", DateTimeOffset.UtcNow).WriteTo(target, DateTimeOffset.UtcNow);
            var leaf = BackupLeafOf(CreateBackupVm(settings, dialogs));
            // A profile created after the archive was taken: restore must not delete it.
            var bystander = Path.Combine(ProfileSettingsPaths.ProfilesDir(dir), "liveonly.json");
            File.WriteAllText(bystander, "{\"Id\":\"liveonly\",\"Name\":\"Live only\"}");
            dialogs.OpenPath = target;

            await leaf.RestoreAsync();
            await leaf.ApplyRestoreAsync();

            Assert.True(settings.RestorePending);
            Assert.True(File.Exists(bystander));
            Assert.Equal("{\"Id\":\"liveonly\",\"Name\":\"Live only\"}", File.ReadAllText(bystander));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
            if (File.Exists(target))
                File.Delete(target);
        }
    }

    [Fact]
    public async Task A_foreign_or_broken_archive_is_rejected_with_a_message_and_writes_nothing()
    {
        var (dir, settings, dialogs) = MakeBackupFixture();
        var foreign = TempBackupPath();
        var brokenZip = TempBackupPath();
        var brokenPayload = TempBackupPath();
        try
        {
            // (a) a well-formed zip from another tool: right shape, wrong manifest kind.
            new AppSettingsBundle
            {
                Manifest = new AppSettingsBundleManifest("something-else", AppSettingsBundle.CurrentFormatVersion,
                    "2026-10-13T12:00:00Z", "1.0", 1),
                Files = new Dictionary<string, string> { ["global.json"] = "{}" },
            }.WriteTo(foreign, DateTimeOffset.UtcNow);

            // (b) not a zip at all.
            File.WriteAllText(brokenZip, "this is not a zip archive");

            // (c) a readable archive whose profile payload does not parse.
            WriteArchive(brokenPayload,
                ("global.json", "{\"ActiveProfileId\":\"mill\",\"Profiles\":{\"mill\":\"Mill\"}}"),
                ("profiles/mill.json", "{ not json"));

            var leaf = BackupLeafOf(CreateBackupVm(settings, dialogs));
            var before = DiskState(dir);

            foreach (var (path, fragment) in new[]
                     {
                         (foreign, "something-else"),
                         (brokenZip, "not a readable settings archive"),
                         (brokenPayload, "profiles/mill.json"),
                     })
            {
                dialogs.OpenPath = path;

                await leaf.RestoreAsync();

                Assert.False(leaf.HasRestorePlan);
                Assert.True(leaf.HasStatus);
                Assert.Contains("Restore failed", leaf.Status);
                Assert.Contains(fragment, leaf.Status);
                Assert.Equal(before, DiskState(dir));
            }
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
            foreach (var path in new[] { foreign, brokenZip, brokenPayload })
                if (File.Exists(path))
                    File.Delete(path);
        }
    }

    [Fact]
    public async Task Validate_warnings_are_shown_before_the_restore()
    {
        var (dir, settings, dialogs) = MakeBackupFixture();
        var target = TempBackupPath();
        try
        {
            // The archive's active profile is not among its own profiles: a warning, not an error.
            WriteArchive(target,
                ("global.json", "{\"ActiveProfileId\":\"ghost\",\"Profiles\":{\"ghost\":\"Ghost\"}}"),
                ("profiles/mill.json", "{\"Id\":\"mill\",\"Name\":\"Mill\"}"));
            var leaf = BackupLeafOf(CreateBackupVm(settings, dialogs));
            var before = DiskState(dir);
            dialogs.OpenPath = target;

            await leaf.RestoreAsync();

            Assert.True(leaf.HasRestorePlan); // a warning never blocks the restore
            Assert.Contains("active profile", leaf.PlanSummary);
            Assert.Equal(before, DiskState(dir));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
            if (File.Exists(target))
                File.Delete(target);
        }
    }

    [Fact]
    public async Task The_restore_is_blocked_by_the_gate()
    {
        var (dir, settings, dialogs) = MakeBackupFixture();
        var target = TempBackupPath();
        try
        {
            AppSettingsBundle.Capture(dir, "test", DateTimeOffset.UtcNow).WriteTo(target, DateTimeOffset.UtcNow);
            var vm = CreateBackupVm(settings, dialogs);
            var leaf = BackupLeafOf(vm);
            dialogs.OpenPath = target;
            await leaf.RestoreAsync(); // plan while switching is still allowed
            Assert.True(leaf.HasRestorePlan);

            File.WriteAllText(Path.Combine(dir, "global.json"), "stale");
            var before = DiskState(dir);
            vm.ProfileSwitchAllowed = () => false; // pushes the gate into the leaf

            Assert.False(leaf.CanRestore);
            Assert.True(leaf.ShowBlocked);
            Assert.False(leaf.RestoreCommand.CanExecute(null));
            Assert.False(leaf.ApplyRestoreCommand.CanExecute(null));

            // Executing anyway (a stale UI) must still refuse: nothing is written.
            await leaf.ApplyRestoreAsync();

            Assert.Equal(before, DiskState(dir));
            Assert.Equal(Loc.T("appSettings.backup.blocked"), leaf.Status);
            Assert.False(settings.RestorePending);

            // The gate passed to the constructor reaches the leaf too.
            var gatedInCtor = CreateBackupVm(settings, dialogs, () => false);
            Assert.False(BackupLeafOf(gatedInCtor).CanRestore);
            Assert.True(BackupLeafOf(gatedInCtor).ShowBlocked);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
            if (File.Exists(target))
                File.Delete(target);
        }
    }

    [Fact]
    public void RelocalizeButtons_refreshes_the_labels()
    {
        var (dir, settings, dialogs) = MakeBackupFixture();
        try
        {
            var vm = CreateBackupVm(settings, dialogs);
            var leaf = BackupLeafOf(vm);
            var before = leaf.RestoreButtonText;
            var notified = new List<string?>();
            leaf.PropertyChanged += (_, e) => notified.Add(e.PropertyName);

            if (Loc.AvailableLocales.Contains("ru"))
            {
                try
                {
                    Loc.SetLocale("ru");

                    // The VM's OnLocaleChanged must ask the leaf to re-raise its labels.
                    Assert.Contains(nameof(BackupSettingsLeaf.RestoreButtonText), notified);
                    Assert.Contains(nameof(BackupSettingsLeaf.RestoreNowText), notified);
                    Assert.NotEqual(before, leaf.RestoreButtonText);
                    Assert.Equal(Loc.T("appSettings.backup.restore"), leaf.RestoreButtonText);
                    Assert.Equal(Loc.T("appSettings.backup.restoreNow"), leaf.RestoreNowText);
                    Assert.Equal(Loc.T("appSettings.backup.folder").Replace("{0}", dir), leaf.FolderText);
                }
                finally
                {
                    Loc.SetLocale("en");
                }
            }
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    /* -----------------------------------------------------------------------
     * Restore-pending: blocked mutations and a warned-about archive (F11, F13, F15)
     * --------------------------------------------------------------------- */

    [Fact]
    public void Restore_pending_disables_profile_mutations_and_shows_the_restart_copy()
    {
        var (dir, settings) = MakeSettings();
        try
        {
            var vm = CreateVm(settings);
            var leaf = (ProfileSettingsLeaf)((AppSettingsGroupItem)vm.Groups[0]).Children[0];
            settings.CreateProfile("other", MachineTypes.Mill); // not activated: default stays active
            leaf.Refresh();
            leaf.SelectedProfile = leaf.Profiles.Single(p => p.Id == "other");
            leaf.NewName = "third";
            leaf.RenameName = "Renamed";
            leaf.SelectedMachineType = MachineTypes.Foam;

            // Control: with the gate open every mutation is offered, so the asserts below
            // fail for the restore guard, not because a condition was already false.
            Assert.True(leaf.CanSwitchProfiles);
            Assert.True(leaf.AddCommand.CanExecute(null));
            Assert.True(leaf.DuplicateCommand.CanExecute(null));
            Assert.True(leaf.RenameCommand.CanExecute(null));
            Assert.True(leaf.RemoveCommand.CanExecute(null));
            Assert.True(leaf.ActivateCommand.CanExecute(null));
            Assert.True(leaf.ApplyTypeCommand.CanExecute(null));

            settings.MarkRestorePending();

            Assert.False(leaf.CanSwitchProfiles);
            Assert.True(leaf.ShowSwitchBlocked);
            Assert.False(leaf.AddCommand.CanExecute(null));
            Assert.False(leaf.DuplicateCommand.CanExecute(null));
            Assert.False(leaf.RenameCommand.CanExecute(null));
            Assert.False(leaf.RemoveCommand.CanExecute(null));
            Assert.False(leaf.ActivateCommand.CanExecute(null));
            Assert.False(leaf.ApplyTypeCommand.CanExecute(null));

            // The blocked caption is the restart copy: switching is not blocked by the
            // session here, it is blocked by the pending restart.
            Assert.Equal(Loc.T("appSettings.backup.restartRequired"), leaf.SwitchBlockedText);
            Assert.NotEqual(Loc.T("appSettings.profiles.switchBlocked"), leaf.SwitchBlockedText);

            // A stale UI can still call Execute (DelegateCommand.Execute bypasses
            // CanExecute): the leaf itself must refuse — no Activated for an add that
            // never happened, and nothing written.
            var activated = 0;
            leaf.Activated += _ => activated++;
            var before = DiskState(dir);

            leaf.AddCommand.Execute(null);
            leaf.ActivateCommand.Execute(null);

            Assert.Equal(0, activated);
            Assert.Equal(before, DiskState(dir));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task A_partial_restore_marks_restore_pending_and_keeps_the_retry_wording()
    {
        var (dir, settings, dialogs) = MakeBackupFixture();
        var target = TempBackupPath();
        try
        {
            // global.json sorts first, so it lands and the locked profile fails: a
            // genuinely partial restore.
            WriteArchive(target,
                ("global.json", "{\"ActiveProfileId\":\"default\",\"Profiles\":{\"default\":\"Default\"}}"),
                ("profiles/default.json", "{\"Id\":\"default\",\"Name\":\"Restored\"}"));
            var leaf = BackupLeafOf(CreateBackupVm(settings, dialogs));
            dialogs.OpenPath = target;

            await leaf.RestoreAsync();
            Assert.True(leaf.HasRestorePlan);

            var locked = Path.Combine(ProfileSettingsPaths.ProfilesDir(dir), "default.json");
            // A directory where the profile file must land: the same portable simulation as
            // AppSettingsRestoreTests — FileShare.None blocks a replace on Windows only, because
            // .NET maps it to an advisory lock on Unix and rename(2) ignores those.
            File.Delete(locked);
            Directory.CreateDirectory(locked);
            await leaf.ApplyRestoreAsync();

            // The file that landed must be frozen too, or the stale graph's next save
            // overwrites it before the user retries.
            Assert.True(settings.RestorePending);
            Assert.Equal(
                Loc.T("appSettings.backup.restorePartial").Replace("{0}", "1"),
                leaf.Status);
            Assert.Contains("run the restore again", leaf.Status, StringComparison.Ordinal);
            Assert.Equal("{\"ActiveProfileId\":\"default\",\"Profiles\":{\"default\":\"Default\"}}",
                File.ReadAllText(Path.Combine(dir, "global.json")));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
            if (File.Exists(target))
                File.Delete(target);
        }
    }

    [Fact]
    public async Task Backup_warns_when_the_captured_payload_cannot_restore()
    {
        var (dir, settings, dialogs) = MakeBackupFixture();
        var target = TempBackupPath();
        try
        {
            var leaf = BackupLeafOf(CreateBackupVm(settings, dialogs));
            dialogs.SavePath = target;

            // A truncated live profile (ProfilesStore writes non-atomically) is captured
            // verbatim, so the archive the user just made is one Apply will always refuse.
            // Written after the dialog is built: the profile manager loads every profile
            // when it builds, which would quarantine an unparseable one as ".corrupt"
            // before Capture ever saw it.
            File.WriteAllText(Path.Combine(ProfileSettingsPaths.ProfilesDir(dir), "truncated.json"), "{ not json");

            await leaf.BackupAsync();

            // Still written: a backup is a safety net, corrupt payload and all.
            Assert.True(File.Exists(target), leaf.Status);
            var read = AppSettingsBundle.ReadFrom(target);
            Assert.True(read.Ok, read.Error);
            Assert.True(read.Bundle!.Files.ContainsKey("profiles/truncated.json"),
                string.Join(",", read.Bundle.Files.Keys));
            var validation = AppSettingsBundleValidator.Validate(read.Bundle);
            Assert.False(validation.Ok);

            // Asserted as fragments: true with the catalog key and with the inline
            // fallback (the catalog stream adds the key in parallel).
            Assert.Contains("Warning: this archive may not restore", leaf.Status, StringComparison.Ordinal);
            Assert.Contains(validation.Errors[0], leaf.Status, StringComparison.Ordinal);
            Assert.Contains($"{read.Bundle.Files.Count} files", leaf.Status, StringComparison.Ordinal);
            Assert.Contains(target, leaf.Status, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
            if (File.Exists(target))
                File.Delete(target);
        }
    }

    [Fact]
    public async Task Backup_warns_when_the_archive_itself_would_be_refused()
    {
        // Validate inspects payloads only; the archive-LEVEL caps live in the reader, and Capture
        // will happily archive a >4 MiB entry that ReadFrom then refuses. Without the read-back the
        // leaf reported a clean "saved" for an archive that could never be restored.
        var (dir, settings, dialogs) = MakeBackupFixture();
        try
        {
            var blob = new string('x', 5 * 1024 * 1024);
            File.WriteAllText(
                ProfileSettingsPaths.ProfilePath(dir, MachineProfile.DefaultId),
                "{\"Id\":\"" + MachineProfile.DefaultId + "\",\"Name\":\"Default\",\"Blob\":\"" + blob + "\"}");

            var vm = CreateBackupVm(settings, dialogs);
            var leaf = BackupLeafOf(vm);
            var target = TempBackupPath();
            dialogs.SavePath = target;

            await leaf.BackupAsync();

            Assert.True(File.Exists(target)); // still written: a backup is a safety net
            Assert.Contains("Warning", leaf.Status);
            Assert.Contains("larger than", leaf.Status);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}

/// <summary>
/// The machine card's axis editor (T6): the profile is where the user expresses their
/// machine, so these cover the edit primitives, the resolver's invariants rendered as
/// data, the destructive machine-type switch, and the bug-report layout round-tripping
/// through disk.
/// </summary>
public class MachineAxesEditorTests
{
    // A private instance: the one locale test below calls SetLocale and must never race
    // another class that reads the shared static catalog.
    private static readonly LocalizationService Loc =
        LocalizationService.LoadFromDirectory(Path.Combine(AppContext.BaseDirectory, "Locale"));

    private static (string Dir, SettingsService Settings) MakeSettings()
    {
        var dir = Path.Combine(Path.GetTempPath(), "senderng-axes-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return (dir, new SettingsService(dir));
    }

    private static MachineAxesViewModel CreateEditor(SettingsService settings, string profileId,
        IMachineKinematicsProvider? machines = null) =>
        new(settings, Loc.Resolve, profileId, machines);

    /// <summary>A kinematic provider double: the editor only reads the board's letters and
    /// the reported axis count from it.</summary>
    private sealed class StubProvider : IMachineKinematicsProvider
    {
        private readonly Func<MachineKinematics> _resolve;
        private EventHandler? _changed;

        public StubProvider(Func<MachineKinematics> resolve) => _resolve = resolve;

        public MachineKinematics Current => _resolve();

        public event EventHandler? Changed { add => _changed += value; remove => _changed -= value; }

        public void Refresh() => _changed?.Invoke(this, EventArgs.Empty);
    }

    private static readonly IModule[] NoModules = [];

    private static ProfileSettingsLeaf ProfilesLeafOf(AppSettingsViewModel vm) =>
        ((AppSettingsGroupItem)vm.Groups[0]).Children.OfType<ProfileSettingsLeaf>().Single();

    private static string ProblemText(string code, int number) =>
        Loc.T("appSettings.axes.problem." + code).Replace("{0}", number.ToString());

    [Fact]
    public void Adding_an_optional_axis_appends_a_removable_row_and_persists()
    {
        var (dir, settings) = MakeSettings();
        try
        {
            var profile = settings.Load(); // Mill: indices 0,1,2 are the base plane
            var editor = CreateEditor(settings, profile.Id);

            Assert.True(editor.AddAxisCommand.CanExecute(null));
            editor.AddAxisCommand.Execute(null);

            var row = Assert.Single(editor.OptionalAxes);
            Assert.False(row.IsBase);
            Assert.True(row.CanRemove);
            Assert.Equal(3, row.Index); // the lowest free grbl index

            // Base rows are locked: the plane rows are all there and none is removable.
            var baseRows = editor.Planes.SelectMany(p => p.Axes).ToList();
            Assert.Equal(3, baseRows.Count);
            Assert.All(baseRows, r => Assert.True(r.IsBase));
            Assert.All(baseRows, r => Assert.False(r.CanRemove));
            Assert.False(editor.HasNoOptionalAxes);

            // Persisted, not just in memory.
            var reloaded = new SettingsService(dir).Load();
            Assert.Equal(3, Assert.Single(reloaded.OptionalAxes).Index);
            Assert.Equal(AxisKind.Linear, reloaded.OptionalAxes[0].Kind);

            row.RemoveCommand.Execute(null);
            Assert.Empty(editor.OptionalAxes);
            Assert.True(editor.HasNoOptionalAxes);
            Assert.Empty(new SettingsService(dir).Load().OptionalAxes);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Duplicate_index_surfaces_as_a_localized_problem()
    {
        var (dir, settings) = MakeSettings();
        try
        {
            var profile = settings.Load();
            var editor = CreateEditor(settings, profile.Id);
            editor.AddAxisCommand.Execute(null);
            Assert.True(editor.IsValid); // control: a fresh optional axis is clean

            editor.OptionalAxes[0].ApplyIndex(1); // plane slot 1 already uses index 1

            Assert.False(editor.IsValid);
            Assert.Contains(editor.Problems, p => p.IsError && p.Text == ProblemText("axis.indexDuplicate", 1));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Problem_messages_follow_the_locale()
    {
        var (dir, settings) = MakeSettings();
        try
        {
            var profile = settings.Load();
            var editor = CreateEditor(settings, profile.Id);
            editor.AddAxisCommand.Execute(null);
            editor.OptionalAxes[0].ApplyIndex(0);

            var english = editor.Problems.Single(p => p.IsError).Text;

            if (!Loc.AvailableLocales.Contains("ru"))
                return;
            try
            {
                Loc.SetLocale("ru");
                editor.Relocalize();

                var russian = editor.Problems.Single(p => p.IsError).Text;
                Assert.NotEqual(english, russian);
                Assert.Equal(Loc.T("appSettings.axes.problem.axis.indexDuplicate").Replace("{0}", "0"), russian);
                // No raw key ever leaks: a missing translation falls back to the resolver's
                // English developer text, not to "appSettings.axes.problem.…".
                Assert.DoesNotContain("appSettings.axes", russian, StringComparison.Ordinal);
            }
            finally
            {
                Loc.SetLocale("en");
            }
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Rotary_row_shows_exactly_the_two_perpendicular_pivots()
    {
        var (dir, settings) = MakeSettings();
        try
        {
            var profile = settings.Load(); // Mill plane: index 0 = Width, 1 = Length, 2 = Height
            var editor = CreateEditor(settings, profile.Id);
            editor.AddAxisCommand.Execute(null);
            var row = editor.OptionalAxes[0];

            // A linear row has no pivot inputs at all.
            Assert.Empty(row.Pivots);

            row.ApplyKind(AxisKind.Rotary);
            row.ApplyDirection(AxisDirection.Height);

            // Exactly two, in canonical Width/Length order of the perpendicular pair, and
            // the editor's own write satisfies invariant 7 rather than violating it.
            Assert.Equal(2, row.Pivots.Count);
            Assert.Equal(new[] { AxisDirection.Width, AxisDirection.Length },
                row.Pivots.Select(p => p.Direction).ToArray());
            var stored = Assert.IsType<double[]>(row.Model.RotationOrigin);
            Assert.Equal(2, stored.Length);

            // Each label is the board letter of an axis occupying that direction
            // (conventional when no controller reported any).
            Assert.Equal("X", row.Pivots[0].Label);
            Assert.Equal("Y", row.Pivots[1].Label);

            row.Pivots[0].ApplyValue(12.5);
            Assert.Equal(12.5, row.Model.RotationOrigin![0]);
            Assert.Equal(12.5, new SettingsService(dir).Load().OptionalAxes[0].RotationOrigin![0]);

            // Back to linear: the pivot is gone (a linear axis must not carry one).
            row.ApplyKind(AxisKind.Linear);
            Assert.Empty(row.Pivots);
            Assert.Null(row.Model.RotationOrigin);
            Assert.True(editor.IsValid);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Pivot_label_is_the_bare_direction_name_when_no_axis_occupies_it()
    {
        var (dir, settings) = MakeSettings();
        try
        {
            var profile = settings.CreateProfile("foam", MachineTypes.Foam);
            settings.Activate(profile.Id);
            var editor = CreateEditor(settings, profile.Id);

            // Foam spans Length/Height in both planes, so Width has no axis — and so no letter.
            Assert.Null(editor.LetterForDirection(AxisDirection.Width));

            editor.AddAxisCommand.Execute(null);
            var row = editor.OptionalAxes[0];
            row.ApplyKind(AxisKind.Rotary);
            row.ApplyDirection(AxisDirection.Height);

            Assert.Equal(new[] { AxisDirection.Width, AxisDirection.Length },
                row.Pivots.Select(p => p.Direction).ToArray());
            Assert.Equal(Loc.T("appSettings.axes.direction.width"), row.Pivots[0].Label);
            Assert.Equal(editor.Planes[0].Axes[0].BoardLetterText, row.Pivots[1].Label);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Hand_edited_pivot_arity_is_reported_as_data_not_thrown()
    {
        var (dir, settings) = MakeSettings();
        try
        {
            var profile = settings.Load();
            profile.OptionalAxes.Add(new MachineAxis
            {
                Index = 3,
                Kind = AxisKind.Rotary,
                Direction = AxisDirection.Height,
                RotationOrigin = [1, 2, 3], // three values: impossible, must be reported
            });
            profile.OptionalAxes.Add(new MachineAxis
            {
                Index = 4,
                Kind = AxisKind.Linear,
                Direction = AxisDirection.Width,
                RotationOrigin = [1, 2], // a pivot on a linear axis: also impossible
            });
            settings.Save(profile);

            var editor = CreateEditor(settings, profile.Id);

            Assert.False(editor.IsValid);
            Assert.Contains(editor.Problems,
                p => p.Text == ProblemText("rotary.originArity", 3));
            Assert.Contains(editor.Problems,
                p => p.Text == ProblemText("linear.originPresent", 4));

            // Still editable, and the row shows the two inputs arity requires.
            var rotary = editor.OptionalAxes.Single(r => r.Index == 3);
            Assert.Equal(2, rotary.Pivots.Count);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Foam_fix_plane2_slot0_index3_round_trips_through_save_and_load()
    {
        // The bug report: on a conventional XYZA board foam plane 2's horizontal is the
        // axis the board calls A (index 3) and its vertical is Z (index 2). Expressed by
        // moving the axis at index 3 into plane 2's first slot.
        var (dir, settings) = MakeSettings();
        try
        {
            var profile = settings.CreateProfile("foam", MachineTypes.Foam);
            settings.Activate(profile.Id);
            var editor = CreateEditor(settings, profile.Id);
            Assert.True(editor.IsMultiPlane);

            editor.Planes[1].Axes[0].ApplyIndex(3);
            editor.Planes[1].Axes[1].ApplyIndex(2);

            var reloaded = new SettingsService(dir).Load();
            Assert.Equal(new[] { 3, 2 }, reloaded.Planes[1].Axes.Select(a => a.Index).ToArray());

            var kinematics = MachineKinematicsResolver.Resolve(reloaded, null, 0);
            Assert.True(kinematics.IsValid, string.Join("; ", kinematics.Problems.Select(p => p.Message)));
            // Plane order, deliberately non-monotonic — the slots carry the orientation.
            Assert.Equal(new[] { 3, 2 }, kinematics.PlaneIndices(1));
            Assert.Equal("A (3)", editor.Planes[1].Axes[0].SelectedIndex!.Display);
            Assert.Equal("Z (2)", editor.Planes[1].Axes[1].SelectedIndex!.Display);
            Assert.Equal('A', kinematics.LetterFor(kinematics.IndexAt(1, 0)));
            Assert.Equal('Z', kinematics.LetterFor(kinematics.IndexAt(1, 1)));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Board_letters_follow_the_controller_not_the_picker()
    {
        var (dir, settings) = MakeSettings();
        try
        {
            var profile = settings.Load(); // Mill: indices 0,1,2 are the base plane
            var editor = CreateEditor(settings, profile.Id,
                new StubProvider(() => MachineKinematicsResolver.Resolve(settings.Load(), "XYUV", 4)));

            editor.AddAxisCommand.Execute(null);
            var row = Assert.Single(editor.OptionalAxes);

            // The picker offers the conventional letter (A means index 3) and the profile
            // stores the index — while the row displays the letter the board uses.
            Assert.Equal(3, row.Index);
            Assert.Equal("A (3)", row.SelectedIndex!.Display);
            Assert.Equal("V", row.BoardLetterText);

            // And on a remapped board the foam layout needs no edit at all: plane 2's
            // slots are indices 2 and 3, which the board calls U and V.
            var foam = settings.CreateProfile("foam", MachineTypes.Foam);
            settings.Activate(foam.Id);
            var foamEditor = CreateEditor(settings, foam.Id,
                new StubProvider(() => MachineKinematicsResolver.Resolve(settings.Load(), "XYUV", 4)));
            Assert.Equal("U", foamEditor.Planes[1].Axes[0].BoardLetterText);
            Assert.Equal("V", foamEditor.Planes[1].Axes[1].BoardLetterText);
            Assert.Equal("Z (2)", foamEditor.Planes[1].Axes[0].SelectedIndex!.Display);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Separation_is_multi_plane_only_and_persists()
    {
        var (dir, settings) = MakeSettings();
        try
        {
            var mill = settings.Load();
            var millEditor = CreateEditor(settings, mill.Id);
            Assert.False(millEditor.HasSeparation);

            var foam = settings.CreateProfile("foam", MachineTypes.Foam);
            settings.Activate(foam.Id);
            var foamEditor = CreateEditor(settings, foam.Id);
            Assert.True(foamEditor.HasSeparation);
            Assert.Equal(0d, foamEditor.Separation); // the preset's default, carried not absent

            foamEditor.Separation = 300;
            Assert.Equal(300d, new SettingsService(dir).Load().Separation);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Machine_type_switch_is_warned_and_destructive()
    {
        var (dir, settings) = MakeSettings();
        try
        {
            var vm = new AppSettingsViewModel(settings, NoModules, Loc);
            var leaf = ProfilesLeafOf(vm);
            leaf.MachineAxes.AddAxisCommand.Execute(null);
            Assert.Single(leaf.MachineAxes.OptionalAxes);

            // The draft only warns: nothing is replaced until the confirming click.
            leaf.SelectedMachineType = MachineTypes.Foam;
            Assert.True(leaf.IsTypeChangePending);
            Assert.Contains("layout", leaf.TypeChangeWarning, StringComparison.OrdinalIgnoreCase);

            leaf.ApplyTypeCommand.Execute(null);
            Assert.Equal("Change again to confirm", leaf.TypeButtonText);
            Assert.Equal(MachineTypes.Mill, settings.Load().MachineTypeId);

            leaf.ApplyTypeCommand.Execute(null);

            // Destructive by ruling: the new type's defaults replace planes, optional
            // axes and separation outright — no merge, nothing carried over.
            Assert.Equal(MachineTypes.Foam, settings.Load().MachineTypeId);
            Assert.Equal(new[] { 2, 2 }, settings.Load().Planes.Select(p => p.Axes.Count).ToArray());
            Assert.Empty(settings.Load().OptionalAxes);
            Assert.Equal(MachineTypeCatalog.Resolve(MachineTypes.Foam).DefaultSeparation, settings.Load().Separation);

            // The card followed the switch.
            Assert.Equal(MachineTypes.Foam, leaf.MachineAxes.MachineTypeId);
            Assert.Equal(2, leaf.MachineAxes.Planes.Count);
            Assert.Empty(leaf.MachineAxes.OptionalAxes);
            Assert.True(leaf.MachineAxes.HasSeparation);
            Assert.EndsWith("machine-topology-foam.png", leaf.MachineAxes.TopologyImage!);
            Assert.False(leaf.IsTypeChangePending);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Mill_to_foam_switch_surfaces_the_hidden_demo_plane()
    {
        // T1 leaves DemoAxes alone on a type switch, so a Mill→Foam switch leaves the demo
        // simulating 3 axes and the second plane never appears. The card says so.
        var (dir, settings) = MakeSettings();
        try
        {
            var profile = settings.Load();
            Assert.Equal(3, profile.DemoAxes);
            var millEditor = CreateEditor(settings, profile.Id);
            Assert.False(millEditor.HasDemoAxesHint);

            settings.SetProfileMachineType(profile.Id, MachineTypes.Foam);

            var foamEditor = CreateEditor(settings, profile.Id);
            Assert.True(foamEditor.HasDemoAxesHint);
            Assert.Contains("3", foamEditor.DemoAxesHint!);
            Assert.Contains("4", foamEditor.DemoAxesHint!);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Profile_written_before_the_axis_model_shows_the_seeded_layout()
    {
        var (dir, settings) = MakeSettings();
        try
        {
            var profile = settings.Load();
            profile.Planes = [];
            profile.Separation = 0;
            settings.Save(profile);

            var editor = CreateEditor(settings, profile.Id);

            // The editor seeds the preset's base planes in memory (the resolver treats an
            // empty layout the same way), so the card shows a machine rather than nothing.
            Assert.Single(editor.Planes);
            Assert.Equal(3, editor.Planes[0].Axes.Count);

            // Opening the card writes nothing: a profile file is not rewritten just because
            // the dialog was opened. The seed reaches disk with the first edit.
            Assert.Empty(new SettingsService(dir).Load().Planes);

            editor.AddAxisCommand.Execute(null);
            var reloaded = new SettingsService(dir).Load();
            Assert.Equal(new[] { 0, 1, 2 }, reloaded.Planes[0].Axes.Select(a => a.Index).ToArray());
            Assert.Equal(3, Assert.Single(reloaded.OptionalAxes).Index);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void The_editor_follows_the_selected_profile()
    {
        var (dir, settings) = MakeSettings();
        try
        {
            var vm = new AppSettingsViewModel(settings, NoModules, Loc);
            var leaf = ProfilesLeafOf(vm);
            var foam = settings.CreateProfile("foam", MachineTypes.Foam);
            leaf.Refresh();

            var millEditor = leaf.MachineAxes;
            Assert.Equal(MachineTypes.Mill, millEditor.MachineTypeId);

            leaf.SelectedProfile = leaf.Profiles.Single(p => p.Id == foam.Id);

            Assert.Same(millEditor, leaf.MachineAxes); // one card, re-pointed at the selection
            Assert.Equal(MachineTypes.Foam, leaf.MachineAxes.MachineTypeId);
            Assert.Equal(2, leaf.MachineAxes.Planes.Count);
            Assert.EndsWith("machine-topology-foam.png", leaf.MachineAxes.TopologyImage!);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}