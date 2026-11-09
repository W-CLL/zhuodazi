using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace ZhuoDazi.Services;

/// <summary>Explicit local test identities; never opens the installed app's license or settings store.</summary>
internal sealed class AlipayPreviewIdentity
{
    public string InstallationId { get; set; } = string.Empty;
    public string Credential { get; set; } = string.Empty;
    public string? LicenseId { get; set; }

    internal void Validate(bool activated)
    {
        if (!Regex.IsMatch(InstallationId, "^[A-Za-z0-9_-]{20,80}$") ||
            !Regex.IsMatch(Credential, "^[A-Za-z0-9_-]{40,80}$") ||
            (activated ? !Guid.TryParse(LicenseId, out _) : !string.IsNullOrEmpty(LicenseId)))
            throw new InvalidOperationException("本地测试身份格式无效；激活页使用未激活身份，小铺使用独立的已激活身份。");
    }
    internal AuthenticationHeaderValue Authorization => LicenseId is null
        ? new("Trial", $"{InstallationId}.{Credential}") : new("Bearer", $"{LicenseId}.{Credential}");
}

internal sealed class AlipayPreviewConfig
{
    public string BaseUrl { get; set; } = "http://127.0.0.1:18788/";
    public string DataDirectory { get; set; } = string.Empty;
    public AlipayPreviewIdentity ActivationIdentity { get; set; } = new();
    public AlipayPreviewIdentity ShopIdentity { get; set; } = new();
    internal Uri Origin => new(BaseUrl);
    internal string StorageDirectory => Path.Combine(DataDirectory, "alipay-preview");

    internal static AlipayPreviewConfig Load(string path)
    {
        if (!IsLocalAbsolute(path) || !File.Exists(path) || new FileInfo(path).Length > 256 * 1024)
            throw new InvalidOperationException("支付宝预览配置必须是本机绝对路径的小型 JSON 文件。");
        AlipayPreviewConfig configuration;
        try
        {
            configuration = JsonSerializer.Deserialize<AlipayPreviewConfig>(File.ReadAllText(path), JsonOptions)
                ?? throw new JsonException();
        }
        catch (JsonException) { throw new InvalidOperationException("支付宝预览配置格式无效。"); }
        configuration.Validate();
        Directory.CreateDirectory(configuration.StorageDirectory);
        return configuration;
    }

