using System.Text.Json;
using ZhuoDazi.Models;

namespace ZhuoDazi.Services;

public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public string DataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "poko-desktop-pet");

    public string SettingsPath => Path.Combine(DataDirectory, "settings.json");
    public string PetsDirectory => Path.Combine(DataDirectory, "pets");
    public string UpdatesDirectory => Path.Combine(DataDirectory, "updates-native");
    public string InteractionsPath => Path.Combine(DataDirectory, "interactions.json");

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return CreateDefault();
            var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath), JsonOptions) ?? CreateDefault();
            settings.Normalize();
            return settings;
        }
        catch
        {
            return CreateDefault();
        }
    }

    public void Save(AppSettings settings)
    {
        Directory.CreateDirectory(DataDirectory);
        var temporaryPath = $"{SettingsPath}.{Environment.ProcessId}.tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(settings, JsonOptions));
        File.Move(temporaryPath, SettingsPath, true);
    }

    public string ImportPet(string sourcePath)
    {
        if (!File.Exists(sourcePath) || !sourcePath.EndsWith(".gif", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("请选择有效的 GIF 文件。");
        Directory.CreateDirectory(PetsDirectory);
        var destination = Path.Combine(PetsDirectory, $"pet-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}-{Guid.NewGuid():N}.gif");
        File.Copy(sourcePath, destination, false);
        return destination;
    }

    private static AppSettings CreateDefault()
    {
        var settings = new AppSettings();
        settings.Normalize();
        return settings;
    }
}
