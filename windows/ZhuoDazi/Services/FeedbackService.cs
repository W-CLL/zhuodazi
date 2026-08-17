using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ZhuoDazi.Services;

public sealed class FeedbackService : IDisposable
{
    private const string FeedbackUrl = DeskPetApi.Feedback;
    private const int MaxResponseBytes = 256 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly LicenseService _licenses;
    private readonly HttpClient _httpClient = DeskPetHttp.CreateClient(TimeSpan.FromSeconds(25));

    public FeedbackService(LicenseService licenses) => _licenses = licenses;

    public async Task<FeedbackListResponse> GetAsync(CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, FeedbackUrl);
        Authorize(request);
        return await SendAsync<FeedbackListResponse>(request, cancellationToken);
    }

    public async Task<FeedbackSubmitResponse> SubmitAsync(
        string type,
        string title,
        string content,
        CancellationToken cancellationToken = default)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(new { type, title, content });
        using var request = new HttpRequestMessage(HttpMethod.Post, FeedbackUrl)
        {
            Content = new ByteArrayContent(payload)
        };
        request.Content.Headers.ContentType = new("application/json") { CharSet = "utf-8" };
        Authorize(request);
        return await SendAsync<FeedbackSubmitResponse>(request, cancellationToken);
    }

    private void Authorize(HttpRequestMessage request)
    {
        _licenses.Authorize(request);
        request.Headers.TryAddWithoutValidation("X-DeskPet-Platform", "windows");
    }

    private async Task<T> SendAsync<T>(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
        }
        catch (Exception error)
        {
            var message = NetworkConnectionErrors.Format(error, "连接反馈服务超时，请稍后重试。");
            throw new InvalidOperationException(message, error);
        }

        using (response)
        {
            if (response.Content.Headers.ContentLength is > MaxResponseBytes)
                throw new InvalidOperationException("反馈服务响应无效。");
            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            if (bytes.Length > MaxResponseBytes)
                throw new InvalidOperationException("反馈服务响应无效。");
            if (!response.IsSuccessStatusCode)
            {
                var message = TryReadError(bytes) ?? "反馈请求失败，请稍后重试。";
                throw new InvalidOperationException(message);
            }
            return JsonSerializer.Deserialize<T>(bytes, JsonOptions)
                ?? throw new InvalidOperationException("反馈服务返回的数据无效。");
        }
    }

    private static string? TryReadError(byte[] bytes)
    {
        try
        {
            return JsonSerializer.Deserialize<FeedbackErrorResponse>(bytes, JsonOptions)?.Error;
        }
        catch
        {
            return null;
        }
    }

    public void Dispose() => _httpClient.Dispose();
}

public sealed class FeedbackQuota
{
    [JsonPropertyName("active")]
    public int Active { get; set; }

    [JsonPropertyName("maximum")]
    public int Maximum { get; set; } = 3;

    [JsonPropertyName("remaining")]
    public int Remaining { get; set; } = 3;
}

public sealed class FeedbackItem
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; set; } = "problem";

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("content")]
    public string Content { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = "pending";

    [JsonPropertyName("adminNote")]
    public string AdminNote { get; set; } = string.Empty;

    [JsonPropertyName("createdAt")]
    public string CreatedAt { get; set; } = string.Empty;

    [JsonPropertyName("updatedAt")]
    public string UpdatedAt { get; set; } = string.Empty;
}

public sealed class FeedbackListResponse
{
    [JsonPropertyName("quota")]
    public FeedbackQuota Quota { get; set; } = new();

    [JsonPropertyName("items")]
    public List<FeedbackItem> Items { get; set; } = [];
}

public sealed class FeedbackSubmitResponse
{
    [JsonPropertyName("quota")]
    public FeedbackQuota Quota { get; set; } = new();

    [JsonPropertyName("item")]
    public FeedbackItem Item { get; set; } = new();
}

internal sealed class FeedbackErrorResponse
{
    [JsonPropertyName("error")]
    public string? Error { get; set; }
}
