
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

using SenderNG.Core.Settings;
using SenderNG.Modules.Fixed;

namespace SenderNG.Core.Tests;

/// <summary>
/// The pure restore planner: a backup diffed against the loaded board settings.
/// Everything but the types themselves is exercised through the public Core surface —
/// the planner is <c>internal</c> in the app assembly, reached via InternalsVisibleTo.
/// </summary>
public class GrblRestorePlanTests
{
    private static GrblSettingItem BoardItem(int id, GrblSettingType type, string value,
        double? min = null, double? max = null, string format = "") =>
        new(new GrblSetting
        {
            Id = id, Name = $"S{id}", DataType = type, Value = value,
            Min = min, Max = max, Format = format,
        }, numAxes: 3);

    private static GrblBackupSetting BackupSetting(int id, string value) =>
        new(id, value, $"S{id}", "", GrblSettingType.Integer, "", null, null, false);

    private static GrblSettingsBackup Backup(string controllerKey, params GrblBackupSetting[] settings) =>
        new()
        {
            Controller = new GrblBackupController(controllerKey, "grblHAL", "1.1f", 20240101, 3, true, true),
            Settings = settings,
        };

    [Fact]
    public void A_differing_valid_value_is_Apply()
    {
        var board = new[] { BoardItem(110, GrblSettingType.Integer, "800") };

        var plan = GrblRestorePlan.Create(Backup("k", BackupSetting(110, "900")), board, "k");

        var item = Assert.Single(plan.Items);
        Assert.Equal(GrblRestoreAction.Apply, item.Action);
        Assert.Equal("900", item.Value);
        Assert.Null(item.Reason);
        Assert.Equal(1, plan.ApplyCount);
        Assert.Equal(new[] { 110 }, plan.Writes.Select(static w => w.Id));
    }

    [Fact]
    public void An_identical_value_is_Unchanged()
    {
        var board = new[] { BoardItem(110, GrblSettingType.Integer, "800") };

        var plan = GrblRestorePlan.Create(Backup("k", BackupSetting(110, "800")), board, "k");

        Assert.Equal(GrblRestoreAction.Unchanged, Assert.Single(plan.Items).Action);
        Assert.Equal(1, plan.UnchangedCount);
        Assert.Equal(0, plan.ApplyCount);
        Assert.Empty(plan.Writes);
    }

    [Fact]
    public void An_id_the_board_does_not_have_is_UnknownOnBoard()
    {
        var board = new[] { BoardItem(110, GrblSettingType.Integer, "800") };

        var plan = GrblRestorePlan.Create(
            Backup("k", BackupSetting(110, "800"), BackupSetting(111, "5")), board, "k");

        var unknown = plan.Items.Single(static i => i.Id == 111);
        Assert.Equal(GrblRestoreAction.UnknownOnBoard, unknown.Action);
        Assert.Equal(1, plan.UnknownCount);
        Assert.Equal(1, plan.UnchangedCount);
        Assert.Empty(plan.Writes);
    }

    [Fact]
    public void An_id_the_backup_does_not_have_is_LeftAlone()
    {
        var board = new[]
        {
            BoardItem(110, GrblSettingType.Integer, "800"),
            BoardItem(111, GrblSettingType.Integer, "5"),
        };

        var plan = GrblRestorePlan.Create(Backup("k", BackupSetting(110, "800")), board, "k");

        var left = plan.Items.Single(static i => i.Id == 111);
        Assert.Equal(GrblRestoreAction.LeftAlone, left.Action);
        Assert.Equal(1, plan.LeftAloneCount);
        Assert.Empty(plan.Writes);
        // A left-alone row carries no candidate value: nothing is planned for it.
        Assert.Null(left.Value);
    }

    [Fact]
    public void An_out_of_range_value_is_Invalid_with_the_reason()
    {
        var board = new[] { BoardItem(110, GrblSettingType.Integer, "800", min: 1, max: 1000) };

        var plan = GrblRestorePlan.Create(Backup("k", BackupSetting(110, "2000")), board, "k");

        var item = Assert.Single(plan.Items);
        Assert.Equal(GrblRestoreAction.Invalid, item.Action);
        Assert.Equal(GrblSettingValidationError.OutOfRange, item.Reason!.Value);
        Assert.Equal(1, plan.InvalidCount);
        Assert.Equal(0, plan.ApplyCount);
        Assert.Empty(plan.Writes);
    }

    [Fact]
    public void A_non_numeric_value_for_an_integer_is_Invalid()
    {
        var board = new[] { BoardItem(110, GrblSettingType.Integer, "800") };

        var plan = GrblRestorePlan.Create(Backup("k", BackupSetting(110, "1e")), board, "k");

        var item = Assert.Single(plan.Items);
        Assert.Equal(GrblRestoreAction.Invalid, item.Action);
        Assert.Equal(GrblSettingValidationError.NotNumeric, item.Reason!.Value);
        Assert.Equal("1e", item.Value);
        Assert.Equal(1, plan.InvalidCount);
        Assert.Empty(plan.Writes);
    }

