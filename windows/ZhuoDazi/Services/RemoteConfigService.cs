using System.Text.Json;
using System.Net.Http;
using ZhuoDazi.Models;

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
    public DailySummaryConfig? DailySummaries { get; init; }
}

internal sealed class RemoteConfigService
{
    private const int MaximumConfigBytes = 1024 * 1024;
    private static Uri? _previewSiteSettingsUri;
    internal static Uri? PreviewSiteSettingsUri
    {
        get => _previewSiteSettingsUri;
        set
        {
            if (value is not null && (!value.IsAbsoluteUri || !value.IsLoopback
                || (value.Scheme != Uri.UriSchemeHttp && value.Scheme != Uri.UriSchemeHttps)
                || !string.IsNullOrEmpty(value.UserInfo)))
                throw new ArgumentException("本地预览配置地址必须使用 loopback HTTP 或 HTTPS 地址。", nameof(value));
            _previewSiteSettingsUri = value;
        }
    }
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
            using var response = await client.GetAsync(PreviewSiteSettingsUri ?? new Uri(DeskPetApi.SiteSettings),
                HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode) return false;
            if (response.Content.Headers.ContentLength > MaximumConfigBytes) return false;
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var body = new MemoryStream();
            var buffer = new byte[8192];
            using var readTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            readTimeout.CancelAfter(TimeSpan.FromSeconds(8));
            int count;
            while ((count = await stream.ReadAsync(buffer, readTimeout.Token)) != 0)
            {
                if (body.Length + count > MaximumConfigBytes) return false;
                body.Write(buffer, 0, count);
            }
            body.Position = 0;
            using var document = await JsonDocument.ParseAsync(body, cancellationToken: cancellationToken);
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
            if (!File.Exists(path) || new FileInfo(path).Length > MaximumConfigBytes) return new RemoteConfig();
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

    private string CachePath() => Path.Combine(_store.DataDirectory,
        PreviewSiteSettingsUri is null ? "remote-config.json" : $"remote-config-preview-{PreviewSiteSettingsUri.Port}.json");

    private static RemoteConfig Parse(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object) return new RemoteConfig();
        if (root.TryGetProperty("publicSiteSettings", out var nested) && nested.ValueKind == JsonValueKind.Object) root = nested;
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
            TheaterIntervalSeconds = theaterInterval is 60 or 180 or 300 or 600 or 1800 ? theaterInterval : 300,
            DailySummaries = root.TryGetProperty("dailySummaries", out var summaries)
                ? DailySummaryService.ParseConfiguration(summaries) : null
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
