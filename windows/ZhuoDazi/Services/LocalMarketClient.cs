using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace ZhuoDazi.Services;

/// <summary>A separate, loopback-only client for the local payment experiment.</summary>
internal sealed class LocalMarketConfig
{
    public string BaseUrl { get; set; } = string.Empty;
    public string ClientToken { get; set; } = string.Empty;
    public string? AdminToken { get; set; }
    public string DataDirectory { get; set; } = string.Empty;
    public string? QrImagePath { get; set; }

    internal static LocalMarketConfig Load(string configPath)
    {
        if (!IsLocalAbsolutePath(configPath))
            throw new InvalidOperationException("本地市场配置必须使用本机绝对路径。");
        if (!File.Exists(configPath) || new FileInfo(configPath).Length > 256 * 1024)
            throw new InvalidOperationException("找不到有效的本地市场配置文件。");
        LocalMarketConfig config;
        try
        {
            config = JsonSerializer.Deserialize<LocalMarketConfig>(File.ReadAllText(configPath),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? throw new JsonException();
        }
        catch (JsonException)
        {
            throw new InvalidOperationException("本地市场配置格式无效。");
        }
        if (!Uri.TryCreate(config.BaseUrl, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttp || uri.Host != "127.0.0.1" ||
            uri.Port is < 1 or > 65535 || uri.AbsolutePath != "/" ||
            uri.Query.Length != 0 || uri.Fragment.Length != 0 || uri.UserInfo.Length != 0)
            throw new InvalidOperationException("本地市场只允许连接 http://127.0.0.1:端口。");
        if (!ValidToken(config.ClientToken) ||
            (!string.IsNullOrEmpty(config.AdminToken) && !ValidToken(config.AdminToken)))
            throw new InvalidOperationException("本地市场访问令牌缺失或格式无效。");
        if (!IsLocalAbsolutePath(config.DataDirectory) ||
            (!string.IsNullOrWhiteSpace(config.QrImagePath) && !IsLocalAbsolutePath(config.QrImagePath)))
            throw new InvalidOperationException("本地测试数据和收款码必须使用本机绝对路径。");
        config.BaseUrl = uri.AbsoluteUri;
        config.DataDirectory = Path.GetFullPath(config.DataDirectory);
        return config;
    }

    private static bool ValidToken(string value) =>
        value.Length is >= 16 and <= 2048 && !value.Any(char.IsWhiteSpace) && !value.Any(char.IsControl);

    private static bool IsLocalAbsolutePath(string path) =>
        !string.IsNullOrWhiteSpace(path) && Path.IsPathFullyQualified(path) &&
        !path.StartsWith(@"\\", StringComparison.Ordinal) && !path.StartsWith("//", StringComparison.Ordinal);
}

internal sealed record LocalMarketOrder(string Id, string ProductId, string Reference, int AmountFen,
    string Currency, string Status, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt);

internal sealed record LocalMarketCatalog(string ProductTitle, LocalMarketOrder? CurrentOrder);

internal sealed record LocalMarketPaymentEvent(string Id, string Reference, int? AmountFen, string Category,
    string Status, string ReviewStatus, string Detail, DateTimeOffset? ObservedAt, string TransactionIdSuffix)
{
    internal bool IsPersonalTransfer => Category == "personal_transfer";
    internal bool CanConfirm => ReviewStatus == "candidate_review" && Category == "receipt" && Status == "completed";
    internal string TypeLabel => IsPersonalTransfer ? "好友转账" : Category == "receipt" ? "收款码通知" : "其他支付通知";
    internal string StateLabel => ReviewStatus == "approved" ? (IsPersonalTransfer ? "订单已确认" : "已人工确认") :
        IsPersonalTransfer ? "待核对" : "待核对到账";
    public override string ToString() =>
        $"{TypeLabel} · {StateLabel}　{Reference} · {(AmountFen.HasValue ? $"¥{AmountFen.Value / 100m:F2}" : "金额待核对")}\n" +
        $"消息时间：{(ObservedAt.HasValue ? ObservedAt.Value.ToLocalTime().ToString("MM-dd HH:mm:ss") : "未识别")}" +
        $"　交易尾号：{(string.IsNullOrWhiteSpace(TransactionIdSuffix) ? "未识别" : TransactionIdSuffix)}" +
        (string.IsNullOrWhiteSpace(Detail) ? string.Empty : $"\n{Detail}");
}

internal sealed class LocalMarketClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly LocalMarketConfig _config;
    internal DateTimeOffset? ServerTime { get; private set; }

    internal LocalMarketClient(LocalMarketConfig config)
    {
        _config = config;
        var endpoint = new Uri(config.BaseUrl);
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseProxy = false,
            UseCookies = false,
            ConnectCallback = async (context, cancellationToken) =>
            {
                if (context.DnsEndPoint.Host != "127.0.0.1" || context.DnsEndPoint.Port != endpoint.Port)
                    throw new InvalidOperationException("已阻止非本地网络连接。");
                var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                try
                {
                    await socket.ConnectAsync(new IPEndPoint(IPAddress.Loopback, endpoint.Port), cancellationToken);
                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch
                {
                    socket.Dispose();
                    throw;
                }
            }
        };
        _http = new HttpClient(handler) { BaseAddress = endpoint, Timeout = TimeSpan.FromSeconds(8) };
    }

    internal async Task CheckHealthAsync(CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Get, "health");
        using var response = await _http.SendAsync(request, cancellationToken);
        EnsureSuccess(response);
        UpdateServerTime(response, null);
    }