    internal void Validate()
    {
        if (!Uri.TryCreate(BaseUrl, UriKind.Absolute, out var origin) || origin.Scheme != "http" ||
            origin.Host != "127.0.0.1" || origin.Port != 18788 || origin.AbsolutePath != "/" ||
            origin.Query.Length != 0 || origin.Fragment.Length != 0 || origin.UserInfo.Length != 0)
            throw new InvalidOperationException("支付宝本地预览只连接 http://127.0.0.1:18788/。");
        if (!IsLocalAbsolute(DataDirectory)) throw new InvalidOperationException("预览数据必须使用独立的本机绝对目录。");
        DataDirectory = Path.GetFullPath(DataDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var production = Path.GetFullPath(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "poko-desktop-pet"));
        if (DataDirectory.Length == 0 || DataDirectory.Equals(Path.GetPathRoot(DataDirectory)?.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)
            || DataDirectory.Equals(production, StringComparison.OrdinalIgnoreCase)
            || DataDirectory.StartsWith(production + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("支付宝预览不能使用磁盘根目录或正式用户数据目录。");
        for (var directory = new DirectoryInfo(StorageDirectory); directory is not null; directory = directory.Parent)
            if (directory.Exists && (directory.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("支付宝预览目录不能经过符号链接或目录联接。");
        if (ActivationIdentity is null || ShopIdentity is null) throw new InvalidOperationException("缺少两组独立的本地测试身份。");
        ActivationIdentity.Validate(false); ShopIdentity.Validate(true);
        if (ActivationIdentity.InstallationId == ShopIdentity.InstallationId || ActivationIdentity.Credential == ShopIdentity.Credential)
            throw new InvalidOperationException("激活页和小铺必须使用两组独立的本地测试身份。");
        BaseUrl = origin.AbsoluteUri;
    }
    private static bool IsLocalAbsolute(string path) => !string.IsNullOrWhiteSpace(path) && Path.IsPathFullyQualified(path)
        && !path.StartsWith(@"\\", StringComparison.Ordinal) && !path.StartsWith("//", StringComparison.Ordinal);
    internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}

internal sealed record AlipayPreviewActivation(string Title, string Description, int AmountFen);
internal sealed record AlipayPreviewProduct(string Id, string Title, string Description, int PriceFen)
{
    public string PriceLabel => $"¥{PriceFen / 100m:F2}";
}
internal sealed record AlipayPreviewProvider(bool Configured, string? Reason);
internal sealed record AlipayPreviewCatalog(AlipayPreviewActivation Activation, AlipayPreviewProduct[] Products,
    AlipayPreviewProvider Provider, bool? Activated);
internal sealed record AlipayPreviewOrder
{
    public string Id { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Kind { get; init; } = string.Empty;
    public int AmountFen { get; init; }
    public string Status { get; init; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; init; }
    public string? FulfillmentStatus { get; init; }
    public string? DeliveryStatus { get; init; }
    public string? DeliveryMode { get; init; }
    public string? ActivationCode { get; init; }
    public string? ProductId { get; init; }
    public string? ProductVersion { get; init; }
    public DateTimeOffset CreatedAt { get; init; }

    internal bool IsActivationDelivered => Kind == "activation" && Status == "paid" && FulfillmentStatus == "fulfilled";
    internal string? ActivationDeliveryMessage => !IsActivationDelivered ? null : DeliveryMode switch
    {
        "device_activated" => "此本地测试身份已在服务端激活。激活码支持同账号两台设备使用。",
        "code_only" => "本次购买已交付激活码，可复制保存后用于其他设备。",
        _ => "本次购买的激活码已交付，可复制保存。"
    };
}
internal sealed record AlipayPreviewOrderResponse(AlipayPreviewOrder Order, string? CheckoutUrl, string? ActivationCode);
internal sealed record AlipayPreviewOrderRequest(string Kind, string? ProductId = null);

/// <summary>Official checkout API, with a separately selected loopback transport for local checks.</summary>
internal sealed class AlipayPreviewClient : IDisposable
{
    private readonly HttpClient _http;
    internal AlipayPreviewClient(AlipayPreviewConfig configuration, HttpMessageHandler? testTransport = null)
        : this(ValidatedOrigin(configuration), testTransport)
    {
    }

    private static Uri ValidatedOrigin(AlipayPreviewConfig configuration)
    {
        configuration.Validate();
        return configuration.Origin;
    }

    internal AlipayPreviewClient(Uri origin, HttpMessageHandler? testTransport = null)
    {
        if (origin.AbsoluteUri != "http://127.0.0.1:18788/" && origin.AbsoluteUri != "https://in.desktoppet.online/")
            throw new InvalidOperationException("支付服务地址无效。");
        var transport = testTransport ?? new SocketsHttpHandler
        {
            AllowAutoRedirect = false, UseProxy = false, UseCookies = false, ConnectTimeout = TimeSpan.FromSeconds(5),
            ConnectCallback = origin.Scheme == "https" ? null : async (context, cancellationToken) =>
            {
                if (context.DnsEndPoint.Host != "127.0.0.1" || context.DnsEndPoint.Port != 18788)
                    throw new HttpRequestException("已阻止非本机支付宝预览连接。");
                var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                try
                {
                    await socket.ConnectAsync(IPAddress.Loopback, 18788, cancellationToken);
                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch { socket.Dispose(); throw; }
            }
        };
        _http = new HttpClient(new PaymentOriginGuard(origin, transport)) { BaseAddress = origin, Timeout = TimeSpan.FromSeconds(12) };
    }
    internal Task<AlipayPreviewCatalog> CatalogAsync(AlipayPreviewIdentity identity, CancellationToken cancellationToken)
        => SendAsync<AlipayPreviewCatalog>(HttpMethod.Get, "api/payments/alipay/catalog", identity, null, cancellationToken);
    internal async Task<AlipayPreviewOrderResponse> CreateOrderAsync(AlipayPreviewIdentity identity, bool activation,
        string? productId, CancellationToken cancellationToken)
    {
        if (!activation && (identity.LicenseId is null || productId is null || productId is "." or ".."
            || !Regex.IsMatch(productId, "^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$")))
            throw new InvalidOperationException("请选择已激活测试身份和有效的桌宠商品。");
        var response = await SendAsync<AlipayPreviewOrderResponse>(HttpMethod.Post, "api/payments/alipay/orders", identity,
            new AlipayPreviewOrderRequest(activation ? "activation" : "pet", activation ? null : productId), cancellationToken);
        if (!Guid.TryParse(response.Order.Id, out _) || response.CheckoutUrl is null)
            throw new InvalidOperationException("订单创建失败，请稍后重试。");
        ValidateCheckoutUrl(response.CheckoutUrl, response.Order.Id);
        return response;
    }
    internal Task<AlipayPreviewOrderResponse> OrderAsync(AlipayPreviewIdentity identity, string orderId, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(orderId, out _)) throw new InvalidOperationException("订单编号无效。");
        return SendAsync<AlipayPreviewOrderResponse>(HttpMethod.Get, $"api/payments/alipay/orders/{orderId}", identity, null, cancellationToken);
    }
    internal Task<AlipayPreviewOrderResponse> RefreshOrderAsync(AlipayPreviewIdentity identity, string orderId, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(orderId, out _)) throw new InvalidOperationException("订单编号无效。");
        return SendAsync<AlipayPreviewOrderResponse>(HttpMethod.Post, $"api/payments/alipay/orders/{orderId}/refresh", identity, null, cancellationToken);
    }
    internal static Uri ValidateCheckoutUrl(string value, string? orderId = null)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.Host != "in.desktoppet.online"
            || !uri.IsDefaultPort || uri.UserInfo.Length != 0 || uri.Query.Length != 0
            || !Regex.IsMatch(uri.AbsolutePath, "^/checkout/alipay/[0-9a-fA-F-]{36}$")
            || !Guid.TryParse(uri.Segments.Last(), out var id)
            || (orderId is not null && (!Guid.TryParse(orderId, out var expected) || id != expected))
            || !Regex.IsMatch(uri.Fragment, "^#token=[A-Za-z0-9_-]{20,256}$"))
            throw new InvalidOperationException("已阻止不受信任的支付宝收银台地址。");
        return uri;
    }
    private async Task<T> SendAsync<T>(HttpMethod method, string path, AlipayPreviewIdentity identity, object? body, CancellationToken token)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = identity.Authorization;
        request.Headers.Add("X-DeskPet-Installation-Id", identity.InstallationId);
        request.Headers.Add("X-DeskPet-Platform", "windows");
        if (body is not null) request.Content = JsonContent.Create(body, options: AlipayPreviewConfig.JsonOptions);
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
        if ((int)response.StatusCode is >= 300 and < 400) throw new InvalidOperationException("支付服务暂不可用，请稍后重试。");
        if (response.Content.Headers.ContentLength is > 2 * 1024 * 1024) throw new InvalidOperationException("订单加载失败，请稍后重试。");
        using var payload = new MemoryStream();
        await using var stream = await response.Content.ReadAsStreamAsync(token);
        var buffer = new byte[8192];
        int count;
        while ((count = await stream.ReadAsync(buffer, token)) > 0)
        {
            if (payload.Length + count > 2 * 1024 * 1024) throw new InvalidOperationException("订单加载失败，请稍后重试。");
            payload.Write(buffer, 0, count);
        }
        if (!response.IsSuccessStatusCode)
        {
            string? message = null;
            try
            {
                using var error = JsonDocument.Parse(payload.ToArray());
                if (error.RootElement.TryGetProperty("error", out var value) || error.RootElement.TryGetProperty("message", out value))
                    message = value.ValueKind == JsonValueKind.String ? value.GetString() : null;
            }
            catch { }
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(message) ? "支付服务暂不可用，请稍后重试。" : message[..Math.Min(message.Length, 300)]);
        }
        try { return JsonSerializer.Deserialize<T>(payload.ToArray(), AlipayPreviewConfig.JsonOptions) ?? throw new JsonException(); }
        catch (JsonException) { throw new InvalidOperationException("订单加载失败，请稍后重试。"); }
    }
    private sealed class PaymentOriginGuard(Uri origin, HttpMessageHandler transport) : DelegatingHandler(transport)
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var uri = request.RequestUri;
            if (uri is null || uri.Scheme != origin.Scheme || uri.Host != origin.Host || uri.Port != origin.Port || uri.UserInfo.Length != 0
                || uri.Fragment.Length != 0 || !uri.AbsolutePath.StartsWith("/api/payments/alipay/", StringComparison.Ordinal)
                || (request.Headers.Host is { } host && host != origin.Authority))
                throw new HttpRequestException("支付宝预览 API 仅允许配置的本机地址。");
            return base.SendAsync(request, token);
        }
    }
    public void Dispose() => _http.Dispose();
}
