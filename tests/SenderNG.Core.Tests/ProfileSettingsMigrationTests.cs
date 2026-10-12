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

using System.Text.Json;
using System.Text.Json.Nodes;
using CNC.Core.Services;

namespace SenderNG.Core.Tests;

/// <summary>
/// Tests for the two-tier settings layout (global.json + profiles/&lt;id&gt;.json)
/// and the one-time migration of a legacy settings.json.
/// </summary>
public class ProfileSettingsMigrationTests
{
    private static string NewDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "senderng-migr-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static string Read(string dir, string file) => File.ReadAllText(Path.Combine(dir, file));

    [Fact]
    public void Migration_splits_mode_blocks_and_module_sections()
    {
        var dir = NewDir();
        // A modern mode-scoped file: Foam active, distinct Mill/Foam blocks,
        // mode-nested Jog, shared module sections, connection "last" state.
        File.WriteAllText(Path.Combine(dir, "settings.json"), """
            {
              "Mode": "Foam",
              "Language": "ru",
              "RecentFiles": ["C:/a.nc", "C:/b.nc"],
              "NetworkAvailable": false,
              "NetworkEndpoint": "10.0.0.9:23",
              "LastSerialPort": "COM7",
              "LastSerialBaudRate": 250000,
              "LastNetworkEndpoint": "",
              "SerialResetMode": 1,
              "Modes": {
                "Mill": {
                  "DemoAxes": 3,
                  "Port": "COM3",
                  "BaudRate": 115200,
                  "ModulesVisibility": { "jog": false },
                  "VisibleTools": 8
                },
                "Foam": {
                  "DemoAxes": 4,
                  "Port": "auto",
                  "BaudRate": 115200,
                  "ModulesVisibility": { "macro": false },
                  "VisibleTools": 6
                }
              },
              "Modules": {
                "Jog": {
                  "Mill": { "Distances": [0.1, 1, 10], "Speeds": [500, 1000, 2000], "DistanceIndex": 1, "SpeedIndex": 1, "Diagonals": true },
                  "Foam": { "Distances": [1, 5, 25], "Speeds": [300, 600, 1200], "DistanceIndex": 0, "SpeedIndex": 2, "Diagonals": false }
                },
                "Console": { "Verbose": true, "FilterRealtime": false, "ShowAllRealtime": true, "FilterOk": false },
                "Goto": { "AxisCount": 4 },
                "Offsets": { "ShowToolTable": false, "ToolNames": { "grblHAL-xyz": { "1": "3mm flat" } } },
                "Outline": { "FeedRate": 700.0 },
                "Macros": [ { "Name": "Home foam", "Code": "$H" } ],
                "Probing": { "ProfilesFile": "C:/old/probing.json", "EnabledTools": { "tool-length": true, "edge-external": false } },
                "Dummy": []
              }
            }
            """);
        File.WriteAllText(Path.Combine(dir, "probing.json"),
            """{ "Profiles": [ { "Name": "default", "ProbeDiameter": 2.0 } ] }""");
        var legacy = Read(dir, "settings.json");

        var ids = SettingsMigrator.MigrateIfNeeded(dir);

        Assert.Equal(["foam", "mill"], ids.OrderBy(x => x).ToArray());
        // Legacy file is never deleted.
        Assert.Equal(legacy, Read(dir, "settings.json"));

        // ---- global tier ----
        var g = GlobalSettingsStore.Load(dir);
        Assert.Equal("ru", g.Language);
        Assert.Equal("foam", g.ActiveProfileId);
        Assert.Equal(["C:/a.nc", "C:/b.nc"], g.RecentFiles);
        Assert.Equal(10, g.MaxRecentFiles);
        Assert.False(g.NetworkAvailable);
        Assert.Equal("Mill", g.Profiles["mill"]);
        Assert.Equal("Foam", g.Profiles["foam"]);
        var consoleJson = g.ModuleSettings["Console"].ToString();
        Assert.Contains("Verbose", consoleJson);
        Assert.Contains("FilterOk", consoleJson);

        // ---- profile files ----
        Assert.True(ProfilesStore.Exists(dir, "mill"));
        Assert.True(ProfilesStore.Exists(dir, "foam"));
        Assert.Equal(["foam", "mill"], ProfilesStore.Ids(dir).OrderBy(x => x).ToArray());

        var foam = ProfilesStore.TryLoad(dir, "foam")!;
        Assert.Equal("Foam", foam.Name);
        Assert.Equal(MachineTypes.Foam, foam.MachineTypeId);
        Assert.Equal(4, foam.DemoAxes);
        Assert.Equal(6, foam.VisibleTools);
        Assert.False(foam.Modules["macro"]);
        // Last-used machine (Foam was active) gets the connection "last" state…
        Assert.Equal("COM7", foam.Connection.LastSerialPort);
        Assert.Equal(250000, foam.Connection.LastSerialBaudRate);
        // …and a manually configured NetworkEndpoint seeds the initial last-endpoint.
        Assert.Equal("10.0.0.9:23", foam.Connection.LastNetworkEndpoint);
        Assert.Equal(1, (int)foam.Connection.SerialResetMode);
        Assert.Equal("auto", foam.Connection.SerialPort);

        var mill = ProfilesStore.TryLoad(dir, "mill")!;
        Assert.Equal(MachineTypes.Mill, mill.MachineTypeId);
        Assert.Equal(3, mill.DemoAxes);
        Assert.Equal("COM3", mill.Connection.SerialPort);
        Assert.False(mill.Modules["jog"]);
        // The non-active profile carries no connection "last" state.
        Assert.Empty(mill.Connection.LastSerialPort);
        Assert.Equal(0, mill.Connection.LastSerialBaudRate);

        // ---- module sections ----
        // Jog unwrapped per profile: foam distances are [1,5,25], mill [0.1,1,10].
        Assert.Equal([1d, 5d, 25d], JogDistances(foam));
        Assert.Equal([0.1d, 1d, 10d], JogDistances(mill));
        Assert.Equal(0, JogNumber(foam, "DistanceIndex"));
        Assert.Equal(2, JogNumber(foam, "SpeedIndex"));
        Assert.Equal(1, JogNumber(mill, "DistanceIndex"));

        // Shared sections copied into both profiles.
        foreach (var profile in new[] { mill, foam })
        {
            Assert.Contains("AxisCount", profile.ModuleSettings["Goto"].ToString());
            Assert.Contains("grblHAL-xyz", profile.ModuleSettings["Offsets"].ToString());
            Assert.Contains("3mm flat", profile.ModuleSettings["Offsets"].ToString());
            Assert.Contains("Home foam", profile.ModuleSettings["Macros"].ToString());
            Assert.Contains("FeedRate", profile.ModuleSettings["Outline"].ToString());
            // Probing keeps EnabledTools but drops the ProfilesFile override.
            var probing = profile.ModuleSettings["Probing"].ToString();
            Assert.DoesNotContain("ProfilesFile", probing);
            Assert.DoesNotContain("C:/old/probing.json", probing);
            Assert.Contains("edge-external", probing);
        }

        // Per-profile probing companions derived from the old probing.json.
        Assert.True(File.Exists(ProfileSettingsPaths.CompanionPath(dir, "mill", ".probing.json")));
        Assert.True(File.Exists(ProfileSettingsPaths.CompanionPath(dir, "foam", ".probing.json")));
        Assert.Contains("ProbeDiameter", Read(dir, Path.Combine("profiles", "mill.probing.json")));
    }