    internal async Task<LocalMarketCatalog> GetCatalogAsync(CancellationToken cancellationToken)
    {
        var data = await JsonAsync(HttpMethod.Get, "api/local-market/catalog", null, false, cancellationToken);
        if (!data.TryGetProperty("products", out var products) || products.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("本地商品列表格式无效。");
        foreach (var product in products.EnumerateArray())
        {
            if (GetString(product, "id") != "local-test-pet-pack") continue;
            if (!product.TryGetProperty("testPriceFen", out var amount) || amount.GetInt32() != 1 ||
                !product.TryGetProperty("localOnly", out var localOnly) || localOnly.ValueKind != JsonValueKind.True)
                throw new InvalidOperationException("该商品不是 0.01 元本地测试商品。");
            if (!data.TryGetProperty("currentOrder", out var currentOrder) ||
                currentOrder.ValueKind is not (JsonValueKind.Object or JsonValueKind.Null))
                throw new InvalidOperationException("本地商品列表未返回当前订单状态，请更新本地测试服务。");
            return new LocalMarketCatalog(GetString(product, "title") ?? "桌宠角色包（本地测试）",
                currentOrder.ValueKind == JsonValueKind.Null ? null : ParseOrderValue(currentOrder));
        }
        throw new InvalidOperationException("本地测试商品未配置。");
    }

    internal async Task<LocalMarketOrder> CreateOrderAsync(CancellationToken cancellationToken) =>
        ParseOrder(await JsonAsync(HttpMethod.Post, "api/local-market/orders",
            new { productId = "local-test-pet-pack" }, false, cancellationToken));

    internal async Task<LocalMarketOrder> GetOrderAsync(string id, CancellationToken cancellationToken) =>
        ParseOrder(await JsonAsync(HttpMethod.Get, $"api/local-market/orders/{Uri.EscapeDataString(id)}",
            null, false, cancellationToken));

    internal async Task<LocalMarketOrder> CancelOrderAsync(string id, CancellationToken cancellationToken) =>
        ParseOrder(await JsonAsync(HttpMethod.Post, $"api/local-market/orders/{Uri.EscapeDataString(id)}/cancel",
            new { }, false, cancellationToken));

    internal async Task<IReadOnlyList<LocalMarketPaymentEvent>> GetPaymentEventsAsync(CancellationToken cancellationToken)
    {
        var data = await JsonAsync(HttpMethod.Get, "api/local-market/payment-events", null, true, cancellationToken);
        if (!data.TryGetProperty("events", out var events) || events.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("本地到账候选列表格式无效。");
        return events.EnumerateArray().Select(item => new LocalMarketPaymentEvent(
            GetString(item, "id") ?? string.Empty,
            GetString(item, "paymentReference") ?? GetString(item, "reference") ?? "备注未识别",
            item.TryGetProperty("amountFen", out var amount) && amount.ValueKind == JsonValueKind.Number && amount.TryGetInt32(out var fen) ? fen : null,
            GetString(item, "category") ?? string.Empty,
            GetString(item, "status") ?? string.Empty,
            GetString(item, "reviewStatus") ?? string.Empty,
            (item.TryGetProperty("late", out var late) && late.ValueKind == JsonValueKind.True ? "迟到付款 · " : string.Empty) +
            FormatReviewReason(GetString(item, "reviewReason") ?? GetString(item, "reason") ?? GetString(item, "summary") ?? string.Empty),
            GetTimestamp(item, "observedAt") ?? GetTimestamp(item, "receivedAt"),
            GetString(item, "transactionIdSuffix") ?? string.Empty))
            .Where(item => item.Id.Length != 0).ToArray();
    }

    internal async Task ConfirmPaymentAsync(string eventId, string orderId, CancellationToken cancellationToken)
    {
        await JsonAsync(HttpMethod.Post, $"api/local-market/payment-events/{Uri.EscapeDataString(eventId)}/confirm",
            new { orderId, note = "本地人工核对0.01元到账" }, true, cancellationToken);
    }

    internal async Task<string> DownloadAsync(LocalMarketOrder order, CancellationToken cancellationToken)
    {
        if (order.Status != "paid") throw new InvalidOperationException("订单尚未确认。");
        using var request = CreateRequest(HttpMethod.Get,
            $"api/local-market/products/{Uri.EscapeDataString(order.ProductId)}/download");
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        EnsureSuccess(response);
        if (response.Content.Headers.ContentType?.MediaType != "application/zip")
            throw new InvalidOperationException("本地服务没有返回测试资源包。");
        const long maxBytes = 64 * 1024 * 1024;
        if (response.Content.Headers.ContentLength > maxBytes)
            throw new InvalidOperationException("测试资源包超过大小限制。");
        var downloadDirectory = Path.Combine(_config.DataDirectory, "downloads");
        Directory.CreateDirectory(downloadDirectory);
        var destination = Path.Combine(downloadDirectory, $"local-test-pet-pack-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.zip");
        var partial = destination + ".partial";
        try
        {
            await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var target = new FileStream(partial, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
            {
                var buffer = new byte[81920];
                long total = 0;
                int read;
                while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    total += read;
                    if (total > maxBytes) throw new InvalidOperationException("测试资源包超过大小限制。");
                    await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                }
                if (total == 0) throw new InvalidOperationException("测试资源包为空。");
            }
            File.Move(partial, destination);
            return destination;
        }
        finally
        {
            if (File.Exists(partial)) File.Delete(partial);
        }
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string path, bool admin = false)
    {
        // Paths originate exclusively from this class; absolute addresses and redirects never get a token.
        var request = new HttpRequestMessage(method, new Uri(_http.BaseAddress!, path));
        var token = admin ? _config.AdminToken : _config.ClientToken;
        if (string.IsNullOrWhiteSpace(token)) throw new InvalidOperationException("本地管理员权限未配置。");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    private async Task<JsonElement> JsonAsync(HttpMethod method, string path, object? body, bool admin,
        CancellationToken cancellationToken)
    {
        using var request = CreateRequest(method, path, admin);
        if (body is not null)
            request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        using var response = await _http.SendAsync(request, cancellationToken);
        EnsureSuccess(response);
        JsonDocument document;
        try { document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken)); }
        catch (JsonException) { throw new InvalidOperationException("本地服务响应格式无效。"); }
        using (document)
        {
            var root = document.RootElement;
            if (!root.TryGetProperty("ok", out var ok) || ok.ValueKind != JsonValueKind.True ||
                !root.TryGetProperty("data", out var data))
                throw new InvalidOperationException("本地服务未能完成请求，请检查本地测试服务。");
            UpdateServerTime(response, data);
            return data.Clone();
        }
    }

    private void UpdateServerTime(HttpResponseMessage response, JsonElement? data)
    {
        ServerTime = data.HasValue && data.Value.TryGetProperty("serverTime", out var serverTime) &&
            serverTime.TryGetDateTimeOffset(out var parsed) ? parsed : response.Headers.Date;
    }

    private static void EnsureSuccess(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode) return;
        throw new InvalidOperationException(response.StatusCode switch
        {
            HttpStatusCode.Unauthorized => "本地访问令牌无效，请重新生成本地测试配置。",
            HttpStatusCode.Forbidden => "尚未取得权限，请核对到账状态或本地管理员配置。",
            HttpStatusCode.NotFound => "本地订单或测试资源不存在。",
            HttpStatusCode.Conflict => "该订单或到账记录当前不能执行此操作，请刷新状态。",
            HttpStatusCode.TooManyRequests => "操作较频繁，请稍后重试。",
            _ => $"本地服务请求失败（HTTP {(int)response.StatusCode}）。"
        });
    }

