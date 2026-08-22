using System.Text.Json;

namespace CNC.Core.Services;

public class SettingsService
{
    private readonly string _dir;

    public SettingsService(string? dir = null)
    {
        _dir = dir ?? (IsWritable(AppContext.BaseDirectory)
            ? AppContext.BaseDirectory
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SenderNG"));
    }

    public AppSettings Load()
    {
        var p = Path.Combine(_dir, "settings.json");
        if (!File.Exists(p)) return new AppSettings();
        return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(p)) ?? new AppSettings();
    }

    public void Save(AppSettings s)
    {
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
