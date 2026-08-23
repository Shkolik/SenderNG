using System.Text.Json;

namespace CNC.Core.Services;

public class SettingsService
{
    private readonly string _dir;
    private AppSettings? _current;

    public SettingsService(string? dir = null)
    {
        _dir = dir ?? DefaultDir();
    }

    // Writable base dir for all persisted app files (settings, demo machine data).
    public static string DefaultDir() =>
        IsWritable(AppContext.BaseDirectory)
            ? AppContext.BaseDirectory
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SenderNG");

    // All consumers share one AppSettings graph: the first Load() reads disk,
    // later calls return the same instance so per-module Saves never resurrect
    // stale snapshots and clobber sections written by other modules.
    public AppSettings Load()
    {
        if (_current is not null)
            return _current;

        var p = Path.Combine(_dir, "settings.json");
        _current = File.Exists(p)
            ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(p)) ?? new AppSettings()
            : new AppSettings();
        return _current;
    }

    public void Save(AppSettings s)
    {
        _current = s ?? throw new ArgumentNullException(nameof(s));
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "settings.json"),
            JsonSerializer.Serialize(s, new JsonSerializerOptions { WriteIndented = true }));
    }

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
