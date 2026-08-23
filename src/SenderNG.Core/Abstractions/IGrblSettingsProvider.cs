namespace CNC.Core.Abstractions;

/// <summary>
/// Provides raw grbl setting lines ("$13=1" format), e.g. from the $$ report.
/// </summary>
public interface IGrblSettingsProvider
{
    Task<IReadOnlyList<string>> GetSettingLinesAsync();
}
