using System;
using System.IO;
using System.Text.Json;
using AudioShare.Models;
using Serilog;

namespace AudioShare.Services;

public class SettingsService : ISettingsService
{
    private static readonly string ConfigDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AudioShare");

    private static readonly string ConfigPath = Path.Combine(ConfigDir, "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public AppSettings Current { get; private set; } = new();

    public void Load()
    {
        try
        {
            Directory.CreateDirectory(ConfigDir);
            if (!File.Exists(ConfigPath))
            {
                Save();
                return;
            }

            var json = File.ReadAllText(ConfigPath);
            var loaded = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
            if (loaded is not null) Current = loaded;
            Log.Information("Settings loaded from {Path}", ConfigPath);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to load settings, using defaults");
            Current = new AppSettings();
        }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(ConfigDir);
            var json = JsonSerializer.Serialize(Current, JsonOptions);
            File.WriteAllText(ConfigPath, json);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to save settings");
        }
    }
}
