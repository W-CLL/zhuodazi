using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ZhuoDazi.Models;

namespace ZhuoDazi.Services;

internal sealed class PetMarketDebugConfig
{
    public string BaseUrl { get; set; } = string.Empty;
    public string DataDirectory { get; set; } = string.Empty;
    public string AccountId { get; set; } = string.Empty;
    public string LicenseId { get; set; } = string.Empty;
    public string InstallationId { get; set; } = string.Empty;
    public string Credential { get; set; } = string.Empty;
    public string SigningPublicKeySpki { get; set; } = string.Empty;

    internal Uri Origin { get; private set; } = null!;
    internal byte[] SigningPublicKey { get; private set; } = [];

    internal static PetMarketDebugConfig Load(string configPath)
    {
        if (!IsLocalAbsolutePath(configPath) || !File.Exists(configPath)
            || new FileInfo(configPath).Length is <= 0 or > 256 * 1024)
            throw new InvalidOperationException("市场调试配置必须是有效的本机绝对 JSON 文件。");
        PetMarketDebugConfig config;
        try
        {
            config = JsonSerializer.Deserialize<PetMarketDebugConfig>(File.ReadAllText(configPath),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? throw new JsonException();
        }
        catch (JsonException error)
        {
            throw new InvalidOperationException("市场调试配置格式无效。", error);
        }
        if (!Uri.TryCreate(config.BaseUrl, UriKind.Absolute, out var origin))
            throw new InvalidOperationException("市场调试 baseUrl 无效。");
        config.Origin = ValidateLoopbackOrigin(origin);
        if (!IsLocalAbsolutePath(config.DataDirectory))
            throw new InvalidOperationException("市场调试 dataDirectory 必须是本机绝对目录。");
        config.DataDirectory = NormalizeDirectoryPath(config.DataDirectory);
        var production = NormalizeDirectoryPath(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "poko-desktop-pet"));
        if (IsSameOrChildDirectory(config.DataDirectory, production)
            || (TryResolveDirectory(config.DataDirectory) is { } resolved
                && IsSameOrChildDirectory(resolved, production)))
            throw new InvalidOperationException("市场调试不能使用正式用户数据目录或其子目录。");
        if (!Guid.TryParse(config.AccountId, out _) || !Guid.TryParse(config.LicenseId, out _)
            || !ValidIdentityPart(config.InstallationId, 20, 80)
            || !ValidIdentityPart(config.Credential, 40, 80))
            throw new InvalidOperationException("市场调试账号或设备凭证格式无效。");
        byte[] spki;
        try { spki = Convert.FromBase64String(config.SigningPublicKeySpki); }
        catch (FormatException error) { throw new InvalidOperationException("市场调试签名公钥格式无效。", error); }
        if (spki.Length < 32) throw new InvalidOperationException("市场调试签名公钥格式无效。");
        config.SigningPublicKey = spki[^32..];
        Directory.CreateDirectory(config.DataDirectory);
        return config;
    }

    private static bool ValidIdentityPart(string value, int min, int max)
        => value.Length >= min && value.Length <= max
            && value.All(character => char.IsLetterOrDigit(character) || character is '-' or '_');

    private static bool IsLocalAbsolutePath(string path)
        => !string.IsNullOrWhiteSpace(path) && Path.IsPathFullyQualified(path)
            && !path.StartsWith(@"\\", StringComparison.Ordinal) && !path.StartsWith("//", StringComparison.Ordinal)
            && Path.GetFullPath(path) != Path.GetPathRoot(Path.GetFullPath(path));

