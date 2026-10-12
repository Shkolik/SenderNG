/*

Copyright (c) 2026 Andrew Shkolik

All rights reserved.

Redistribution and use in source and binary forms, with or without modification,
are permitted provided that the following conditions are met:

· Redistributions of source code must retain the above copyright notice, this
list of conditions and the following disclaimer.

· Redistributions in binary form must reproduce the above copyright notice,
this list of conditions and the following disclaimer in the documentation and/or
other materials provided with the distribution.

· Neither the name of the copyright holder nor the names of its contributors may
be used to endorse or promote products derived from this software without
specific prior written permission.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND
ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE FOR
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
/// A corrupt, truncated or hand-edited settings file must fall back to
/// defaults, never throw. These reach the startup path through
/// <see cref="SettingsService"/>.
/// </summary>
public class ProfileSettingsStoreCorruptionTests
{
    private static string NewDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "senderng-corrupt-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void WriteGlobal(string dir, string content) =>
        File.WriteAllText(ProfileSettingsPaths.GlobalPath(dir), content);

    private static void WriteProfile(string dir, string id, string content)
    {
        Directory.CreateDirectory(ProfileSettingsPaths.ProfilesDir(dir));
        File.WriteAllText(ProfileSettingsPaths.ProfilePath(dir, id), content);
    }

    [Fact]
    public void GlobalSettingsStore_returns_defaults_for_truncated_json()
    {
        var dir = NewDir();
        WriteGlobal(dir, "{ \"activeProfileId\": \"mill\"");

        var global = GlobalSettingsStore.Load(dir);

        Assert.NotNull(global);
        Assert.Equal(MachineProfile.DefaultId, global.ActiveProfileId);
    }

    [Fact]
    public void GlobalSettingsStore_returns_defaults_when_the_file_is_not_an_object()
    {
        var dir = NewDir();
        WriteGlobal(dir, "this is not json");

        var global = GlobalSettingsStore.Load(dir);

        Assert.NotNull(global);
        Assert.Equal(MachineProfile.DefaultId, global.ActiveProfileId);
    }

    [Fact]
    public void GlobalSettingsStore_returns_defaults_for_a_json_null_literal()
    {
        var dir = NewDir();
        WriteGlobal(dir, "null");

        var global = GlobalSettingsStore.Load(dir);

        Assert.NotNull(global);
        Assert.Equal(MachineProfile.DefaultId, global.ActiveProfileId);
    }

    [Fact]
    public void ProfilesStore_TryLoad_returns_null_for_truncated_json()
    {
        var dir = NewDir();
        WriteProfile(dir, "mill", "{ \"id\": \"mill\", \"name\": ");

        Assert.Null(ProfilesStore.TryLoad(dir, "mill"));
    }

    [Fact]
    public void ProfilesStore_TryLoad_returns_null_for_a_missing_file()
    {
        var dir = NewDir();

        Assert.Null(ProfilesStore.TryLoad(dir, "mill"));
    }

    [Fact]
    public void SettingsService_Load_survives_a_corrupt_global_file()
    {
        var dir = NewDir();
        WriteGlobal(dir, "{ \"language\": ");

        var profile = new SettingsService(dir).Load();

        Assert.NotNull(profile);
        Assert.Equal(MachineProfile.DefaultId, profile.Id);
    }

    [Fact]
    public void SettingsService_Load_falls_back_when_the_active_profile_file_is_corrupt()
    {
        var dir = NewDir();
        WriteGlobal(dir, "{ \"activeProfileId\": \"mill\" }");
        WriteProfile(dir, "mill", "{ \"id\": \"mill\"");

        var profile = new SettingsService(dir).Load();

        Assert.NotNull(profile);
    }
}
