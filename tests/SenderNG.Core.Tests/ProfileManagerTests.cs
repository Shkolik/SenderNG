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

using System.Reactive.Subjects;
using CNC.Core.Abstractions;
using CNC.Core.Services;
using CNC.Core.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using SenderNG.Core.Modules;
using SenderNG.Core.Services;
using SenderNG.Modules.Fixed;
using SenderNG.Modules.Plugins;
using Xunit;

namespace SenderNG.Core.Tests;

/// <summary>
/// Profile manager: add / duplicate / rename /
/// remove / activate on the App Settings Profiles leaf, backed by
/// SettingsService CRUD, plus the switch guard and the tree rebuild on
/// activation.
/// </summary>
public class ProfileManagerTests
{
    private static (string Dir, SettingsService Service) MakeSettings()
    {
        var dir = Path.Combine(Path.GetTempPath(), "senderng-profiles-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var services = new ServiceCollection()
            .AddSingleton<IProfileCompanionSuffixes>(new ProbingCompanionStub())
            .BuildServiceProvider();
        return (dir, new SettingsService(dir, services));
    }

    // Stands in for the probing module's companion registration (Task 3 wires
    // the real module): DeleteProfile removes ".probing.json" beside the profile.
    private sealed class ProbingCompanionStub : IProfileCompanionSuffixes
    {
        public IReadOnlyList<string> Suffixes { get; } = [".probing.json"];
    }

    private static ProfileSettingsLeaf CreateLeaf(SettingsService settings, Func<bool>? guard = null)
    {
        var leaf = new ProfileSettingsLeaf(settings, (_, fallback) => fallback, "Profiles", "Machines");
        leaf.SetSwitchGuard(guard);
        return leaf;
    }

    private sealed class StubModule(string id, string name) : IModule
    {
        public string Id => id;
        public string Name => name;
        public int Order => 0;
        public IReadOnlyList<HotKey> HotKeys => [];
        public void RegisterServices(IServiceCollection services) { }
        public object CreateViewModel(IServiceProvider serviceProvider) => new object();
        public object CreateView(object viewModel) => new object();
    }

    [Fact]
    public void Add_creates_activates_and_persists_the_profile()
    {
        var (dir, settings) = MakeSettings();
        var leaf = CreateLeaf(settings);

        Assert.False(leaf.AddCommand.CanExecute(null)); // no name yet
        leaf.NewName = "CNC mill 2";
        Assert.True(leaf.AddCommand.CanExecute(null));
        leaf.NewType = MachineTypes.Foam;
        leaf.AddCommand.Execute(null);

        Assert.Equal(2, leaf.Profiles.Count);
        var created = leaf.Profiles.Single(p => p.Id == "cnc-mill-2");
        Assert.True(created.IsActive); // a fresh profile becomes active
        Assert.Equal(MachineTypes.Foam, created.MachineTypeId);
        Assert.True(File.Exists(Path.Combine(dir, "profiles", "cnc-mill-2.json")));
        Assert.Equal("cnc-mill-2", new SettingsService(dir).Global.ActiveProfileId);
        Assert.Equal("CNC mill 2", settings.Load().Name);
        Assert.Equal(string.Empty, leaf.NewName); // box cleared
    }

    [Fact]
    public void Add_generates_unique_slugs_on_name_collision()
    {
        var (_, settings) = MakeSettings();
        var leaf = CreateLeaf(settings);

        leaf.NewName = "Default";
        leaf.AddCommand.Execute(null);
        leaf.NewName = "Default";
        leaf.AddCommand.Execute(null);

        // Active-first ordering: the second add activated "default-3".
        Assert.Equal("default-3", leaf.Profiles[0].Id);
        Assert.Contains(leaf.Profiles, p => p.Id == "default-2");
    }

    [Fact]
    public void Duplicate_copies_settings_under_a_new_id_without_activating()
    {
        var (_, settings) = MakeSettings();
        var profile = settings.Load();
        profile.DemoAxes = 5;
        JogConfigStore.Save(profile, new JogConfig { Distances = [0.05, 1, 10] });
        settings.Save(profile);

        var leaf = CreateLeaf(settings);
        // The action is the row's own: it copies THAT row's profile, not the selection's.
        var row = leaf.Profiles.Single(p => p.Id == "default");
        Assert.True(row.CanDuplicate);
        row.DuplicateCommand.Execute(null);

        var dup = leaf.Profiles.Single(p => p.Id == "default-copy");
        Assert.Equal("Default copy", dup.Name);   // named after the row it came from
        Assert.False(dup.IsActive); // duplicate does not yank the user off the current machine
        Assert.Equal(5, settings.AllProfiles().Single(p => p.Id == "default-copy").DemoAxes);
        Assert.Equal(0.05, JogConfigStore.Load(settings.AllProfiles().Single(p => p.Id == "default-copy")).Distances[0]);
        Assert.Equal(2, leaf.Profiles.Count);
    }

    [Fact]
    public void Rename_changes_display_name_only()
    {
        var (dir, settings) = MakeSettings();
        var leaf = CreateLeaf(settings);
        var changed = 0;
        leaf.ProfilesChanged += () => changed++;

        // The rename is in-place: the row opens for editing, the draft is committed.
        var row = leaf.Profiles.Single(p => p.Id == "default");
        leaf.BeginRename(row);
        Assert.True(row.IsEditing);
        leaf.UpdateRename(row, "Big mill");
        leaf.CommitRename(row);

        Assert.Equal("Big mill", leaf.Profiles.Single(p => p.Id == "default").Name);
        Assert.False(leaf.Profiles.Single(p => p.Id == "default").IsEditing);
        Assert.Equal(1, changed);
        Assert.True(File.Exists(Path.Combine(dir, "profiles", "default.json")));
        Assert.Equal("Big mill", new SettingsService(dir).Global.Profiles["default"]);
        Assert.True(leaf.Profiles.Single(p => p.Id == "default").IsActive); // still active
    }

    [Fact]
    public void Rename_refuses_a_blank_or_duplicate_name_and_keeps_editing()
    {
        var (_, settings) = MakeSettings();
        var leaf = CreateLeaf(settings);
        leaf.NewName = "second";
        leaf.AddCommand.Execute(null);
        var other = leaf.Profiles.Single(p => p.Id == "second");

        leaf.BeginRename(other);
        leaf.UpdateRename(other, "   ");
        Assert.True(other.HasRenameError);
        leaf.CommitRename(other);
        Assert.True(other.IsEditing);                     // refused, still open
        Assert.Equal("second", settings.AllProfiles().Single(p => p.Id == "second").Name);

        leaf.UpdateRename(other, "Default");              // the other profile's name
        Assert.True(other.HasRenameError);
        leaf.CommitRename(other);
        Assert.Equal("second", settings.AllProfiles().Single(p => p.Id == "second").Name);

        leaf.UpdateRename(other, "Renamed");
        Assert.False(other.HasRenameError);
        leaf.CommitRename(other);
        Assert.Equal("Renamed", settings.AllProfiles().Single(p => p.Id == "second").Name);
    }

    [Fact]
    public void Selecting_another_row_finishes_the_open_rename()
    {
        // Clicking a different row is the natural "done" gesture — the list owns the
        // pointer, so an edit left open on the old row would otherwise stay armed and
        // commit later, against whatever the operator did next.
        var (_, settings) = MakeSettings();
        var leaf = CreateLeaf(settings);
        leaf.NewName = "second";
        leaf.AddCommand.Execute(null);

        var first = leaf.Profiles.Single(p => p.Id == "default");
        var second = leaf.Profiles.Single(p => p.Id == "second");
        leaf.SelectedProfile = first;
        leaf.BeginRename(first);
        leaf.UpdateRename(first, "Bench mill");

        leaf.SelectedProfile = second;   // the operator clicks away mid-edit

        Assert.False(first.IsEditing);                       // the editor is closed
        Assert.Equal("Bench mill", settings.AllProfiles().Single(p => p.Id == "default").Name);
        Assert.Equal("Bench mill", leaf.Profiles.Single(p => p.Id == "default").Name);
        // The click still landed: the commit rebuilds the rows, so the selection has to
        // come back as the live instance for that profile, not the pre-commit one.
        Assert.Equal("second", leaf.SelectedProfile!.Id);
        Assert.True(leaf.Profiles.Single(p => p.Id == "second").IsSelected);
        // Exactly one row is flagged: the rebuild re-selects the renamed row by id, so
        // clearing the old flag has to happen after the commit, not before it.
        Assert.Single(leaf.Profiles, p => p.IsSelected);
    }

    [Fact]
    public void Selecting_another_row_discards_an_uncommittable_draft()
    {
        // A refused draft cannot be written, so leaving the row must not leave the editor
        // armed either: the stored name stays and the row goes back to its label.
        var (_, settings) = MakeSettings();
        var leaf = CreateLeaf(settings);
        leaf.NewName = "second";
        leaf.AddCommand.Execute(null);

        var first = leaf.Profiles.Single(p => p.Id == "default");
        leaf.SelectedProfile = first;
        leaf.BeginRename(first);
        leaf.UpdateRename(first, "   ");                     // blank: not committable

        leaf.SelectedProfile = leaf.Profiles.Single(p => p.Id == "second");

        Assert.False(first.IsEditing);
        Assert.Null(first.RenameError);
        Assert.Equal("Default", settings.AllProfiles().Single(p => p.Id == "default").Name);
    }

    [Fact]
    public void Rename_escape_keeps_the_original_name()
    {
        var (_, settings) = MakeSettings();
        var leaf = CreateLeaf(settings);
        var row = leaf.Profiles.Single(p => p.Id == "default");

        leaf.BeginRename(row);
        leaf.UpdateRename(row, "Discarded");
        leaf.CancelRename();

        Assert.False(row.IsEditing);
        Assert.Equal("Default", row.Name);
        Assert.Equal("Default", settings.AllProfiles().Single(p => p.Id == "default").Name);
    }

    [Fact]
    public void Remove_refuses_active_and_last_profile_and_deletes_companion()
    {
        var (dir, settings) = MakeSettings();
        var leaf = CreateLeaf(settings);

        // Only one profile: the row refuses outright.
        Assert.False(leaf.Profiles.Single(p => p.Id == "default").CanRemove);

        leaf.NewName = "other";
        leaf.AddCommand.Execute(null);
        var other = leaf.Profiles.Single(p => p.Id == "other");
        // The add made "other" active; the active profile cannot be removed.
        Assert.False(other.CanRemove);

        // Make "default" active again, then remove "other" (two-step confirm).
        leaf.Profiles.Single(p => p.Id == "default").ActivateCommand.Execute(null);
        other = leaf.Profiles.Single(p => p.Id == "other");
        Assert.True(other.CanRemove);
        File.WriteAllText(ProfileSettingsPaths.CompanionPath(dir, "other", ".probing.json"), "{}");
        other.RemoveCommand.Execute(null);
        Assert.True(other.RemoveArmed);                       // armed, not executed
        Assert.Equal("Click again to confirm", other.RemoveHint);
        other.RemoveCommand.Execute(null);

        Assert.Single(leaf.Profiles);
        Assert.False(File.Exists(ProfileSettingsPaths.ProfilePath(dir, "other")));
        Assert.False(File.Exists(ProfileSettingsPaths.CompanionPath(dir, "other", ".probing.json")));
        Assert.DoesNotContain("other", new SettingsService(dir).Global.Profiles.Keys);
    }

    [Fact]
    public void Remove_arming_is_per_row_and_does_not_survive_leaving_the_row()
    {
        // Two-step delete used to be one flag on the leaf, which meant arming one profile
        // and then clicking another row's button deleted the WRONG profile. It is the
        // row's own state now.
        var (_, settings) = MakeSettings();
        var leaf = CreateLeaf(settings);
        foreach (var name in new[] { "second", "third" })
        {
            leaf.NewName = name;
            leaf.AddCommand.Execute(null);
        }
        leaf.Profiles.Single(p => p.Id == "default").ActivateCommand.Execute(null);

        var second = leaf.Profiles.Single(p => p.Id == "second");
        var third = leaf.Profiles.Single(p => p.Id == "third");

        second.RemoveCommand.Execute(null);                   // arm "second"
        Assert.True(second.RemoveArmed);
        Assert.False(third.RemoveArmed);

        // Selecting another row disarms: the confirmation is about the row you clicked.
        leaf.SelectedProfile = third;
        Assert.False(second.RemoveArmed);

        // And a first click on "third" deletes nothing, however recently "second" was armed.
        third.RemoveCommand.Execute(null);
        Assert.Equal(3, leaf.Profiles.Count);
        Assert.True(File.Exists(ProfileSettingsPaths.ProfilePath(settings.SettingsDir, "second")));
        Assert.True(File.Exists(ProfileSettingsPaths.ProfilePath(settings.SettingsDir, "third")));
    }

    [Fact]
    public void Activate_swaps_the_shared_graph_and_raises_events()
    {
        var (_, settings) = MakeSettings();
        var leaf = CreateLeaf(settings);
        leaf.NewName = "second";
        leaf.AddCommand.Execute(null); // activates "second"

        var changed = 0;
        var switched = new List<string>();
        settings.Changed += () => changed++;
        settings.ProfileSwitched += id => switched.Add(id);

        leaf.Profiles.Single(p => p.Id == "default").ActivateCommand.Execute(null);

        Assert.Equal("default", settings.Global.ActiveProfileId);
        Assert.Equal("Default", settings.Load().Name);
        Assert.Contains("default", switched);
        Assert.True(changed > 0);
        Assert.True(leaf.Profiles.Single(p => p.Id == "default").IsActive);
    }

    [Fact]
    public async Task Switch_pipeline_owns_the_activation_and_reports_success()
    {
        var (_, settings) = MakeSettings();
        var leaf = CreateLeaf(settings);
        leaf.NewName = "second";
        leaf.AddCommand.Execute(null); // activates "second"

        var ran = new TaskCompletionSource();
        var handedOver = new List<string>();
        var problems = new List<(string Title, string Message)>();
        var activated = 0;
        settings.ProfileSwitched += _ => activated++;
        leaf.ReportSwitchProblem = (title, message) => problems.Add((title, message));
        leaf.SetSwitchPipeline(profile =>
        {
            // The host stands in for the real disconnect → activate → reconnect.
            handedOver.Add(profile.Id);
            settings.Activate(profile.Id);
            ran.SetResult();
            return Task.FromResult(ProfileSwitchResult.Success);
        });

        leaf.Profiles.Single(p => p.Id == "default").ActivateCommand.Execute(null);
        await ran.Task;

        Assert.Equal(["default"], handedOver);
        Assert.Equal(1, activated);                                 // not activated twice
        Assert.Equal("default", settings.Global.ActiveProfileId);
        Assert.True(leaf.Profiles.Single(p => p.Id == "default").IsActive);
        Assert.False(leaf.SwitchBusy);                              // cleared after the run
        // A successful switch says nothing: the active row's checkmark is the whole
        // confirmation, and a caption would shove the list around.
        Assert.Empty(problems);
    }

    [Fact]
    public async Task Switch_pipeline_reports_a_reconnect_failure_as_a_popup()
    {
        var (_, settings) = MakeSettings();
        var leaf = CreateLeaf(settings);
        leaf.NewName = "second";
        leaf.AddCommand.Execute(null);
        var ran = new TaskCompletionSource();
        var problems = new List<(string Title, string Message)>();
        leaf.ReportSwitchProblem = (title, message) => problems.Add((title, message));
        // The host activated the profile but could not reopen its port.
        leaf.SetSwitchPipeline(profile =>
        {
            settings.Activate(profile.Id);
            ran.SetResult();
            return Task.FromResult(ProfileSwitchResult.ReconnectFailed("Unable to reconnect", "COM9 is busy"));
        });

        leaf.Profiles.Single(p => p.Id == "default").ActivateCommand.Execute(null);
        await ran.Task;

        // The switch DID happen — the profile is active, the app is simply offline — and
        // the operator is told through a pop-up rather than an in-pane caption.
        Assert.Equal("default", settings.Global.ActiveProfileId);
        Assert.Equal(("Unable to reconnect", "COM9 is busy"), Assert.Single(problems));
        Assert.False(leaf.SwitchBusy);
    }

    [Fact]
    public async Task Switch_pipeline_reports_a_refused_activation_and_keeps_the_old_profile()
    {
        var (_, settings) = MakeSettings();
        var leaf = CreateLeaf(settings);
        leaf.NewName = "second";
        leaf.AddCommand.Execute(null);
        var ran = new TaskCompletionSource();
        var problems = new List<(string Title, string Message)>();
        leaf.ReportSwitchProblem = (title, message) => problems.Add((title, message));
        // The activation itself was refused, so nothing moved.
        leaf.SetSwitchPipeline(_ =>
        {
            ran.SetResult();
            return Task.FromResult(ProfileSwitchResult.ActivationFailed("Could not switch: refused"));
        });

        leaf.Profiles.Single(p => p.Id == "default").ActivateCommand.Execute(null);
        await ran.Task;

        Assert.Equal("second", settings.Global.ActiveProfileId);    // unchanged
        Assert.Single(problems);
        Assert.Equal("Could not switch: refused", problems[0].Message);
        Assert.False(leaf.SwitchBusy);
    }

    [Fact]
    public void Switch_guard_blocks_all_mutations_while_a_session_runs()
    {
        var (_, settings) = MakeSettings();
        var leaf = CreateLeaf(settings, guard: () => false);

        Assert.False(leaf.CanSwitchProfiles);
        leaf.NewName = "blocked";
        Assert.False(leaf.AddCommand.CanExecute(null));
        // The row actions are gated by the same predicate, through the row's own state.
        var row = leaf.Profiles[0];
        Assert.False(row.CanDuplicate);
        Assert.False(row.CanRemove);
        Assert.False(row.CanActivate);
        // The in-place rename is gated by the same predicate: opening an editor the
        // service would refuse to save is worse than not offering one.
        leaf.BeginRename(row);
        Assert.False(row.IsEditing);

        leaf.SetSwitchGuard(() => true);
        Assert.True(leaf.CanSwitchProfiles);
        Assert.True(leaf.AddCommand.CanExecute(null));
        row = leaf.Profiles[0];
        Assert.True(row.CanDuplicate);
        Assert.False(row.CanRemove);    // still the active (and only) profile
        leaf.BeginRename(row);
        Assert.True(row.IsEditing);
    }

    [Fact]
    public void Machine_type_change_is_two_step_and_persists()
    {
        var (_, settings) = MakeSettings();
        var leaf = CreateLeaf(settings);

        leaf.SelectedMachineType = MachineTypes.Foam;
        leaf.ApplyTypeCommand.Execute(null);
        Assert.Equal("Change again to confirm", leaf.TypeButtonText); // armed, not executed
        leaf.ApplyTypeCommand.Execute(null);

        Assert.Equal(MachineTypes.Foam, settings.AllProfiles().Single(p => p.Id == "default").MachineTypeId);
        Assert.Equal(MachineTypes.Foam, leaf.Profiles.Single(p => p.Id == "default").MachineTypeId);
    }

    [Fact]
    public void Activation_rebuilds_the_modules_group_for_the_new_profile()
    {
        var (_, settings) = MakeSettings();
        var jog = new StubModule("jog", "Jog");
        var vm = new AppSettingsViewModel(settings, [jog]);
        var profilesLeaf = (ProfileSettingsLeaf)((AppSettingsGroupItem)vm.Groups[0]).Children[0];

        // Default profile shows jog; create a second machine without it.
        Assert.True(vm.ModuleLeaves.Single(l => l.Id == "jog").IsVisible);
        var noJog = settings.CreateProfile("no-jog", MachineTypes.Mill);
        noJog.Modules["jog"] = false;
        settings.SaveProfile(noJog);
        profilesLeaf.Refresh();

        // Activating it rebuilds the Modules group for the new profile.
        profilesLeaf.Profiles.Single(p => p.Id == "no-jog").ActivateCommand.Execute(null);

        Assert.False(vm.ModuleLeaves.Single(l => l.Id == "jog").IsVisible);
        Assert.Single(((AppSettingsGroupItem)vm.Groups[1]).Children);
    }

    [Fact]
    public void PreselectProfiles_opens_the_dialog_on_the_profiles_leaf()
    {
        var (_, settings) = MakeSettings();
        var vm = new AppSettingsViewModel(settings, [new StubModule("jog", "Jog")]);

        Assert.IsType<LanguageSettingsLeaf>(vm.SelectedSetting); // default: language

        vm.PreselectProfiles = true;
        Assert.IsType<ProfileSettingsLeaf>(vm.SelectedSetting);
    }
}

/// <summary>
/// Cached-config VMs must follow profile switches: after an activation they
/// keep saving to the ACTIVE machine's file and
/// reload their state from it.
/// </summary>
public class ProfileSwitchReReadTests
{
    private static (string Dir, SettingsService Service) MakeSettings()
    {
        var dir = Path.Combine(Path.GetTempPath(), "senderng-switch-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return (dir, new SettingsService(dir));
    }

    private sealed class StubGrbl : IGrblService
    {
        private readonly BehaviorSubject<MachineState> _subject = new(new MachineState(3));
        public bool IsConnected => true;
        public IObservable<MachineState> State => _subject;
        public Task ConnectAsync(string port) => Task.CompletedTask;
        public void Disconnect() { }
        public void SendRealtime(byte code) { }
        public Task SendAsync(string line) => Task.CompletedTask;
    }

    [Fact]
    public void JogViewModel_reloads_and_saves_to_the_active_profile_after_switch()
    {
        var (_, settings) = MakeSettings();
        var mill = settings.Load();
        JogConfigStore.Save(mill, new JogConfig { Distances = [0.05, 1, 10] });
        settings.Save(mill);

        var vm = new JogViewModel(new StubGrbl(), settings);
        Assert.Equal(0.05, vm.Config.Distances[0]);

        var second = settings.CreateProfile("second", MachineTypes.Mill);
        settings.Activate(second.Id); // fires Changed; the VM re-points

        Assert.Equal(0.1, vm.Config.Distances[0]); // second machine's defaults
        vm.Config.Distances[0] = 0.25;
        vm.Save();

        Assert.Equal(0.25, JogConfigStore.Load(settings.Load()).Distances[0]); // active = second
        Assert.Equal(0.05, JogConfigStore.Load(settings.AllProfiles().Single(p => p.Id == "default")).Distances[0]);
    }

    [Fact]
    public void MacroViewModel_reloads_and_saves_to_the_active_profile_after_switch()
    {
        var (dir, settings) = MakeSettings();
        var vm = new MacroViewModel(settings, new StubGrbl());
        vm.AddMacroCommand.Execute(null); // auto-saves "Macro 1" into the default profile

        var second = settings.CreateProfile("second", MachineTypes.Mill);
        settings.Activate(second.Id);

        Assert.Empty(vm.Macros); // the new machine has no macros
        vm.AddMacroCommand.Execute(null);

        // The second add went to the ACTIVE machine; the default machine keeps
        // exactly the one macro it had before the switch.
        Assert.Contains("Macros", File.ReadAllText(ProfileSettingsPaths.ProfilePath(dir, "second")));
        Assert.Equal(1, MacrosIn(ProfileSettingsPaths.ProfilePath(dir, "default")));
        Assert.Equal(1, MacrosIn(ProfileSettingsPaths.ProfilePath(dir, "second")));
    }

    private static int MacrosIn(string profilePath)
    {
        using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(profilePath));
        return doc.RootElement.GetProperty("ModuleSettings").GetProperty("Macros").GetArrayLength();
    }

