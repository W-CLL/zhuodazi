using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ZhuoDazi.Services;

internal sealed record UninstallFeedbackChoice(string? Reason, string Comment)
{
    private static readonly HashSet<string> AllowedReasons = new(StringComparer.Ordinal)
    {
        "not_often_used", "resource_usage", "experience_issue", "temporary_pause", "other"
    };

    internal static UninstallFeedbackChoice Skip { get; } = new(null, string.Empty);

    internal static UninstallFeedbackChoice Create(string? reason, string? comment)
    {
        reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim().ToLowerInvariant();
        comment = (comment ?? string.Empty).Trim();
        if (reason is not null && !AllowedReasons.Contains(reason))
            throw new ArgumentException("卸载原因无效。", nameof(reason));
        if (comment.Length > 500)
            throw new ArgumentException("补充说明最多 500 字。", nameof(comment));
        if (comment.Length > 0 && reason != "other")
            throw new ArgumentException("补充说明仅用于“其他”原因。", nameof(comment));
        return new UninstallFeedbackChoice(reason, comment);
    }
}

internal sealed record UninstallIntentReceipt(string EventId, string CompletionToken);

internal sealed class UninstallFeedbackService : IDisposable
{
    internal const string CompletionTimestampPlaceholder = "__POST_UNINSTALL_UTC__";
    private const int MaximumResponseBytes = 32 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };
    private readonly HttpClient _httpClient = DeskPetHttp.CreateClient(
        TimeSpan.FromSeconds(4),
        allowAutoRedirect: false);

    internal async Task<bool> ReportInstalledStartAsync(
        InstallEventIdentity identity,
        InstallBatchStore store,
        InstallBatchRecord batch,
        CancellationToken cancellationToken = default)
    {
        var current = store.LoadExisting();
        if (current is null
            || !current.Id.Equals(batch.Id, StringComparison.OrdinalIgnoreCase)
            || current.InstalledStartReportedAt is not null)
            return current?.InstalledStartReportedAt is not null;

        var payload = IdentityPayload(
            identity,
            current,
            current.InstalledStartEventId,
            "installed_start",
            current.CreatedAt,
            null);
        var response = await SendAsync(payload, cancellationToken);
        if (response is null || !response.EventId.Equals(current.InstalledStartEventId, StringComparison.OrdinalIgnoreCase))
            return false;
        return store.MarkInstalledStartReported(current, DateTimeOffset.UtcNow);
    }

    internal async Task<UninstallIntentReceipt?> SubmitIntentAsync(
        InstallEventIdentity identity,
        InstallBatchRecord batch,
        UninstallFeedbackChoice choice,
        CancellationToken cancellationToken = default)
    {
        var eventId = Guid.NewGuid().ToString("D");
        var payload = IdentityPayload(identity, batch, eventId, "uninstall_intent", DateTimeOffset.UtcNow, choice);
        var response = await SendAsync(payload, cancellationToken);
        if (response is null
            || !response.EventId.Equals(eventId, StringComparison.OrdinalIgnoreCase)
            || !IsCompletionToken(response.CompletionToken))
            return null;
        return new UninstallIntentReceipt(eventId, response.CompletionToken!);
    }

    internal static void WriteCompletionPayloadTemplate(string path, UninstallIntentReceipt receipt)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("完成事件临时路径无效。", nameof(path));
        if (!Guid.TryParseExact(receipt.EventId, "D", out _) || !IsCompletionToken(receipt.CompletionToken))
            throw new ArgumentException("完成事件凭据无效。", nameof(receipt));

        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath);
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            throw new DirectoryNotFoundException("完成事件临时目录不存在。");

        var payload = new Dictionary<string, object?>
        {
            ["schemaVersion"] = 1,
            ["eventId"] = Guid.NewGuid().ToString("D"),
            ["eventType"] = "uninstall_completed",
            ["intentEventId"] = receipt.EventId,
            ["completionToken"] = receipt.CompletionToken,
            ["occurredAt"] = CompletionTimestampPlaceholder
        };
        var temporaryPath = $"{fullPath}.{Environment.ProcessId}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllBytes(temporaryPath, JsonSerializer.SerializeToUtf8Bytes(payload));
            File.Move(temporaryPath, fullPath, true);
        }
        finally
        {
            try { File.Delete(temporaryPath); } catch { }
        }
    }

    private static Dictionary<string, object?> IdentityPayload(
        InstallEventIdentity identity,
        InstallBatchRecord batch,
        string eventId,
        string eventType,
        DateTimeOffset occurredAt,
        UninstallFeedbackChoice? choice)
    {
        var payload = new Dictionary<string, object?>
        {
            ["schemaVersion"] = 1,
            ["eventId"] = eventId,
            ["eventType"] = eventType,
            ["installationId"] = identity.InstallationId,
            ["credential"] = identity.Credential,
            ["installBatchId"] = batch.Id,
            ["installBatchCreatedAt"] = Timestamp(batch.CreatedAt),
            ["platform"] = "windows",
            ["appVersion"] = UpdateService.CurrentVersion,
            ["occurredAt"] = Timestamp(occurredAt)
        };
        if (choice is not null)
        {
            payload["reason"] = choice.Reason;
            payload["comment"] = choice.Comment;
        }
        return payload;
    }

    private async Task<InstallEventResponse?> SendAsync(
        Dictionary<string, object?> payload,
        CancellationToken cancellationToken)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(3));
            using var request = new HttpRequestMessage(HttpMethod.Post, DeskPetApi.DeviceInstallEvents)
            {
                Content = new ByteArrayContent(JsonSerializer.SerializeToUtf8Bytes(payload))
            };
            request.Content.Headers.ContentType = new("application/json") { CharSet = "utf-8" };
            request.Headers.UserAgent.ParseAdd($"ZhuoDazi/{UpdateService.CurrentVersion}");
            request.Headers.TryAddWithoutValidation("X-DeskPet-Platform", "windows");
            request.Headers.TryAddWithoutValidation("X-DeskPet-Version", UpdateService.CurrentVersion);
            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength is > MaximumResponseBytes)
                return null;
            var bytes = await ReadBoundedResponseAsync(response.Content, timeout.Token);
            if (bytes.Length is 0 or > MaximumResponseBytes) return null;
            var result = JsonSerializer.Deserialize<InstallEventResponse>(bytes, JsonOptions);
            return result is { Ok: true } && Guid.TryParseExact(result.EventId, "D", out _) ? result : null;
        }
        catch
        {
            return null;
        }
    }

    private static async Task<byte[]> ReadBoundedResponseAsync(
        HttpContent content,
        CancellationToken cancellationToken)
    {
        await using var source = await content.ReadAsStreamAsync(cancellationToken);
        using var destination = new MemoryStream();
        var buffer = new byte[8 * 1024];
        while (true)
        {
            var remaining = MaximumResponseBytes + 1 - checked((int)destination.Length);
            var count = await source.ReadAsync(
                buffer.AsMemory(0, Math.Min(buffer.Length, remaining)),
                cancellationToken);
            if (count == 0) return destination.ToArray();
            destination.Write(buffer, 0, count);
            if (destination.Length > MaximumResponseBytes) return Array.Empty<byte>();
        }
    }

    private static bool IsCompletionToken(string? value)
        => value is { Length: >= 40 and <= 80 }
            && value.All(character => char.IsLetterOrDigit(character) || character is '-' or '_');

    private static string Timestamp(DateTimeOffset value) => value.ToUniversalTime().ToString("O");

    public void Dispose() => _httpClient.Dispose();

    private sealed class InstallEventResponse
    {
        [JsonPropertyName("ok")]
        public bool Ok { get; set; }

        [JsonPropertyName("eventId")]
        public string EventId { get; set; } = string.Empty;

        [JsonPropertyName("completionToken")]
        public string? CompletionToken { get; set; }
    }
}
