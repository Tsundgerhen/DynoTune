using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using DynoTune.Models;

namespace DynoTune.Services;

public class AppSettingsService
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DynoTune", "settings.json");

    public AppSettings Current { get; private set; } = new();

    public void Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
            {
                return;
            }

            string json = File.ReadAllText(SettingsPath, Encoding.UTF8);
            AppSettings? loaded = JsonSerializer.Deserialize<AppSettings>(json);
            if (loaded is not null)
            {
                Current = loaded;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Settings] Load failed: {ex.Message}");
        }
    }

    public void Save()
    {
        try
        {
            string? dir = Path.GetDirectoryName(SettingsPath);
            if (!string.IsNullOrWhiteSpace(dir))
            {
                Directory.CreateDirectory(dir);
            }

            string json = JsonSerializer.Serialize(Current, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SettingsPath, json, Encoding.UTF8);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Settings] Save failed: {ex.Message}");
        }
    }
}
