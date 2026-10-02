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
using Xunit;

namespace SenderNG.Core.Tests;

/// <summary>
/// A settings file that cannot be loaded must (a) say so and (b) keep the
/// original bytes, because the next Save overwrites the file in place. These
/// reach the startup path through SettingsService (audit §1.6).
/// </summary>
public class SettingsFallbackTests
{
    private static string NewDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "senderng-fallback-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static string WriteGlobal(string dir, string content)
    {
        var path = ProfileSettingsPaths.GlobalPath(dir);
        File.WriteAllText(path, content);
        return path;
    }

    private static string WriteProfile(string dir, string id, string content)
    {
        Directory.CreateDirectory(ProfileSettingsPaths.ProfilesDir(dir));
        var path = ProfileSettingsPaths.ProfilePath(dir, id);
        File.WriteAllText(path, content);
        return path;
    }

    private static List<SettingsFallback> Capture(out Action<SettingsFallback> callback)
    {
        var seen = new List<SettingsFallback>();
        callback = seen.Add;
        return seen;
    }

    [Fact]
    public void SettingsService_reports_a_fallback_it_loads()
    {
        // SettingsService is the only production caller of the loaders, so this
        // is the seam that makes the notice reach the host at all.
        var dir = NewDir();
        WriteGlobal(dir, "{ broken");
        var seen = Capture(out var onFallback);

        var service = new SettingsService(dir, services: null, onFallback);
        service.Load();

        Assert.Contains(seen, f => f.Reason == SettingsFallbackReason.Corrupt);
    }

    [Fact]
    public void Corrupt_global_is_quarantined_and_reported()
    {
        var dir = NewDir();
        // PascalCase, matching what GlobalSettingsStore.Save writes and what
        // GlobalSettings.ActiveProfileId actually binds. A camelCase key could
        // never bind (the loader is case-sensitive), which would make the
        // DefaultId assertion below pass even if the file parsed cleanly.
        var payload = "{ \"ActiveProfileId\": \"mill\"";
        var path = WriteGlobal(dir, payload);
        var seen = Capture(out var onFallback);

        var global = GlobalSettingsStore.Load(dir, onFallback);

        // Fallback behaviour is unchanged: defaults, no throw. This is a real
        // assertion - a clean parse would have yielded "mill" here.
        Assert.Equal(MachineProfile.DefaultId, global.ActiveProfileId);

        var fallback = Assert.Single(seen);
        Assert.Equal(SettingsFallbackReason.Corrupt, fallback.Reason);
        Assert.True(fallback.Quarantined);
        Assert.Equal(path, fallback.Path);

        // The original is preserved under a .corrupt name, and the live file is
        // gone so the next Save writes a clean one.
        Assert.False(File.Exists(path));
        Assert.True(File.Exists(path + ".corrupt"));
        Assert.Equal(payload, File.ReadAllText(path + ".corrupt"));
    }

    [Fact]
    public void Corrupt_profile_is_quarantined_and_reported()
    {
        var dir = NewDir();
        var id = "mill";
        var path = WriteProfile(dir, id, "not json at all");
        var seen = Capture(out var onFallback);

        var profile = ProfilesStore.TryLoad(dir, id, onFallback);

        Assert.Null(profile);   // unchanged Try contract
        var fallback = Assert.Single(seen);
        Assert.Equal(SettingsFallbackReason.Corrupt, fallback.Reason);
        Assert.True(fallback.Quarantined);
        Assert.True(File.Exists(path + ".corrupt"));
    }

    [Fact]
    public void A_second_corrupt_file_does_not_overwrite_the_first_quarantine()
    {
        var dir = NewDir();
        var path = WriteGlobal(dir, "{ broken");
        var first = Capture(out var onFallback);

        GlobalSettingsStore.Load(dir, onFallback);
        Assert.True(File.Exists(path + ".corrupt"));

        // A later run hits the same bad file again (user restored it, or a
        // different tool rewrote it). The earlier .corrupt must survive.
        WriteGlobal(dir, "{ broken again");
        var second = Capture(out var onFallback2);
        GlobalSettingsStore.Load(dir, onFallback2);

        Assert.True(File.Exists(path + ".corrupt"));
        Assert.True(File.Exists(path + ".corrupt.1"));
        Assert.Equal("{ broken", File.ReadAllText(path + ".corrupt"));
        Assert.Equal("{ broken again", File.ReadAllText(path + ".corrupt.1"));
        Assert.Single(first);
        Assert.Single(second);
    }

    [Fact]
    public void Quarantine_path_never_reuses_a_taken_name()
    {
        var dir = NewDir();
        // One taken name is a file and one is a DIRECTORY, so both halves of the
        // File.Exists || Directory.Exists test are covered. The directory case is
        // load-bearing: without that half, a directory sitting at global.json.corrupt
        // would turn a successful quarantine into Quarantined=false.
        var taken = Path.Combine(dir, "g.json.corrupt");
        File.WriteAllText(taken, "earlier");
        var taken1 = Path.Combine(dir, "g.json.corrupt.1");
        Directory.CreateDirectory(taken1);

        var free = ProfileSettingsStore.QuarantinePath(Path.Combine(dir, "g.json"));

        Assert.Equal(Path.Combine(dir, "g.json.corrupt.2"), free);
    }

