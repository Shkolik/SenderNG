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

using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CNC.Core.Abstractions;
using CNC.Core.Services;
using CNC.Core.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using SenderNG.Core.Localization;
using SenderNG.Core.Modules;
using SenderNG.Modules.Fixed;
using Xunit;

namespace SenderNG.Tests;

[Collection("LocalizationState")]
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
    {        var prev = LocalizationService.Current;
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
            var remove = buttons.SingleOrDefault(b => b.Classes.Contains("remove"))
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

                    // Computed (PropertyChanged-driven) labels follow the locale: the type
                    // button carries its verb, the remove glyph carries it in its tooltip.
                    Assert.Equal("Удалить этот профиль", ToolTip.GetTip(remove));
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
                    var revivedRemove = window.GetVisualDescendants().OfType<Button>()
                        .Single(b => b.Classes.Contains("remove"));
                    Assert.Equal("Удалить этот профиль", ToolTip.GetTip(revivedRemove));
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

    [AvaloniaFact]
    public async Task Backup_leaf_renders_its_localized_buttons()
    {
        // Regression guard: without a DataTemplate for BackupSettingsLeaf the detail pane
        // stays empty even though the leaf is selected.
        var prev = LocalizationService.Current;
        try
        {
            LocalizationService.Current = Loc;
            var settings = MakeSettings();
            var vm = new AppSettingsViewModel(settings, Array.Empty<IModule>(), Loc,
                fileDialogs: new StubFileDialogs());
            var leaf = ((AppSettingsGroupItem)vm.Groups[0]).Children.OfType<BackupSettingsLeaf>().Single();
            vm.SelectedNode = leaf;

            var view = new AppSettingsView { DataContext = vm };
            var window = new Window { Width = 920, Height = 560, Content = view };
            window.Show();
            for (var i = 0; i < 4; i++)
                await Pump();

            var buttons = window.GetVisualDescendants().OfType<Button>().ToList();
            var dump = string.Join(" | ", buttons.Select(b => b.Content is null ? "<null>" : $"\"{b.Content}\""));
            var backup = buttons.SingleOrDefault(b => Equals(b.Content, Loc.T("appSettings.backup.save")))
                ?? throw new InvalidOperationException($"Backup button not found. Buttons: {dump}");
            var restore = buttons.SingleOrDefault(b => Equals(b.Content, Loc.T("appSettings.backup.restore")))
                ?? throw new InvalidOperationException($"Restore button not found. Buttons: {dump}");
            Assert.True(backup.IsEnabled);
            Assert.True(restore.IsEnabled);

            // The folder path and the on-disk counts are rendered too.
            var summary = window.GetVisualDescendants().OfType<TextBlock>()
                .SingleOrDefault(t => Equals(t.Text, leaf.Summary))
                ?? throw new InvalidOperationException("Backup summary not rendered.");
            Assert.Contains(settings.SettingsDir, leaf.FolderText);
            Assert.True(summary.IsVisible);

            window.Close();
        }
        finally
        {
            LocalizationService.Current = prev;
        }
    }

    [AvaloniaFact]
    public async Task The_topology_picture_renders_beside_the_profile_list_and_follows_the_selection()
    {
        var prev = LocalizationService.Current;
        try
        {
            LocalizationService.Current = Loc;
            var settings = MakeSettings();
            var vm = new AppSettingsViewModel(settings, Array.Empty<IModule>(), Loc);
            var leaf = ((AppSettingsGroupItem)vm.Groups[0]).Children.OfType<ProfileSettingsLeaf>().Single();
            var foam = settings.CreateProfile("foam", MachineTypes.Foam);
            leaf.Refresh();
            vm.SelectedNode = leaf;

            var view = new AppSettingsView { DataContext = vm };
            var window = new Window { Width = 920, Height = 560, Content = view };
            window.Show();
            for (var i = 0; i < 4; i++)
                await Pump();

            // The left column is one dock panel: the add-profile row pinned under the
            // list, the picture spanning the whole column. Fixed 204 = 170 list + 10 gap
            // + 24 row; Uniform stretch derives the width from the type art's aspect
            // ratio (the mill art is 820x560, so ~299 wide at 204 tall), so height is
            // the dimension that must match.
            var list = window.GetVisualDescendants().OfType<ListBox>()
                .Single(l => l.ItemsSource is IEnumerable<ProfileRow>);
            var row = list.GetVisualAncestors().OfType<Grid>()
                .First(g => g.GetVisualDescendants().OfType<Image>().Any());
            var image = row.GetVisualDescendants().OfType<Image>().Single();
            Assert.Equal(204, row.Bounds.Height, 1);
            Assert.Equal(170, list.Bounds.Height, 1);
            Assert.Equal(row.Bounds.Height, image.Height, 1);
            Assert.Equal(row.Bounds.Height, image.Bounds.Height, 1);
            Assert.True(image.Bounds.Width > image.Bounds.Height,
                "the mill art is wider than it is tall; a narrower picture means the height was capped");

            // The add row really sits in that column under the list, and the picture
            // reaches down to its bottom edge — that two-column alignment is the point.
            var add = window.GetVisualDescendants().OfType<TextBox>()
                .Single(t => Equals(t.PlaceholderText, Loc.T("appSettings.profiles.newName")));
            double Top(Visual v) => v.TranslatePoint(new Point(0, 0), window)!.Value.Y;
            double Bottom(Visual v) => v.TranslatePoint(new Point(0, v.Bounds.Height), window)!.Value.Y;
            Assert.Equal(10, Top(add) - Bottom(list), 1);
            Assert.Equal(Bottom(add), Bottom(image), 1);
            Assert.Equal(Top(list), Top(image), 1);
            Assert.True(Top(add) > Bottom(list), "the add row belongs under the list, not beside it");
            Assert.True(add.TranslatePoint(new Point(add.Bounds.Width, 0), window)!.Value.X
                    < image.TranslatePoint(new Point(0, 0), window)!.Value.X,
                "the add row's controls stay left of the picture column");

            // It resolves through avares://SenderNG/Assets/... — the one thing that proves
            // the asset registration works at runtime (docs/images/ would not load at all).
            var bitmap = Assert.IsType<Bitmap>(image.Source);
            Assert.True(bitmap.PixelSize.Width > 0 && bitmap.PixelSize.Height > 0,
                "the topology art decoded to an empty bitmap");
            Assert.EndsWith("machine-topology-mill.png", leaf.TopologyImage!);

            // Selecting another profile swaps the picture in place.
            leaf.SelectedProfile = leaf.Profiles.Single(p => p.Id == foam.Id);
            for (var i = 0; i < 4; i++)
                await Pump();

            Assert.EndsWith("machine-topology-foam.png", leaf.TopologyImage!);
            Assert.NotSame(bitmap, image.Source);
            Assert.True(Assert.IsType<Bitmap>(image.Source).PixelSize.Width > 0);

            window.Close();
        }
        finally
        {
            LocalizationService.Current = prev;
        }
    }

    [AvaloniaFact]
    public async Task A_profile_row_does_not_move_when_it_becomes_the_selected_one()
    {
        // The pencil only exists on the selected row, so the row's content used to grow
        // with selection (16px → 20px): the name and the machine badge visibly dropped a
        // couple of pixels the moment a row was picked. The row now reserves its height.
        var prev = LocalizationService.Current;
        try
        {
            LocalizationService.Current = Loc;
            var settings = MakeSettings();
            var vm = new AppSettingsViewModel(settings, Array.Empty<IModule>(), Loc);
            var leaf = ((AppSettingsGroupItem)vm.Groups[0]).Children.OfType<ProfileSettingsLeaf>().Single();
            settings.CreateProfile("Foam bench", MachineTypes.Foam);
            leaf.Refresh();
            vm.SelectedNode = leaf;

            var view = new AppSettingsView { DataContext = vm };
            var window = new Window { Width = 920, Height = 560, Content = view };
            window.Show();
            for (var i = 0; i < 4; i++)
                await Pump();

            var row = leaf.Profiles.Single(p => p.Id == "foam-bench");
            double Offset(Visual v) => v.TranslatePoint(new Point(0, 0), window)!.Value.Y;
            var rowPanel = window.GetVisualDescendants().OfType<StackPanel>()
                .Single(p => ReferenceEquals(p.DataContext, row) && p.Classes.Contains("row"));
            var name = rowPanel.GetVisualDescendants().OfType<TextBlock>()
                .Single(t => Equals(t.Text, "Foam bench"));
            // The badge is the border whose child is the type label, not the item's own chrome.
            var badge = rowPanel.GetVisualDescendants().OfType<Border>()
                .Single(b => b.GetVisualDescendants().OfType<TextBlock>()
                    .Any(t => Equals(t.Text, "Foam")));

            var panelBefore = Offset(rowPanel);
            var nameBefore = Offset(name);
            var badgeBefore = Offset(badge);

            leaf.SelectedProfile = row;
            for (var i = 0; i < 4; i++)
                await Pump();

            Assert.True(row.IsSelected);
            Assert.Equal(panelBefore, Offset(rowPanel), 1);
            Assert.Equal(nameBefore, Offset(name), 1);
            Assert.Equal(badgeBefore, Offset(badge), 1);

            window.Close();
        }
        finally
        {
            LocalizationService.Current = prev;
        }
    }

    [AvaloniaFact]
    public async Task The_row_actions_are_pictograms_with_their_own_tooltips()
    {
        var prev = LocalizationService.Current;
        try
        {
            LocalizationService.Current = Loc;
            var settings = MakeSettings();
            var vm = new AppSettingsViewModel(settings, Array.Empty<IModule>(), Loc);
            var leaf = ((AppSettingsGroupItem)vm.Groups[0]).Children.OfType<ProfileSettingsLeaf>().Single();
            settings.CreateProfile("second", MachineTypes.Mill);
            leaf.Refresh();
            vm.SelectedNode = leaf;

            var view = new AppSettingsView { DataContext = vm };
            var window = new Window { Width = 920, Height = 560, Content = view };
            window.Show();
            for (var i = 0; i < 4; i++)
                await Pump();

            var row = leaf.Profiles.Single(p => p.Id == "default");
            // The rename hint lives on the pencil glyph alone now: the row itself carries
            // no tip, and every action explains itself.
            var rowPanel = window.GetVisualDescendants().OfType<StackPanel>()
                .Single(p => ReferenceEquals(p.DataContext, row) && p.Classes.Contains("row"));
            Assert.Null(ToolTip.GetTip(rowPanel));

            // Every row action is on the row itself, and only on the selected one: the
            // strip that used to sit under the list acted on whatever was selected.
            var actions = rowPanel.GetVisualDescendants().OfType<Button>()
                .Where(b => b.Classes.Contains("rowact")).ToList();
            Assert.Equal(4, actions.Count);                       // rename, activate, duplicate, remove
            var rename = actions.Single(b => b.Classes.Contains("pencil"));
            var activate = actions.Single(b => b.Classes.Contains("activate"));
            var duplicate = actions.Single(b => b.Classes.Contains("duplicate"));
            var remove = actions.Single(b => b.Classes.Contains("remove"));

            // All four are pictograms now; the words live in the tooltips.
            Assert.Equal("\u270E", rename.Content);
            Assert.Equal("\u2713", activate.Content);
            Assert.Equal("\u2750", duplicate.Content);
            Assert.Equal("\u2715", remove.Content);
            Assert.Equal(Loc.T("appSettings.profiles.renameHint"), ToolTip.GetTip(rename));
            Assert.Equal(row.ActivateHint, ToolTip.GetTip(activate));
            Assert.Equal(row.DuplicateHint, ToolTip.GetTip(duplicate));
            Assert.Equal(row.RemoveHint, ToolTip.GetTip(remove));

            // This is the ACTIVE profile: re-activating and deleting it are refused, so
            // those buttons are hidden rather than greyed (the context-menu test pins the
            // disabled items that keep the full set visible).
            Assert.True(rename.IsVisible);
            Assert.False(activate.IsVisible);
            Assert.True(duplicate.IsVisible);
            Assert.False(remove.IsVisible);

            // The actions are chrome-less so the hover wash cannot draw boxes in the row;
            // the same template background is pinned for the pointerover/pressed states by
            // the view's styles.
            Assert.All(actions, b => Assert.Equal(new Thickness(0), b.BorderThickness));
            Assert.All(actions, b => Assert.Equal(Colors.Transparent,
                Assert.IsAssignableFrom<ISolidColorBrush>(b.Background).Color));

            // Selecting another row moves the whole set with the selection.
            var other = leaf.Profiles.Single(p => p.Id == "second");
            leaf.SelectedProfile = other;
            for (var i = 0; i < 4; i++)
                await Pump();
            Assert.All(actions, b => Assert.False(b.IsVisible));
            var otherActions = window.GetVisualDescendants().OfType<Button>()
                .Where(b => b.Classes.Contains("rowact") && ReferenceEquals(b.DataContext, other)).ToList();
            Assert.All(otherActions, b => Assert.True(b.IsVisible));

            // On a row that can be activated the check is black — an offer to become
            // active, not the green mark the active row itself wears.
            var otherActivate = otherActions.Single(b => b.Classes.Contains("activate"));
            Assert.Equal(Colors.Black,
                Assert.IsAssignableFrom<ISolidColorBrush>(otherActivate.Foreground).Color);

            static ContentPresenter FillOf(Button b) =>
                b.GetVisualDescendants().OfType<ContentPresenter>().First();

            // The delete is two-step: the first click arms the row — the glyph turns white
            // on the red fill, a border appears, the tooltip becomes the ask for the
            // confirming click — and deletes nothing.
            var otherRemove = otherActions.Single(b => b.Classes.Contains("remove"));
            // The delete stands a little apart from the rest of the set (extra left
            // margin): a mis-aimed click on its neighbour must not arm the destructive
            // one. The other three keep the plain Spacing="6" gaps.
            Assert.Equal(new Thickness(8, 0, 0, 0), otherRemove.Margin);
            otherRemove.Command!.Execute(null);
            for (var i = 0; i < 4; i++)
                await Pump();
            Assert.True(other.RemoveArmed);
            Assert.Equal(2, leaf.Profiles.Count);
            Assert.Equal(Color.FromRgb(0xAA, 0x33, 0x33),
                Assert.IsAssignableFrom<ISolidColorBrush>(FillOf(otherRemove).Background).Color);
            Assert.Equal(Colors.White,
                Assert.IsAssignableFrom<ISolidColorBrush>(otherRemove.Foreground).Color);
            Assert.Equal(new Thickness(1), otherRemove.BorderThickness);
            Assert.Equal(Loc.T("appSettings.profiles.removeConfirmHint"), ToolTip.GetTip(otherRemove));

            // The pointer usually still sits on the button when it arms; hovering must keep
            // the fill red (the armed rule is defined after the pointerover rule) instead
            // of letting the themed wash repaint it.
            var center = otherRemove.TranslatePoint(
                new Point(otherRemove.Bounds.Width / 2, otherRemove.Bounds.Height / 2), window)!.Value;
            window.MouseMove(center, RawInputModifiers.None);
            for (var i = 0; i < 4; i++)
                await Pump();
            Assert.True(otherRemove.IsPointerOver);
            Assert.Equal(Color.FromRgb(0xAA, 0x33, 0x33),
                Assert.IsAssignableFrom<ISolidColorBrush>(FillOf(otherRemove).Background).Color);

            // The second click is the destructive one.
            otherRemove.Command!.Execute(null);
            for (var i = 0; i < 4; i++)
                await Pump();
            Assert.DoesNotContain(leaf.Profiles, p => p.Id == "second");

            window.Close();
        }
        finally
        {
            LocalizationService.Current = prev;
        }
    }

    [AvaloniaFact]
    public async Task The_row_context_menu_keeps_every_action_disabled_where_refused()
    {
        var prev = LocalizationService.Current;
        try
        {
            LocalizationService.Current = Loc;
            var settings = MakeSettings();
            var vm = new AppSettingsViewModel(settings, Array.Empty<IModule>(), Loc);
            var leaf = ((AppSettingsGroupItem)vm.Groups[0]).Children.OfType<ProfileSettingsLeaf>().Single();
            settings.CreateProfile("second", MachineTypes.Mill);
            leaf.Refresh();
            vm.SelectedNode = leaf;

            var view = new AppSettingsView { DataContext = vm };
            var window = new Window { Width = 920, Height = 560, Content = view };
            window.Show();
            for (var i = 0; i < 4; i++)
                await Pump();

            var row = leaf.Profiles.Single(p => p.Id == "default"); // the active one
            var rowPanel = window.GetVisualDescendants().OfType<StackPanel>()
                .Single(p => ReferenceEquals(p.DataContext, row) && p.Classes.Contains("row"));
            var menu = rowPanel.ContextMenu;
            Assert.NotNull(menu);
            menu!.Open(rowPanel);
            for (var i = 0; i < 4; i++)
                await Pump();

            var items = menu.Items.OfType<MenuItem>().ToList();
            Assert.Equal(
                new[] { "rename", "activate", "duplicate", "remove" }
                    .Select(k => Loc.T($"appSettings.profiles.{k}")),
                items.Select(m => m.Header as string));
            // The full set is present on the ACTIVE profile, with the refused commands
            // disabled rather than omitted — the row buttons hide them instead.
            Assert.True(items[0].IsEnabled);                       // rename
            Assert.False(items[1].IsEnabled);                      // activate: already active
            Assert.True(items[2].IsEnabled);                       // duplicate
            Assert.False(items[3].IsEnabled);                      // remove: the active profile
            menu.Close();
            for (var i = 0; i < 4; i++)
                await Pump();

            // On a row where every action is possible the menu is fully enabled.
            var other = leaf.Profiles.Single(p => p.Id == "second");
            var otherPanel = window.GetVisualDescendants().OfType<StackPanel>()
                .Single(p => ReferenceEquals(p.DataContext, other) && p.Classes.Contains("row"));
            var otherMenu = otherPanel.ContextMenu;
            Assert.NotNull(otherMenu);
            otherMenu!.Open(otherPanel);
            for (var i = 0; i < 4; i++)
                await Pump();
            Assert.All(otherMenu.Items.OfType<MenuItem>(), m => Assert.True(m.IsEnabled));
            otherMenu.Close();

            window.Close();
        }
        finally
        {
            LocalizationService.Current = prev;
        }
    }

    [AvaloniaFact]
    public async Task Removing_from_the_context_menu_takes_the_same_two_clicks()
    {
        // Delete is two-step everywhere: the first click arms, the second deletes. A menu
        // that closed after the arming click would force the operator to right-click and
        // aim again just to confirm, so the delete entry stays open on click — it is the
        // only one that does.
        var prev = LocalizationService.Current;
        try
        {
            LocalizationService.Current = Loc;
            var settings = MakeSettings();
            var vm = new AppSettingsViewModel(settings, Array.Empty<IModule>(), Loc);
            var leaf = ((AppSettingsGroupItem)vm.Groups[0]).Children.OfType<ProfileSettingsLeaf>().Single();
            settings.CreateProfile("second", MachineTypes.Mill);
            leaf.Refresh();
            vm.SelectedNode = leaf;

            var view = new AppSettingsView { DataContext = vm };
            var window = new Window { Width = 920, Height = 560, Content = view };
            window.Show();
            for (var i = 0; i < 4; i++)
                await Pump();

            var other = leaf.Profiles.Single(p => p.Id == "second");
            var otherPanel = window.GetVisualDescendants().OfType<StackPanel>()
                .Single(p => ReferenceEquals(p.DataContext, other) && p.Classes.Contains("row"));
            var menu = otherPanel.ContextMenu!;
            menu.Open(otherPanel);
            for (var i = 0; i < 4; i++)
                await Pump();

            var items = menu.Items.OfType<MenuItem>().ToList();
            var removeItem = items[3];
            Assert.True(removeItem.StaysOpenOnClick,
                "the delete entry must keep the menu open for its confirming click");
            Assert.All(items.Where(m => !ReferenceEquals(m, removeItem)),
                m => Assert.False(m.StaysOpenOnClick));
            Assert.Equal(Loc.T("appSettings.profiles.removeHint"), ToolTip.GetTip(removeItem));

            // A real click runs the item's command through MenuItem's own class handler;
            // raising Click is that same path (headless has no pointer on the popup). Manually
            // executing the command as well would double-invoke it and delete on the first click.
            removeItem.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent, removeItem));
            for (var i = 0; i < 4; i++)
                await Pump();
            Assert.True(other.RemoveArmed);
            Assert.Contains(leaf.Profiles, p => p.Id == "second");
            Assert.True(menu.IsOpen, "the first, arming click must not close the menu");

            // The armed entry says what the next click will do, the same way the button
            // does: the fill reddens, the text goes white and the tooltip flips to the
            // confirm ask — the glyph alone cannot carry the two-step.
            Assert.Equal(Loc.T("appSettings.profiles.removeConfirmHint"), ToolTip.GetTip(removeItem));
            var armedFill = removeItem.GetVisualDescendants().OfType<Border>()
                .Single(b => b.Name == "PART_LayoutRoot");
            Assert.Equal(Color.FromRgb(0xAA, 0x33, 0x33),
                Assert.IsAssignableFrom<ISolidColorBrush>(armedFill.Background).Color);
            var armedText = removeItem.GetVisualDescendants().OfType<ContentPresenter>()
                .Single(p => p.Name == "PART_HeaderPresenter");
            Assert.Equal(Colors.White,
                Assert.IsAssignableFrom<ISolidColorBrush>(armedText.Foreground).Color);

            // In the real session the pointer still rests on the armed item, which wears
            // the flyout's :selected wash; the armed red must outrank it instead of
            // greying out under the pointer that is about to confirm.
            removeItem.IsSelected = true;
            for (var i = 0; i < 4; i++)
                await Pump();
            Assert.True(removeItem.Classes.Contains(":selected"),
                "a vacuous pass: the item must wear :selected for this to test the wash");
            Assert.Equal(Color.FromRgb(0xAA, 0x33, 0x33),
                Assert.IsAssignableFrom<ISolidColorBrush>(armedFill.Background).Color);
            Assert.Equal(Colors.White,
                Assert.IsAssignableFrom<ISolidColorBrush>(armedText.Foreground).Color);

            // The second click is the destructive one, and the menu closes with its row.
            removeItem.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent, removeItem));
            for (var i = 0; i < 4; i++)
                await Pump();
            Assert.DoesNotContain(leaf.Profiles, p => p.Id == "second");
            Assert.False(menu.IsOpen);

            window.Close();
        }
        finally
        {
            LocalizationService.Current = prev;
        }
    }

    [AvaloniaFact]
    public async Task Double_clicking_a_profile_name_opens_the_inline_rename_editor()
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

            var row = leaf.Profiles.Single(p => p.Id == "default");
            var name = window.GetVisualDescendants().OfType<TextBlock>()
                .First(t => t.DataContext == row && Equals(t.Text, "Default"));

            // The standalone rename box is gone; the row edits in place.
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<TextBox>(),
                t => Equals(t.Text, "Default") && !t.Classes.Contains("rename"));
            Assert.True(name.IsVisible);

            // The row's own command is what the pencil and the F2/double-click path call;
            // the gesture wiring itself is Avalonia's DoubleTapped handler.
            row.BeginRenameCommand.Execute(null);
            for (var i = 0; i < 4; i++)
                await Pump();

            var box = window.GetVisualDescendants().OfType<TextBox>()
                .Single(t => t.Classes.Contains("rename") && t.DataContext == row);
            Assert.True(box.IsVisible);
            Assert.Equal("Default", box.Text);
            Assert.False(name.IsVisible);

            // Committing through the row's own handler persists the name.
            leaf.UpdateRename(row, "Bench mill");
            leaf.CommitRename(row);
            for (var i = 0; i < 4; i++)
                await Pump();

            Assert.Equal("Bench mill", new SettingsService(settings.SettingsDir).Load().Name);
            Assert.False(leaf.Profiles.Single(p => p.Id == "default").IsEditing);

            window.Close();
        }
        finally
        {
            LocalizationService.Current = prev;
        }
    }

    [AvaloniaFact]
    public async Task Rename_from_the_pencil_or_the_context_menu_focuses_and_selects_the_name()
    {
        // Both the pencil and the context menu start the edit through the row's command,
        // which is view-less: the box opened, but focus stayed where it was and the whole
        // name was unselected, so the first keystroke went to the wrong place. The view
        // now lands the box focused and fully selected — like double-click.
        var prev = LocalizationService.Current;
        try
        {
            LocalizationService.Current = Loc;
            var settings = MakeSettings();
            var vm = new AppSettingsViewModel(settings, Array.Empty<IModule>(), Loc);
            var leaf = ((AppSettingsGroupItem)vm.Groups[0]).Children.OfType<ProfileSettingsLeaf>().Single();
            settings.CreateProfile("second", MachineTypes.Mill);
            leaf.Refresh();
            vm.SelectedNode = leaf;

            var view = new AppSettingsView { DataContext = vm };
            var window = new Window { Width = 920, Height = 560, Content = view };
            window.Show();
            for (var i = 0; i < 4; i++)
                await Pump();

            // The pencil: a real click, the gesture the operator makes.
            var row = leaf.Profiles.Single(p => p.Id == "default");
            var rowPanel = window.GetVisualDescendants().OfType<StackPanel>()
                .Single(p => ReferenceEquals(p.DataContext, row) && p.Classes.Contains("row"));
            var pencil = rowPanel.GetVisualDescendants().OfType<Button>()
                .Single(b => b.Classes.Contains("pencil"));
            var center = pencil.TranslatePoint(
                new Point(pencil.Bounds.Width / 2, pencil.Bounds.Height / 2), window)!.Value;
            window.MouseMove(center, RawInputModifiers.None);
            window.MouseDown(center, MouseButton.Left, RawInputModifiers.None);
            window.MouseUp(center, MouseButton.Left, RawInputModifiers.None);
            for (var i = 0; i < 4; i++)
                await Pump();

            var box = window.GetVisualDescendants().OfType<TextBox>()
                .Single(t => t.Classes.Contains("rename") && t.DataContext == row);
            Assert.True(box.IsVisible);
            Assert.True(box.IsFocused, "the pencil must land the caret in the box");
            Assert.Equal(0, box.SelectionStart);
            Assert.Equal(box.Text!.Length, box.SelectionEnd);

            leaf.CancelRename();
            for (var i = 0; i < 4; i++)
                await Pump();

            // The context menu, on a row that is not even selected: the command's
            // select-the-row step must not break the focus hook. Executing the command
            // and raising Click mirrors what the framework does for a real menu click
            // (the popup itself is out of headless pointer reach); the close that
            // follows is the real click's other half — the view's focus hook hangs
            // off Closed because MenuItem's own class handler swallows Click.
            var other = leaf.Profiles.Single(p => p.Id == "second");
            var otherPanel = window.GetVisualDescendants().OfType<StackPanel>()
                .Single(p => ReferenceEquals(p.DataContext, other) && p.Classes.Contains("row"));
            var menu = otherPanel.ContextMenu!;
            menu.Open(otherPanel);
            for (var i = 0; i < 4; i++)
                await Pump();
            var renameItem = menu.Items.OfType<MenuItem>().First();
            renameItem.Command!.Execute(null);
            renameItem.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent, renameItem));
            menu.Close();
            for (var i = 0; i < 4; i++)
                await Pump();

            var otherBox = window.GetVisualDescendants().OfType<TextBox>()
                .Single(t => t.Classes.Contains("rename") && t.DataContext == other);
            Assert.True(otherBox.IsVisible);
            Assert.True(otherBox.IsFocused, "the context menu must land the caret in the box");
            Assert.Equal(0, otherBox.SelectionStart);
            Assert.Equal(otherBox.Text!.Length, otherBox.SelectionEnd);

            window.Close();
        }
        finally
        {
            LocalizationService.Current = prev;
        }
    }

    [AvaloniaFact]
    public async Task Clicking_another_row_finishes_the_open_rename_editor()
    {
        // The reported bug: start renaming a profile, click a different row, and the first
        // row stayed in edit mode. Clicking away is the natural "done" gesture.
        var prev = LocalizationService.Current;
        try
        {
            LocalizationService.Current = Loc;
            var settings = MakeSettings();
            var vm = new AppSettingsViewModel(settings, Array.Empty<IModule>(), Loc);
            var leaf = ((AppSettingsGroupItem)vm.Groups[0]).Children.OfType<ProfileSettingsLeaf>().Single();
            settings.CreateProfile("Foam bench", MachineTypes.Foam);
            leaf.Refresh();
            vm.SelectedNode = leaf;

            var view = new AppSettingsView { DataContext = vm };
            var window = new Window { Width = 920, Height = 560, Content = view };
            window.Show();
            for (var i = 0; i < 4; i++)
                await Pump();

            var list = window.GetVisualDescendants().OfType<ListBox>()
                .Single(l => l.ItemsSource is IEnumerable<ProfileRow>);
            var first = leaf.Profiles.Single(p => p.Id == "default");
            var second = leaf.Profiles.Single(p => p.Id == "foam-bench");
            leaf.SelectedProfile = first;
            leaf.BeginRename(first);
            leaf.UpdateRename(first, "Bench mill");
            for (var i = 0; i < 4; i++)
                await Pump();

            var box = window.GetVisualDescendants().OfType<TextBox>()
                .Single(t => t.Classes.Contains("rename") && t.DataContext == first);
            Assert.True(box.IsVisible);

            // The operator clicks the other row (this is what the ListBox writes).
            list.SelectedItem = second;
            for (var i = 0; i < 4; i++)
                await Pump();

            Assert.False(leaf.Profiles.Single(p => p.Id == "default").IsEditing);
            Assert.Equal("Bench mill", new SettingsService(settings.SettingsDir).Load().Name);
            Assert.Equal("foam-bench", leaf.SelectedProfile!.Id);
            // The live list has exactly one editor and exactly one pencil-bearing row.
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<TextBox>(),
                t => t.Classes.Contains("rename") && t.IsVisible);
            Assert.Single(leaf.Profiles, p => p.IsSelected);

            window.Close();
        }
        finally
        {
            LocalizationService.Current = prev;
        }
    }

    private sealed class StubFileDialogs : IFileDialogService
    {
        public Task<string?> OpenFileAsync(string title, params string[] filters) => Task.FromResult<string?>(null);
        public Task<string?> SaveFileAsync(string title, string suggestedFileName, params string[] filters)
            => Task.FromResult<string?>(null);
        public Task ShowErrorAsync(string title, string message) => Task.CompletedTask;
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