    private static double[] JogDistances(MachineProfile profile) =>
        JogObject(profile)["Distances"]!.AsArray().Select(n => n!.GetValue<double>()).ToArray();

    private static int JogNumber(MachineProfile profile, string key) =>
        (int)JogObject(profile)[key]!.GetValue<double>();

    private static JsonObject JogObject(MachineProfile profile) =>
        JsonNode.Parse(profile.ModuleSettings["Jog"].ToString()!) as JsonObject ?? throw new InvalidOperationException("Jog section missing");

    [Fact]
    public void Migration_seeds_flat_legacy_file_into_mill_and_defaults_foam()
    {
        var dir = NewDir();
        File.WriteAllText(Path.Combine(dir, "settings.json"), """
            {
              "Language": "",
              "DemoAxes": 5,
              "Port": "COM9",
              "BaudRate": 230400,
              "ModulesVisibility": { "console": false },
              "RecentFiles": [],
              "Modules": {}
            }
            """);

        SettingsMigrator.MigrateIfNeeded(dir);

        var g = GlobalSettingsStore.Load(dir);
        Assert.Equal("mill", g.ActiveProfileId);
        var mill = ProfilesStore.TryLoad(dir, "mill")!;
        Assert.Equal(MachineTypes.Mill, mill.MachineTypeId);
        Assert.Equal(5, mill.DemoAxes);
        Assert.Equal("COM9", mill.Connection.SerialPort);
        Assert.Equal(230400, mill.Connection.SerialBaudRate);
        Assert.False(mill.Modules["console"]);
        // Foam profile: fresh defaults.
        var foam = ProfilesStore.TryLoad(dir, "foam")!;
        Assert.Equal(3, foam.DemoAxes);
        Assert.Equal("auto", foam.Connection.SerialPort);
        Assert.Equal(8, foam.VisibleTools);
    }

