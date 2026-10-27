using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using Org.BouncyCastle.Security;
using ZhuoDazi.Models;

/// <summary>In-memory content endpoint with a test-only Ed25519 key; never opens a socket.</summary>
internal sealed class RealtimeContentHandler(Action<string>? log = null) : HttpMessageHandler
{
    private readonly Ed25519PrivateKeyParameters _privateKey = new(new SecureRandom());
    public byte[] PublicKey => _privateKey.GeneratePublicKey().GetEncoded();
    public List<ContentRequest> BatchRequests { get; } = [];
    public List<InteractionContentItem> Items { get; set; } = [];
    public List<string> DisabledIds { get; set; } = [];
    public long CatalogVersion { get; set; } = 1;
    public bool IgnoreTypeFilter { get; set; }
    public bool IgnoreExclusions { get; set; }
    public bool CorruptSignature { get; set; }
    public bool FailConnection { get; set; }
    public bool IgnoreCancellation { get; set; }
    public HttpStatusCode? FailureStatus { get; set; }
    public Func<CancellationToken, Task>? BeforeContentResponse { get; set; }
    public Func<CancellationToken, Task>? BeforeProfileResponse { get; set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri!.AbsolutePath;
        log?.Invoke($"MOCK {request.Method} {path}");
        if (path == "/api/interactions/profile")
        {
            if (BeforeProfileResponse is { } beforeProfile) await beforeProfile(cancellationToken);
            return Json(new { profile = new { mode = "lively", promptsEnabled = true } });
        }
        if (path == "/api/interactions/events") return Json(new { accepted = true });
        if (path != "/api/content/batch" || request.Method != HttpMethod.Post)
            throw new InvalidOperationException($"Unexpected request in isolated content test: {request.Method} {path}");

        using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
        var contentRequest = new ContentRequest(
            body.RootElement.GetProperty("types").EnumerateArray().Select(value => value.GetString()!).ToArray(),
            body.RootElement.GetProperty("limit").GetInt32(),
            body.RootElement.GetProperty("excludeIds").EnumerateArray().Select(value => value.GetString()!).ToArray());
        BatchRequests.Add(contentRequest);
        if (BeforeContentResponse is { } beforeResponse) await beforeResponse(cancellationToken);
        if (!IgnoreCancellation) cancellationToken.ThrowIfCancellationRequested();
        if (FailConnection) throw new HttpRequestException("Test-only connection failure");
        if (FailureStatus is { } status) return Json(new { error = "模拟联网失败，请稍后重试。" }, status);

        // Match the real server's requested-type ordering, so limit=1 preference is exercised.
        var selected = Items
            .Where(item => IgnoreTypeFilter || contentRequest.Types.Contains(item.Type, StringComparer.Ordinal))
            .Where(item => IgnoreExclusions || !contentRequest.ExcludeIds.Contains(item.Id, StringComparer.Ordinal))
            .OrderBy(item => Array.IndexOf(contentRequest.Types, item.Type))
            .Take(contentRequest.Limit).ToList();
        var payload = new SignedContentPayload
        {
            SchemaVersion = 1,
            Kind = "batch",
            CatalogVersion = CatalogVersion,
            CatalogUpdatedAt = DateTimeOffset.UtcNow.ToString("O"),
            Items = selected,
            DisabledIds = DisabledIds.ToList()
        };
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload);
        var signer = new Ed25519Signer();
        signer.Init(true, _privateKey);
        signer.BlockUpdate(bytes, 0, bytes.Length);
        var signature = signer.GenerateSignature();
        if (CorruptSignature) signature[0] ^= 1;
        return Json(new SignedContentEnvelope
        {
            SignedPayload = Convert.ToBase64String(bytes),
            Sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
            SignatureAlgorithm = "ed25519",
            Signature = Convert.ToBase64String(signature)
        });
    }

    public static InteractionContentItem Question(string id, string type = "math", string? prompt = null)
        => new()
        {
            Id = id,
            Type = type,
            Revision = 1,
            Prompt = prompt ?? "【自动验收模拟题】2 + 2 等于多少？",
            Answer = "4",
            Choices = ["3", "4", "5"],
            Explanation = "这道题只用于本地自动验收，不会加入线上题库。"
        };

    private static HttpResponseMessage Json(object value, HttpStatusCode status = HttpStatusCode.OK)
        => new(status) { Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json") };
}

internal sealed record ContentRequest(string[] Types, int Limit, string[] ExcludeIds);
