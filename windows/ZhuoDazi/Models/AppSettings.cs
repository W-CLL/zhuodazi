using System.Text.Json.Serialization;

namespace ZhuoDazi.Models;

public sealed class AppSettings
{
    [JsonPropertyName("pets")]
    public List<PetDefinition> Pets { get; set; } = [];

    [JsonPropertyName("activePetId")]
    public string? ActivePetId { get; set; }

    [JsonPropertyName("size")]
    public int Size { get; set; } = 220;

    [JsonPropertyName("opacity")]
    public int Opacity { get; set; } = 100;

    [JsonPropertyName("alwaysOnTop")]
    public bool AlwaysOnTop { get; set; } = true;

    [JsonPropertyName("startWithWindows")]
    public bool StartWithWindows { get; set; }

    [JsonPropertyName("clickThrough")]
    public bool ClickThrough { get; set; }

    [JsonPropertyName("mirrored")]
    public bool Mirrored { get; set; }

    [JsonPropertyName("personality")]
    public string Personality { get; set; } = "lively";

    [JsonPropertyName("mouseInteractionEnabled")]
    public bool MouseInteractionEnabled { get; set; } = true;

    [JsonPropertyName("randomMovementEnabled")]
    public bool RandomMovementEnabled { get; set; } = true;

    [JsonPropertyName("randomInteractionsEnabled")]
    public bool RandomInteractionsEnabled { get; set; } = true;

    [JsonPropertyName("interactionMode")]
    public string InteractionMode { get; set; } = "standard";

    [JsonPropertyName("theaterEnabled")]
    public bool TheaterEnabled { get; set; }

    [JsonPropertyName("theaterIntervalSeconds")]
    public int TheaterIntervalSeconds { get; set; } = 300;

    [JsonPropertyName("theaterScripts")]
    public List<TheaterScriptDefinition> TheaterScripts { get; set; } = [];

