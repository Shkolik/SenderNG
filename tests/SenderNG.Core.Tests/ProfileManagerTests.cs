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
using SenderNG.Modules.Fixed;
using SenderNG.Modules.Plugins;
using Xunit;

namespace SenderNG.Core.Tests;

/// <summary>
/// Profile manager (docs/settings-migration.md §7): add / duplicate / rename /
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
        return (dir, new SettingsService(dir));
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
        leaf.NewName = "Second mill";
        leaf.DuplicateCommand.Execute(null);

        var dup = leaf.Profiles.Single(p => p.Id == "second-mill");
        Assert.Equal("Second mill", dup.Name);
        Assert.False(dup.IsActive); // duplicate does not yank the user off the current machine
        Assert.Equal(5, settings.AllProfiles().Single(p => p.Id == "second-mill").DemoAxes);
        Assert.Equal(0.05, JogConfigStore.Load(settings.AllProfiles().Single(p => p.Id == "second-mill")).Distances[0]);
        Assert.Equal(2, leaf.Profiles.Count);
    }

    [Fact]
    public void Rename_changes_display_name_only()
    {
        var (dir, settings) = MakeSettings();
        var leaf = CreateLeaf(settings);

        leaf.RenameName = "Big mill";
        leaf.RenameCommand.Execute(null);

        Assert.Equal("Big mill", leaf.Profiles.Single(p => p.Id == "default").Name);
        Assert.True(File.Exists(Path.Combine(dir, "profiles", "default.json")));
        Assert.Equal("Big mill", new SettingsService(dir).Global.Profiles["default"]);
        Assert.True(leaf.Profiles.Single(p => p.Id == "default").IsActive); // still active
    }

    [Fact]
    public void Remove_refuses_active_and_last_profile_and_deletes_companion()
    {
        var (dir, settings) = MakeSettings();
        var leaf = CreateLeaf(settings);

        // Only one profile: remove is refused outright.
        Assert.False(leaf.RemoveCommand.CanExecute(null));

        leaf.NewName = "other";
        leaf.AddCommand.Execute(null);
        var other = leaf.Profiles.Single(p => p.Id == "other");
        // The add made "other" active; an active profile cannot be removed.
        leaf.SelectedProfile = other;
        Assert.False(leaf.RemoveCommand.CanExecute(null));

        // Make "default" active again, then remove "other" (two-step confirm).
        leaf.SelectedProfile = leaf.Profiles.Single(p => p.Id == "default");
        leaf.ActivateCommand.Execute(null);
        leaf.SelectedProfile = leaf.Profiles.Single(p => p.Id == "other");
        Assert.True(leaf.RemoveCommand.CanExecute(null));
        File.WriteAllText(ProfileSettingsPaths.ProbeCompanionPath(dir, "other"), "{}");
        leaf.RemoveCommand.Execute(null);
        Assert.Equal("Remove again to confirm", leaf.RemoveButtonText); // armed, not executed
        leaf.RemoveCommand.Execute(null);

        Assert.Single(leaf.Profiles);
        Assert.False(File.Exists(ProfileSettingsPaths.ProfilePath(dir, "other")));
        Assert.False(File.Exists(ProfileSettingsPaths.ProbeCompanionPath(dir, "other")));
        Assert.DoesNotContain("other", new SettingsService(dir).Global.Profiles.Keys);
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

        leaf.SelectedProfile = leaf.Profiles.Single(p => p.Id == "default");
        leaf.ActivateCommand.Execute(null);

        Assert.Equal("default", settings.Global.ActiveProfileId);
        Assert.Equal("Default", settings.Load().Name);
        Assert.Contains("default", switched);
        Assert.True(changed > 0);
        Assert.True(leaf.Profiles.Single(p => p.Id == "default").IsActive);
    }

    [Fact]
    public void Switch_guard_blocks_all_mutations_while_connected_or_running()
    {
        var (_, settings) = MakeSettings();
        var leaf = CreateLeaf(settings, guard: () => false);

        Assert.False(leaf.CanSwitchProfiles);
        Assert.True(leaf.ShowSwitchBlocked);
        leaf.NewName = "blocked";
        Assert.False(leaf.AddCommand.CanExecute(null));
        Assert.False(leaf.DuplicateCommand.CanExecute(null));
        Assert.False(leaf.RenameCommand.CanExecute(null));
        Assert.False(leaf.RemoveCommand.CanExecute(null));
        Assert.False(leaf.ActivateCommand.CanExecute(null));

        leaf.SetSwitchGuard(() => true);
        Assert.True(leaf.CanSwitchProfiles);
        Assert.True(leaf.AddCommand.CanExecute(null));
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
        profilesLeaf.SelectedProfile = profilesLeaf.Profiles.Single(p => p.Id == "no-jog");
        profilesLeaf.ActivateCommand.Execute(null);

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
/// Cached-config VMs must follow profile switches (docs/settings-migration.md
/// §6.3): after an activation they keep saving to the ACTIVE machine's file and
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

    [Fact]
    public void ProbeViewModel_repoints_its_companion_store_after_switch()
    {
        var (dir, settings) = MakeSettings();
        var vm = new ProbeViewModel(new StubGrbl(), settings);
        vm.NewProfileName = "p";
        vm.ProfileAddCommand.Execute(null);
        Assert.True(File.Exists(ProfileSettingsPaths.ProbeCompanionPath(dir, "default")));

        var second = settings.CreateProfile("second", MachineTypes.Mill);
        settings.Activate(second.Id);

        Assert.Single(vm.Profiles); // the new machine's companion (default profile only)
        vm.NewProfileName = "q";
        vm.ProfileAddCommand.Execute(null);
        Assert.True(File.Exists(ProfileSettingsPaths.ProbeCompanionPath(dir, "second")));
        Assert.DoesNotContain("p", vm.Profiles.Select(p => p.Name));
    }
}

/// <summary>
/// Fresh profiles seed machine-coupled defaults from the type preset
/// (docs/machine-axis-schema.md M3): the demo controller simulates the type's
/// axis count (Foam needs 4 so both towers exist). Duplicates are machine
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