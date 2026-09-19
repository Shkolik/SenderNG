/*
 * AppSettingsViewHeadlessTests.cs - headless UI tests for the App Settings view
 * (profile manager + language selector bindings)
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
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CNC.Core.Services;
using CNC.Core.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using SenderNG.Core.Localization;
using SenderNG.Core.Modules;
using SenderNG.Modules.Fixed;
using Xunit;

namespace SenderNG.Tests;

public class AppSettingsViewHeadlessTests
{
    // A private instance (not HeadlessTestCatalog.Locale): SetLocale calls here must
    // never race other test classes that read the shared static catalog.
    private static readonly LocalizationService Loc =
        LocalizationService.LoadFromDirectory(Path.Combine(AppContext.BaseDirectory, "Locale"));

    private static SettingsService MakeSettings()
    {
        var dir = Path.Combine(Path.GetTempPath(), "senderng-appsettings-ui-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return new SettingsService(dir);
    }

    [AvaloniaFact]
    public async Task Profiles_buttons_and_checkmark_follow_locale_and_render()
    {
        var prev = LocalizationService.Current;
        try
        {
            LocalizationService.Current = Loc;
            var settings = MakeSettings();
            var vm = new AppSettingsViewModel(settings, Array.Empty<IModule>(), Loc);
            var leaf = ((AppSettingsGroupItem)vm.Groups[0]).Children.OfType<ProfileSettingsLeaf>().Single();
            vm.SelectedNode = leaf;

            var view = new AppSettingsView { DataContext = vm };
            var window = new Window { Width = 920, Height = 560, Content = view };
            window.Show();
            for (var i = 0; i < 4; i++)
                await Pump();

            // Active-profile marker: the XAML entity &#x2713; must render as the
            // checkmark glyph, not the literal escape "\u2713".
            var marks = window.GetVisualDescendants().OfType<TextBlock>()
                .Where(t => t.Text == "\u2713").ToList();
            Assert.NotEmpty(marks);
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<TextBlock>(),
                t => t.Text == "\\u2713");

            var buttons = window.GetVisualDescendants().OfType<Button>().ToList();
            var dump = string.Join(" | ", buttons.Select(b => b.Content is null ? "<null>" : $"\"{b.Content}\""));
            var remove = buttons.SingleOrDefault(b => Equals(b.Content, "Remove"))
                ?? throw new InvalidOperationException($"Remove button not found. Buttons: {dump}");
            var type = buttons.SingleOrDefault(b => Equals(b.Content, "Change type"))
                ?? throw new InvalidOperationException($"Type button not found. Buttons: {dump}");
            var add = buttons.SingleOrDefault(b => Equals(b.Content, "Add"))
                ?? throw new InvalidOperationException($"Add button not found. Buttons: {dump}");
            Assert.Equal("Add", add.Content);

            if (Loc.AvailableLocales.Contains("ru"))
            {
                try
                {
                    Loc.SetLocale("ru");
                    for (var i = 0; i < 4; i++)
                        await Pump();

                    // The Profiles leaf title itself follows the locale.
                    Assert.Equal("Профили", leaf.Name);

                    // Computed (PropertyChanged-driven) labels follow the locale...
                    Assert.Equal("Удалить", remove.Content);
                    Assert.Equal("Изменить тип", type.Content);
                    // ...as do the {loc:Loc} markup labels.
                    Assert.Equal("Добавить", window.GetVisualDescendants().OfType<Button>()
                        .Single(b => Equals(b.Content, "Добавить")).Content);

                    // User's repro sequence: language leaf -> change locale -> visit
                    // Profiles. The hidden-while-away pane re-realizes already localized.
                    vm.SelectedNode = ((AppSettingsGroupItem)vm.Groups[0]).Children
                        .OfType<LanguageSettingsLeaf>().Single();
                    for (var i = 0; i < 4; i++)
                        await Pump();
                    vm.SelectedNode = leaf;
                    for (var i = 0; i < 4; i++)
                        await Pump();
                    Assert.Equal("Удалить", window.GetVisualDescendants().OfType<Button>()
                        .Single(b => Equals(b.Content, "Удалить")).Content);
                }
                finally
                {
                    Loc.SetLocale("en");
                    for (var i = 0; i < 4; i++)
                        await Pump();
                }
            }

            window.Close();
        }
        finally
        {
            LocalizationService.Current = prev;
        }
    }

    [AvaloniaFact]
    public async Task Language_selector_keeps_its_selection_when_navigating_away_and_back()
    {
        var prev = LocalizationService.Current;
        try
        {
            LocalizationService.Current = Loc;
            var settings = MakeSettings();
            var vm = new AppSettingsViewModel(settings, Array.Empty<IModule>(), Loc);
            var language = ((AppSettingsGroupItem)vm.Groups[0]).Children.OfType<LanguageSettingsLeaf>().Single();
            var locales = language.AvailableLocales; // computed getter: capture once
            vm.SelectedNode = language;

            var view = new AppSettingsView { DataContext = vm };
            var window = new Window { Width = 920, Height = 560, Content = view };
            window.Show();
            for (var i = 0; i < 4; i++)
                await Pump();

            var combo = window.GetVisualDescendants().OfType<ComboBox>()
                .Single(c => ReferenceEquals(c.ItemsSource, locales));
            Assert.Equal("en", combo.SelectedItem);

            // Navigate away (the editor template is torn down) and back (a fresh
            // ComboBox is realized from the DataTemplate).
            vm.SelectedNode = ((AppSettingsGroupItem)vm.Groups[0]).Children
                .OfType<ProfileSettingsLeaf>().Single();
            for (var i = 0; i < 4; i++)
                await Pump();
            vm.SelectedNode = language;
            for (var i = 0; i < 4; i++)
                await Pump();

            // VM state survives the old control's teardown, and the newly realized
            // selector shows the same locale.
            Assert.Equal("en", language.SelectedLocale);
            var revived = window.GetVisualDescendants().OfType<ComboBox>()
                .Single(c => ReferenceEquals(c.ItemsSource, locales));
            Assert.Equal("en", revived.SelectedItem);
            window.Close();
        }
        finally
        {
            LocalizationService.Current = prev;
        }
    }

    [AvaloniaFact]
    public async Task Expanded_group_realizes_leaf_items_whose_IsExpanded_binding_resolves()
    {
        // Regression: expanding a group materializes a TreeViewItem per leaf, and the
        // tree style binds {ReflectionBinding IsExpanded, Mode=TwoWay} against each
        // item's DataContext. Leaves used to lack the accessor, so the first group
        // opening logged a "Could not find a matching property accessor" error per leaf.
        var prev = LocalizationService.Current;
        try
        {
            LocalizationService.Current = Loc;
            var settings = MakeSettings();
            var vm = new AppSettingsViewModel(settings, Array.Empty<IModule>(), Loc);
            var view = new AppSettingsView { DataContext = vm };
            var window = new Window { Width = 920, Height = 560, Content = view };
            window.Show();
            for (var i = 0; i < 4; i++)
                await Pump();

            // Real user repro: open the first group. This realizes the leaf
            // TreeViewItems that used to fire the binding error.
            var group = (AppSettingsGroupItem)vm.Groups[0];
            var leafCount = group.Children.Count;
            group.IsExpanded = true;
            for (var i = 0; i < 4; i++)
                await Pump();

            // Every realized TreeViewItem's DataContext must expose IsExpanded so the
            // style's two-way ReflectionBinding resolves against it (group or leaf).
            var items = window.GetVisualDescendants().OfType<TreeViewItem>()
                .Where(t => ReferenceEquals(TopLevel.GetTopLevel(t), window)).ToList();
            Assert.True(items.Count >= leafCount + 1,
                $"Expected at least the group + {leafCount} realized leaves, got {items.Count}");
            foreach (var item in items)
            {
                var prop = item.DataContext?.GetType().GetProperty("IsExpanded");
                Assert.True(item.DataContext is not null
                        && prop is { GetMethod.IsPublic: true, SetMethod.IsPublic: true },
                    $"{item.DataContext?.GetType().Name} lacks a public IsExpanded accessor");
            }

            // The leaves stay selectable and the app stays functional afterwards.
            foreach (var leaf in group.Children)
            {
                vm.SelectedNode = leaf;
                for (var i = 0; i < 3; i++)
                    await Pump();
            }
            window.Close();
        }
        finally
        {
            LocalizationService.Current = prev;
        }
    }

    [AvaloniaFact]
    public async Task Enabled_checkbox_hidden_on_non_hideable_leaf_shown_on_hideable_leaf()
    {
        var prev = LocalizationService.Current;
        try
        {
            LocalizationService.Current = Loc;
            var settings = MakeSettings();
            var vm = new AppSettingsViewModel(settings, new SenderNG.Core.Modules.IModule[]
            {
                new StubModule("jog", 20, canHide: true),
                new StubFieldModule("work-parameters", 50),
            }, Loc);

            var view = new AppSettingsView { DataContext = vm };
            var window = new Window { Width = 920, Height = 560, Content = view };
            window.Show();
            for (var i = 0; i < 4; i++)
                await Pump();

            // Hideable leaf: the Enabled toggle is visible and enables/disables.
            vm.SelectedNode = vm.ModuleLeaves.Single(e => e.Id == "jog");
            for (var i = 0; i < 4; i++)
                await Pump();
            var enabled = window.GetVisualDescendants().OfType<CheckBox>()
                .Single(c => Equals(c.Content, "Enabled"));
            Assert.True(enabled.IsVisible);
            Assert.True(enabled.IsEnabled);

            // Non-hideable leaf with settings: no Enabled toggle is shown.
            vm.SelectedNode = vm.ModuleLeaves.Single(e => e.Id == "work-parameters");
            for (var i = 0; i < 4; i++)
                await Pump();
            var fixedCheck = window.GetVisualDescendants().OfType<CheckBox>()
                .Single(c => Equals(c.Content, "Enabled"));
            Assert.False(fixedCheck.IsVisible);

            window.Close();
        }
        finally
        {
            LocalizationService.Current = prev;
        }
    }

    /// <summary>Run queued dispatcher jobs (loc:Loc re-emissions land there) and yield
    /// so the headless SynchronizationContext runs a pending layout/render.</summary>
    private static async Task Pump()
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(16);
        await Task.Yield();
    }

    private class StubModule : IModule
    {
        public StubModule(string id, int order, bool canHide)
        {
            Id = id;
            Order = order;
            CanHide = canHide;
        }
        public string Id { get; }
        public string Name => Id;
        public string Description => $"Description of {Id}.";
        public int Order { get; }
        public PanelHint Panel => PanelHint.Center;
        public bool IsVisibleByDefault => true;
        public bool CanHide { get; }
        public IReadOnlyList<HotKey> HotKeys => [];
        public void RegisterServices(IServiceCollection services) { }
        public object CreateViewModel(IServiceProvider sp) => new object();
        public object CreateView(object vm) => new object();
    }

    private sealed class StubFieldModule : StubModule, IModuleSettingsProvider
    {
        public StubFieldModule(string id, int order) : base(id, order, canHide: false) { }
        public SettingsScope Scope => SettingsScope.Profile;
        public IEnumerable<ModuleSettingField> GetSettingFields()
        {
            yield return new ModuleSettingField("Visible tools", () => 1, _ => { });
        }
        public IEnumerable<ModuleSettingBool> GetBoolSettingFields() => [];
        public object? CreateSettingsView() => null;
    }
}