    private static string NormalizeDirectoryPath(string path)
        => Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private static bool IsSameOrChildDirectory(string candidate, string parent)
        => candidate.Equals(parent, StringComparison.OrdinalIgnoreCase)
            || candidate.StartsWith(parent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    private static string? TryResolveDirectory(string path)
    {
        try
        {
            if (!Directory.Exists(path)) return null;
            var target = new DirectoryInfo(path).ResolveLinkTarget(returnFinalTarget: true);
            return target is null ? null : NormalizeDirectoryPath(target.FullName);
        }
        catch { return null; }
    }

    private static Uri ValidateLoopbackOrigin(Uri origin)
    {
        if (origin.Scheme != Uri.UriSchemeHttp || origin.Host != "127.0.0.1"
            || origin.Port is < 1 or > 65535 || origin.AbsolutePath != "/"
            || origin.Query.Length != 0 || origin.Fragment.Length != 0 || origin.UserInfo.Length != 0)
            throw new InvalidOperationException("市场调试只允许 http://127.0.0.1:端口/。");
        return origin;
    }
}

internal interface IPetMarketIdentity
{
    bool IsActivated { get; }
    string AccountId { get; }
    string LicenseId { get; }
    string InstallationId { get; }
    void Authorize(HttpRequestMessage request);
    bool AcceptSignedAccountId(string accountId);
}

internal sealed class LicensePetMarketIdentity(LicenseService licenses) : IPetMarketIdentity
{
    public bool IsActivated => licenses.IsActivated;
    public string AccountId => licenses.AccountId;
    public string LicenseId => licenses.LicenseId;
    public string InstallationId => licenses.InstallationId;
    public void Authorize(HttpRequestMessage request) => licenses.Authorize(request);
    public bool AcceptSignedAccountId(string accountId) => licenses.AcceptSignedAccountId(accountId);
}

internal sealed class DebugPetMarketIdentity(PetMarketDebugConfig config) : IPetMarketIdentity
{
    public bool IsActivated => true;
    public string AccountId => config.AccountId;
    public string LicenseId => config.LicenseId;
    public string InstallationId => config.InstallationId;
    public void Authorize(HttpRequestMessage request)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", $"{LicenseId}.{config.Credential}");
        request.Headers.TryAddWithoutValidation("X-DeskPet-Version", UpdateService.CurrentVersion);
        request.Headers.UserAgent.ParseAdd($"ZhuoDazi/{UpdateService.CurrentVersion}");
    }
    public bool AcceptSignedAccountId(string accountId)
        => AccountId.Equals(accountId, StringComparison.OrdinalIgnoreCase);
}

internal sealed class PetMarketClient : IDisposable
{
    private const int MaxJsonBytes = 1024 * 1024;
    private const int MaxOrderHistoryEntries = 10_000;
    private const int MaxOrderHistoryPageEntries = 100;
    private const int MaxOrderCursorLength = 512;
    private const long MaxPreviewBytes = 8 * 1024 * 1024;
    private const long MaxPackageBytes = 128 * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IPetMarketIdentity _identity;
    private readonly HttpClient _http;
    private readonly Uri _origin;
    private readonly string _dataDirectory;
    private readonly byte[]? _debugSigningPublicKey;

    internal bool IsActivated => _identity.IsActivated;

    internal PetMarketClient(LicenseService licenses, string dataDirectory)
        : this(new LicensePetMarketIdentity(licenses), dataDirectory, new Uri(DeskPetApi.BaseUrl + "/"), null)
    {
    }

    internal PetMarketClient(PetMarketDebugConfig config)
        : this(new DebugPetMarketIdentity(config), config.DataDirectory, config.Origin, config.SigningPublicKey)
    {
    }

    private PetMarketClient(IPetMarketIdentity identity, string dataDirectory, Uri origin, byte[]? debugSigningPublicKey)
    {
        _identity = identity;
        _dataDirectory = Path.GetFullPath(dataDirectory);
        _origin = origin;
        _debugSigningPublicKey = debugSigningPublicKey;
        var debug = debugSigningPublicKey is not null;
        var handler = DeskPetHttp.HandlerFactory?.Invoke() ?? CreateHandler(_origin, debug);
        _http = new HttpClient(handler)
        {
            BaseAddress = _origin,
            Timeout = TimeSpan.FromSeconds(25),
            DefaultRequestVersion = HttpVersion.Version20,
            DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower
        };
    }

    internal async Task<PetMarketCatalog> GetCatalogAsync(CancellationToken cancellationToken = default)
    {
        var catalog = await SendJsonAsync<PetMarketCatalog>(HttpMethod.Get, "/api/pet-market/catalog", null,
            authenticated: _identity.IsActivated, cancellationToken);
        catalog.Payment = NormalizePayment(catalog.Payment);
        catalog.Products ??= [];
        foreach (var product in catalog.Products) ValidateProduct(product);
        catalog.Products = catalog.Products.OrderBy(item => item.SortOrder).ThenBy(item => item.Title).ToList();
        return catalog;
    }

