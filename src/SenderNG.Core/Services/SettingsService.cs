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

using Microsoft.Extensions.DependencyInjection;

namespace CNC.Core.Services;

/// <summary>
/// Two-tier settings store:
/// <list type="bullet">
/// <item><b>Global</b> (<c>global.json</c> — <see cref="GlobalSettings"/>):
/// language, recent files, network-availability gate, profile registry,
/// global-scoped module sections (Console).</item>
/// <item><b>Active profile</b> (<c>profiles/&lt;id&gt;.json</c> — <see cref="MachineProfile"/>):
/// one machine: type id, connection, per-module sections. <see cref="Load"/>
/// returns this shared in-memory graph; <see cref="Save(MachineProfile)"/>
/// persists it to the active profile's file.</item>
/// </list>
/// A legacy <c>settings.json</c> migrates once on first <see cref="Load"/>
/// (<see cref="SettingsMigrator"/>); a fresh directory lazily creates the
/// default profile. All consumers share the same instances so per-module Saves
/// never resurrect stale snapshots and clobber sections written by others.
/// </summary>
public class SettingsService
{
    private readonly string _dir;
    private readonly IServiceProvider? _services;
    private readonly Action<SettingsFallback>? _onFallback;
    private MachineProfile? _current;
    private GlobalSettings? _global;

    /// <summary>Raised at the end of every Save so UI can re-read live visibility/mode state.</summary>
    public event System.Action? Changed;

    /// <summary>Raised when the active profile is switched via <see cref="Activate"/>
    /// (profileId). Consumers that hold the old graph (program, jog limits) must
    /// re-read or reset — a machine swap invalidates loaded state.</summary>
    public event System.Action<string>? ProfileSwitched;

    /// <summary>Creates a settings service rooted at <paramref name="dir"/> (default:
    /// the per-user settings directory). <paramref name="services"/> — when present —
    /// supplies module companion providers (<see cref="IProfileCompanionSuffixes"/>)
    /// whose per-profile files <see cref="DeleteProfile"/> removes alongside the
    /// profile file.</summary>
    /// <param name="onFallback">Invoked when a settings file exists but cannot be
    /// loaded. The host passes a callback that shows the user a notice; Core
    /// itself stays silent and free of user-facing text.</param>
    public SettingsService(string? dir = null, IServiceProvider? services = null,
                           Action<SettingsFallback>? onFallback = null)
    {
        _dir = dir ?? DefaultDir();
        _services = services;
        _onFallback = onFallback;
    }

    /// <summary>Writable directory used by this service (companion files live here too).</summary>
    public string SettingsDir => _dir;

    /// <summary>
    /// The shared active-profile graph: the first call migrates a legacy
    /// settings.json if present, then loads the profile pointed to by
    /// <see cref="GlobalSettings.ActiveProfileId"/> (falling back to the first
    /// profile on disk, or creating the fresh "default" profile). Later calls
    /// return the same instance.
    /// </summary>
    public MachineProfile Load()
    {
        if (_current is not null)
            return _current;

        // One-time migration of a legacy settings.json (inert once global.json
        // exists, or on a fresh directory).
        SettingsMigrator.MigrateIfNeeded(_dir);

        _global = GlobalSettingsStore.Load(_dir, _onFallback);
        var profileIds = ProfilesStore.Ids(_dir).ToArray();

        if (profileIds.Length == 0)
        {
            // Fresh install: one default profile (Mill with default settings).
            var fresh = new MachineProfile();
            ProfilesStore.Save(_dir, fresh);
            _global.Profiles[fresh.Id] = fresh.Name;
            _global.ActiveProfileId = fresh.Id;
            GlobalSettingsStore.Save(_dir, _global);
            _current = fresh;
        }
        else
        {
            var activeId = profileIds.Contains(_global.ActiveProfileId)
                ? _global.ActiveProfileId
                : profileIds[0];
            if (!string.Equals(activeId, _global.ActiveProfileId, System.StringComparison.Ordinal))
            {
                _global.ActiveProfileId = activeId;
                GlobalSettingsStore.Save(_dir, _global);
            }
            _current = ProfilesStore.TryLoad(_dir, activeId, _onFallback) ?? new MachineProfile();
        }

        return _current;
    }

