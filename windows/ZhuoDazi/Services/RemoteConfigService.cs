using System.Text.Json;

namespace ZhuoDazi.Services;

public sealed class RemoteConfig
{
    public string WechatId { get; init; } = "wcl_lcw627";
    public string Announcement { get; init; } = "";
    public string XianyuUrl { get; init; } = "";
    public bool TrialVisits { get; init; } = true;
    public bool CompanionHall { get; init; } = true;
    public bool FishMode { get; init; } = true;
    public bool AutoUpdates { get; init; } = true;
    public string Personality { get; init; } = "lively";
    public string InteractionMode { get; init; } = "standard";
    public int TheaterIntervalSeconds { get; init; } = 300;
}

internal sealed class RemoteConfigService
{
    private readonly SettingsStore _store;
    private readonly object _gate = new();
    private RemoteConfig _current = new();

    public RemoteConfigService(SettingsStore store)
    {
        _store = store;
        _current = LoadCached();
    }

    public RemoteConfig Current
    {
        get { lock (_gate) return _current; }
    }

    public event Action? Changed;

    public async Task<bool> RefreshAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var client = DeskPetHttp.CreateClient(TimeSpan.FromSeconds(8));
            using var response = await client.GetAsync(DeskPetApi.SiteSettings, cancellationToken);
            if (!response.IsSuccessStatusCode) return false;
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var next = Parse(document.RootElement);
            SaveCache(document.RootElement);
            lock (_gate) _current = next;
            Changed?.Invoke();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private RemoteConfig LoadCached()
    {
        try
        {
            var path = CachePath();
            if (!File.Exists(path)) return new RemoteConfig();
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            return Parse(document.RootElement);
        }
        catch
        {
            return new RemoteConfig();
        }
    }

    private void SaveCache(JsonElement root)
    {
        try
        {
            Directory.CreateDirectory(_store.DataDirectory);
            var temporaryPath = $"{CachePath()}.{Environment.ProcessId}.tmp";
            File.WriteAllText(temporaryPath, root.GetRawText());
            File.Move(temporaryPath, CachePath(), true);
        }
        catch
        {
        }
    }

    private string CachePath() => Path.Combine(_store.DataDirectory, "remote-config.json");

    private static RemoteConfig Parse(JsonElement root)
    {
        var features = root.TryGetProperty("features", out var featuresElement) ? featuresElement : default;
        var defaults = root.TryGetProperty("defaults", out var defaultsElement) ? defaultsElement : default;
        var personality = ReadString(defaults, "personality", "lively");
        var interactionMode = ReadString(defaults, "interactionMode", "standard");
        var theaterInterval = ReadInt(defaults, "theaterIntervalSeconds", 300);
        var wechatId = ReadString(root, "wechatId", "wcl_lcw627");
        return new RemoteConfig
        {
            WechatId = IsWechatId(wechatId) ? wechatId : "wcl_lcw627",
            Announcement = ReadString(root, "announcement", "").Replace('\r', ' ').Replace('\n', ' ').Trim(),
            XianyuUrl = ReadString(root, "xianyuUrl", ""),
            TrialVisits = ReadBool(features, "trialVisits", true),
            CompanionHall = ReadBool(features, "companionHall", true),
            FishMode = ReadBool(features, "fishMode", true),
            AutoUpdates = ReadBool(features, "autoUpdates", true),
            Personality = personality is "lively" or "shy" or "clingy" or "chaotic" ? personality : "lively",
            InteractionMode = interactionMode is "quiet" or "standard" or "lively" ? interactionMode : "standard",
            TheaterIntervalSeconds = theaterInterval is 60 or 180 or 300 or 600 or 1800 ? theaterInterval : 300
        };
    }

    private static string ReadString(JsonElement element, string name, string fallback)
    {
        if (element.ValueKind != JsonValueKind.Object) return fallback;
        if (!element.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
            return fallback;
        return value.GetString()?.Trim() ?? fallback;
    }

    private static bool ReadBool(JsonElement element, string name, bool fallback)
    {
        if (element.ValueKind != JsonValueKind.Object) return fallback;
        if (!element.TryGetProperty(name, out var value)) return fallback;
        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => fallback
        };
    }

    private static int ReadInt(JsonElement element, string name, int fallback)
    {
        if (element.ValueKind != JsonValueKind.Object) return fallback;
        if (!element.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Number)
            return fallback;
        return value.TryGetInt32(out var number) ? number : fallback;
    }

    private static bool IsWechatId(string value)
        => value.Length is >= 6 and <= 20 && System.Text.RegularExpressions.Regex.IsMatch(value, "^[A-Za-z][-_A-Za-z0-9]{5,19}$");
}