    private static LocalMarketOrder ParseOrder(JsonElement data)
    {
        if (!data.TryGetProperty("order", out var order))
            throw new InvalidOperationException("本地服务未返回订单。");
        return ParseOrderValue(order);
    }

    private static LocalMarketOrder ParseOrderValue(JsonElement order)
    {
        try
        {
            var parsed = new LocalMarketOrder(order.GetProperty("id").GetString()!,
                order.GetProperty("productId").GetString()!, order.GetProperty("paymentReference").GetString()!,
                order.GetProperty("amountFen").GetInt32(), order.GetProperty("currency").GetString()!,
                order.GetProperty("status").GetString()!, order.GetProperty("createdAt").GetDateTimeOffset(),
                order.GetProperty("expiresAt").GetDateTimeOffset());
            if (string.IsNullOrEmpty(parsed.Id) || parsed.ProductId != "local-test-pet-pack" ||
                parsed.AmountFen != 1 || parsed.Currency != "CNY" || string.IsNullOrEmpty(parsed.Reference) ||
                parsed.Reference.Length != 5 || !parsed.Reference.Take(3).All(char.IsAsciiDigit) ||
                !parsed.Reference.Skip(3).All(character => character is >= 'A' and <= 'Z' or >= 'a' and <= 'z') ||
                parsed.Status is not ("pending_payment" or "expired" or "canceled" or "paid"))
                throw new InvalidOperationException("本地测试订单信息不符合预期。");
            return parsed;
        }
        catch (Exception error) when (error is KeyNotFoundException or FormatException or JsonException)
        {
            throw new InvalidOperationException("本地测试订单格式无效。");
        }
    }

