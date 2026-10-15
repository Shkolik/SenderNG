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

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
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

            // The picture resolves through avares://SenderNG/Assets/... — the one thing
            // that proves the asset registration works at runtime (docs/images/ would not
            // load at all). The rotary reference art beside the optional list loads too.
            var images = view.GetVisualDescendants().OfType<Image>().ToList();
            Assert.Equal(2, images.Count);
            foreach (var image in images)
            {
                var bitmap = Assert.IsType<Bitmap>(image.Source);
                Assert.True(bitmap.PixelSize.Width > 0 && bitmap.PixelSize.Height > 0,
                    "the topology art decoded to an empty bitmap");
            }
            Assert.EndsWith("machine-topology-mill.png", editor.TopologyImage!);

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
    {
        // The recycled-control regression (docs/avalonia-gotchas.md §1): the axis rows'
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
            var before = axesView.GetVisualDescendants().OfType<Image>().First().Source;
            Assert.NotNull(before);
            Assert.Equal(3, AxisPickers(axesView, leaf.MachineAxes).Count);

            leaf.SelectedProfile = leaf.Profiles.Single(p => p.Id == foam.Id);
            for (var i = 0; i < 6; i++)
                await Pump();

            // The card followed the selection, and the topology art was reloaded.
            Assert.Equal(MachineTypes.Foam, leaf.MachineAxes.MachineTypeId);
            Assert.Equal(4, AxisPickers(axesView, leaf.MachineAxes).Count);
            var after = axesView.GetVisualDescendants().OfType<Image>().First().Source;
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
                MachineTypeCatalog.Resolve(MachineTypes.Mill).EffectiveBasePlanes[0],
                mill.Planes.Single().Axes.Select(a => a.Direction).ToArray());

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
