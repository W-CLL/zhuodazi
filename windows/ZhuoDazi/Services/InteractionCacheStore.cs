using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using ZhuoDazi.Models;

namespace ZhuoDazi.Services;

internal sealed class InteractionCacheStore
{
    private const int MaximumCachedItems = 5000;
    private const int MaximumShownItems = 500;
    private const int MaximumPendingEvents = 1000;
    private static readonly TimeSpan ShownRetention = TimeSpan.FromDays(30);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    private readonly string _path;

    public InteractionCacheStore(string path) => _path = path;

    public InteractionCacheDocument Load()
    {
        try
        {
            if (!File.Exists(_path)) return new InteractionCacheDocument();
            var document = JsonSerializer.Deserialize<InteractionCacheDocument>(
                File.ReadAllText(_path), JsonOptions) ?? new InteractionCacheDocument();
            return Normalize(document, DateTimeOffset.UtcNow);
        }
        catch
        {
            return new InteractionCacheDocument();
        }
    }

    public void Save(InteractionCacheDocument document)
    {
        document = Normalize(document, DateTimeOffset.UtcNow);
        var directory = Path.GetDirectoryName(_path)!;
        Directory.CreateDirectory(directory);
        var temporaryPath = $"{_path}.{Environment.ProcessId}.tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(document, JsonOptions));
        File.Move(temporaryPath, _path, true);
    }

    internal static InteractionCacheDocument Normalize(
        InteractionCacheDocument document,
        DateTimeOffset now)
    {
        document.Version = 1;
        document.CatalogVersion = Math.Max(0, document.CatalogVersion);
        document.Items = (document.Items ?? [])
            .Where(ContentEnvelopeVerifier.IsValidItem)
            .GroupBy(item => item.Id, StringComparer.Ordinal)
            .Select(group => group.OrderByDescending(item => item.Revision).First())
            .Take(MaximumCachedItems)
            .ToList();
        document.Shown = (document.Shown ?? [])
            .Where(item => item is not null && ContentEnvelopeVerifier.IsValidItemId(item.Id)
                && DateTimeOffset.TryParse(item.ShownAt, out var shownAt)
                && shownAt <= now.AddMinutes(10) && shownAt >= now - ShownRetention)
            .GroupBy(item => item.Id, StringComparer.Ordinal)
            .Select(group => group.OrderByDescending(item => item.ShownAt, StringComparer.Ordinal).First())
            .OrderByDescending(item => item.ShownAt, StringComparer.Ordinal)
            .Take(MaximumShownItems)
            .ToList();
        document.PendingEvents = (document.PendingEvents ?? [])
            .Where(item => item is not null && IsValidEvent(item))
            .GroupBy(item => item.EventId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .TakeLast(MaximumPendingEvents)
            .ToList();
        document.InteractionMode = document.InteractionMode is "quiet" or "standard" or "lively"
            ? document.InteractionMode : "standard";
        document.LastPromptType = document.LastPromptType is "mood" or "joke" or "math" or "trivia"
            ? document.LastPromptType : null;
        if (!DateTimeOffset.TryParse(document.NextMoodPromptAt, out _)) document.NextMoodPromptAt = null;
        return document;
    }

    private static bool IsValidEvent(InteractionEventRecord item)
    {
        if (!Guid.TryParse(item.EventId, out _) || !DateTimeOffset.TryParse(item.OccurredAt, out _)) return false;
        return item.Type switch
        {
            "mood_response" => item.Mood is "happy" or "okay" or "low",
            "content_shown" or "joke_revealed" => ContentEnvelopeVerifier.IsValidItemId(item.ContentId),
            "quiz_answered" => ContentEnvelopeVerifier.IsValidItemId(item.ContentId) && item.Correct.HasValue,
            _ => false
        };
    }
}

internal sealed class InteractionCacheDocument
{
    [JsonPropertyName("version")]
    public int Version { get; set; } = 1;

    [JsonPropertyName("catalogVersion")]
    public long CatalogVersion { get; set; }

    [JsonPropertyName("catalogUpdatedAt")]
    public string CatalogUpdatedAt { get; set; } = string.Empty;

    [JsonPropertyName("items")]
    public List<InteractionContentItem> Items { get; set; } = [];

    [JsonPropertyName("shown")]
    public List<InteractionShownContent> Shown { get; set; } = [];

    [JsonPropertyName("pendingEvents")]
    public List<InteractionEventRecord> PendingEvents { get; set; } = [];

    [JsonPropertyName("nextMoodPromptAt")]
    public string? NextMoodPromptAt { get; set; }

    [JsonPropertyName("lastPromptType")]
    public string? LastPromptType { get; set; }

    [JsonPropertyName("interactionMode")]
    public string InteractionMode { get; set; } = "standard";

    [JsonPropertyName("promptsEnabled")]
    public bool PromptsEnabled { get; set; } = true;

    [JsonPropertyName("profileDirty")]
    public bool ProfileDirty { get; set; }
}