    [JsonPropertyName("libraryDirectory")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LibraryDirectory { get; set; }

    [JsonPropertyName("libraries")]
    public List<LibraryDefinition> Libraries { get; set; } = [];

    [JsonPropertyName("activeLibraryId")]
    public string? ActiveLibraryId { get; set; }

    [JsonPropertyName("randomPetEnabled")]
    public bool RandomPetEnabled { get; set; } = true;

    [JsonPropertyName("randomPetIntervalSeconds")]
    public int RandomPetIntervalSeconds { get; set; } = 300;

    [JsonPropertyName("interactionWords")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, List<string>>? InteractionWords { get; set; }

    [JsonPropertyName("interactionWordPackName")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? InteractionWordPackName { get; set; }

    [JsonPropertyName("interactionWordPacks")]
    public List<InteractionWordPackDefinition> InteractionWordPacks { get; set; } = [];

    [JsonPropertyName("activeInteractionWordPackId")]
    public string? ActiveInteractionWordPackId { get; set; }

    [JsonPropertyName("autoCheckUpdates")]
    public bool AutoCheckUpdates { get; set; } = true;

    [JsonPropertyName("ignoredUpdateVersion")]
    public string? IgnoredUpdateVersion { get; set; }

    [JsonPropertyName("reminders")]
    public List<ReminderDefinition> Reminders { get; set; } = [];

    [JsonPropertyName("position")]
    public WindowPosition? Position { get; set; }

    public void Normalize()
    {
        Pets = (Pets ?? []).Where(item => !string.IsNullOrWhiteSpace(item.Id)
            && !string.IsNullOrWhiteSpace(item.Path) && File.Exists(item.Path)).Take(3).ToList();
        Size = Math.Clamp(Size, 140, 300);
        Opacity = Math.Clamp(Opacity, 20, 100);
        ClickThrough = false;
        Personality = Personality is "lively" or "shy" or "clingy" or "chaotic"
            ? Personality : "lively";
        InteractionMode = InteractionMode is "quiet" or "standard" or "lively"
            ? InteractionMode : "standard";
        TheaterIntervalSeconds = TheaterIntervalSeconds is 60 or 180 or 300 or 600 or 1800
            ? TheaterIntervalSeconds : 300;
        NormalizeTheaterScripts();
        RandomPetIntervalSeconds = RandomPetIntervalSeconds is 30 or 60 or 300 or 600 or 1800
            ? RandomPetIntervalSeconds : 300;
        NormalizeLibraries();
        NormalizeInteractionWordPacks();
        Reminders = (Reminders ?? []).Take(20).ToList();
        if (!Pets.Any(item => item.Id == ActivePetId)) ActivePetId = Pets.FirstOrDefault()?.Id;
    }

    private void NormalizeLibraries()
    {
        Libraries ??= [];
        var legacyDirectory = string.IsNullOrWhiteSpace(LibraryDirectory) ? null : LibraryDirectory.Trim();
        if (legacyDirectory is not null && !Libraries.Any(item => SamePath(item.Path, legacyDirectory)))
        {
            Libraries.Insert(0, new LibraryDefinition
            {
                Id = $"library-{Guid.NewGuid():N}",
                Name = DirectoryName(legacyDirectory),
                Path = legacyDirectory
            });
        }

        var unique = new List<LibraryDefinition>();
        foreach (var library in Libraries.Where(item => item is not null && !string.IsNullOrWhiteSpace(item.Path)))
        {
            var path = library.Path.Trim();
            if (unique.Any(item => SamePath(item.Path, path))) continue;
            library.Id = string.IsNullOrWhiteSpace(library.Id) ? $"library-{Guid.NewGuid():N}" : library.Id.Trim();
            library.Name = CleanName(library.Name, DirectoryName(path));
            library.Path = path;
            unique.Add(library);
            if (unique.Count == 3) break;
        }
        Libraries = unique;

        if (!Libraries.Any(item => item.Id == ActiveLibraryId))
            ActiveLibraryId = Libraries.FirstOrDefault(item => legacyDirectory is not null && SamePath(item.Path, legacyDirectory))?.Id;
        LibraryDirectory = null;
    }

    private void NormalizeInteractionWordPacks()
    {
        InteractionWordPacks ??= [];
        var legacyWords = NormalizeWords(InteractionWords);
        var migratedPackId = (string?)null;
        if (InteractionWordPacks.Count == 0 && legacyWords.Count > 0)
        {
            migratedPackId = $"words-{Guid.NewGuid():N}";
            InteractionWordPacks.Add(new InteractionWordPackDefinition
            {
                Id = migratedPackId,
                Name = CleanName(InteractionWordPackName, "已迁移词包"),
                Words = legacyWords
            });
        }

        var normalized = new List<InteractionWordPackDefinition>();
        foreach (var pack in InteractionWordPacks.Where(item => item is not null))
        {
            var words = NormalizeWords(pack.Words);
            if (words.Count == 0) continue;
            pack.Id = string.IsNullOrWhiteSpace(pack.Id) ? $"words-{Guid.NewGuid():N}" : pack.Id.Trim();
            pack.Name = CleanName(pack.Name, "互动词包");
            pack.Words = words;
            normalized.Add(pack);
            if (normalized.Count == 5) break;
        }
        InteractionWordPacks = normalized;

        if (!InteractionWordPacks.Any(item => item.Id == ActiveInteractionWordPackId))
        {
            ActiveInteractionWordPackId = migratedPackId
                ?? InteractionWordPacks.FirstOrDefault(item => string.Equals(
                    item.Name, InteractionWordPackName, StringComparison.OrdinalIgnoreCase))?.Id;
        }
        InteractionWords = null;
        InteractionWordPackName = null;
    }

    private void NormalizeTheaterScripts()
    {
        TheaterScripts ??= [];
        var normalized = new List<TheaterScriptDefinition>();
        foreach (var script in TheaterScripts.Where(item => item is not null))
        {
            var scenes = (script.Scenes ?? [])
                .Where(scene => scene is not null)
                .Select(scene => new TheaterSceneDefinition
                {
                    Main = CleanLine(scene.Main),
                    Companion = CleanLine(scene.Companion)
                })
                .Where(scene => scene.Main.Length > 0 && scene.Companion.Length > 0)
                .Take(5)
                .ToList();
            if (scenes.Count < 3) continue;
            script.Id = string.IsNullOrWhiteSpace(script.Id) ? $"theater-{Guid.NewGuid():N}" : script.Id.Trim();
            script.Name = CleanName(script.Name, "小剧场剧本");
            script.Scenes = scenes;
            normalized.Add(script);
            if (normalized.Count == 10) break;
        }
        TheaterScripts = normalized;
    }

    private static Dictionary<string, List<string>> NormalizeWords(Dictionary<string, List<string>>? source)
    {
        var result = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        if (source is null) return result;
        foreach (var (action, values) in source)
        {
            if (string.IsNullOrWhiteSpace(action) || values is null) continue;
            var cleanValues = values.Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim()).Distinct().Take(50).ToList();
            if (cleanValues.Count > 0) result[action.Trim()] = cleanValues;
        }
        return result;
    }

    private static bool SamePath(string left, string right)
    {
        try
        {
            return string.Equals(
                Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);
        }
    }

    private static string DirectoryName(string path)
    {
        try
        {
            var trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return Path.GetFileName(trimmed) is { Length: > 0 } name ? name : trimmed;
        }
        catch { return "GIF 资源库"; }
    }

    private static string CleanName(string? value, string fallback)
    {
        var clean = string.Join(' ', (value ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (clean.Length == 0) clean = fallback;
        return clean.Length <= 40 ? clean : clean[..40];
    }

    private static string CleanLine(string? value)
    {
        var clean = string.Join(' ', (value ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return clean.Length <= 60 ? clean : clean[..60];
    }
}

public sealed class LibraryDefinition
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = $"library-{Guid.NewGuid():N}";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "GIF 资源库";

    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;
}

public sealed class InteractionWordPackDefinition
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = $"words-{Guid.NewGuid():N}";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "互动词包";

    [JsonPropertyName("words")]
    public Dictionary<string, List<string>> Words { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    [JsonIgnore]
    public int WordCount => Words.Sum(item => item.Value.Count);
}

public sealed class TheaterScriptDefinition
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = $"theater-{Guid.NewGuid():N}";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "小剧场剧本";

    [JsonPropertyName("scenes")]
    public List<TheaterSceneDefinition> Scenes { get; set; } = [];
}

public sealed class TheaterSceneDefinition
{
    [JsonPropertyName("main")]
    public string Main { get; set; } = string.Empty;

    [JsonPropertyName("companion")]
    public string Companion { get; set; } = string.Empty;
}

public sealed class PetDefinition
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    [JsonPropertyName("name")]
    public string Name { get; set; } = "自定义桌宠";

    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;
}

public sealed class ReminderDefinition
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = $"reminder-{Guid.NewGuid():N}";

    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonPropertyName("at")]
    public long? At { get; set; }

    [JsonPropertyName("message")]
    public string Message { get; set; } = "休息一下吧";

    [JsonPropertyName("emotion")]
    public string Emotion { get; set; } = "happy";

    [JsonPropertyName("repeatDaily")]
    public bool RepeatDaily { get; set; }

    [JsonIgnore]
    public DateTime LocalTime
    {
        get => At.HasValue ? DateTimeOffset.FromUnixTimeMilliseconds(At.Value).LocalDateTime : DateTime.Now.AddMinutes(10);
        set => At = new DateTimeOffset(value).ToUnixTimeMilliseconds();
    }
}

public sealed class WindowPosition
{
    [JsonPropertyName("x")]
    public double X { get; set; }

    [JsonPropertyName("y")]
    public double Y { get; set; }
}
