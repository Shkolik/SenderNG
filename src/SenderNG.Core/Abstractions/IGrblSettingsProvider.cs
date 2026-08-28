namespace CNC.Core.Abstractions;

/// <summary>
/// Provides raw grbl setting lines from the controller. The plain <c>$$</c> dump is
/// universal; the <c>$ES</c> (setting metadata) / <c>$EG</c> (group tree) / <c>$SED=</c>
/// (per-setting description) queries are grblHAL-only — providers that can't answer
/// (classic grbl, demo without a catalog) fall back to the default empty responses.
/// </summary>
public interface IGrblSettingsProvider
{
    /// <summary><c>$$</c> dump: "<c>$N=value</c>" lines (all firmwares).</summary>
    Task<IReadOnlyList<string>> GetSettingLinesAsync();

    /// <summary>
    /// <c>$ES</c> dump: "<c>[SETTING:id|group|name|unit|type|format|min|max|reboot|allowNull]</c>"
    /// lines (grblHAL with ENUMS only; empty otherwise).
    /// </summary>
    Task<IReadOnlyList<string>> GetSettingDetailLinesAsync() =>
        Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());

    /// <summary>
    /// <c>$EG</c> dump: "<c>[SETTINGGROUP:id|parent|name]</c>" lines — the settings tree
    /// (grblHAL with ENUMS only; empty otherwise).
    /// </summary>
    Task<IReadOnlyList<string>> GetSettingGroupLinesAsync() =>
        Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());

    /// <summary>
    /// <c>$SED=&lt;id&gt;</c>: long description for one setting (grblHAL with SED only;
    /// null when the firmware has none). Descriptions arrive as
    /// "<c>[SETTINGDESCR:id|text]</c>" with literal <c>\n</c> escapes.
    /// </summary>
    Task<string?> GetSettingDescriptionAsync(int id) =>
        Task.FromResult<string?>(null);
}
