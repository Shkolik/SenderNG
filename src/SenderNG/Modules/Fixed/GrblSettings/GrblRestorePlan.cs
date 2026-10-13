/*
 * GrblRestorePlan.cs - pure diff of a grbl settings backup against the loaded board
 */

/*

Copyright (c) 2018-2022 Io Engineering (Terje Io)

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

using System.Globalization;
using SenderNG.Core.Settings;

namespace SenderNG.Modules.Fixed;

/// <summary>What a restore would do with one setting id.</summary>
internal enum GrblRestoreAction
{
    /// <summary>The value differs from the board's and passes validation — send it.</summary>
    Apply,

    /// <summary>The board already holds the value — nothing is sent.</summary>
    Unchanged,

    /// <summary>The file holds an id this controller does not report — skipped.</summary>
    UnknownOnBoard,

    /// <summary>The controller has an id the file does not — left exactly as it is.</summary>
    LeftAlone,

    /// <summary>The value differs but fails the board's own validation — skipped, with <see cref="GrblRestorePlanItem.Reason"/>.</summary>
    Invalid,
}

/// <summary>
/// One planned row. <see cref="Value"/> is the candidate value from the backup, or null
/// when there is none (<see cref="GrblRestoreAction.LeftAlone"/>). <see cref="Reason"/> is
/// the structured validation verdict — never localized text, so the planner stays free of
/// <c>LocalizationService</c>/<c>LocText</c>; the UI renders it with the existing
/// <c>module.grbl-settings.reason.*</c> keys.
/// </summary>
internal sealed record GrblRestorePlanItem(int Id, GrblRestoreAction Action, string? Value,
                                           GrblSettingValidationError? Reason);

/// <summary>
/// The diff between a backup file and the settings the board currently reports: for every
/// id in either, whether a restore would write it, skip it as identical, skip it as unknown
/// on this controller, skip it as invalid, or leave it alone. Pure — it validates candidate
/// values against the board items' metadata and never mutates one, so the plan can be shown
/// (and cancelled) before anything reaches the controller.
/// </summary>
internal sealed class GrblRestorePlan
{
    private GrblRestorePlan(IReadOnlyList<GrblRestorePlanItem> items, bool controllerMismatch,
                            string backupController)
    {
        Items = items;
        ControllerMismatch = controllerMismatch;
        BackupController = backupController;
        Writes = items.Where(static i => i.Action == GrblRestoreAction.Apply).ToList();
        ApplyCount = Writes.Count;
        UnchangedCount = items.Count(static i => i.Action == GrblRestoreAction.Unchanged);
        UnknownCount = items.Count(static i => i.Action == GrblRestoreAction.UnknownOnBoard);
        InvalidCount = items.Count(static i => i.Action == GrblRestoreAction.Invalid);
        LeftAloneCount = items.Count(static i => i.Action == GrblRestoreAction.LeftAlone);
    }

    /// <summary>Every id in the union of file and board, ascending by id.</summary>
    public IReadOnlyList<GrblRestorePlanItem> Items { get; }

    /// <summary>True when the file came from a different <c>$I</c> fingerprint. A warning
    /// only: restoring a known-good file onto a rebuilt board is the point of the feature.</summary>
    public bool ControllerMismatch { get; }

    /// <summary>The backup's readable identity for the mismatch warning: its firmware and
    /// version, or the raw <c>$I</c> fingerprint when the file carries neither. Never the
    /// board's, and never a count — the warning exists to say WHICH controller it came from.</summary>
    public string BackupController { get; }

    public int ApplyCount { get; }
    public int UnchangedCount { get; }
    public int UnknownCount { get; }
    public int InvalidCount { get; }
    public int LeftAloneCount { get; }

    /// <summary>Exactly what a restore sends: <see cref="GrblRestoreAction.Apply"/> only, ascending id.</summary>
    public IReadOnlyList<GrblRestorePlanItem> Writes { get; }

    public static GrblRestorePlan Create(GrblSettingsBackup backup,
                                        IReadOnlyList<GrblSettingItem> boardItems,
                                        string controllerKey)
    {
        ArgumentNullException.ThrowIfNull(backup);
        ArgumentNullException.ThrowIfNull(boardItems);

        // A repeated id (a hand-edited file, an odd board dump) must not throw: the first
        // occurrence wins, and every id still gets exactly one row.
        var boardById = new Dictionary<int, GrblSettingItem>();
        foreach (var item in boardItems)
            boardById.TryAdd(item.Id, item);

        var fileById = new Dictionary<int, GrblBackupSetting>();
        foreach (var setting in backup.Settings)
            fileById.TryAdd(setting.Id, setting);

        var items = new List<GrblRestorePlanItem>();
        foreach (var id in boardById.Keys.Union(fileById.Keys).OrderBy(static id => id))
        {
            if (!boardById.TryGetValue(id, out var board))
            {
                items.Add(new GrblRestorePlanItem(id, GrblRestoreAction.UnknownOnBoard, fileById[id].Value, null));
                continue;
            }

            if (!fileById.TryGetValue(id, out var fromFile))
            {
                items.Add(new GrblRestorePlanItem(id, GrblRestoreAction.LeftAlone, null, null));
                continue;
            }

            // Diff against what the board HOLDS (OriginalValue), never the possibly-edited
            // Value: the post-restore reload re-reads the controller, so an unsaved edit
            // must not mask a real difference.
            if (string.Equals(fromFile.Value, board.OriginalValue, StringComparison.Ordinal))
            {
                items.Add(new GrblRestorePlanItem(id, GrblRestoreAction.Unchanged, fromFile.Value, null));
                continue;
            }

            // The board's own metadata wins — the file's is informational.
            var reason = ValidateRestoreValue(board, fromFile.Value);
            items.Add(reason == GrblSettingValidationError.None
                ? new GrblRestorePlanItem(id, GrblRestoreAction.Apply, fromFile.Value, null)
                : new GrblRestorePlanItem(id, GrblRestoreAction.Invalid, fromFile.Value, reason));
        }

        return new GrblRestorePlan(items,
            !string.Equals(backup.Controller.Key, controllerKey, StringComparison.Ordinal),
            DescribeController(backup.Controller));
    }

    /// <summary>
    /// The backup's human-readable identity: "firmware version" when the file carries
    /// either field, else the raw <c>$I</c> fingerprint (a file written by a foreign or
    /// hand-edited tool). Never null or empty — the warning always names something.
    /// </summary>
    private static string DescribeController(GrblBackupController controller)
    {
        var readable = string.Join(" ", new[] { controller.Firmware, controller.Version }
            .Where(static s => !string.IsNullOrWhiteSpace(s))
            .Select(static s => s.Trim()));
        return readable.Length > 0 ? readable : controller.Key;
    }

    /// <summary>
    /// The restore-path verdict for one candidate value: the board's own rules first, then
    /// the domain rules for the editors the save path can trust. <see cref="GrblSettingItem.ValidateValue"/>
    /// deliberately waves Bool / RadioButtons / bit types with options through — their UI
    /// editors cannot produce an out-of-domain value — but a backup file can, so the
    /// stricter domain check lives here and <c>ValidateValue</c> keeps its documented
    /// behaviour for the save path.
    /// </summary>
    private static GrblSettingValidationError ValidateRestoreValue(GrblSettingItem board, string value)
    {
        var reason = board.ValidateValue(value);
        if (reason != GrblSettingValidationError.None)
            return reason;

        switch (board.Type)
        {
            // Bool: the checkbox is "1"/"0" only.
            case GrblSettingType.Bool:
                return ValidateDomain(value, 0, 1);

            // RadioButtons: the combobox can only select an existing option.
            case GrblSettingType.RadioButtons:
                return ValidateDomain(value, 0, board.Options.Length - 1);

            // A bit type WITH options renders as a checkbox set, whose bits are exactly
            // the item's masks — so no other bit may be set. Without options it is a raw
            // integer mask and ValidateValue already ranged it.
            case GrblSettingType.Bitfield when board.IsBits:
            case GrblSettingType.XBitfield when board.IsBits:
            case GrblSettingType.AxisMask when board.IsBits:
                return ValidateBitMask(value, board);

            default:
                return GrblSettingValidationError.None;
        }
    }

    private static GrblSettingValidationError ValidateDomain(string value, int min, int max)
    {
        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
            return GrblSettingValidationError.NotNumeric;
        return n >= min && n <= max ? GrblSettingValidationError.None : GrblSettingValidationError.OutOfRange;
    }

    private static GrblSettingValidationError ValidateBitMask(string value, GrblSettingItem board)
    {
        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var mask))
            return GrblSettingValidationError.NotNumeric;

        var allowed = 0;
        foreach (var bit in board.Bits)
            allowed |= bit.Mask;

        return mask >= 0 && (mask & ~allowed) == 0
            ? GrblSettingValidationError.None
            : GrblSettingValidationError.OutOfRange;
    }
}