    [Fact]
    public void A_malformed_ip_value_is_Invalid()
    {
        var board = new[] { BoardItem(110, GrblSettingType.Ip4, "192.168.1.1") };

        var plan = GrblRestorePlan.Create(Backup("k", BackupSetting(110, "192.168.1.256")), board, "k");

        var item = Assert.Single(plan.Items);
        Assert.Equal(GrblRestoreAction.Invalid, item.Action);
        Assert.Equal(GrblSettingValidationError.MalformedIp, item.Reason!.Value);
        Assert.Equal(1, plan.InvalidCount);
        Assert.Empty(plan.Writes);
    }

    [Fact]
    public void An_out_of_domain_bool_value_is_Invalid()
    {
        // The checkbox editor can only ever produce 0/1; a file can hold anything.
        var board = new[] { BoardItem(110, GrblSettingType.Bool, "0") };

        var plan = GrblRestorePlan.Create(Backup("k", BackupSetting(110, "42")), board, "k");

        var item = Assert.Single(plan.Items);
        Assert.Equal(GrblRestoreAction.Invalid, item.Action);
        Assert.Equal(GrblSettingValidationError.OutOfRange, item.Reason!.Value);
        Assert.Equal(0, plan.ApplyCount);
        Assert.Empty(plan.Writes);
    }

    [Fact]
    public void A_radio_value_outside_the_option_range_is_Invalid()
    {
        // Two options: only 0 and 1 are reachable from the combobox.
        var board = new[] { BoardItem(110, GrblSettingType.RadioButtons, "0", format: "Left,Right") };

        var plane = GrblRestorePlan.Create(Backup("k", BackupSetting(110, "9")), board, "k");

        var item = Assert.Single(plane.Items);
        Assert.Equal(GrblRestoreAction.Invalid, item.Action);
        Assert.Equal(GrblSettingValidationError.OutOfRange, item.Reason!.Value);
        Assert.Empty(plane.Writes);

        // The last real option is still writable: the check is the domain, not a blanket refusal.
        var inDomain = GrblRestorePlan.Create(Backup("k", BackupSetting(110, "1")), board, "k");
        Assert.Equal(GrblRestoreAction.Apply, Assert.Single(inDomain.Items).Action);
    }

    [Fact]
    public void A_radio_setting_with_no_options_is_not_rejected_by_an_invented_domain()
    {
        // A board that reported no Format leaves the option set UNKNOWABLE. Judging such a
        // setting against the empty range 0..-1 would mark every value Invalid and silently
        // block a legitimate restore, so it falls through to the shared validation and the
        // controller gets the final say — the same "only reject what the metadata proves
        // wrong" rule the rest of the planner follows.
        var board = new[] { BoardItem(110, GrblSettingType.RadioButtons, "0", format: "") };

        var plan = GrblRestorePlan.Create(Backup("k", BackupSetting(110, "3")), board, "k");

        var item = Assert.Single(plan.Items);
        Assert.Equal(GrblRestoreAction.Apply, item.Action);
        Assert.Null(item.Reason);
        Assert.Equal(1, plan.ApplyCount);
    }

    [Fact]
    public void A_bitfield_bit_outside_the_allowed_mask_is_Invalid()
    {
        // "A,B" yields the two bits 0b01 and 0b10, so the union of the item's masks is 0b11.
        var board = new[] { BoardItem(110, GrblSettingType.Bitfield, "1", format: "A,B") };

        var invalid = GrblRestorePlan.Create(Backup("k", BackupSetting(110, "7")), board, "k");
        var item = Assert.Single(invalid.Items);
        Assert.Equal(GrblRestoreAction.Invalid, item.Action);
        Assert.Equal(GrblSettingValidationError.OutOfRange, item.Reason!.Value);
        Assert.Empty(invalid.Writes);

        // A value the checkbox set can actually produce is written.
        var valid = GrblRestorePlan.Create(Backup("k", BackupSetting(110, "3")), board, "k");
        Assert.Equal(GrblRestoreAction.Apply, Assert.Single(valid.Items).Action);
        Assert.Equal(new[] { "3" }, valid.Writes.Select(static w => w.Value));
    }

    [Fact]
    public void ValidateValue_keeps_waving_a_bitfield_with_options_through()
    {
        // The save path's rule is deliberately unchanged: the UI editors cannot produce an
        // out-of-domain value there, so only the restore planner got the stricter domain
        // check (see GrblSettingsViewModelTests for the same expectation on the item itself).
        var item = BoardItem(110, GrblSettingType.Bitfield, "1", format: "A,B");

        Assert.Equal(GrblSettingValidationError.None, item.ValidateValue("7"));
        Assert.Equal(GrblSettingValidationError.None, item.ValidateValue("abc"));
    }