    private static string? GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() : null;

    private static DateTimeOffset? GetTimestamp(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String &&
        value.TryGetDateTimeOffset(out var timestamp) ? timestamp : null;

    private static string FormatReviewReason(string reasons) => string.Join("；", reasons
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(reason => reason switch
        {
            "transfer_receipt_requires_manual_confirmation" or "manual_transfer_receipt_required" => "此历史转账记录待核对",
            "auto_personal_transfer_policy" => "转账信息已匹配，订单自动确认",
            "source_unverified" => "来源为微信消息，未独立核验结算",
            "reference_invalid" => "备注短码格式不符",
            "reference_not_found" => "未找到对应备注订单",
            "amount_missing" => "未识别金额",
            "amount_mismatch" => "金额与订单不符",
            "currency_missing" => "未识别币种",
            "currency_mismatch" => "币种与订单不符",
            "occurred_at_missing" => "未取得来源消息时间",
            "transaction_id_missing" => "未取得交易编号",
            "collection_account_mismatch" => "收款账户与订单不符",
            "order_expired" => "原订单已过期",
            "order_canceled" => "原订单已取消",
            "order_paid" => "原订单已支付",
            "order_paid_by_other_event" => "该订单已由另一笔支付记录确认",
            "payment_after_expiry" => "来源消息时间晚于订单截止时间",
            "notification_after_expiry" => "通知晚于订单截止时间",
            "category_not_receipt" => "消息不是收款通知",
            "direction_not_incoming" => "消息不是收入记录",
            "status_not_observed" or "status_not_completed" or "status_unknown" => "消息状态仍需核对",
            _ => reason.Any(character => character > 127) ? reason : "需人工核对此支付记录"
        }).Distinct());

    public void Dispose() => _http.Dispose();
}