    internal Task<PetMarketClaimResponse> ClaimAsync(string productId, CancellationToken cancellationToken = default)
        => SendJsonAsync<PetMarketClaimResponse>(HttpMethod.Post,
            $"/api/pet-market/products/{EscapeSegment(productId)}/claim", new { }, true, cancellationToken);

    internal async Task<PetMarketOrderResponse> CreateOrderAsync(
        string productId, CancellationToken cancellationToken = default)
    {
        var response = await SendJsonAsync<PetMarketOrderResponse>(HttpMethod.Post, "/api/pet-market/orders",
            new { productId }, true, cancellationToken);
        NormalizeOrderPayment(response);
        return response;
    }

    internal async Task<PetMarketOrderListResponse> ListOrdersAsync(CancellationToken cancellationToken = default)
    {
        var orders = new List<PetMarketOrder>();
        var orderIds = new HashSet<string>(StringComparer.Ordinal);
        var cursors = new HashSet<string>(StringComparer.Ordinal);
        var serverTime = default(DateTimeOffset);
        var path = "/api/pet-market/orders";
        while (true)
        {
            var page = await SendJsonAsync<PetMarketOrderListResponse>(
                HttpMethod.Get, path, null, true, cancellationToken);
            page.Orders ??= [];
            if (page.Orders.Count > MaxOrderHistoryPageEntries)
                throw new InvalidOperationException("订单记录分页数量异常。");
            if (serverTime == default) serverTime = page.ServerTime;
            foreach (var order in page.Orders)
            {
                if (order is null) throw new InvalidOperationException("订单记录无效。");
                NormalizeOrderPayment(order);
                if (string.IsNullOrWhiteSpace(order.Id) || order.Id.Length > 128
                    || string.IsNullOrWhiteSpace(order.ProductId) || order.ProductId.Length > 128
                    || order.Status is not ("pending_payment" or "paid" or "expired" or "canceled")
                    || order.AmountFen < 0 || order.Currency != "CNY"
                    || order.CreatedAt == default || order.ExpiresAt == default
                    || order.Status == "paid" && string.IsNullOrWhiteSpace(order.ProductVersion)
                    || !orderIds.Add(order.Id))
                    throw new InvalidOperationException("订单记录无效。");
                orders.Add(order);
                if (orders.Count > MaxOrderHistoryEntries)
                    throw new InvalidOperationException("订单记录超过本机可处理上限。");
            }

            var cursor = page.NextCursor?.Trim();
            if (string.IsNullOrEmpty(cursor)) break;
            if (orders.Count >= MaxOrderHistoryEntries)
                throw new InvalidOperationException("订单记录超过本机可处理上限。");
            if (!ValidOrderCursor(cursor) || !cursors.Add(cursor))
                throw new InvalidOperationException("订单记录分页游标无效。");
            path = $"/api/pet-market/orders?cursor={Uri.EscapeDataString(cursor)}";
        }
        return new PetMarketOrderListResponse
        {
            ServerTime = serverTime,
            Orders = orders
                .OrderByDescending(order => order.CreatedAt)
                .ThenByDescending(order => order.Id, StringComparer.Ordinal)
                .ToList()
        };
    }

    internal async Task<PetMarketOrder> GetOrderAsync(string orderId, CancellationToken cancellationToken = default)
    {
        var response = await SendJsonAsync<PetMarketOrderResponse>(HttpMethod.Get,
            $"/api/pet-market/orders/{EscapeSegment(orderId)}", null, true, cancellationToken);
        NormalizeOrderPayment(response);
        return response.Order;
    }

    internal async Task<PetMarketOrder> CancelOrderAsync(string orderId, CancellationToken cancellationToken = default)
    {
        var response = await SendJsonAsync<PetMarketOrderResponse>(HttpMethod.Post,
            $"/api/pet-market/orders/{EscapeSegment(orderId)}/cancel", new { }, true, cancellationToken);
        NormalizeOrderPayment(response);
        return response.Order;
    }

    internal Task<PetMarketDownloadPayload> GetDownloadDescriptorAsync(
        string productId, CancellationToken cancellationToken = default)
        => GetDownloadDescriptorAsync(productId, expectedVersion: null, cancellationToken);