    [Fact]
    public void OutlineViewModel_reloads_and_saves_to_the_active_profile_after_switch()
    {
        var (dir, settings) = MakeSettings();
        var mill = settings.Load();
        mill.ModuleSettings[OutlineModule.SettingsSection] =
            new Dictionary<string, object> { [OutlineModule.FeedRateKey] = 1000.0 };
        settings.Save(mill);

        var vm = new OutlineViewModel(new StubGrbl(), settings, new ProgramViewModel());
        Assert.Equal(1000, vm.FeedRate);

        var second = settings.CreateProfile("second", MachineTypes.Mill);
        settings.Activate(second.Id); // fires Changed; the VM re-reads

        Assert.Equal(OutlineViewModel.DefaultFeedRate, vm.FeedRate); // second machine's default
        vm.FeedRate = 750;

        var active = settings.Load();
        Assert.Equal(750.0, (double)((Dictionary<string, object>)active.ModuleSettings[OutlineModule.SettingsSection])[OutlineModule.FeedRateKey]);
        // The default machine keeps the value it had before the switch.
        using var doc = System.Text.Json.JsonDocument.Parse(
            File.ReadAllText(ProfileSettingsPaths.ProfilePath(dir, "default")));
        var feed = doc.RootElement.GetProperty("ModuleSettings")
            .GetProperty(OutlineModule.SettingsSection).GetProperty(OutlineModule.FeedRateKey).GetDouble();
        Assert.Equal(1000.0, feed);
    }
}

/// <summary>
/// Fresh profiles seed machine-coupled defaults from the type preset: the
/// demo controller simulates the type's axis count (Foam needs 4 so both
/// towers exist). Duplicates are machine
/// copies, not templates — they keep the source's configured values.
/// </summary>
public class ProfileDemoAxesSeedTests
{
    [Fact]
    public void Fresh_profiles_seed_demo_axes_from_the_machine_type()
    {
        var dir = Path.Combine(Path.GetTempPath(), "senderng-profiles-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var settings = new SettingsService(dir);

        var foam = settings.CreateProfile("Foam demo", MachineTypes.Foam);
        Assert.Equal(4, foam.DemoAxes);

        var mill = settings.CreateProfile("Mill demo", MachineTypes.Mill);
        Assert.Equal(3, mill.DemoAxes);

        foam.DemoAxes = 6;
        settings.Save(foam);
        var copy = settings.CreateProfile("Foam copy", MachineTypes.Foam, copyFromId: foam.Id);
        Assert.Equal(6, copy.DemoAxes);
    }
}