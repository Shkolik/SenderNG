/*
 * SettingsRestoreGuardTests.cs - a restored settings directory must not be overwritten by the stale graph
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

using CNC.Core.Services;

namespace SenderNG.Core.Tests;

/// <summary>
/// Restore-pending guard: once an app-settings restore has written the archive
/// over the live settings directory, the in-memory graph is stale, so every
/// mutator must be a no-op until the app restarts. Reads keep working.
/// </summary>
public class SettingsRestoreGuardTests
{
    private static (string Dir, SettingsService Settings) MakeSettings()
    {
        var dir = Path.Combine(Path.GetTempPath(), "senderng-restoreguard-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return (dir, new SettingsService(dir));
    }

    /// <summary>Every file under the settings dir as ordered "relative/path=contents"
    /// entries, for an exact before/after comparison of what is on disk.</summary>
    private static string[] Snapshot(string dir) =>
        Directory.Exists(dir)
            ? Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)
                .Select(p => Path.GetRelativePath(dir, p).Replace('\\', '/') + "=" + File.ReadAllText(p))
                .OrderBy(s => s, StringComparer.Ordinal)
                .ToArray()
            : [];

    [Fact]
    public void MarkRestorePending_sets_the_flag_and_raises_the_event_once()
    {
        var (_, settings) = MakeSettings();
        settings.Load();

        var pendingRaised = 0;
        var changedRaised = 0;
        settings.RestorePendingChanged += () => pendingRaised++;
        settings.Changed += () => changedRaised++;

        Assert.False(settings.RestorePending);
        settings.MarkRestorePending();
        settings.MarkRestorePending(); // idempotent: the notice is raised once

        Assert.True(settings.RestorePending);
        Assert.Equal(1, pendingRaised);
        Assert.Equal(0, changedRaised); // nothing changed on disk yet
    }

    [Fact]
    public void Save_does_not_touch_disk_while_pending()
    {
        var (dir, settings) = MakeSettings();
        var profile = settings.Load();
        settings.Save(profile); // settle the file before the snapshot
        var before = Snapshot(dir);

        profile.DemoAxes = 9; // mutate the shared in-memory graph
        settings.MarkRestorePending();
        settings.Save(profile);

        Assert.Equal(before, Snapshot(dir));
    }

    [Fact]
    public void SaveGlobal_SaveProfile_Activate_CreateProfile_RenameProfile_DeleteProfile_SetProfileMachineType_are_no_ops_while_pending()
    {
        var (dir, settings) = MakeSettings();
        var active = settings.Load();
        var other = settings.CreateProfile("Other", MachineTypes.Mill);
        settings.SaveGlobal(settings.Global); // settle global.json before the snapshot
        var before = Snapshot(dir);

        settings.MarkRestorePending();

        // Mutate the shared graphs, then call every mutator: the disk must not move.
        active.DemoAxes = 7;
        settings.Save(active);

        other.VisibleTools = 3;
        settings.SaveProfile(other);

        var global = settings.Global;
        global.Language = "ru";
        settings.SaveGlobal(global);

        settings.Activate(other.Id);
        settings.CreateProfile("Third", MachineTypes.Foam);
        settings.RenameProfile(other.Id, "Renamed");
        settings.DeleteProfile(other.Id);
        settings.SetProfileMachineType(other.Id, MachineTypes.Foam);

        Assert.Equal(before, Snapshot(dir));
        Assert.True(File.Exists(ProfileSettingsPaths.ProfilePath(dir, other.Id)));
    }

    [Fact]
    public void Reads_still_work_while_pending()
    {
        var (dir, settings) = MakeSettings();
        var loaded = settings.Load();
        settings.CreateProfile("Other", MachineTypes.Foam);
        var global = settings.Global;
        var ids = settings.AllProfiles().Select(p => p.Id).ToArray();
        var before = Snapshot(dir);

        settings.MarkRestorePending();

        Assert.Same(loaded, settings.Load());
        Assert.Same(global, settings.Global);
        Assert.Equal(ids, settings.AllProfiles().Select(p => p.Id).ToArray());
        Assert.Equal(dir, settings.SettingsDir);
        Assert.Equal(before, Snapshot(dir)); // reads do not write either
    }

    [Fact]
    public void Writes_resume_after_a_fresh_service_on_the_same_dir()
    {
        var (dir, settings) = MakeSettings();
        settings.Load();
        settings.MarkRestorePending();

        // A restart builds a new service over the same directory: the guard is
        // per-instance, so writing works again (and reloads the restored files).
        var restarted = new SettingsService(dir);
        Assert.False(restarted.RestorePending);

        var reloaded = restarted.Load();
        reloaded.DemoAxes = 4;
        restarted.Save(reloaded);

        Assert.Equal(4, new SettingsService(dir).Load().DemoAxes);
    }
}
