/*
 * GrblSettingsViewModelBackupRestoreTests.cs - backup / restore / verify command tests
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

using System.Reactive.Subjects;
using CNC.Core.Abstractions;
using CNC.Core.Services;
using SenderNG.Core.Localization;
using SenderNG.Core.Services;
using SenderNG.Core.Settings;
using SenderNG.Modules.Fixed;

namespace SenderNG.Core.Tests;

[Collection("LocalizationState")] // serializes with the tests that mutate LocalizationService.Current
public class GrblSettingsViewModelBackupRestoreTests
{
    private sealed class StubProvider : IGrblSettingsProvider
    {
        public IReadOnlyList<string> ValueLines { get; set; } = [];
        public IReadOnlyList<string> DetailLines { get; set; } = [];
        public IReadOnlyList<string> GroupLines { get; set; } = [];

        /// <summary>When true every value read throws — models a failed <c>$$</c> re-read.</summary>
        public bool FailValueLines { get; set; }

        public Task<IReadOnlyList<string>> GetSettingLinesAsync() =>
            FailValueLines
                ? Task.FromException<IReadOnlyList<string>>(new InvalidOperationException("$$ read failed"))
                : Task.FromResult(ValueLines);

        public Task<IReadOnlyList<string>> GetSettingDetailLinesAsync() => Task.FromResult(DetailLines);
        public Task<IReadOnlyList<string>> GetSettingGroupLinesAsync() => Task.FromResult(GroupLines);
        public Task<string?> GetSettingDescriptionAsync(int id) => Task.FromResult<string?>(null);
    }

    private sealed class FakeGrbl : IGrblService
    {
        public List<string> Sent { get; } = [];
        public Dictionary<string, Exception> ThrowsByLine { get; } = new(StringComparer.Ordinal);
        public ControllerCapabilities Capabilities { get; init; } = new() { NumAxes = 3 };
        public bool IsConnected { get; private set; }
        public IObservable<MachineState> State { get; } = new Subject<MachineState>();
        public Task ConnectAsync(string port) { IsConnected = true; return Task.CompletedTask; }
        public void Disconnect() => IsConnected = false;
        public void SendRealtime(byte code) { }

        public Task SendAsync(string line)
        {
            Sent.Add(line);
            if (ThrowsByLine.TryGetValue(line, out var ex))
                throw ex;
            return Task.CompletedTask;
        }
    }

    /// <summary>Recording dialog double (pattern: MainViewModelTests.StubFileDialogs).</summary>
    private sealed class StubDialogs : IFileDialogService
    {
        public string? SavePath { get; set; }
        public string? OpenPath { get; set; }
        public string? LastSuggestedName { get; private set; }
        public string[] LastFilters { get; private set; } = [];

        public Task<string?> OpenFileAsync(string title, params string[] filters)
        {
            LastFilters = filters;
            return Task.FromResult(OpenPath);
        }

        public Task<string?> SaveFileAsync(string title, string suggestedFileName, params string[] filters)
        {
            LastSuggestedName = suggestedFileName;
            LastFilters = filters;
            return Task.FromResult(SavePath);
        }

        public Task ShowErrorAsync(string title, string message) => Task.CompletedTask;
    }

    private static readonly LocalizationService Loc = TestLocalization.Locale;

    private static GrblSettingsViewModel CreateVm(IGrblSettingsProvider provider, IGrblService grbl,
        IFileDialogService? dialogs = null) => new(provider, grbl, Loc, dialogs);

    private static GrblSettingItem Find(IEnumerable<object> nodes, int id) =>
        Flatten(nodes).Single(s => s.Id == id);

    private static IEnumerable<GrblSettingItem> Flatten(IEnumerable<object> nodes)
    {
        foreach (var node in nodes)
        {
            switch (node)
            {
                case GrblSettingItem setting:
                    yield return setting;
                    break;
                case GrblSettingGroupItem group:
                    foreach (var child in Flatten(group.Children))
                        yield return child;
                    break;
            }
        }
    }

    private static string TempFile(string extension)
    {
        var dir = Path.Combine(Path.GetTempPath(), "senderng-grbl-backup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "grbl-settings" + extension);
    }

    private static async Task<GrblSettingsBackup> ParseBackup(string path)
    {
        var result = GrblSettingsBackup.Parse(await File.ReadAllTextAsync(path));
        Assert.True(result.Ok, result.Error);
        return result.Backup!;
    }

    [Fact]
    public async Task Backup_writes_a_file_holding_the_controllers_current_settings()
    {
        var provider = new StubProvider { ValueLines = ["$0=10", "$110=800.000"] };
        var grbl = new FakeGrbl();
        await grbl.ConnectAsync("stub");
        var dialogs = new StubDialogs();
        var vm = CreateVm(provider, grbl, dialogs);
        await vm.LoadAsync();

        var path = TempFile(".json");
        dialogs.SavePath = path;

        await vm.BackupAsync();

        Assert.True(File.Exists(path));
        var backup = await ParseBackup(path);
        Assert.Equal([0, 110], backup.Settings.Select(s => s.Id).ToList());
        Assert.Equal("10", backup.Settings.Single(s => s.Id == 0).Value);
        Assert.Equal("800.000", backup.Settings.Single(s => s.Id == 110).Value);
        Assert.Contains("2 settings", vm.Status);
        Assert.Contains(path, vm.Status);
    }

    [Fact]
    public async Task Backup_rereads_the_controller_and_ignores_unsaved_edits()
    {
        var provider = new StubProvider { ValueLines = ["$0=10"] };
        var grbl = new FakeGrbl();
        await grbl.ConnectAsync("stub");
        var dialogs = new StubDialogs();
        var vm = CreateVm(provider, grbl, dialogs);
        await vm.LoadAsync();

        var item = Find(vm.Groups, 0);
        item.Value = "999"; // unsaved editor edit — must never reach the file
        provider.ValueLines = ["$0=20"]; // the controller's real current value

        var path = TempFile(".json");
        dialogs.SavePath = path;

        await vm.BackupAsync();

        var backup = await ParseBackup(path);
        Assert.Equal("20", backup.Settings.Single(s => s.Id == 0).Value);
        Assert.True(item.IsDirty); // and the unsaved edit survives untouched
        Assert.Equal("999", item.Value);
    }

    [Fact]
    public async Task Backup_reports_how_many_unsaved_edits_it_excluded()
    {
        var provider = new StubProvider { ValueLines = ["$0=10", "$110=800.000"] };
        var grbl = new FakeGrbl();
        await grbl.ConnectAsync("stub");
        var dialogs = new StubDialogs();
        var vm = CreateVm(provider, grbl, dialogs);
        await vm.LoadAsync();

        Find(vm.Groups, 0).Value = "999";

        var path = TempFile(".json");
        dialogs.SavePath = path;

        await vm.BackupAsync();

        Assert.Contains("1 unsaved edits were not included", vm.Status);
    }

    [Fact]
    public async Task Backup_is_blocked_with_a_message_when_not_connected()
    {
        var provider = new StubProvider { ValueLines = ["$0=10"] };
        var dialogs = new StubDialogs();
        var vm = CreateVm(provider, new FakeGrbl(), dialogs); // never connected
        await vm.LoadAsync();

        var path = TempFile(".json");
        dialogs.SavePath = path;

        await vm.BackupAsync();

        Assert.False(File.Exists(path));
        Assert.Equal("Connect to a controller first.", vm.Status);
    }

    [Fact]
    public async Task Backup_aborts_when_the_controller_reports_no_settings()
    {
        var provider = new StubProvider { ValueLines = [] };
        var grbl = new FakeGrbl();
        await grbl.ConnectAsync("stub");
        var dialogs = new StubDialogs();
        var vm = CreateVm(provider, grbl, dialogs);
        await vm.LoadAsync();

        var path = TempFile(".json");
        dialogs.SavePath = path;

        await vm.BackupAsync();

        Assert.False(File.Exists(path));
        Assert.Equal("The controller reported no settings.", vm.Status);
    }

    [Fact]
    public async Task Backup_failure_from_the_dialog_writes_nothing()
    {
        var provider = new StubProvider { ValueLines = ["$0=10"] };
        var grbl = new FakeGrbl();
        await grbl.ConnectAsync("stub");
        var dialogs = new StubDialogs(); // OpenPath/SavePath stay null → cancelled
        var vm = CreateVm(provider, grbl, dialogs);
        await vm.LoadAsync();

        var before = vm.Status;
        await vm.BackupAsync();

        Assert.Equal(before, vm.Status);
        Assert.False(string.IsNullOrWhiteSpace(dialogs.LastSuggestedName)); // the picker was offered
    }

    [Fact]
    public async Task Restore_parses_the_file_and_shows_a_plan_without_sending()
    {
        var provider = new StubProvider { ValueLines = ["$0=10", "$110=800.000"] };
        var grbl = new FakeGrbl();
        await grbl.ConnectAsync("stub");
        var dialogs = new StubDialogs();
        var vm = CreateVm(provider, grbl, dialogs);
        await vm.LoadAsync();

        var path = TempFile(".json");
        await File.WriteAllTextAsync(path, BackupJson(("0", "50"), ("110", "800.000"), ("999", "1")));
        dialogs.OpenPath = path;

        await vm.RestoreAsync();

        Assert.True(vm.HasRestorePlan);
        Assert.Empty(grbl.Sent); // nothing reaches the controller before Apply
        Assert.NotNull(vm.RestoreSummary);
        Assert.Contains("Restore 1 of 3 settings?", vm.RestoreSummary); // 1 to write, 1 unchanged, 1 unknown
        Assert.Contains("1 not on this controller", vm.RestoreSummary);
        Assert.DoesNotContain("999", vm.RestoreSummary); // unknown ids are counted, not listed
    }

    [Fact]
    public async Task Restore_rejects_a_foreign_file_with_a_message()
    {
        var provider = new StubProvider { ValueLines = ["$0=10"] };
        var grbl = new FakeGrbl();
        await grbl.ConnectAsync("stub");
        var dialogs = new StubDialogs();
        var vm = CreateVm(provider, grbl, dialogs);
        await vm.LoadAsync();

        var path = TempFile(".json");
        await File.WriteAllTextAsync(path, "{\"kind\":\"senderng-app-settings\",\"formatVersion\":1,\"files\":{}}");
        dialogs.OpenPath = path;

        await vm.RestoreAsync();

        Assert.False(vm.HasRestorePlan);
        Assert.Contains("Not a SenderNG grbl settings backup", vm.Status);
        Assert.Empty(grbl.Sent);
    }

    [Fact]
    public async Task ApplyRestore_sends_only_the_planned_settings_in_ascending_id_order()
    {
        var provider = new StubProvider { ValueLines = ["$0=10", "$110=800.000", "$100=250.000"] };
        var grbl = new FakeGrbl();
        await grbl.ConnectAsync("stub");
        var dialogs = new StubDialogs();
        var vm = CreateVm(provider, grbl, dialogs);
        await vm.LoadAsync();

        var path = TempFile(".json");
        await File.WriteAllTextAsync(path, BackupJson(("100", "300.000"), ("0", "10"), ("110", "900.000")));
        dialogs.OpenPath = path;
        await vm.RestoreAsync();

        await vm.ApplyRestoreAsync();

        Assert.Equal(["$100=300.000", "$110=900.000"], grbl.Sent); // $0 is unchanged and never written
        Assert.False(vm.HasRestorePlan); // the plan is consumed by the apply
    }

    [Fact]
    public async Task ApplyRestore_continues_past_a_rejected_setting_and_reports_it()
    {
        var provider = new StubProvider { ValueLines = ["$0=10", "$110=800.000", "$100=250.000"] };
        var grbl = new FakeGrbl();
        await grbl.ConnectAsync("stub");
        // A controller rejection throws with the raw reply, exactly like RealGrblService.
        grbl.ThrowsByLine["$110=900.000"] = new InvalidOperationException("error:52");
        var dialogs = new StubDialogs();
        var vm = CreateVm(provider, grbl, dialogs);
        await vm.LoadAsync();

        var path = TempFile(".json");
        await File.WriteAllTextAsync(path, BackupJson(("0", "11"), ("100", "300.000"), ("110", "900.000")));
        dialogs.OpenPath = path;
        await vm.RestoreAsync();

        provider.ValueLines = ["$0=11", "$100=300.000", "$110=800.000"];
        await vm.ApplyRestoreAsync();

        Assert.Equal(["$0=11", "$100=300.000", "$110=900.000"], grbl.Sent); // the rejection did not stop the rest
        Assert.Contains("$110", vm.Status);
        Assert.Contains("out of range", vm.Status); // GrblErrorCodes.Describe(52)
        Assert.Contains("Restored 2 settings", vm.Status); // only the ones that landed
    }

    [Fact]
    public async Task ApplyRestore_aborts_when_the_link_drops()
    {
        var provider = new StubProvider { ValueLines = ["$0=10", "$100=250.000", "$110=800.000"] };
        var grbl = new FakeGrbl();
        await grbl.ConnectAsync("stub");
        grbl.ThrowsByLine["$110=900.000"] = new InvalidOperationException("Not connected.");
        var dialogs = new StubDialogs();
        var vm = CreateVm(provider, grbl, dialogs);
        await vm.LoadAsync();

        var path = TempFile(".json");
        await File.WriteAllTextAsync(path, BackupJson(("0", "11"), ("100", "300.000"), ("110", "900.000")));
        dialogs.OpenPath = path;
        await vm.RestoreAsync();

        await vm.ApplyRestoreAsync();

        Assert.Equal(["$0=11", "$100=300.000", "$110=900.000"], grbl.Sent); // nothing after the drop
        Assert.Contains("$110", vm.Status);
        Assert.Contains("Not connected.", vm.Status);
        Assert.Contains("Restored 2 settings", vm.Status); // reports how many landed before the drop
    }

    [Fact]
    public async Task ApplyRestore_verifies_and_reports_settings_that_did_not_stick()
    {
        var provider = new StubProvider { ValueLines = ["$0=10"] };
        var grbl = new FakeGrbl();
        await grbl.ConnectAsync("stub");
        var dialogs = new StubDialogs();
        var vm = CreateVm(provider, grbl, dialogs);
        await vm.LoadAsync();

        var path = TempFile(".json");
        await File.WriteAllTextAsync(path, BackupJson(("0", "11")));
        dialogs.OpenPath = path;
        await vm.RestoreAsync();

        provider.ValueLines = ["$0=10"]; // the write did not take
        await vm.ApplyRestoreAsync();

        Assert.Contains("1 settings did not take the restored value", vm.Status);
    }

    [Fact]
    public async Task The_verify_mismatch_message_names_the_settings_when_the_catalog_is_active()
    {
        // The real app always has LocalizationService.Current set (HostSetup), so the status
        // text comes from the catalog, not the inline fallback. Regression for the catalog
        // dropping the "{1}" the call site passes: the failed setting ids vanished for real
        // users while every other test in this file (no Current => fallback) stayed green.
        var prev = LocalizationService.Current;
        LocalizationService.Current = TestLocalization.Locale;
        try
        {
            var provider = new StubProvider { ValueLines = ["$0=10"] };
            var grbl = new FakeGrbl();
            await grbl.ConnectAsync("stub");
            var dialogs = new StubDialogs();
            var vm = CreateVm(provider, grbl, dialogs);
            await vm.LoadAsync();

            var path = TempFile(".json");
            await File.WriteAllTextAsync(path, BackupJson(("0", "11")));
            dialogs.OpenPath = path;
            await vm.RestoreAsync();

            provider.ValueLines = ["$0=10"]; // the write did not take
            await vm.ApplyRestoreAsync();

            Assert.Contains("did not take the restored value", vm.Status);
            Assert.Contains("$0", vm.Status); // the id the catalog used to drop
        }
        finally
        {
            LocalizationService.Current = prev;
        }
    }

    [Fact]
    public async Task ApplyRestore_reports_a_failed_verify_read_instead_of_claiming_a_mismatch()
    {
        var provider = new StubProvider { ValueLines = ["$0=10"] };
        var grbl = new FakeGrbl();
        await grbl.ConnectAsync("stub");
        var dialogs = new StubDialogs();
        var vm = CreateVm(provider, grbl, dialogs);
        await vm.LoadAsync();

        var path = TempFile(".json");
        await File.WriteAllTextAsync(path, BackupJson(("0", "11")));
        dialogs.OpenPath = path;
        await vm.RestoreAsync();

        // The write lands, but the verifying $$ re-read fails (dropped link, garbage reply).
        provider.FailValueLines = true;
        await vm.ApplyRestoreAsync();

        Assert.Equal(["$0=11"], grbl.Sent);
        Assert.Contains("Restored 1 settings", vm.Status); // the write did land
        Assert.Contains("Load failed", vm.Status); // and the re-read failure is what is reported
        // A failed re-read leaves the pre-reload values in the tree, so claiming the value
        // "did not stick" would be a fabrication.
        Assert.DoesNotContain("did not take the restored value", vm.Status);
    }

    [Fact]
    public async Task ApplyRestore_reports_reboot_required_settings()
    {
        // A hand-built $ES line: reboot flag in field 8 (see GrblSettingsParser.ParseSettingLine).
        var provider = new StubProvider
        {
            ValueLines = ["$38=0"],
            DetailLines = ["[SETTING:38|35|Spindle RPM|rpm|5|#0|0||1|0]"],
        };
        var grbl = new FakeGrbl();
        await grbl.ConnectAsync("stub");
        var dialogs = new StubDialogs();
        var vm = CreateVm(provider, grbl, dialogs);
        await vm.LoadAsync();
        Assert.True(Find(vm.Groups, 38).RebootRequired, "sanity: the metadata must mark $38 reboot-required");

        var path = TempFile(".json");
        await File.WriteAllTextAsync(path, BackupJson(("38", "1000")));
        dialogs.OpenPath = path;
        await vm.RestoreAsync();

        provider.ValueLines = ["$38=1000"];
        await vm.ApplyRestoreAsync();

        Assert.Equal(["$38=1000"], grbl.Sent);
        Assert.Contains("controller restart", vm.Status); // module.grbl-settings.rebootRequired
    }

    [Fact]
    public async Task ApplyRestore_refreshes_the_tree_from_the_board()
    {
        var provider = new StubProvider { ValueLines = ["$0=10", "$110=800.000"] };
        var grbl = new FakeGrbl();
        await grbl.ConnectAsync("stub");
        var dialogs = new StubDialogs();
        var vm = CreateVm(provider, grbl, dialogs);
        await vm.LoadAsync();

        var path = TempFile(".json");
        await File.WriteAllTextAsync(path, BackupJson(("0", "11"), ("110", "900.000")));
        dialogs.OpenPath = path;
        await vm.RestoreAsync();

        provider.ValueLines = ["$0=11", "$110=900.000"];
        await vm.ApplyRestoreAsync();

        Assert.Equal("11", Find(vm.Groups, 0).Value);
        Assert.Equal("900.000", Find(vm.Groups, 110).Value);
        Assert.All(Flatten(vm.Groups), static i => Assert.False(i.IsDirty));
    }

    [Fact]
    public async Task The_plan_warns_about_a_mismatch_with_the_backups_identity_not_the_item_count()
    {
        var provider = new StubProvider { ValueLines = ["$0=10"] };
        var grbl = new FakeGrbl();
        await grbl.ConnectAsync("stub");
        var dialogs = new StubDialogs();
        var vm = CreateVm(provider, grbl, dialogs);
        await vm.LoadAsync();

        var path = TempFile(".json");
        await File.WriteAllTextAsync(path, BackupJson(("0", "11")));
        dialogs.OpenPath = path;
        await vm.RestoreAsync();

        // The file's fingerprint ("demo 1") differs from this board's, and the plan holds
        // exactly one item — the warning must name the backup, not the count.
        Assert.NotNull(vm.RestoreSummary);
        Assert.Contains("different controller (demo 1)", vm.RestoreSummary);
        Assert.DoesNotContain("different controller (1)", vm.RestoreSummary);
    }

    [Fact]
    public async Task CancelRestore_discards_the_plan_and_sends_nothing()
    {
        var provider = new StubProvider { ValueLines = ["$0=10"] };
        var grbl = new FakeGrbl();
        await grbl.ConnectAsync("stub");
        var dialogs = new StubDialogs();
        var vm = CreateVm(provider, grbl, dialogs);
        await vm.LoadAsync();

        var path = TempFile(".json");
        await File.WriteAllTextAsync(path, BackupJson(("0", "11")));
        dialogs.OpenPath = path;
        await vm.RestoreAsync();
        Assert.True(vm.HasRestorePlan);

        vm.CancelRestoreCommand.Execute(null);

        Assert.False(vm.HasRestorePlan);
        Assert.Null(vm.RestoreSummary);
        Assert.Empty(grbl.Sent);
    }

    [Fact]
    public async Task The_plan_warns_that_unsaved_edits_will_be_discarded()
    {
        var provider = new StubProvider { ValueLines = ["$0=10", "$110=800.000"] };
        var grbl = new FakeGrbl();
        await grbl.ConnectAsync("stub");
        var dialogs = new StubDialogs();
        var vm = CreateVm(provider, grbl, dialogs);
        await vm.LoadAsync();

        Find(vm.Groups, 110).Value = "999";

        var path = TempFile(".json");
        await File.WriteAllTextAsync(path, BackupJson(("0", "11")));
        dialogs.OpenPath = path;
        await vm.RestoreAsync();

        Assert.Contains("1 unsaved edits will be discarded", vm.RestoreSummary);
    }

    [Fact]
    public async Task CanTransfer_tracks_the_connection_state()
    {
        var provider = new StubProvider { ValueLines = ["$0=10"] };
        var grbl = new FakeGrbl();
        var dialogs = new StubDialogs();
        var vm = CreateVm(provider, grbl, dialogs);
        await vm.LoadAsync();
        Assert.False(vm.CanTransfer); // never connected

        await grbl.ConnectAsync("stub");
        Assert.True(vm.CanTransfer);

        var path = TempFile(".json");
        dialogs.SavePath = path;
        await vm.BackupAsync();
        Assert.True(File.Exists(path)); // the same gate lets a connected board through
    }

    [Fact]
    public async Task The_transfer_button_labels_come_from_the_locale_catalog()
    {
        var vm = CreateVm(new StubProvider { ValueLines = ["$0=10"] }, new FakeGrbl(), new StubDialogs());
        await vm.LoadAsync();

        Assert.Equal("Back up…", vm.BackupText);
        Assert.Equal("Restore…", vm.RestoreText);
        Assert.Equal("Apply restore", vm.ApplyRestoreText);
        Assert.Equal("Cancel", vm.CancelRestoreText);
        Assert.Equal("Load", vm.LoadText);
        Assert.Equal("Save changes", vm.SaveText);
    }

    [Fact]
    public async Task A_demo_round_trip_restores_the_board()
    {
        var settingsPath = TempFile(".txt");
        var demo = new DemoGrblService(new MachineProfile(), settingsFilePath: settingsPath, localization: Loc);
        await demo.ConnectAsync("demo");

        var dialogs = new StubDialogs();
        var vm = CreateVm(demo, demo, dialogs);
        await vm.LoadAsync();

        var item = Find(vm.Groups, 110);
        var original = item.Value;

        var backupPath = TempFile(".json");
        dialogs.SavePath = backupPath;
        await vm.BackupAsync();
        Assert.True(File.Exists(backupPath));

        // Change the controller's value behind the editor's back.
        await demo.SendAsync("$110=999");
        var changed = (await demo.GetSettingLinesAsync()).Single(l => l.StartsWith("$110=", StringComparison.Ordinal));
        Assert.Equal("$110=999", changed);
        await vm.LoadAsync(); // the plan diffs against what the board now holds

        dialogs.OpenPath = backupPath;
        await vm.RestoreAsync();
        Assert.True(vm.HasRestorePlan);
        await vm.ApplyRestoreAsync();

        var line = (await demo.GetSettingLinesAsync())
            .Single(l => l.StartsWith("$110=", StringComparison.Ordinal));
        Assert.Equal($"$110={original}", line);
    }

    /// <summary>A minimal valid backup file holding the given id/value pairs.</summary>
    private static string BackupJson(params (string Id, string Value)[] settings)
    {
        var entries = string.Join(",", settings.Select(s =>
            $"{{\"id\":{s.Id},\"value\":\"{s.Value}\",\"name\":\"n\",\"unit\":\"\",\"type\":6,\"format\":\"\",\"min\":null,\"max\":null,\"rebootRequired\":false}}"));
        return "{\"kind\":\"senderng-grbl-settings\",\"formatVersion\":1,\"createdUtc\":\"2026-10-13T00:00:00Z\","
            + "\"appVersion\":\"test\",\"controller\":{\"key\":\"demo\",\"firmware\":\"demo\",\"version\":\"1\",\"build\":0,"
            + "\"numAxes\":3,\"isGrblHAL\":true,\"metadataFromController\":true},\"settings\":[" + entries + "]}";
    }
}
