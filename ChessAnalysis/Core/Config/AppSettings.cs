using System;
using System.IO;
using System.Text.Json;
using ChessAnalysis.Core.Engine;

namespace ChessAnalysis.Core.Config;

public class AppSettings
{
    public string? StockfishPath { get; set; }
    public bool IsSetupCompleted { get; set; }
    public int MultiPv { get; set; } = 2;
    public int Threads { get; set; } = 4;
    public int Depth { get; set; } = 14;
    public int Nodes { get; set; } = 200000;

    private static readonly string LocalAppDataPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ChessAnalysis",
        "settings.json");

    private static readonly string PortableSettingsPath = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory,
        "settings.json");

    public static AppSettings Load()
    {
        string? activePath = null;
        if (File.Exists(PortableSettingsPath))
        {
            activePath = PortableSettingsPath;
        }
        else if (File.Exists(LocalAppDataPath))
        {
            activePath = LocalAppDataPath;
        }

        AppSettings? settings = null;
        if (activePath != null)
        {
            try
            {
                string json = File.ReadAllText(activePath);
                settings = JsonSerializer.Deserialize<AppSettings>(json);
            }
            catch { }
        }

        settings ??= new AppSettings();

        // Если путь к движку еще не задан или файл перемещен, пробуем найти автоматически
        if (string.IsNullOrWhiteSpace(settings.StockfishPath) || !File.Exists(settings.StockfishPath))
        {
            string? discovered = StockfishEngine.FindStockfishPath();
            if (!string.IsNullOrEmpty(discovered) && File.Exists(discovered))
            {
                settings.StockfishPath = Path.GetFullPath(discovered);
                settings.IsSetupCompleted = true;
                settings.Save();
            }
        }

        return settings;
    }

    public void Save()
    {
        try
        {
            string json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });

            // 1. Попытка сохранить в портативный файл рядом с программой
            try
            {
                File.WriteAllText(PortableSettingsPath, json);
                return;
            }
            catch { }

            // 2. Если каталог программы защищен от записи, сохраняем в LocalAppData
            string? dir = Path.GetDirectoryName(LocalAppDataPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
            File.WriteAllText(LocalAppDataPath, json);
        }
        catch { }
    }
}