    [Fact]
    public void Migration_is_inert_when_global_json_exists_or_no_legacy_file()
    {
        // global.json present → legacy is ignored.
        var dir = NewDir();
        File.WriteAllText(Path.Combine(dir, "settings.json"), """{ "Mode": "Foam", "Modes": {} }""");
        GlobalSettingsStore.Save(dir, new GlobalSettings { ActiveProfileId = "custom" });
        Assert.Empty(SettingsMigrator.MigrateIfNeeded(dir));
        Assert.False(ProfilesStore.Exists(dir, "mill"));
        Assert.Equal("custom", GlobalSettingsStore.Load(dir).ActiveProfileId);

        // Fresh dir (no legacy) → nothing created.
        var fresh = NewDir();
        Assert.Empty(SettingsMigrator.MigrateIfNeeded(fresh));
        Assert.False(GlobalSettingsStore.Exists(fresh));
    }

    [Fact]
    public void Migration_is_idempotent()
    {
        var dir = NewDir();
        File.WriteAllText(Path.Combine(dir, "settings.json"), """{ "Mode": "Mill" }""");
        Assert.NotEmpty(SettingsMigrator.MigrateIfNeeded(dir));
        Assert.Empty(SettingsMigrator.MigrateIfNeeded(dir));
    }

    [Fact]
    public void Migration_throws_on_unparseable_legacy_file()
    {
        var dir = NewDir();
        File.WriteAllText(Path.Combine(dir, "settings.json"), "{ not json ");
        Assert.Throws<InvalidDataException>(() => SettingsMigrator.MigrateIfNeeded(dir));
    }

    // ---------------------------------------------------------------- stores

    [Fact]
    public void Stores_roundtrip_global_and_profile()
    {
        var dir = NewDir();

        var global = new GlobalSettings
        {
            Language = "ru",
            ActiveProfileId = "mill-2",
            RecentFiles = ["a", "b"],
            MaxRecentFiles = 5,
            Profiles = new Dictionary<string, string> { ["mill-2"] = "Big mill" },
            ModuleSettings = new Dictionary<string, object> { ["Console"] = new Dictionary<string, object> { ["Verbose"] = true } },
        };
        GlobalSettingsStore.Save(dir, global);
        var reloaded = GlobalSettingsStore.Load(dir);
        Assert.Equal("ru", reloaded.Language);
        Assert.Equal("mill-2", reloaded.ActiveProfileId);
        Assert.Equal(["a", "b"], reloaded.RecentFiles);
        Assert.Equal(5, reloaded.MaxRecentFiles);
        Assert.Equal("Big mill", reloaded.Profiles["mill-2"]);
        Assert.Contains("Verbose", reloaded.ModuleSettings["Console"].ToString());

        var profile = new MachineProfile
        {
            Id = "mill-2",
            Name = "Big mill",
            MachineTypeId = MachineTypes.Mill,
            DemoAxes = 5,
            VisibleTools = 12,
            Modules = new Dictionary<string, bool> { ["macro"] = false },
            Connection = new MachineConnection { SerialPort = "COM4", LastSerialPort = "COM4", LastSerialBaudRate = 250000 },
            ModuleSettings = new Dictionary<string, object> { ["Goto"] = new Dictionary<string, object> { ["AxisCount"] = 4 } },
        };
        ProfilesStore.Save(dir, profile);
        var p = ProfilesStore.TryLoad(dir, "mill-2")!;
        Assert.Equal("Big mill", p.Name);
        Assert.Equal(5, p.DemoAxes);
        Assert.Equal(12, p.VisibleTools);
        Assert.False(p.Modules["macro"]);
        Assert.Equal("COM4", p.Connection.LastSerialPort);
        Assert.Equal(250000, p.Connection.LastSerialBaudRate);
        Assert.Contains("AxisCount", p.ModuleSettings["Goto"].ToString());
        Assert.Contains("mill-2", ProfilesStore.Ids(dir));
        Assert.Equal(MachineTypes.Mill, p.MachineTypeId);
    }

    [Fact]
    public void Profile_ids_exclude_probing_companions()
    {
        var dir = NewDir();
        ProfilesStore.Save(dir, new MachineProfile { Id = "mill", Name = "Mill" });
        ProfilesStore.Save(dir, new MachineProfile { Id = "foam", Name = "Foam" });
        File.WriteAllText(ProfileSettingsPaths.CompanionPath(dir, "mill", ".probing.json"), "{}");

        Assert.Equal(["foam", "mill"], ProfilesStore.Ids(dir).OrderBy(x => x).ToArray());
        Assert.False(File.Exists(Path.Combine(dir, "settings.json")));
        _ = JsonSerializer.Serialize(new MachineProfile()); // model round-trips cleanly
    }
}