    [Fact]
    public void Writes_are_in_ascending_id_order()
    {
        var board = new[]
        {
            BoardItem(1, GrblSettingType.Integer, "0"),
            BoardItem(2, GrblSettingType.Integer, "0"),
            BoardItem(3, GrblSettingType.Integer, "0"),
        };

        // Deliberately out of order in the file.
        var plan = GrblRestorePlan.Create(
            Backup("k", BackupSetting(3, "30"), BackupSetting(1, "10"), BackupSetting(2, "20")), board, "k");

        Assert.Equal(new[] { 1, 2, 3 }, plan.Writes.Select(static w => w.Id));
        Assert.Equal(new[] { "10", "20", "30" }, plan.Writes.Select(static w => w.Value));
    }

    [Fact]
    public void A_differing_controller_key_sets_ControllerMismatch()
    {
        var board = new[] { BoardItem(110, GrblSettingType.Integer, "800") };

        var plan = GrblRestorePlan.Create(Backup("other-board", BackupSetting(110, "900")), board, "this-board");

        Assert.True(plan.ControllerMismatch);
        // A mismatch is a warning, not a block: restoring a known-good file onto a
        // rebuilt board is exactly the case this feature exists for.
        Assert.Equal(1, plan.ApplyCount);
        Assert.Equal(new[] { 110 }, plan.Writes.Select(static w => w.Id));
    }

    [Fact]
    public void The_plan_carries_the_backups_readable_controller_identity()
    {
        var board = new[] { BoardItem(110, GrblSettingType.Integer, "800") };

        var plan = GrblRestorePlan.Create(
            Backup("other-board", BackupSetting(110, "900")), board, "this-board");

        // What the mismatch warning shows the operator: firmware and version, not the
        // raw $I fingerprint (and certainly not a count).
        Assert.True(plan.ControllerMismatch);
        Assert.Equal("grblHAL 1.1f", plan.BackupController);
    }

    [Fact]
    public void A_backup_without_a_firmware_identity_falls_back_to_the_fingerprint_key()
    {
        var board = new[] { BoardItem(110, GrblSettingType.Integer, "800") };
        var backup = new GrblSettingsBackup
        {
            Controller = new GrblBackupController("raw-key", "", " ", 0, 3, false, false),
            Settings = [BackupSetting(110, "900")],
        };

        var plan = GrblRestorePlan.Create(backup, board, "this-board");

        Assert.Equal("raw-key", plan.BackupController);
    }

    [Fact]
    public void An_empty_board_marks_every_setting_UnknownOnBoard()
    {
        var plan = GrblRestorePlan.Create(
            Backup("k", BackupSetting(110, "800"), BackupSetting(111, "5")), [], "k");

        Assert.Equal(2, plan.UnknownCount);
        Assert.Equal(0, plan.ApplyCount);
        Assert.Empty(plan.Writes);
        Assert.All(plan.Items, static i => Assert.Equal(GrblRestoreAction.UnknownOnBoard, i.Action));
    }

    [Fact]
    public void A_locally_edited_value_is_compared_against_the_boards_value()
    {
        // The board reports 800 and the operator has typed 999 without saving. A plan
        // must diff against what the board HOLDS (the post-restore reload re-reads it),
        // not against the unsaved edit — otherwise the edit masks a real difference.
        var item = BoardItem(110, GrblSettingType.Integer, "800");
        item.Value = "999";
        Assert.True(item.IsDirty);

        var unchanged = GrblRestorePlan.Create(
            Backup("k", BackupSetting(110, "800")), new[] { item }, "k");
        Assert.Equal(GrblRestoreAction.Unchanged, Assert.Single(unchanged.Items).Action);

        var changed = GrblRestorePlan.Create(
            Backup("k", BackupSetting(110, "900")), new[] { item }, "k");
        Assert.Equal(GrblRestoreAction.Apply, Assert.Single(changed.Items).Action);
        Assert.Equal("900", changed.Writes.Single().Value);
    }

    [Fact]
    public void Planning_never_mutates_a_board_item()
    {
        var item = BoardItem(110, GrblSettingType.Integer, "800");

        var plan = GrblRestorePlan.Create(Backup("k", BackupSetting(110, "abc")), new[] { item }, "k");

        Assert.Equal(GrblRestoreAction.Invalid, Assert.Single(plan.Items).Action);
        Assert.Equal("800", item.Value);
        Assert.Equal("800", item.OriginalValue);
        Assert.False(item.IsDirty);
    }
}