    internal async Task<PetMarketDownloadPayload> GetDownloadDescriptorAsync(
        string productId, string? expectedVersion, CancellationToken cancellationToken = default)
    {
        var envelope = await SendJsonAsync<PetMarketSignedEnvelope>(HttpMethod.Get,
            $"/api/pet-market/products/{EscapeSegment(productId)}/download-descriptor", null, true, cancellationToken);
        var payload = VerifyEnvelope<PetMarketDownloadPayload>(envelope);
        ValidateSignedIdentity(payload.AccountId, payload.LicenseId, payload.InstallationId,
            payload.IssuedAt, payload.ExpiresAt);
        if (payload.SchemaVersion != 1 || payload.Kind != "pet-market-download"
            || !payload.ProductId.Equals(productId, StringComparison.Ordinal)
            || (!string.IsNullOrWhiteSpace(expectedVersion)
                && !payload.Version.Equals(expectedVersion, StringComparison.Ordinal))
            || payload.SizeBytes is <= 0 or > MaxPackageBytes
            || !ValidSha256(payload.Sha256) || !ValidSha256(payload.ManifestSha256)
            || string.IsNullOrWhiteSpace(payload.FileName)
            || !ValidDownloadPath(payload.DownloadPath, payload.ProductId, payload.Version))
            throw new InvalidOperationException("市场下载授权内容无效，请刷新后重试。");
        return payload;
    }