    /// <summary>
    /// The shared global settings graph (app-level). Call <see cref="Load"/>
    /// first — global state is (re)read there; this accessor then returns the
    /// same instance until <see cref="Save(MachineProfile)"/> or
    /// <see cref="SaveGlobal"/> replaces it.
    /// </summary>
    public GlobalSettings Global
    {
        get
        {
            if (_global is null)
            {
                Load();
                _global = GlobalSettingsStore.Load(_dir, _onFallback);
            }
            return _global;
        }
    }

    /// <summary>Persist the active profile to <c>profiles/&lt;id&gt;.json</c>.</summary>
    public void Save(MachineProfile profile)
    {
        _current = profile ?? throw new ArgumentNullException(nameof(profile));
        Directory.CreateDirectory(_dir);
        ProfilesStore.Save(_dir, profile);
        Changed?.Invoke();
    }

    /// <summary>Persist a (possibly inactive) profile file without touching the shared graph.</summary>
    public void SaveProfile(MachineProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ProfilesStore.Save(_dir, profile);
        if (_current?.Id == profile.Id)
            Changed?.Invoke();
    }

    /// <summary>Every profile on disk (registry order via <see cref="GlobalSettings.Profiles"/>, then ids).</summary>
    public IReadOnlyList<MachineProfile> AllProfiles()
    {
        var ids = ProfilesStore.Ids(_dir).ToList();
        // Registry carries insertion/rename order; ids fall back for unregistered files.
        var ordered = _global is null
            ? ids
            : _global.Profiles.Keys.Where(ids.Contains).Concat(ids.Where(id => !_global.Profiles.ContainsKey(id))).ToList();
        return ordered.Select(id => ProfilesStore.TryLoad(_dir, id, _onFallback)).OfType<MachineProfile>().ToList();
    }

    /// <summary>
    /// Creates a profile (fresh from the machine-type template, or a copy of
    /// <paramref name="copyFromId"/> — duplicate). Registers it, persists the
    /// registry, and returns it without activating.
    /// </summary>
    public MachineProfile CreateProfile(string name, string machineTypeId, string? copyFromId = null)
    {
        var id = UniqueId(name);
        // Fresh profiles seed machine-coupled defaults from the type preset: the
        // demo controller simulates the type's axis count (Foam needs 4 so both
        // towers exist). Duplicates keep the
        // source machine's configured values — a copy is that machine, not a
        // fresh template.
        var profile = copyFromId is null
            ? new MachineProfile
            {
                Id = id,
                Name = name,
                MachineTypeId = machineTypeId,
                DemoAxes = MachineTypeCatalog.Resolve(machineTypeId).DefaultDemoAxes,
            }
            : Clone(ProfilesStore.TryLoad(_dir, copyFromId, _onFallback), id, name);
        ProfilesStore.Save(_dir, profile);
        _global ??= GlobalSettingsStore.Load(_dir, _onFallback);
        _global.Profiles[profile.Id] = profile.Name;
        GlobalSettingsStore.Save(_dir, _global);
        return profile;
    }

    /// <summary>Display-name rename: id and file untouched, registry updated, active graph re-raised.</summary>
    public void RenameProfile(string id, string name)
    {
        var profile = ProfilesStore.TryLoad(_dir, id, _onFallback)
            ?? throw new ArgumentException($"Unknown profile '{id}'", nameof(id));
        profile.Name = name;
        ProfilesStore.Save(_dir, profile);
        _global ??= GlobalSettingsStore.Load(_dir, _onFallback);
        _global.Profiles[id] = name;
        GlobalSettingsStore.Save(_dir, _global);
        if (_current?.Id == id)
            Changed?.Invoke();
    }

