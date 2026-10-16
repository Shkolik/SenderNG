/*
 * MachineAxesViewHeadlessTests.cs - headless UI tests for the machine card's axis editor
 * (topology picture + per-plane axis rows + optional axes)
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
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CNC.Core.Services;
using CNC.Core.ViewModels;
using SenderNG.Core.Localization;
using SenderNG.Core.Modules;
using SenderNG.Modules.Fixed;
using Xunit;

namespace SenderNG.Tests;

[Collection("LocalizationState")]
public class MachineAxesViewHeadlessTests
{
    // A private instance (not HeadlessTestCatalog.Locale): the {loc:Loc} strings the
    // card renders must resolve from the real catalog without touching a shared static.
    private static readonly LocalizationService Loc =
        LocalizationService.LoadFromDirectory(Path.Combine(AppContext.BaseDirectory, "Locale"));

    /// <summary>The five table columns, in the order the header must render them.</summary>
    private static readonly string[] HeaderKeys =
    [
        "appSettings.axes.index",
        "appSettings.axes.type",
        "appSettings.axes.role",
        "appSettings.axes.direction",
        "appSettings.axes.alias",
    ];

    private static SettingsService MakeSettings()
    {
        var dir = Path.Combine(Path.GetTempPath(), "senderng-axes-ui-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return new SettingsService(dir);
    }

    /// <summary>The rendered index pickers: one per axis row of the card.</summary>
    private static List<ComboBox> AxisPickers(Visual root, MachineAxesViewModel editor) =>
        root.GetVisualDescendants().OfType<ComboBox>()
            .Where(c => ReferenceEquals(c.ItemsSource, editor.IndexChoices))
            .ToList();

    /// <summary>
    /// The machine-type picture beside the profile list. Found through the list rather than
    /// by name: the row that holds both is the evidence for the layout, so a picture that
    /// drifted into another panel fails here instead of quietly passing.
    /// </summary>
    private static Image The_list_image(Visual root)
    {
        var list = root.GetVisualDescendants().OfType<ListBox>()
            .Single(l => l.ItemsSource is IEnumerable<ProfileRow>);
        // The nearest ancestor that actually holds a picture: the list's own row.
        var row = list.GetVisualAncestors().OfType<Grid>()
            .First(g => g.GetVisualDescendants().OfType<Image>().Any());
        return row.GetVisualDescendants().OfType<Image>().Single();
    }

    [AvaloniaFact]
    public async Task Renders_a_row_per_axis_and_loads_the_topology_picture_from_assets()
    {
        var prev = LocalizationService.Current;
        try
        {
            LocalizationService.Current = Loc;
            var settings = MakeSettings();
            var profile = settings.Load();
            var editor = new MachineAxesViewModel(settings, Loc.Resolve, profile.Id);

            var view = new MachineAxesView { DataContext = editor };
            var window = new Window { Width = 700, Height = 900, Content = view };
            window.Show();
            for (var i = 0; i < 4; i++)
                await Pump();

            // One row per axis: a mill's single plane holds three.
            Assert.Equal(3, AxisPickers(window, editor).Count);

            // …and the rows render their derived data: the localized plane title and the
            // board letter each row shows for its index (conventional when disconnected).
            var texts = view.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
            Assert.Contains(Loc.T("appSettings.axes.plane").Replace("{0}", "1"), texts);
            Assert.Contains("X", texts);

            // The card carries no picture at all any more: the machine-type topology art
            // moved beside the profile list (AppSettingsView, where the asset registration
            // is proven instead), and the rotary reference art was already dropped.
            Assert.Empty(view.GetVisualDescendants().OfType<Image>());
            Assert.EndsWith("machine-topology-mill.png", editor.TopologyImage!);

            window.Close();
        }
        finally
        {
            LocalizationService.Current = prev;
        }
    }

    [AvaloniaFact]
    public async Task The_axis_table_header_reads_index_type_role_direction_cam_alias()
    {
        var prev = LocalizationService.Current;
        try
        {
            LocalizationService.Current = Loc;
            var settings = MakeSettings();
            var profile = settings.Load();
            var editor = new MachineAxesViewModel(settings, Loc.Resolve, profile.Id);

            // The exact English wording the header must carry…
            Assert.Equal(
                new[] { "Index", "Type", "Role", "Direction", "CAM Alias" },
                HeaderKeys.Select(Loc.T).ToArray());

            var view = new MachineAxesView { DataContext = editor };
            var window = new Window { Width = 900, Height = 900, Content = view };
            window.Show();
            for (var i = 0; i < 4; i++)
                await Pump();

            // …and the rendered order, left to right. The card draws the header over every
            // plane and over the optional list, so each column label appears more than once:
            // group by position and require one distinct x per column, ascending.
            var rendered = view.GetVisualDescendants().OfType<TextBlock>()
                .Where(t => t.Text is not null && HeaderKeys.Select(Loc.T).Contains(t.Text))
                .Select(t => (Text: t.Text!, X: Position(t, view).X))
                .GroupBy(h => Math.Round(h.X, 1))
                .OrderBy(g => g.Key)
                .Select(g => g.First().Text)
                .ToList();
            Assert.Equal(HeaderKeys.Select(Loc.T).ToArray(), rendered);

            window.Close();
        }
        finally
        {
            LocalizationService.Current = prev;
        }
    }

    [AvaloniaFact]
    public async Task Every_axis_row_lines_up_under_the_shared_column_header()
    {
        // The rows drift when a cell is not stretched: Fluent leaves a ComboBox
        // HorizontalAlignment=Left, so it sized itself to its own text and every row's
        // controls ended at a different x. The columns are shared now, and the editors fill
        // them, so each column has exactly one left edge and one width across the card.
        var prev = LocalizationService.Current;
        try
        {
            LocalizationService.Current = Loc;
            var settings = MakeSettings();
            var foam = settings.CreateProfile("foam", MachineTypes.Foam);
            settings.Activate(foam.Id);
            var editor = new MachineAxesViewModel(settings, Loc.Resolve, foam.Id);
            editor.AddAxisCommand.Execute(null);              // an optional row…
            editor.OptionalAxes[0].ApplyKind(AxisKind.Rotary); // …with its pivot row, too

            var view = new MachineAxesView { DataContext = editor };
            var window = new Window { Width = 1000, Height = 1000, Content = view };
            window.Show();
            for (var i = 0; i < 4; i++)
                await Pump();

            // One cell of each row's own axis-table grid, by column: the row grid is the
            // first Grid above the row's index picker, so a control from a picker's own
            // template can never be mistaken for a cell.
            var rows = editor.Planes.SelectMany(p => p.Axes).Concat(editor.OptionalAxes).ToList();
            Assert.Equal(5, rows.Count);
            var cells = new List<(int Column, Control Control)>();
            foreach (var row in rows)
            {
                var grid = GridOf(view, row, editor.IndexChoices);
                cells.AddRange(grid.Children.Select(c => (Grid.GetColumn(c), c)));
            }

            (int Column, string Key, Type Editor)[] columns =
            [
                (1, "appSettings.axes.index", typeof(ComboBox)),
                (2, "appSettings.axes.type", typeof(ComboBox)),
                (3, "appSettings.axes.role", typeof(ComboBox)),
                (4, "appSettings.axes.direction", typeof(CheckBox)),
                (5, "appSettings.axes.alias", typeof(TextBox)),
            ];

            var previousX = double.NegativeInfinity;
            foreach (var (column, key, editorType) in columns)
            {
                var editors = cells.Where(c => c.Column == column).Select(c => c.Control).ToList();
                Assert.Equal(5, editors.Count);
                Assert.All(editors, c => Assert.IsType(editorType, c));

                var edges = editors.Select(c => Math.Round(Position(c, view).X, 1)).Distinct().ToList();
                var edge = Assert.Single(edges);

                // Every header copy sits over the same edge, so the labels sit over the controls.
                var headers = view.GetVisualDescendants().OfType<TextBlock>()
                    .Where(t => t.Text == Loc.T(key)).ToList();
                Assert.NotEmpty(headers);
                Assert.All(headers, h => Assert.Equal(edge, Math.Round(Position(h, view).X, 1)));

                // …and the column is one width, not one width per row.
                Assert.Single(editors.Select(c => Math.Round(c.Bounds.Width, 1)).Distinct());

                Assert.True(edge > previousX, $"column {key} is not to the right of the previous one");
                previousX = edge;
            }

            // The delete button shares the row's baseline column arithmetic: it sits in the
            // column right after the CAM alias, not out at the card's far edge.
            var optional = GridOf(view, editor.OptionalAxes[0], editor.IndexChoices);
            var aliasRight = Math.Round(Position(
                optional.Children.Single(c => Grid.GetColumn(c) == 5), view).X, 1);
            var del = view.GetVisualDescendants().OfType<Button>()
                .Single(b => ReferenceEquals(b.Content, editor.OptionalAxes[0].RemoveText));
            Assert.True(Position(del, view).X > aliasRight);

            window.Close();
        }
        finally
        {
            LocalizationService.Current = prev;
        }
    }

    [AvaloniaFact]
    public async Task The_optional_axes_area_renders_no_rotary_picture()
    {
        var prev = LocalizationService.Current;
        try
        {
            LocalizationService.Current = Loc;
            var settings = MakeSettings();
            var foam = settings.CreateProfile("foam", MachineTypes.Foam);
            settings.Activate(foam.Id);
            var editor = new MachineAxesViewModel(settings, Loc.Resolve, foam.Id);
            editor.AddAxisCommand.Execute(null);
            editor.OptionalAxes[0].ApplyKind(AxisKind.Rotary);

            var view = new MachineAxesView { DataContext = editor };
            var window = new Window { Width = 900, Height = 1000, Content = view };
            window.Show();
            for (var i = 0; i < 4; i++)
                await Pump();

            // The optional-axis section is the block that owns the "Optional axes" title:
            // no image may live in it (the rotary art was unreadable at that size and it
            // squeezed the axis table).
            var section = view.GetVisualDescendants().OfType<TextBlock>()
                .First(t => t.Text == Loc.T("appSettings.axes.optional"))
                .GetVisualAncestors().OfType<StackPanel>().First();
            Assert.Empty(section.GetVisualDescendants().OfType<Image>());
            // The card itself is picture-free; the type's art lives beside the profile list.
            Assert.Empty(view.GetVisualDescendants().OfType<Image>());

            window.Close();
        }
        finally
        {
            LocalizationService.Current = prev;
        }
    }

    [AvaloniaFact]
    public async Task The_inverted_checkbox_reflects_and_commits_the_sign()
    {
        // The Direction column's checkbox replaces the old [+]/[−] toggle: checked means the
        // axis counts negative. The stored Sign keeps its +1/−1 semantics.
        var prev = LocalizationService.Current;
        try
        {
            LocalizationService.Current = Loc;
            var settings = MakeSettings();
            var dir = settings.SettingsDir;
            var profile = settings.Load();
            var editor = new MachineAxesViewModel(settings, Loc.Resolve, profile.Id);
            editor.AddAxisCommand.Execute(null);
            var row = Assert.Single(editor.OptionalAxes);
            Assert.Equal(1, row.Sign);

            var view = new MachineAxesView { DataContext = editor };
            var window = new Window { Width = 900, Height = 900, Content = view };
            window.Show();
            for (var i = 0; i < 4; i++)
                await Pump();

            var box = Assert.Single(view.GetVisualDescendants().OfType<CheckBox>(),
                c => ReferenceEquals(c.DataContext, row));

            // Localized label, and the state it reflects today.
            Assert.Equal(Loc.T("appSettings.axes.inverted"), box.Content);
            Assert.False(box.IsChecked);

            // Click it: the gesture's state is committed to Sign and persisted.
            box.IsChecked = true;
            box.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, box));
            for (var i = 0; i < 3; i++)
                await Pump();

            Assert.Equal(-1, row.Sign);
            Assert.True(box.IsChecked);
            Assert.Equal(-1, new SettingsService(dir).Load().OptionalAxes[0].Sign);

            // …and back: unchecked means +1 again.
            box.IsChecked = false;
            box.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, box));
            for (var i = 0; i < 3; i++)
                await Pump();

            Assert.Equal(1, row.Sign);
            Assert.False(box.IsChecked);
            Assert.Equal(1, new SettingsService(dir).Load().OptionalAxes[0].Sign);

            window.Close();
        }
        finally
        {
            LocalizationService.Current = prev;
        }
    }

    [AvaloniaFact]
    public async Task Changing_a_rotary_rows_role_re_letters_the_pivot_labels_live()
    {
        // Invariant 7's labels follow the row's role without a reload. The mill plane maps
        // Width/Length/Height to X/Y/Z, so a rotary axis on Width shows the letters of
        // Length and Height (Y, Z); moving it to Length must re-letter the pair (X, Z).
        var prev = LocalizationService.Current;
        try
        {
            LocalizationService.Current = Loc;
            var settings = MakeSettings();
            var profile = settings.Load();
            var editor = new MachineAxesViewModel(settings, Loc.Resolve, profile.Id);
            editor.AddAxisCommand.Execute(null);
            var row = Assert.Single(editor.OptionalAxes);
            row.ApplyKind(AxisKind.Rotary);
            row.ApplyDirection(AxisDirection.Width);
            Assert.Equal(new[] { "Y", "Z" }, row.Pivots.Select(p => p.Label).ToArray());

            var view = new MachineAxesView { DataContext = editor };
            var window = new Window { Width = 900, Height = 900, Content = view };
            window.Show();
            for (var i = 0; i < 4; i++)
                await Pump();

            Assert.Equal(new[] { "Y", "Z" }, PivotLabels(view));

            // Through the Role picker, exactly as the user changes it.
            var role = view.GetVisualDescendants().OfType<ComboBox>().First(c =>
                ReferenceEquals(c.DataContext, row)
                && ReferenceEquals(c.ItemsSource, editor.DirectionChoices));
            role.SelectedItem = editor.DirectionChoices.Single(c => c.Value == AxisDirection.Length);
            for (var i = 0; i < 4; i++)
                await Pump();

            Assert.Equal(AxisDirection.Length, row.Direction);
            Assert.Equal(new[] { "X", "Z" }, row.Pivots.Select(p => p.Label).ToArray());
            // The rendered labels followed — no reload, no re-selection of the profile.
            Assert.Equal(new[] { "X", "Z" }, PivotLabels(view));

            window.Close();
        }
        finally
        {
            LocalizationService.Current = prev;
        }
    }

    [AvaloniaFact]
    public async Task A_rotary_row_renders_exactly_two_pivot_inputs_and_the_separation_field()
    {
        var prev = LocalizationService.Current;
        try
        {
            LocalizationService.Current = Loc;
            var settings = MakeSettings();
            var foam = settings.CreateProfile("foam", MachineTypes.Foam);
            settings.Activate(foam.Id);
            var editor = new MachineAxesViewModel(settings, Loc.Resolve, foam.Id);

            // A linear optional axis has no pivot inputs…
            editor.AddAxisCommand.Execute(null);
            var rotaryRow = Assert.Single(editor.OptionalAxes);
            Assert.Empty(rotaryRow.Pivots);

            // …a rotary one has exactly the two perpendicular coordinates.
            rotaryRow.ApplyKind(AxisKind.Rotary);
            rotaryRow.ApplyDirection(AxisDirection.Height);
            Assert.Equal(2, rotaryRow.Pivots.Count);

            var view = new MachineAxesView { DataContext = editor };
            var window = new Window { Width = 700, Height = 1000, Content = view };
            window.Show();
            for (var i = 0; i < 4; i++)
                await Pump();

            Assert.Equal(2, PivotBoxes(window).Count);

            // Two planes of two axes plus the optional one.
            Assert.Equal(5, AxisPickers(window, editor).Count);

            // Multi-plane only: the wire length is rendered and visible, with the
            // preset's value.
            var separation = Assert.Single(SeparationBoxes(window, editor));
            Assert.True(separation.IsEffectivelyVisible);
            // The foam preset's own default (200 mm); pinned in
            // MachineAxisSchemaTests as the canonical golden value.
            Assert.Equal("200", separation.Text);

            window.Close();
        }
        finally
        {
            LocalizationService.Current = prev;
        }
    }

    [AvaloniaFact]
    public async Task A_single_plane_machine_renders_no_separation_field()
    {
        var prev = LocalizationService.Current;
        try
        {
            LocalizationService.Current = Loc;
            var settings = MakeSettings();
            var profile = settings.Load();
            var editor = new MachineAxesViewModel(settings, Loc.Resolve, profile.Id);
            Assert.False(editor.HasSeparation);

            var view = new MachineAxesView { DataContext = editor };
            var window = new Window { Width = 700, Height = 900, Content = view };
            window.Show();
            for (var i = 0; i < 4; i++)
                await Pump();

            // A single-plane machine must not offer a plane-to-plane distance: the field is
            // realized but hidden (the panel keeps its children; only visibility changes).
            Assert.False(Assert.Single(SeparationBoxes(window, editor)).IsEffectivelyVisible);
            window.Close();
        }
        finally
        {
            LocalizationService.Current = prev;
        }
    }

    [AvaloniaFact]
    public async Task Switching_the_selected_profile_writes_nothing_through_a_recycled_row()
    {        // The recycled-control regression (docs/avalonia-gotchas.md §1): the axis rows'
        // containers are reused when the card re-points at another profile. A recycled
        // ComboBox raises SelectionChanged with the PREVIOUS row's selection while its
        // DataContext is already the new row; the handlers must write nothing then.
        var prev = LocalizationService.Current;
        try
        {
            LocalizationService.Current = Loc;
            var settings = MakeSettings();
            var vm = new AppSettingsViewModel(settings, Array.Empty<IModule>(), Loc);
            var leaf = ((AppSettingsGroupItem)vm.Groups[0]).Children.OfType<ProfileSettingsLeaf>().Single();
            vm.SelectedNode = leaf;

            var millId = settings.Global.ActiveProfileId;
            // Give the mill profile a real, self-describing layout on disk, so a stray
            // write from a recycled row would be visible below.
            var millProfile = settings.Load();
            millProfile.Planes = MachineTypeCatalog.Resolve(MachineTypes.Mill).CreateDefaultPlanes();
            settings.Save(millProfile);
            var foam = settings.CreateProfile("foam", MachineTypes.Foam);
            leaf.Refresh();

            var view = new AppSettingsView { DataContext = vm };
            var window = new Window { Width = 1000, Height = 900, Content = view };
            window.Show();
            for (var i = 0; i < 6; i++)
                await Pump();

            var axesView = window.GetVisualDescendants().OfType<MachineAxesView>().Single();
            // The machine-type picture now sits beside the profile list, not in the card.
            var listImage = The_list_image(window);
            var before = listImage.Source;
            Assert.NotNull(before);
            Assert.Equal(3, AxisPickers(axesView, leaf.MachineAxes).Count);

            leaf.SelectedProfile = leaf.Profiles.Single(p => p.Id == foam.Id);
            for (var i = 0; i < 6; i++)
                await Pump();

            // The card followed the selection, and the list's topology art was reloaded.
            Assert.Equal(MachineTypes.Foam, leaf.MachineAxes.MachineTypeId);
            Assert.Equal(4, AxisPickers(axesView, leaf.MachineAxes).Count);
            var after = listImage.Source;
            Assert.IsType<Bitmap>(after);
            Assert.NotSame(before, after);

            // Nothing was written through a recycled control: neither profile moved.
            var profiles = new SettingsService(settings.SettingsDir).AllProfiles();
            var mill = profiles.Single(p => p.Id == millId);
            Assert.Equal(new[] { 0, 1, 2 }, mill.Planes.Single().Axes.Select(a => a.Index).ToArray());
            Assert.Empty(mill.OptionalAxes);
            // The role bindings too, not just the indices: a recycled direction combo
            // would have moved an axis out of the mill's base plane.
            Assert.Equal(
                MachineTypeCatalog.Resolve(MachineTypes.Mill).BasePlanes[0],
                mill.Planes.Single().Axes.Select(a => a.Direction).ToArray());
            // …and no recycled Inverted checkbox flipped a mill axis's sign.
            Assert.All(mill.Planes.Single().Axes, a => Assert.Equal(1, a.Sign));

            var foamOnDisk = profiles.Single(p => p.Id == foam.Id);
            Assert.Equal(new[] { 2, 2 }, foamOnDisk.Planes.Select(p => p.Axes.Count).ToArray());
            Assert.Equal(new[] { 0, 1, 2, 3 },
                foamOnDisk.Planes.SelectMany(p => p.Axes).Select(a => a.Index).ToArray());
            Assert.Empty(foamOnDisk.OptionalAxes);
            Assert.Equal(200d, foamOnDisk.Separation);

            window.Close();
        }
        finally
        {
            LocalizationService.Current = prev;
        }
    }

    [AvaloniaFact]
    public async Task A_row_edit_commits_through_the_deferred_guarded_handler()
    {
        // The other half of the recycling guard: a change that really differs from the row's
        // model must still land (a handler that only ever ignored events would pass the
        // regression test above for the wrong reason).
        var prev = LocalizationService.Current;
        try
        {
            LocalizationService.Current = Loc;
            var settings = MakeSettings();
            var profile = settings.Load();
            var editor = new MachineAxesViewModel(settings, Loc.Resolve, profile.Id);
            editor.AddAxisCommand.Execute(null);
            var row = Assert.Single(editor.OptionalAxes);
            Assert.Equal(3, row.Index);

            var view = new MachineAxesView { DataContext = editor };
            var window = new Window { Width = 700, Height = 900, Content = view };
            window.Show();
            for (var i = 0; i < 4; i++)
                await Pump();

            var picker = view.GetVisualDescendants().OfType<ComboBox>().First(c =>
                ReferenceEquals(c.DataContext, row) && ReferenceEquals(c.ItemsSource, editor.IndexChoices));
            picker.SelectedItem = editor.IndexChoices.Single(c => c.Index == 5);
            for (var i = 0; i < 3; i++)
                await Pump();
            Assert.Equal(5, row.Index);

            var kind = view.GetVisualDescendants().OfType<ComboBox>().First(c =>
                ReferenceEquals(c.DataContext, row) && ReferenceEquals(c.ItemsSource, editor.KindChoices));
            kind.SelectedItem = editor.KindChoices.Single(c => c.Value == AxisKind.Rotary);
            for (var i = 0; i < 3; i++)
                await Pump();

            Assert.Equal(AxisKind.Rotary, row.Kind);
            // The editor's own kind write keeps invariant 7 satisfiable: exactly two
            // perpendicular pivot coordinates, rendered as exactly two inputs.
            Assert.Equal(2, row.Model.RotationOrigin!.Length);
            Assert.Equal(2, PivotBoxes(window).Count);

            // Both edits reached disk.
            var axes = new SettingsService(settings.SettingsDir).Load().OptionalAxes;
            Assert.Equal(5, Assert.Single(axes).Index);
            Assert.Equal(AxisKind.Rotary, axes[0].Kind);

            window.Close();
        }
        finally
        {
            LocalizationService.Current = prev;
        }
    }

    /// <summary>The row's own axis-table grid: the first Grid above its index picker.</summary>
    private static Grid GridOf(Visual root, MachineAxisRowViewModel row, IReadOnlyList<AxisIndexChoice> choices) =>
        root.GetVisualDescendants().OfType<ComboBox>()
            .First(c => ReferenceEquals(c.DataContext, row) && ReferenceEquals(c.ItemsSource, choices))
            .GetVisualAncestors().OfType<Grid>().First();

    /// <summary>A control's top-left in the view's coordinate space.</summary>
    private static Point Position(Visual visual, Visual root) =>
        visual.TranslatePoint(new Point(0, 0), root) ?? new Point(0, 0);

    /// <summary>The rendered pivot labels: the semibold direction letters of a rotary row,
    /// left to right (the value boxes' internal text blocks carry no weight).</summary>
    private static List<string> PivotLabels(Visual root) =>
        root.GetVisualDescendants().OfType<TextBlock>()
            .Where(t => t.DataContext is AxisPivotRowViewModel && t.FontWeight == FontWeight.SemiBold)
            .Select(t => t.Text ?? string.Empty)
            .ToList();

    private static List<TextBox> PivotBoxes(Visual root) =>
        root.GetVisualDescendants().OfType<TextBox>()
            .Where(t => t.DataContext is AxisPivotRowViewModel)
            .ToList();

    private static List<TextBox> SeparationBoxes(Visual root, MachineAxesViewModel editor) =>
        root.GetVisualDescendants().OfType<TextBox>()
            .Where(t => ReferenceEquals(t.DataContext, editor))
            .ToList();

    /// <summary>Run queued dispatcher jobs (the deferred, guarded writes land there) and
    /// yield so the headless platform runs a pending layout/render.</summary>
    private static async Task Pump()
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(16);
        await Task.Yield();
    }
}
