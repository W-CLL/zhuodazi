using System.Text.Json.Serialization;

namespace ZhuoDazi.Models;

public sealed class InteractionContentItem
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("revision")]
    public int Revision { get; set; }

    [JsonPropertyName("prompt")]
    public string Prompt { get; set; } = string.Empty;

    [JsonPropertyName("answer")]
    public string Answer { get; set; } = string.Empty;

    [JsonPropertyName("explanation")]
    public string Explanation { get; set; } = string.Empty;

    [JsonPropertyName("choices")]
    public List<string> Choices { get; set; } = [];

    [JsonPropertyName("tags")]
    public List<string> Tags { get; set; } = [];

    [JsonPropertyName("difficulty")]
    public int Difficulty { get; set; } = 1;

    [JsonPropertyName("locale")]
    public string Locale { get; set; } = "zh-CN";
}

internal sealed class SignedContentEnvelope
{
    [JsonPropertyName("signedPayload")]
    public string SignedPayload { get; set; } = string.Empty;

    [JsonPropertyName("sha256")]
    public string Sha256 { get; set; } = string.Empty;

    [JsonPropertyName("signatureAlgorithm")]
    public string SignatureAlgorithm { get; set; } = string.Empty;

    [JsonPropertyName("signature")]
    public string Signature { get; set; } = string.Empty;
}

internal sealed class SignedContentPayload
{
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; set; }

    [JsonPropertyName("kind")]
    public string Kind { get; set; } = string.Empty;

    [JsonPropertyName("catalogVersion")]
    public long CatalogVersion { get; set; }

    [JsonPropertyName("catalogUpdatedAt")]
    public string CatalogUpdatedAt { get; set; } = string.Empty;

    [JsonPropertyName("items")]
    public List<InteractionContentItem> Items { get; set; } = [];

    [JsonPropertyName("disabledIds")]
    public List<string> DisabledIds { get; set; } = [];
}

internal sealed class InteractionProfileEnvelope
{
    [JsonPropertyName("profile")]
    public InteractionProfile Profile { get; set; } = new();
}

internal sealed class InteractionProfile
{
    [JsonPropertyName("mode")]
    public string Mode { get; set; } = "standard";

    [JsonPropertyName("promptsEnabled")]
    public bool PromptsEnabled { get; set; } = true;
}

internal sealed class InteractionEventRecord
{
    [JsonPropertyName("eventId")]
    public string EventId { get; set; } = Guid.NewGuid().ToString();

    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("occurredAt")]
    public string OccurredAt { get; set; } = DateTimeOffset.UtcNow.ToString("O");

    [JsonPropertyName("mood")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Mood { get; set; }

    [JsonPropertyName("contentId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ContentId { get; set; }

    [JsonPropertyName("correct")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Correct { get; set; }
}

internal sealed class InteractionShownContent
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("shownAt")]
    public string ShownAt { get; set; } = DateTimeOffset.UtcNow.ToString("O");
}

internal sealed class InteractionErrorResponse
{
    [JsonPropertyName("message")]
    public string? Message { get; set; }
}