    [Fact]
    public void Unreadable_file_is_reported_as_unreadable_not_corrupt()
    {
        // UnauthorizedAccessException is deliberately NOT an IOException, which is
        // exactly why the old filter let it escape and crash startup. The bytes may
        // be fine; the problem is access, so the reason is Unreadable.
        var dir = NewDir();
        WriteGlobal(dir, "{ \"activeProfileId\": \"mill\"");
        var seen = Capture(out var onFallback);

        // Replace the file with a directory so File.ReadAllText throws
        // UnauthorizedAccessException on Windows - the one exception type the
        // old filter could not catch and the reason startup crashed.
        var path = ProfileSettingsPaths.GlobalPath(dir);
        File.Delete(path);
        Directory.CreateDirectory(path);

        var global = GlobalSettingsStore.Load(dir, onFallback);

        Assert.NotNull(global);
        var fallback = Assert.Single(seen);
        Assert.Equal(SettingsFallbackReason.Unreadable, fallback.Reason);
        Assert.IsType<UnauthorizedAccessException>(fallback.Error);

        // There is nothing worth preserving and nothing to move (File.Move cannot
        // move a directory), so the original stays put and Quarantined is false.
        // Task 2 branches on this flag to pick the user-facing wording, so the one
        // production path that reaches it must be pinned here.
        Assert.False(fallback.Quarantined);
        Assert.True(Directory.Exists(path));
    }

    [Fact]
    public void A_failing_quarantine_still_reports_and_does_not_throw()
    {
        // Best-effort guarantee: an unwritable settings directory must not turn
        // a recoverable fallback back into a startup crash. We simulate the
        // quarantine failing by holding global.json open without FILE_SHARE_DELETE:
        // the read still succeeds, but the move fails with a sharing violation.
        // (A directory at the .corrupt name would NOT do it - QuarantinePath skips
        // taken names, including directories, and would land on .corrupt.1.)
        var dir = NewDir();
        WriteGlobal(dir, "{ broken");
        using var held = new FileStream(
            ProfileSettingsPaths.GlobalPath(dir), FileMode.Open, FileAccess.Read, FileShare.Read);

        var seen = Capture(out var onFallback);
        var global = GlobalSettingsStore.Load(dir, onFallback);

        Assert.NotNull(global);
        var fallback = Assert.Single(seen);
        // The read DID succeed (FileShare.Read lets other readers in), so this is
        // still the corrupt-JSON path. If a future change let the handle block the
        // read too, the reason would flip to Unreadable and this would catch it.
        Assert.Equal(SettingsFallbackReason.Corrupt, fallback.Reason);
        Assert.False(fallback.Quarantined);   // caller can say the file is still there
        Assert.True(File.Exists(ProfileSettingsPaths.GlobalPath(dir)));
    }

    [Fact]
    public void A_throwing_callback_does_not_escape_Load()
    {
        // The host callback resolves a service, formats a message and touches the
        // status bar. If any of that throws it must not escape into the HostSetup
        // startup path - that is the same crash class this mechanism exists to
        // close, arriving through the notification instead of the file.
        var dir = NewDir();
        WriteGlobal(dir, "{ broken");
        var invoked = 0;
        Action<SettingsFallback> boom = _ => { invoked++; throw new InvalidOperationException("callback blew up"); };

        var global = GlobalSettingsStore.Load(dir, boom);

        Assert.Equal(1, invoked);
        Assert.NotNull(global);
        Assert.Equal(MachineProfile.DefaultId, global.ActiveProfileId);
    }

    [Fact]
    public void A_throwing_callback_does_not_escape_TryLoad()
    {
        var dir = NewDir();
        WriteProfile(dir, "mill", "not json at all");
        var invoked = 0;
        Action<SettingsFallback> boom = _ => { invoked++; throw new InvalidOperationException("callback blew up"); };

        var profile = ProfilesStore.TryLoad(dir, "mill", boom);

        Assert.Equal(1, invoked);
        Assert.Null(profile);   // unchanged Try contract, no throw
    }

    [Fact]
    public void A_healthy_file_is_never_reported()
    {
        var dir = NewDir();
        WriteGlobal(dir, "{ \"ActiveProfileId\": \"mill\" }");
        var seen = Capture(out var onFallback);

        var global = GlobalSettingsStore.Load(dir, onFallback);

        Assert.Equal("mill", global.ActiveProfileId);
        Assert.Empty(seen);
    }

    [Fact]
    public void A_missing_file_is_a_fresh_install_not_a_fallback()
    {
        // Load returns before entering the try when the file is absent. That is
        // a first run, not a fallback: nothing to report, nothing to preserve.
        var dir = NewDir();
        var seen = Capture(out var onFallback);

        var global = GlobalSettingsStore.Load(dir, onFallback);

        Assert.NotNull(global);
        Assert.Empty(seen);
        Assert.False(File.Exists(ProfileSettingsPaths.GlobalPath(dir)));
    }

    [Fact]
    public void Loading_without_a_callback_still_works()
    {
        // The parameter is optional so the 123 existing `new SettingsService(...)`
        // and every direct loader call keep compiling. This pins that - both
        // loaders are given a CORRUPT file, so each one-argument call below is
        // proven to survive the fallback path, not just the absent-file path.
        var dir = NewDir();
        WriteGlobal(dir, "{ broken");
        WriteProfile(dir, "mill", "not json at all");

        Assert.NotNull(GlobalSettingsStore.Load(dir));
        Assert.Null(ProfilesStore.TryLoad(dir, "mill"));
    }
}
