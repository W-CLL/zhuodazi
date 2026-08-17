using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ZhuoDazi.Services;

public sealed record CompanionPartner(
    [property: JsonPropertyName("displayName")] string DisplayName,
    [property: JsonPropertyName("pairedAt")] string? PairedAt);

public sealed record CompanionProfile(
    [property: JsonPropertyName("displayName")] string DisplayName,
    [property: JsonPropertyName("pairingCode")] string PairingCode,
    [property: JsonPropertyName("partner")] CompanionPartner? Partner);

public sealed record CompanionVisit(string Id, string SenderName, string FilePath);

public sealed class CompanionService : IDisposable
{
    private const string CompanionUrl = DeskPetApi.Companion;
    private const string PairUrl = DeskPetApi.CompanionPair;
    private const string DeliveriesUrl = DeskPetApi.CompanionDeliveries;
    private const int MaximumGifBytes = 8 * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly LicenseService _licenses;
    private readonly HttpClient _httpClient;
    private readonly string _inboxDirectory;

    public CompanionProfile? Profile { get; private set; }

    public CompanionService(SettingsStore store, LicenseService licenses)
    {
        _licenses = licenses;
        _inboxDirectory = store.CompanionDirectory;
        _httpClient = DeskPetHttp.CreateClient(TimeSpan.FromSeconds(35));
        try
        {
            if (Directory.Exists(_inboxDirectory)) Directory.Delete(_inboxDirectory, true);
        }
        catch { }
    }

    public async Task<CompanionProfile> RefreshProfileAsync(CancellationToken cancellationToken = default)
    {
        using var request = CreateRequest(HttpMethod.Get, CompanionUrl);
        Profile = await SendJsonAsync<CompanionProfile>(request, cancellationToken);
        return Profile;
    }

    public async Task<CompanionProfile> UpdateNameAsync(string displayName, CancellationToken cancellationToken = default)
    {
        using var request = CreateJsonRequest(HttpMethod.Patch, CompanionUrl, new { displayName });
        Profile = await SendJsonAsync<CompanionProfile>(request, cancellationToken);
        return Profile;
    }

    public async Task<CompanionProfile> PairAsync(string code, CancellationToken cancellationToken = default)
    {
        using var request = CreateJsonRequest(HttpMethod.Post, PairUrl, new { code });
        Profile = await SendJsonAsync<CompanionProfile>(request, cancellationToken);
        return Profile;
    }

    public async Task<CompanionProfile> UnpairAsync(CancellationToken cancellationToken = default)
    {
        using var request = CreateRequest(HttpMethod.Delete, PairUrl);
        Profile = await SendJsonAsync<CompanionProfile>(request, cancellationToken);
        return Profile;
    }

    public async Task<string> SendCurrentGifAsync(string gifPath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(gifPath)) throw new InvalidOperationException("当前 GIF 文件不存在。");
        var info = new FileInfo(gifPath);
        if (!gifPath.EndsWith(".gif", StringComparison.OrdinalIgnoreCase)
            || info.Length < 10 || info.Length > MaximumGifBytes)
            throw new InvalidOperationException("只能发送不超过 8 MB 的 GIF。");