    /// <summary>
    /// Deletes a profile file + its per-module companion files (registered via
    /// <see cref="IProfileCompanionSuffixes"/>). Refuses the active profile
    /// and the last remaining one (both must be handled by the UI first).
    /// </summary>
    public bool DeleteProfile(string id)
    {
        if (string.Equals(id, _global?.ActiveProfileId, System.StringComparison.Ordinal))
            return false;
        if (ProfilesStore.Ids(_dir).Count() <= 1)
            return false;
        ProfilesStore.Delete(_dir, id);
        foreach (var companion in _services?.GetServices<SenderNG.Core.Services.IProfileCompanionSuffixes>() ?? [])
        {
            foreach (var suffix in companion.Suffixes)
            {
                try { File.Delete(ProfileSettingsPaths.CompanionPath(_dir, id, suffix)); }
                catch { /* best-effort */ }
            }
        }
        _global ??= GlobalSettingsStore.Load(_dir, _onFallback);
        _global.Profiles.Remove(id);
        GlobalSettingsStore.Save(_dir, _global);
        return true;
    }

    /// <summary>
    /// Makes <paramref name="profileId"/> the active machine: swaps the shared
    /// graph to its file, persists the pointer, raises <see cref="Changed"/> and
    /// <see cref="ProfileSwitched"/>.
    /// </summary>
    public MachineProfile Activate(string profileId)
    {
        var profile = ProfilesStore.TryLoad(_dir, profileId, _onFallback)
            ?? throw new ArgumentException($"Unknown profile '{profileId}'", nameof(profileId));
        _current = profile;
        _global ??= GlobalSettingsStore.Load(_dir, _onFallback);
        _global.ActiveProfileId = profileId;
        GlobalSettingsStore.Save(_dir, _global);
        Changed?.Invoke();
        ProfileSwitched?.Invoke(profileId);
        return profile;
    }

    /// <summary>Changes a profile's machine type (confirmed + guarded by the UI).</summary>
    public void SetProfileMachineType(string id, string machineTypeId)
    {
        var profile = ProfilesStore.TryLoad(_dir, id, _onFallback)
            ?? throw new ArgumentException($"Unknown profile '{id}'", nameof(id));
        profile.MachineTypeId = machineTypeId;
        ProfilesStore.Save(_dir, profile);
        if (_current?.Id == id)
            Changed?.Invoke();
    }

    /// <summary>Slugifies a display name into a unique profile id (append -2, -3, … on collision).</summary>
    private string UniqueId(string name)
    {
        var stem = Slugify(name);
        if (string.IsNullOrEmpty(stem))
            stem = MachineProfile.DefaultId;
        var existing = new System.Collections.Generic.HashSet<string>(ProfilesStore.Ids(_dir), System.StringComparer.OrdinalIgnoreCase);
        if (!existing.Contains(stem))
            return stem;
        for (var i = 2; ; i++)
        {
            var candidate = $"{stem}-{i}";
            if (!existing.Contains(candidate))
                return candidate;
        }
    }

    private static string Slugify(string name)
    {
        var builder = new System.Text.StringBuilder();
        foreach (var ch in (name ?? string.Empty).Trim().ToLowerInvariant())
            builder.Append(char.IsLetterOrDigit(ch) ? ch : '-');
        return builder.ToString();
    }

    private static MachineProfile Clone(MachineProfile? source, string id, string name)
    {
        if (source is null)
            return new MachineProfile { Id = id, Name = name };
        // JSON round-trip: deep-copies the opaque ModuleSettings bag.
        var copy = System.Text.Json.JsonSerializer.Deserialize<MachineProfile>(
            System.Text.Json.JsonSerializer.Serialize(source))!;
        copy.Id = id;
        copy.Name = name;
        return copy;
    }

    /// <summary>Persist <c>global.json</c>.</summary>
    public void SaveGlobal(GlobalSettings global)
    {
        _global = global ?? throw new ArgumentNullException(nameof(global));
        Directory.CreateDirectory(_dir);
        GlobalSettingsStore.Save(_dir, global);
        Changed?.Invoke();
    }

    // Writable base dir for all persisted app files (settings, demo machine data).
    public static string DefaultDir() =>
        IsWritable(AppContext.BaseDirectory)
            ? AppContext.BaseDirectory
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SenderNG");

    static bool IsWritable(string d)
    {
        try
        {
            var f = Path.Combine(d, ".w");
            File.WriteAllText(f, "");
            File.Delete(f);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
