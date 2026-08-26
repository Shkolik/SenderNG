using System.Text.Json;
using CNC.Core.Services;

namespace SenderNG.Core.Modules.Probing;

/// <summary>
/// Loads/saves the named probing profile collection to its own JSON file
/// (<c>probing.json</c>), mirroring how ioSender keeps profiles in a separate
/// <c>ProbingProfiles.xml</c> rather than inside the main settings.
/// </summary>
public class ProbingProfileStore
{
    private readonly string _filePath;

    public ProbingProfileStore(string? filePath = null)
    {
        _filePath = filePath ?? Path.Combine(SettingsService.DefaultDir(), "probing.json");
    }

    /// <summary>Path of the profiles file (the value persisted in the module settings).</summary>
    public string FilePath => _filePath;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    /// <summary>Loads the profiles; missing/unreadable file yields ioSender's &lt;Default&gt; profile.</summary>
    public ProbingProfiles Load()
    {
        var profiles = new ProbingProfiles();
        try
        {
            if (File.Exists(_filePath))
            {
                var list = JsonSerializer.Deserialize<List<ProbingProfile>>(File.ReadAllText(_filePath));
                if (list is not null && list.Count > 0)
                {
                    foreach (var p in list)
                        profiles.Profiles.Add(p);
                }
            }
        }
        catch
        {
            // Unreadable/corrupt profiles fall back to defaults rather than crashing.
        }
        profiles.EnsureDefault();
        return profiles;
    }

    /// <summary>Saves the profiles; best-effort (a read-only location must not crash the UI).</summary>
    public bool Save(ProbingProfiles profiles)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
            File.WriteAllText(_filePath, JsonSerializer.Serialize(profiles.Profiles, JsonOptions));
            return true;
        }
        catch
        {
            return false;
        }
    }
}