        using var request = CreateRequest(HttpMethod.Post, DeliveriesUrl);
        await using var stream = File.OpenRead(gifPath);
        request.Content = new StreamContent(stream);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("image/gif");
        request.Content.Headers.ContentLength = info.Length;
        var response = await SendJsonAsync<SendResponse>(request, cancellationToken);
        return response.RecipientName;
    }

    public async Task<IReadOnlyList<CompanionVisit>> ReceiveAsync(CancellationToken cancellationToken = default)
    {
        using var listRequest = CreateRequest(HttpMethod.Get, DeliveriesUrl);
        var pending = await SendJsonAsync<DeliveryListResponse>(listRequest, cancellationToken);
        if (pending.Deliveries.Count == 0) return [];

        Directory.CreateDirectory(_inboxDirectory);
        var visits = new List<CompanionVisit>();
        foreach (var item in pending.Deliveries)
        {
            var filePath = Path.Combine(_inboxDirectory, $"{item.Id}.gif");
            if (!DeskPetHttp.TryCreateCompanionFileUrl(item.DownloadPath, out var downloadUri)
                || downloadUri is null)
                throw new InvalidOperationException("来访下载地址无效。");
            using var downloadRequest = CreateRequest(HttpMethod.Get, downloadUri.AbsoluteUri);
            using var response = await _httpClient.SendAsync(
                downloadRequest,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException(await ReadErrorAsync(response, cancellationToken));
            var length = response.Content.Headers.ContentLength;
            if (length is null or < 10 or > MaximumGifBytes)
                throw new InvalidOperationException("收到的 GIF 大小无效。");
            await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var destination = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None))
                await source.CopyToAsync(destination, cancellationToken);
            ValidateDownloadedGif(filePath, item.Sha256);

            using var acknowledgeRequest = CreateRequest(
                HttpMethod.Post,
                $"{DeliveriesUrl}/{Uri.EscapeDataString(item.Id)}/acknowledge");
            using var acknowledgeResponse = await _httpClient.SendAsync(acknowledgeRequest, cancellationToken);
            if (!acknowledgeResponse.IsSuccessStatusCode)
                throw new InvalidOperationException(await ReadErrorAsync(acknowledgeResponse, cancellationToken));
            visits.Add(new CompanionVisit(item.Id, item.SenderName, filePath));
        }
        return visits;
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string url)
    {
        var request = new HttpRequestMessage(method, url);
        _licenses.Authorize(request);
        request.Headers.TryAddWithoutValidation("X-DeskPet-Platform", "windows");
        return request;
    }

    private HttpRequestMessage CreateJsonRequest(HttpMethod method, string url, object body)
    {
        var request = CreateRequest(method, url);
        request.Content = new ByteArrayContent(JsonSerializer.SerializeToUtf8Bytes(body));
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json") { CharSet = "utf-8" };
        return request;
    }

    private async Task<T> SendJsonAsync<T>(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(await ReadErrorAsync(response, cancellationToken));
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken)
            ?? throw new InvalidOperationException("搭子服务响应无效。");
    }

    private static async Task<string> ReadErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            if (bytes.Length > 32 * 1024) return "搭子服务暂时不可用。";
            return JsonSerializer.Deserialize<ErrorResponse>(bytes, JsonOptions)?.Error
                ?? "搭子服务暂时不可用。";
        }
        catch { return "搭子服务暂时不可用。"; }
    }

    private static void ValidateDownloadedGif(string filePath, string expectedSha256)
    {
        try
        {
            using var stream = File.OpenRead(filePath);
            Span<byte> header = stackalloc byte[10];
            if (stream.Read(header) != header.Length
                || !(header[..6].SequenceEqual("GIF87a"u8) || header[..6].SequenceEqual("GIF89a"u8)))
                throw new InvalidOperationException("收到的文件不是有效 GIF。");
            stream.Position = 0;
            var hash = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
            if (!hash.Equals(expectedSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("收到的 GIF 校验失败。");
        }
        catch
        {
            try { File.Delete(filePath); } catch { }
            throw;
        }
    }

    public void Dispose() => _httpClient.Dispose();

    private sealed record SendResponse(
        [property: JsonPropertyName("recipientName")] string RecipientName);

    private sealed record DeliveryListResponse(
        [property: JsonPropertyName("deliveries")] List<DeliveryItem> Deliveries);

    private sealed record DeliveryItem(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("senderName")] string SenderName,
        [property: JsonPropertyName("sha256")] string Sha256,
        [property: JsonPropertyName("downloadPath")] string DownloadPath);

    private sealed record ErrorResponse([property: JsonPropertyName("error")] string Error);
}
