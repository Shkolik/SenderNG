/*
 * GrblSettingsLoader.cs - fetch, merge and assemble the grbl settings view model
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

using CNC.Core.Abstractions;
using SenderNG.Core.Localization;

namespace SenderNG.Core.Settings;

/// <summary>
/// Snapshot produced by <see cref="GrblSettingsLoader"/>: the assembled group tree
/// (children populated, empty groups dropped) and the flat setting list. Values are
/// the board's current <c>$$</c> dump; names/metadata come from the controller
/// (<c>$ES</c>/<c>$EG</c>, grblHAL) or the embedded English catalog (classic grbl).
/// </summary>
public sealed record GrblSettingsSnapshot(
    IReadOnlyList<GrblSettingGroup> TopGroups,
    IReadOnlyList<GrblSetting> Settings,
    bool MetadataFromController);

/// <summary>
/// Builds the settings tree for the widget from the wire:
///   <c>$$</c> values          — current values (all firmwares)
///   <c>$ES</c> + <c>$EG</c>   — metadata + group tree (grblHAL with ENUMS)
///   embedded en.json catalog  — the fallback for classic grbl 1.1/1.2i, which sends
///                               only <c>$$</c>: the classic settings are a subset of
///                               the grblHAL ones, so names, descriptions, types,
///                               min/max and the group tree all come from the catalog.
/// The localized name/description guard (<see cref="LocalizationService.ApplySettingStrings"/>)
/// runs on every setting so locale overrides apply only when the English names match.
/// </summary>
public sealed class GrblSettingsLoader
{
    private readonly IGrblSettingsProvider _provider;
    private readonly LocalizationService _localization;

    public GrblSettingsLoader(IGrblSettingsProvider provider, LocalizationService localization)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _localization = localization ?? throw new ArgumentNullException(nameof(localization));
    }

    public async Task<GrblSettingsSnapshot> LoadAsync()
    {
        // 1. Current values — every firmware answers $$.
        var values = new SortedDictionary<int, string>();
        foreach (var line in await _provider.GetSettingLinesAsync())
        {
            if (GrblSettingsParser.ParseValueLine(line) is { } parsed)
                values[parsed.Id] = parsed.Value;
        }

        // 2. Metadata — $ES on grblHAL ENUMS boards, else the embedded catalog.
        var metadata = new Dictionary<int, GrblSetting>();
        foreach (var line in await _provider.GetSettingDetailLinesAsync())
        {
            if (GrblSettingsParser.ParseSettingLine(line) is { } setting)
                metadata[setting.Id] = setting;
        }
        // metadataFromController is only true when lines for at least one setting were
        // parsed — classic grbl's "error:NN" reply to $ES parses to nothing, so its
        // names/descriptions come from the catalog (flag false).
        var metadataFromController = metadata.Count > 0;

        // 3. Groups — $EG on grblHAL ENUMS boards, else the catalog's group tree.
        var groupLines = await _provider.GetSettingGroupLinesAsync();
        var groups = groupLines.Count > 0
            ? groupLines.Select(GrblSettingsParser.ParseGroupLine).Where(static g => g is not null).Cast<GrblSettingGroup>().ToList()
            : CatalogGroups(_localization);
        // Defense in depth: a board replying garbage (e.g. classic grbl's "error:NN"
        // to the unsupported $EG) can leave zero parseable groups; falling back to the
        // catalog tree is always safe and keeps every setting on the HAL hierarchy.
        if (groups.Count == 0)
            groups = CatalogGroups(_localization);

        // Localize display names once the source is settled: wire ($EG), demo and
        // catalog groups all arrive with English names and all pass the same guard.
        foreach (var group in groups)
            _localization.ApplyGroupStrings(group);

        // 4. Merge values with metadata into the flat setting list.
        var settings = new List<GrblSetting>(values.Count);
        foreach (var (id, value) in values)
        {
            var setting = metadata.TryGetValue(id, out var fromWire)
                ? fromWire
                : BuildFromCatalog(id, _localization);

            if (setting is null)
            {
                // The board has a setting we know nothing about: show it as-is so
                // no value is hidden, with the key as a placeholder name.
                setting = new GrblSetting { Id = id, Name = $"${id}", GroupId = -1 };
            }

            setting.Value = value;
            _localization.ApplySettingStrings(setting);
            // The language file is the single source of truth for docs. Controller
            // $ES metadata carries no description, so prefer the embedded catalog one
            // (English base, superseded by any localized description from
            // ApplySettingStrings above) and reserve the lazy $SED fetch for ids the
            // catalog doesn't know (the view only asks when Description is still null).
            if (string.IsNullOrEmpty(setting.Description)
                && _localization.Settings.TryGetValue(id, out var def)
                && !string.IsNullOrEmpty(def.Description))
            {
                setting.Description = def.Description;
            }

            settings.Add(setting);
        }

        // 5. Assemble the tree: drop groups with no settings and no children,
        //    order groups/settings by id, keep the Root group's children as the
        //    top level (Root itself is a container, not a displayed node).
        var topGroups = AssembleTree(groups, settings, _localization.Resolve("grblSettings.ungrouped", "Ungrouped"));

        return new GrblSettingsSnapshot(topGroups, settings, metadataFromController);
    }

    private static List<GrblSettingGroup> CatalogGroups(LocalizationService localization) =>
        localization.Groups.Values
            .OrderBy(static g => g.Id)
            .Select(static g => new GrblSettingGroup { Id = g.Id, ParentId = g.ParentId, Name = g.Name })
            .ToList();

    /// <summary>Build a setting from the embedded English catalog (classic fallback), or null if unknown.</summary>
    private static GrblSetting? BuildFromCatalog(int id, LocalizationService localization)
    {
        if (!localization.Settings.TryGetValue(id, out var def))
            return null;

        return new GrblSetting
        {
            Id = def.Id,
            GroupId = def.GroupId,
            Name = def.Name,
            EnglishName = def.Name,
            Unit = def.Unit,
            DataType = def.Type,
            Format = def.Format,
            Min = def.Min,
            Max = def.Max,
            RebootRequired = def.RebootRequired,
            AllowNull = def.AllowNull,
            Description = def.Description,
        };
    }

    private static IReadOnlyList<GrblSettingGroup> AssembleTree(
        IReadOnlyList<GrblSettingGroup> groups, IReadOnlyList<GrblSetting> settings, string ungroupedName)
    {
        var byId = groups.ToDictionary(static g => g.Id);

        // Attach settings to their groups; settings whose group is missing land in
        // a synthetic trailing "Ungrouped" node so nothing disappears.
        foreach (var setting in settings)
        {
            if (byId.TryGetValue(setting.GroupId, out var group))
                group.Settings.Add(setting);
            else
            {
                if (!byId.TryGetValue(SyntheticUngroupedId, out group))
                {
                    group = new GrblSettingGroup { Id = SyntheticUngroupedId, ParentId = 0, Name = ungroupedName };
                    byId[SyntheticUngroupedId] = group;
                    groups = groups.Append(group).ToList();
                }
                group.Settings.Add(setting);
            }
        }

        // Order settings within their group.
        foreach (var group in byId.Values)
            group.Settings.Sort(static (a, b) => a.Id.CompareTo(b.Id));

        // Wire parent → child first (parents may sort after their children by id,
        // so child attachment must not race the empty-group pass).
        foreach (var group in byId.Values)
        {
            if (group.ParentId != 0 && byId.TryGetValue(group.ParentId, out var parent))
                parent.Children.Add(group);
        }

        // Drop groups that are empty (no settings, no children) and collect the roots.
        // Empties must also be removed from their parent's children list — they sort
        // after the parent by id, so the parent was already kept.
        var roots = new List<GrblSettingGroup>();
        foreach (var group in byId.Values.OrderBy(static g => g.Id))
        {
            if (group.Settings.Count == 0 && group.Children.Count == 0)
            {
                if (group.ParentId != 0 && byId.TryGetValue(group.ParentId, out var parent))
                    parent.Children.Remove(group);
                continue; // empty group — never shown
            }

            if (group.ParentId == 0)
            {
                if (group.Id != 0)
                    roots.Add(group); // Root (0) is the container; its children are the top level
            }
            else if (!byId.ContainsKey(group.ParentId))
            {
                roots.Add(group); // orphan parent → promote to top level
            }
        }

        roots.Sort(static (a, b) => a.Id.CompareTo(b.Id));
        return roots;
    }

    private const int SyntheticUngroupedId = -1;
}
