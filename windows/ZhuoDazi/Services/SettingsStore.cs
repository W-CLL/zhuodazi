using System.IO;
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

    public string DataDirectory { get; }

    public SettingsStore(string? dataDirectory = null)
    {
        DataDirectory = dataDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "poko-desktop-pet");
    }

    public string SettingsPath => Path.Combine(DataDirectory, "settings.json");
    public string PetsDirectory => Path.Combine(DataDirectory, "pets");
    public string UpdatesDirectory => Path.Combine(DataDirectory, "updates-native");
    public string InteractionsPath => Path.Combine(DataDirectory, "interactions.json");
    public string CompanionDirectory => Path.Combine(DataDirectory, "companion");

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return CreateDefault();
            var json = File.ReadAllText(SettingsPath);
            var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? CreateDefault();
            settings.Normalize();
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("onboarding", out _))
                settings.Onboarding = OnboardingProgress.ForExistingInstallation();
            if (!document.RootElement.TryGetProperty("remoteDefaultsApplied", out _))
                settings.RemoteDefaultsApplied = true;
            return settings;
        }
        catch
        {
            var settings = CreateDefault();
            if (File.Exists(SettingsPath)) settings.Onboarding = OnboardingProgress.ForExistingInstallation();
            return settings;
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
        GifImportValidator.Validate(sourcePath);
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
