/*
 * BackupSettingsLeaf.cs - App Settings > Global > Backup & restore leaf
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

using System.Globalization;
using CNC.Core.Abstractions;
using CNC.Core.Services;
using SenderNG.Core.Commands;

namespace CNC.Core.ViewModels;

/// <summary>
/// Global &gt; Backup &amp; restore leaf: writes every profile and app setting into a
/// single <c>.ngsettings.zip</c> archive and restores one back. Capture reads the
/// <b>disk</b>, so a backup taken while a restore is pending is still correct; a
/// restore validates the whole archive before writing anything and asks for a second
/// confirmation, and a successful restore marks <see cref="SettingsService"/> pending
/// so no post-restore save can rewrite the stale in-memory graph over the restored
/// files. Restoring is gated by the host's profile-switch predicate, because a
/// mid-session restore invalidates everything the running session holds.
/// </summary>
public sealed class BackupSettingsLeaf : AppSettingsLeaf
{
    private static readonly string[] SettingsFilters =
    [
        "SenderNG settings (*" + AppSettingsBundle.Extension + ")|*" + AppSettingsBundle.Extension,
        "All files (*.*)|*.*",
    ];

    private readonly SettingsService _settings;
    private readonly IFileDialogService? _dialogs;
    private readonly Func<string, string, string> _resolve;
    private Func<bool>? _switchAllowed;
    private AppSettingsBundle? _pending;
    private IReadOnlyList<string> _warnings = [];
    private string _status = string.Empty;

    public BackupSettingsLeaf(
        SettingsService settings,
        IFileDialogService? dialogs,
        Func<string, string, string> resolve,
        string name,
        string description)
        : base("backup", name, description)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(resolve);

        _settings = settings;
        _dialogs = dialogs;
        _resolve = resolve;

        // True two-step: the first press picks the archive and shows the confirm strip,
        // the second applies it. ApplyRestoreCommand is the explicit confirm-strip button.
        BackupCommand = new DelegateCommand(_state => { _ = BackupAsync(); },
            _state => _dialogs is not null);
        RestoreCommand = new DelegateCommand(_state => { _ = ExecuteRestoreAsync(); },
            _state => _dialogs is not null && CanRestore);
        ApplyRestoreCommand = new DelegateCommand(_state => { _ = ApplyRestoreAsync(); },
            _state => _dialogs is not null && CanRestore && HasRestorePlan);
        CancelRestoreCommand = new DelegateCommand(_state => CancelRestore(), _state => HasRestorePlan);

        Refresh();
    }

    /// <summary>Live settings directory this leaf backs up and restores into.</summary>
    public string SettingsFolder => _settings.SettingsDir;

    /// <summary>The folder caption with the path substituted.</summary>
    public string FolderText => Localize("appSettings.backup.folder", "Settings folder: {0}", SettingsFolder);

    /// <summary>Files and profiles counted on the disk (not from the in-memory graph).</summary>
    public string Summary
    {
        get
        {
            var (files, profiles) = CountOnDisk();
            return Localize("appSettings.backup.summary", "{0} files · {1} profiles on disk", files, profiles);
        }
    }

    public string Status => _status;
    public bool HasStatus => !string.IsNullOrWhiteSpace(_status);

    /// <summary>Host gate (shared with profile switching): false while connected or running.</summary>
    public bool CanRestore => _switchAllowed?.Invoke() != false;

    /// <summary>Inverse of <see cref="CanRestore"/> — drives the blocked caption.</summary>
    public bool ShowBlocked => !CanRestore;

    public string BlockedText => _resolve("appSettings.backup.blocked",
        "Restoring is disabled while connected or a session is running.");

    /// <summary>Primary restore button: picks the archive, then confirms it.</summary>
    public string RestoreButtonText => HasRestorePlan
        ? RestoreNowText
        : _resolve("appSettings.backup.restore", "Restore from file…");

    public string RestoreNowText => _resolve("appSettings.backup.restoreNow", "Restore now");

    /// <summary>True once an archive has been read and validated but nothing has been written.</summary>
    public bool HasRestorePlan => _pending is not null;

    /// <summary>Confirm-strip text: what a restore would write, plus any validation warning.</summary>
    public string PlanSummary
    {
        get
        {
            if (_pending is null)
                return string.Empty;
            var text = Localize("appSettings.backup.pending",
                "Restore {0} files? Files already on disk that the archive does not contain are left alone.",
                _pending.Files.Count);
            // The validator's only warning today is "the active profile is not in the
            // archive"; appSettings.backup.warn.activeMissing is its localized copy.
            if (_warnings.Count > 0)
                text += " " + _resolve("appSettings.backup.warn.activeMissing",
                    "The archive's active profile is not in the archive — the first profile will be used.");
            return text;
        }
    }

    public DelegateCommand BackupCommand { get; }
    public DelegateCommand RestoreCommand { get; }
    public DelegateCommand ApplyRestoreCommand { get; }
    public DelegateCommand CancelRestoreCommand { get; }

    /// <summary>Reads the live settings dir and writes a deterministic archive to the chosen path.</summary>
    public async Task BackupAsync()
    {
        if (_dialogs is null)
            return;

        string? path;
        try
        {
            path = await _dialogs.SaveFileAsync(Name,
                AppSettingsBundle.SuggestedFileName(DateTimeOffset.UtcNow), SettingsFilters);
        }
        catch (Exception ex)
        {
            SetStatus(Localize("appSettings.backup.backupFailed", "Backup failed: {0}", ex.Message));
            return;
        }

        if (string.IsNullOrWhiteSpace(path))
            return; // cancelled: nothing written, no state change

        try
        {
            var now = DateTimeOffset.UtcNow;
            // Capture reads the disk, so this stays correct even while a restore is pending.
            var bundle = AppSettingsBundle.Capture(_settings.SettingsDir, AppVersion, now);
            bundle.WriteTo(path, now);
            SetStatus(Localize("appSettings.backup.saved", "Backed up {0} files to {1}.", bundle.Files.Count, path));
        }
        catch (Exception ex)
        {
            SetStatus(Localize("appSettings.backup.backupFailed", "Backup failed: {0}", ex.Message));
        }

        Refresh();
    }

    /// <summary>Reads and validates an archive, then shows the confirm strip. Writes nothing.</summary>
    public async Task RestoreAsync()
    {
        if (_dialogs is null)
            return;
        if (!CanRestore)
        {
            SetStatus(BlockedText);
            return;
        }

        string? path;
        try
        {
            path = await _dialogs.OpenFileAsync(Name, SettingsFilters);
        }
        catch (Exception ex)
        {
            ClearPlan();
            SetStatus(Localize("appSettings.backup.restoreFailed", "Restore failed: {0}", ex.Message));
            return;
        }

        if (string.IsNullOrWhiteSpace(path))
            return; // cancelled

        var read = AppSettingsBundle.ReadFrom(path);
        if (read.Bundle is null)
        {
            ClearPlan();
            // A foreign or broken archive is refused here, with the reader's wording.
            SetStatus(Localize("appSettings.backup.restoreFailed", "Restore failed: {0}", read.Error ?? path));
            return;
        }

        var validation = AppSettingsBundleValidator.Validate(read.Bundle);
        if (!validation.Ok)
        {
            ClearPlan();
            SetStatus(Localize("appSettings.backup.restoreFailed", "Restore failed: {0}",
                string.Join(" ", validation.Errors)));
            return;
        }

        _pending = read.Bundle;
        _warnings = validation.Warnings;
        SetStatus(string.Empty);
        RaisePlanChanged();
    }

    /// <summary>Writes the pending archive, then freezes writes until the app restarts.</summary>
    public Task ApplyRestoreAsync()
    {
        var bundle = _pending;
        if (bundle is null)
            return Task.CompletedTask;

        if (!CanRestore)
        {
            ClearPlan();
            SetStatus(BlockedText);
            return Task.CompletedTask;
        }

        AppSettingsRestoreResult result;
        try
        {
            // AppSettingsRestore validates again, so nothing lands if the bundle is bad.
            result = AppSettingsRestore.Apply(_settings.SettingsDir, bundle);
        }
        catch (Exception ex)
        {
            ClearPlan();
            SetStatus(Localize("appSettings.backup.restoreFailed", "Restore failed: {0}", ex.Message));
            return Task.CompletedTask;
        }

        ClearPlan();
        if (result.Ok)
        {
            // The in-memory graph still holds the pre-restore data: freeze writes so a
            // later save cannot rewrite it over the files just restored.
            _settings.MarkRestorePending();
            SetStatus(Localize("appSettings.backup.restored",
                "Restored {0} files. Restart SenderNG to apply.", result.Written));
        }
        else if (result.Written > 0)
        {
            // A partial restore already replaced some files: protect the ones that DID
            // land — the stale graph would otherwise rewrite them on the next save,
            // before the user gets a chance to run the restore again.
            _settings.MarkRestorePending();
            SetStatus(Localize("appSettings.backup.restorePartial",
                "Restore incomplete after {0} files — run the restore again.", result.Written));
        }
        else
        {
            SetStatus(Localize("appSettings.backup.restoreFailed", "Restore failed: {0}",
                string.Join(", ", result.Failed)));
        }

        Refresh();
        return Task.CompletedTask;
    }

    /// <summary>Discards the pending archive without writing anything.</summary>
    public void CancelRestore()
    {
        if (_pending is null)
            return;
        ClearPlan();
        SetStatus(string.Empty);
    }

    /// <summary>Host-provided gate (same predicate as profile switching). Null = allowed.</summary>
    public void SetSwitchGuard(Func<bool>? allowed)
    {
        _switchAllowed = allowed;
        RefreshCommands();
    }

    /// <summary>Re-reads the on-disk counts and re-evaluates the gate.</summary>
    public void Refresh()
    {
        OnPropertyChanged(nameof(SettingsFolder));
        OnPropertyChanged(nameof(FolderText));
        OnPropertyChanged(nameof(Summary));
        RefreshCommands();
    }

    /// <summary>Re-resolve the button captions after a locale change.</summary>
    public void RelocalizeButtons()
    {
        OnPropertyChanged(nameof(RestoreButtonText));
        OnPropertyChanged(nameof(RestoreNowText));
        OnPropertyChanged(nameof(BlockedText));
        OnPropertyChanged(nameof(FolderText));
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(PlanSummary));
        RefreshCommands();
    }

    private async Task ExecuteRestoreAsync()
    {
        if (HasRestorePlan)
            await ApplyRestoreAsync();
        else
            await RestoreAsync();
    }

    private void ClearPlan()
    {
        _pending = null;
        _warnings = [];
        RaisePlanChanged();
    }

    private void RaisePlanChanged()
    {
        OnPropertyChanged(nameof(HasRestorePlan));
        OnPropertyChanged(nameof(PlanSummary));
        OnPropertyChanged(nameof(RestoreButtonText));
        RefreshCommands();
    }

    private void RefreshCommands()
    {
        BackupCommand?.RaiseCanExecuteChanged();
        RestoreCommand?.RaiseCanExecuteChanged();
        ApplyRestoreCommand?.RaiseCanExecuteChanged();
        CancelRestoreCommand?.RaiseCanExecuteChanged();
        OnPropertyChanged(nameof(CanRestore));
        OnPropertyChanged(nameof(ShowBlocked));
    }

    private void SetStatus(string text)
    {
        if (string.Equals(_status, text, StringComparison.Ordinal))
            return;
        _status = text;
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(HasStatus));
    }

    /// <summary>Files and profiles a backup would capture, read straight from the disk.</summary>
    private (int Files, int Profiles) CountOnDisk()
    {
        var dir = _settings.SettingsDir;
        var files = File.Exists(ProfileSettingsPaths.GlobalPath(dir)) ? 1 : 0;
        var profiles = 0;

        var profilesDir = ProfileSettingsPaths.ProfilesDir(dir);
        if (!Directory.Exists(profilesDir))
            return (files, profiles);

        foreach (var path in Directory.EnumerateFiles(profilesDir, "*.json"))
        {
            var name = Path.GetFileName(path);
            // Quarantine leftovers and half-written temp files are not part of a backup.
            if (name.Contains(".corrupt", StringComparison.OrdinalIgnoreCase) ||
                name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
                continue;
            files++;
            // A dot in the stem means "companion file, not a profile" (ProfilesStore.Ids).
            if (!Path.GetFileNameWithoutExtension(name).Contains('.'))
                profiles++;
        }

        return (files, profiles);
    }

    private string Localize(string key, string fallback, params object?[] args)
    {
        var template = _resolve(key, fallback);
        return args.Length == 0 ? template : string.Format(CultureInfo.InvariantCulture, template, args);
    }

    private static string AppVersion =>
        typeof(BackupSettingsLeaf).Assembly.GetName().Version?.ToString() ?? "0.0.0";
}