    internal async Task<(PetMarketStoredGrant Grant, PetMarketUseGrantPayload Payload)> AuthorizeUseAsync(
        string productId, string expectedVersion, string expectedManifestSha256,
        CancellationToken cancellationToken = default)
    {
        var envelope = await SendJsonAsync<PetMarketSignedEnvelope>(HttpMethod.Post,
            $"/api/pet-market/products/{EscapeSegment(productId)}/authorize-use", new { }, true, cancellationToken);
        var payload = VerifyEnvelope<PetMarketUseGrantPayload>(envelope);
        ValidateSignedIdentity(payload.AccountId, payload.LicenseId, payload.InstallationId,
            payload.IssuedAt, payload.ExpiresAt);
        if (payload.SchemaVersion != 1 || payload.Kind != "pet-market-use-grant"
            || !payload.ProductId.Equals(productId, StringComparison.Ordinal)
            || !payload.Version.Equals(expectedVersion, StringComparison.Ordinal)
            || !payload.ManifestSha256.Equals(expectedManifestSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("市场使用授权与本机图鉴不匹配。");
        return (new PetMarketStoredGrant
        {
            SignedPayload = envelope.SignedPayload,
            Sha256 = envelope.Sha256,
            SignatureAlgorithm = envelope.SignatureAlgorithm,
            Signature = envelope.Signature
        }, payload);
    }

    internal bool HasValidStoredUseGrant(PetMarketInstallation installation)
    {
        if (!_identity.IsActivated || installation.UseGrant is null) return false;
        try
        {
            var payload = VerifyEnvelope<PetMarketUseGrantPayload>(new PetMarketSignedEnvelope
            {
                SignedPayload = installation.UseGrant.SignedPayload,
                Sha256 = installation.UseGrant.Sha256,
                SignatureAlgorithm = installation.UseGrant.SignatureAlgorithm,
                Signature = installation.UseGrant.Signature
            });
            ValidateSignedIdentity(payload.AccountId, payload.LicenseId, payload.InstallationId,
                payload.IssuedAt, payload.ExpiresAt);
            return payload.SchemaVersion == 1 && payload.Kind == "pet-market-use-grant"
                && payload.ProductId.Equals(installation.ProductId, StringComparison.Ordinal)
                && payload.Version.Equals(installation.Version, StringComparison.Ordinal)
                && payload.ManifestSha256.Equals(installation.ManifestSha256, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    internal async Task<string> DownloadPackageAsync(PetMarketDownloadPayload descriptor,
        CancellationToken cancellationToken = default)
    {
        RequireActivated();
        if (!ValidDownloadPath(descriptor.DownloadPath, descriptor.ProductId, descriptor.Version))
            throw new InvalidOperationException("市场下载地址无效。");
        var downloadDirectory = Path.Combine(_dataDirectory, "pet-market", "downloads");
        Directory.CreateDirectory(downloadDirectory);
        var destination = Path.Combine(downloadDirectory,
            $"{SafeFilePart(descriptor.ProductId)}-{SafeFilePart(descriptor.Version)}-{Guid.NewGuid():N}.zip");
        var partial = destination + ".partial";
        try
        {
            using var request = CreateRequest(HttpMethod.Get, descriptor.DownloadPath, authenticated: true);
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            await EnsureSuccessAsync(response, cancellationToken);
            if (response.Content.Headers.ContentLength is { } contentLength && contentLength != descriptor.SizeBytes)
                throw new InvalidOperationException("市场资源包大小与签名清单不一致。");
            long total = 0;
            string digest;
            await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var target = new FileStream(
                partial, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
            {
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                var buffer = new byte[81920];
                int read;
                while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    total += read;
                    if (total > descriptor.SizeBytes || total > MaxPackageBytes)
                        throw new InvalidOperationException("市场资源包超过签名清单声明的大小。");
                    hash.AppendData(buffer, 0, read);
                    await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                }
                await target.FlushAsync(cancellationToken);
                digest = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
            }
            if (total != descriptor.SizeBytes || !CryptographicOperations.FixedTimeEquals(
                    Encoding.ASCII.GetBytes(digest), Encoding.ASCII.GetBytes(descriptor.Sha256.ToLowerInvariant())))
                throw new InvalidOperationException("市场资源包校验失败，文件未安装。");
            File.Move(partial, destination);
            return destination;
        }
        finally
        {
            if (File.Exists(partial)) File.Delete(partial);
        }
    }

    internal async Task<string?> DownloadPreviewAsync(PetMarketProduct product,
        PetMarketPreviewFile? requestedPreview = null,
        CancellationToken cancellationToken = default)
    {
        var preview = requestedPreview is not null && product.PreviewFiles.Contains(requestedPreview)
            ? requestedPreview
            : product.PreviewFiles.FirstOrDefault(item =>
            !string.IsNullOrWhiteSpace(item.DownloadPath) || !string.IsNullOrWhiteSpace(item.Url));
        if (preview is null || !TryNormalizeMarketPath(
                !string.IsNullOrWhiteSpace(preview.DownloadPath) ? preview.DownloadPath : preview.Url,
                out var path)) return null;
        var expectedSize = preview?.SizeBytes ?? 0;
        if (expectedSize is < 0 or > MaxPreviewBytes) return null;
        var expectedSha = preview?.Sha256 ?? string.Empty;
        if (expectedSha.Length != 0 && !ValidSha256(expectedSha)) return null;
        var cacheDirectory = Path.Combine(_dataDirectory, "pet-market", "preview-cache");
        Directory.CreateDirectory(cacheDirectory);
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(path))).ToLowerInvariant();
        var destination = Path.Combine(cacheDirectory, key + ".gif");
        if (File.Exists(destination) && (expectedSize == 0 || new FileInfo(destination).Length == expectedSize))
        {
            if (expectedSha.Length == 0 || HashFile(destination).Equals(expectedSha, StringComparison.OrdinalIgnoreCase))
                return destination;
            File.Delete(destination);
        }
        var partial = destination + $".{Environment.ProcessId}.partial";
        try
        {
            using var request = CreateRequest(HttpMethod.Get, path, authenticated: false);
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            await EnsureSuccessAsync(response, cancellationToken);
            if (response.Content.Headers.ContentLength is > MaxPreviewBytes) return null;
            long total = 0;
            string digest;
            await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var target = new FileStream(
                partial, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
            {
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                var buffer = new byte[81920];
                int read;
                while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    total += read;
                    if (total > MaxPreviewBytes || (expectedSize > 0 && total > expectedSize)) return null;
                    hash.AppendData(buffer, 0, read);
                    await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                }
                await target.FlushAsync(cancellationToken);
                digest = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
            }
            if ((expectedSize > 0 && total != expectedSize)
                || (expectedSha.Length > 0 && !digest.Equals(expectedSha, StringComparison.OrdinalIgnoreCase))) return null;
            File.Move(partial, destination, true);
            return destination;
        }
        finally
        {
            if (File.Exists(partial)) File.Delete(partial);
        }
    }

    private async Task<T> SendJsonAsync<T>(HttpMethod method, string path, object? body, bool authenticated,
        CancellationToken cancellationToken)
    {
        using var request = CreateRequest(method, path, authenticated);
        if (body is not null)
            request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        if (response.Content.Headers.ContentLength is > MaxJsonBytes)
            throw new InvalidOperationException("市场服务响应过大。");
        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        if (bytes.Length is 0 or > MaxJsonBytes) throw new InvalidOperationException("市场服务响应无效。");
        try
        {
            return JsonSerializer.Deserialize<T>(bytes, JsonOptions)
                ?? throw new InvalidOperationException("市场服务响应格式无效。");
        }
        catch (JsonException error)
        {
            throw new InvalidOperationException("市场服务响应格式无效。", error);
        }
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string path, bool authenticated)
    {
        if (!ValidMarketRelativePath(path)) throw new InvalidOperationException("市场请求地址无效。");
        var resolved = new Uri(_origin, path);
        if (!SameOrigin(resolved, _origin)) throw new InvalidOperationException("市场请求已被安全策略阻止。");
        var request = new HttpRequestMessage(method, resolved);
        request.Headers.TryAddWithoutValidation("X-DeskPet-Platform", "windows");
        request.Headers.TryAddWithoutValidation("X-DeskPet-Architecture", "x64");
        if (authenticated)
        {
            RequireActivated();
            _identity.Authorize(request);
        }
        else
        {
            request.Headers.TryAddWithoutValidation("X-DeskPet-Version", UpdateService.CurrentVersion);
            request.Headers.UserAgent.ParseAdd($"ZhuoDazi/{UpdateService.CurrentVersion}");
        }
        return request;
    }

    private T VerifyEnvelope<T>(PetMarketSignedEnvelope envelope)
    {
        if (!envelope.SignatureAlgorithm.Equals("ed25519", StringComparison.OrdinalIgnoreCase)
            || !ValidSha256(envelope.Sha256))
            throw new InvalidOperationException("市场签名信封格式无效。");
        byte[] payload;
        byte[] signature;
        try
        {
            payload = Convert.FromBase64String(envelope.SignedPayload);
            signature = Convert.FromBase64String(envelope.Signature);
        }
        catch (FormatException error)
        {
            throw new InvalidOperationException("市场签名信封编码无效。", error);
        }
        if (payload.Length is 0 or > 64 * 1024 || signature.Length != 64)
            throw new InvalidOperationException("市场签名信封大小无效。");
        var actual = Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();
        if (!CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(actual),
                Encoding.ASCII.GetBytes(envelope.Sha256.ToLowerInvariant()))
            || !(_debugSigningPublicKey is null
                ? ServerSignatureTrust.Verify(payload, signature)
                : Ed25519SignatureVerifier.Verify(_debugSigningPublicKey, payload, signature)))
            throw new InvalidOperationException("市场签名验证失败。");
        try
        {
            return JsonSerializer.Deserialize<T>(payload, JsonOptions)
                ?? throw new InvalidOperationException("市场签名内容无效。");
        }
        catch (JsonException error)
        {
            throw new InvalidOperationException("市场签名内容无效。", error);
        }
    }

    private void ValidateSignedIdentity(string accountId, string licenseId, string installationId,
        DateTimeOffset issuedAt, DateTimeOffset expiresAt)
    {
        RequireActivated();
        if (!licenseId.Equals(_identity.LicenseId, StringComparison.OrdinalIgnoreCase)
            || !installationId.Equals(_identity.InstallationId, StringComparison.Ordinal)
            || !_identity.AcceptSignedAccountId(accountId)
            || issuedAt > DateTimeOffset.UtcNow.AddMinutes(5)
            || expiresAt <= DateTimeOffset.UtcNow || expiresAt <= issuedAt)
            throw new InvalidOperationException("市场授权不属于当前设备或已经过期。");
    }

    private void RequireActivated()
    {
        if (!_identity.IsActivated)
            throw new InvalidOperationException("正式激活后才可以领取、购买、下载和使用市场桌宠。");
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode) return;
        var message = string.Empty;
        var code = string.Empty;
        try
        {
            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            if (bytes.Length <= 64 * 1024)
            {
                using var document = JsonDocument.Parse(bytes);
                var root = document.RootElement;
                code = root.TryGetProperty("code", out var codeNode) ? codeNode.GetString() ?? string.Empty : string.Empty;
                message = root.TryGetProperty("message", out var messageNode) ? messageNode.GetString() ?? string.Empty
                    : root.TryGetProperty("error", out var errorNode) ? errorNode.GetString() ?? string.Empty : string.Empty;
            }
        }
        catch { }
        if (message.Length == 0)
        {
            message = code switch
            {
                "ACTIVATION_REQUIRED" => "正式激活后才可以领取、购买、下载和使用市场桌宠。",
                "ENTITLEMENT_REQUIRED" => "这个桌宠尚未领取或购买。",
                "PAYMENT_UNAVAILABLE" => "当前付款通道暂不可用，请稍后再试。",
                "ORDER_NOT_PAYABLE" => "订单已经不能继续付款，请刷新后重新下单。",
                _ => response.StatusCode switch
                {
                    HttpStatusCode.Unauthorized => "设备授权无效，请重新激活后再试。",
                    HttpStatusCode.Forbidden => "当前设备没有执行此操作的权限。",
                    HttpStatusCode.NotFound => "市场商品、版本或订单不存在。",
                    HttpStatusCode.Conflict => "当前商品或订单状态不允许此操作。",
                    HttpStatusCode.ServiceUnavailable => "市场服务暂不可用，请稍后再试。",
                    _ => $"市场请求失败（HTTP {(int)response.StatusCode}）。"
                }
            };
        }
        throw new PetMarketRequestException(response.StatusCode, code, message);
    }

    private static void ValidateProduct(PetMarketProduct product)
    {
        product.PreviewPaths ??= [];
        product.PreviewFiles ??= [];
        if (string.IsNullOrWhiteSpace(product.Id) || string.IsNullOrWhiteSpace(product.Title)
            || product.ChargingMode is not ("free" or "paid")
            || product.Currency != "CNY" || product.PriceFen < 0 || product.AssetCount < 0
            || string.IsNullOrWhiteSpace(product.ActiveVersion))
            throw new InvalidOperationException("市场商品数据无效。");
    }

    private static void NormalizeOrderPayment(PetMarketOrderResponse response)
    {
        if (response.Order is null) throw new InvalidOperationException("市场订单响应无效。");
        NormalizeOrderPayment(response.Order);
    }

    private static void NormalizeOrderPayment(PetMarketOrder order)
    {
        order.ProductTitle = NormalizeDisplayText(order.ProductTitle, 120);
        order.Payment = NormalizePayment(order.Payment);
    }

    private static string NormalizeDisplayText(string? value, int maxLength)
    {
        var normalized = (value ?? string.Empty).Trim();
        return normalized.Length <= maxLength ? normalized : normalized[..maxLength];
    }

    private static PetMarketPaymentInfo? NormalizePayment(PetMarketPaymentInfo? payment)
    {
        if (payment is null) return null;
        var recipientNameValid = TryNormalizePaymentText(payment.RecipientName, 80, out var recipientName);
        var recipientAccountValid = TryNormalizePaymentText(payment.RecipientAccount, 128, out var recipientAccount);
        var instructionsValid = TryNormalizePaymentText(payment.Instructions, 800, out var instructions);
        var unavailableReasonValid = TryNormalizePaymentText(payment.UnavailableReason, 300, out var unavailableReason);
        var validText = recipientNameValid && recipientAccountValid && instructionsValid && unavailableReasonValid;
        payment.Method = (payment.Method ?? string.Empty).Trim().ToLowerInvariant();
        payment.RecipientName = recipientName;
        payment.RecipientAccount = recipientAccount;
        payment.Instructions = instructions;
        payment.UnavailableReason = unavailableReason;
        var methodValid = payment.Method is "personal_transfer" or "collection_qr";
        var destinationValid = payment.IsPersonalTransfer ? payment.HasRecipient
            : payment.IsCollectionQr && payment.TryGetQrCodeBytes(out _);
        if (validText && methodValid && (!payment.Enabled || !payment.Available || destinationValid)) return payment;
        payment.Available = false;
        if (string.IsNullOrWhiteSpace(payment.UnavailableReason))
            payment.UnavailableReason = "付款信息不完整，请稍后刷新。";
        return payment;
    }

    private static bool TryNormalizePaymentText(string? value, int maxLength, out string normalized)
    {
        normalized = (value ?? string.Empty).Trim();
        if (normalized.Length <= maxLength) return true;
        normalized = normalized[..maxLength];
        return false;
    }

    private static bool ValidMarketRelativePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !path.StartsWith("/api/pet-market/", StringComparison.Ordinal)
            || path.Contains('\\') || path.Contains("..", StringComparison.Ordinal)) return false;
        if (!Uri.TryCreate(path, UriKind.Relative, out _) || path.Contains('#', StringComparison.Ordinal)) return false;
        var queryIndex = path.IndexOf('?');
        if (queryIndex < 0) return true;
        var endpoint = path[..queryIndex];
        var query = path[(queryIndex + 1)..];
        if (endpoint.Equals("/api/pet-market/orders", StringComparison.Ordinal))
            return query.StartsWith("cursor=", StringComparison.Ordinal)
                && ValidOrderCursor(query["cursor=".Length..]);
        return endpoint.EndsWith("/preview", StringComparison.Ordinal)
            && query.StartsWith("file=", StringComparison.Ordinal)
            && query.Length > "file=".Length
            && !query.Contains('&', StringComparison.Ordinal);
    }

    private bool TryNormalizeMarketPath(string candidate, out string path)
    {
        path = string.Empty;
        if (string.IsNullOrWhiteSpace(candidate)) return false;
        if (candidate.StartsWith("/", StringComparison.Ordinal)) path = candidate;
        else if (Uri.TryCreate(candidate, UriKind.Absolute, out var absolute) && SameOrigin(absolute, _origin))
            path = absolute.PathAndQuery;
        return ValidMarketRelativePath(path);
    }

    private static bool ValidDownloadPath(string path, string productId, string version)
        => path.Equals($"/api/pet-market/products/{EscapeSegment(productId)}/versions/{EscapeSegment(version)}/download",
            StringComparison.Ordinal);

    private static bool ValidSha256(string value) => value.Length == 64 && value.All(Uri.IsHexDigit);

    private static bool ValidOrderCursor(string value)
        => value.Length is > 0 and <= MaxOrderCursorLength
            && value.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_');

    private static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static string EscapeSegment(string value)
    {
        var clean = (value ?? string.Empty).Trim();
        if (clean.Length is < 1 or > 128 || clean.Contains('/') || clean.Contains('\\'))
            throw new InvalidOperationException("市场标识无效。");
        return Uri.EscapeDataString(clean);
    }

    private static string SafeFilePart(string value)
        => new(value.Where(character => char.IsLetterOrDigit(character) || character is '-' or '_' or '.').Take(80).ToArray());

    private static bool SameOrigin(Uri left, Uri right)
        => left.Scheme.Equals(right.Scheme, StringComparison.OrdinalIgnoreCase)
            && left.Host.Equals(right.Host, StringComparison.OrdinalIgnoreCase)
            && left.Port == right.Port;

    private static HttpMessageHandler CreateHandler(Uri endpoint, bool debug)
    {
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseProxy = false,
            UseCookies = false,
            ConnectTimeout = TimeSpan.FromSeconds(15),
            PooledConnectionLifetime = TimeSpan.FromMinutes(5)
        };
        if (debug)
        {
            handler.ConnectCallback = async (context, cancellationToken) =>
            {
                if (context.DnsEndPoint.Host != "127.0.0.1" || context.DnsEndPoint.Port != endpoint.Port)
                    throw new InvalidOperationException("已阻止市场调试连接到非本机地址。");
                var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                try
                {
                    await socket.ConnectAsync(IPAddress.Loopback, endpoint.Port, cancellationToken);
                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch
                {
                    socket.Dispose();
                    throw;
                }
            };
        }
        return handler;
    }

    public void Dispose() => _http.Dispose();
}

internal sealed class PetMarketRequestException(HttpStatusCode statusCode, string code, string message)
    : InvalidOperationException(message)
{
    internal HttpStatusCode StatusCode { get; } = statusCode;
    internal string Code { get; } = code;
}
