using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using ZhuoDazi.Services;

var root = Path.Combine(Path.GetTempPath(), "ZhuoDazi-Alipay-Checks", Guid.NewGuid().ToString("N"));
var id = Guid.NewGuid().ToString();
var token = new string('t', 43);
var url = $"https://in.desktoppet.online/checkout/alipay/{id}#token={token}";
var configuration = new AlipayPreviewConfig
{
    DataDirectory = root,
    ActivationIdentity = new() { InstallationId = new string('a', 24), Credential = new string('b', 48) },
    ShopIdentity = new() { InstallationId = new string('c', 24), Credential = new string('d', 48), LicenseId = Guid.NewGuid().ToString() }
};
configuration.Validate();
try
{
    Check(AlipayPreviewClient.ValidateCheckoutUrl(url, id).AbsoluteUri == url, "Expected checkout accepted.");
    foreach (var candidate in new[] {
        url.Replace("https:", "http:"), url.Replace("in.desktoppet.online", "evil.example"),
        url.Replace("/checkout/alipay/", "/other/"), url.Replace("#token=", "?token="), url + "&other=value",
        url.Replace("#token=" + token, "#token=short"), url.Replace("https://", "https://user@"),
        url.Replace(id, Guid.NewGuid().ToString()), url.Replace("in.desktoppet.online", "in.desktoppet.online:444") })
        Reject(() => AlipayPreviewClient.ValidateCheckoutUrl(candidate, id));
    foreach (var origin in new[] { "http://localhost:18788/", "http://127.0.0.1:18789/", "https://127.0.0.1:18788/", "http://127.0.0.1:18788/api/", "https://in.desktoppet.online/" })
    {
        var old = configuration.BaseUrl; configuration.BaseUrl = origin;
        Reject(configuration.Validate); configuration.BaseUrl = old;
    }
    var handler = new RecordingHandler();
    using var client = new AlipayPreviewClient(configuration, handler);
    handler.Response = "{\"activation\":{\"title\":\"激活桌搭子\",\"description\":\"本地预览\",\"amountFen\":688},\"products\":[],\"provider\":{\"configured\":false,\"reason\":\"未配置\"}}";
    var catalog = await client.CatalogAsync(configuration.ActivationIdentity, default);
    Check(catalog.Activation.AmountFen == 688 && !catalog.Provider.Configured, "Actual provider status decoded.");
    Check(handler.Authorization == $"Trial {configuration.ActivationIdentity.InstallationId}.{configuration.ActivationIdentity.Credential}", "Trial identity serialized without production state.");
    handler.Response = JsonSerializer.Serialize(new { order = new { id, title = "激活桌搭子", kind = "activation", amountFen = 688, status = "pending_payment", expiresAt = DateTimeOffset.UtcNow.AddMinutes(15) }, checkoutUrl = url });
    var activation = await client.CreateOrderAsync(configuration.ActivationIdentity, true, null, default);
    using (var payload = JsonDocument.Parse(handler.Body!))
        Check(payload.RootElement.GetProperty("kind").GetString() == "activation" && !payload.RootElement.TryGetProperty("productId", out _), "Activation omits pet productId.");
    Check(activation.Order.Status == "pending_payment", "Unpaid order remains unpaid.");
    handler.Response = JsonSerializer.Serialize(new { order = new { id, kind = "activation", status = "pending" }, checkoutUrl = url,
        paymentUrl = "https://untrusted.example/should-not-open" });
    var websiteCheckout = await client.CreateOrderAsync(configuration.ActivationIdentity, true, null, default);
    Check(websiteCheckout.CheckoutUrl == url, "Desktop create keeps our checkout URL; extra payment links are not its navigation contract.");
    handler.Response = JsonSerializer.Serialize(new { order = new { id, kind = "activation", status = "pending" },
        paymentUrl = "https://openapi.alipay.com/gateway.do?method=alipay.trade.page.pay" });
    await RejectAsync(() => client.CreateOrderAsync(configuration.ActivationIdentity, true, null, default));
    Reject(() => AlipayPreviewClient.ValidateCheckoutUrl("https://openapi.alipay.com/gateway.do?method=alipay.trade.page.pay", id));
    handler.Response = JsonSerializer.Serialize(new { order = new { id, kind = "pet", amountFen = 688, status = "pending" }, checkoutUrl = url });
    await client.CreateOrderAsync(configuration.ShopIdentity, false, "alipay-preview-pet", default);
    using (var payload = JsonDocument.Parse(handler.Body!))
        Check(payload.RootElement.GetProperty("kind").GetString() == "pet" && payload.RootElement.GetProperty("productId").GetString() == "alipay-preview-pet", "Pet request accepts the catalog slug and contains only its product id and kind.");
    Check(handler.Authorization == $"Bearer {configuration.ShopIdentity.LicenseId}.{configuration.ShopIdentity.Credential}", "Shop uses only configured local license credentials.");
    foreach (var deliveryMode in new[] { "device_activated", "code_only" })
    {
        handler.Response = JsonSerializer.Serialize(new { order = new { id, kind = "activation", status = "paid", fulfillmentStatus = "fulfilled", deliveryMode, activationCode = "ABC234" } });
        var delivered = await client.OrderAsync(configuration.ActivationIdentity, id, default);
        Check(delivered.Order.IsActivationDelivered && delivered.Order.DeliveryMode == deliveryMode && delivered.Order.ActivationCode == "ABC234", "Fulfilled activation contract preserves its delivery mode and code.");
        Check(deliveryMode == "device_activated"
            ? delivered.Order.ActivationDeliveryMessage!.Contains("已在服务端激活")
            : delivered.Order.ActivationDeliveryMessage!.Contains("已交付激活码") && !delivered.Order.ActivationDeliveryMessage.Contains("已在服务端激活"),
            "Code-only delivery does not claim that this order activated the device.");
    }
    handler.Response = JsonSerializer.Serialize(new { order = new { id, kind = "activation", status = "pending", fulfillmentStatus = "pending", deliveryMode = "device_activated", activationCode = "ABC234" } });
    var unpaid = await client.OrderAsync(configuration.ActivationIdentity, id, default);
    Check(!unpaid.Order.IsActivationDelivered && unpaid.Order.ActivationDeliveryMessage is null, "An unpaid order never reports completed activation based on delivery mode or a code alone.");
    handler.Status = HttpStatusCode.Conflict; handler.Response = "{\"error\":\"账号已拥有该桌宠\",\"code\":\"ALREADY_ENTITLED\"}";
    try { await client.CatalogAsync(configuration.ActivationIdentity, default); throw new Exception("Backend error was accepted."); }
    catch (InvalidOperationException error) { Check(error.Message == "账号已拥有该桌宠", "The actual backend error field is decoded."); }
    handler.Status = HttpStatusCode.OK;
    Check(handler.AllUris.All(uri => uri.Scheme == "http" && uri.Host == "127.0.0.1" && uri.Port == 18788), "Transport sees only loopback.");
    handler.Status = HttpStatusCode.Found;
    await RejectAsync(() => client.CatalogAsync(configuration.ActivationIdentity, default));
    handler.Status = HttpStatusCode.OK; handler.Response = "{broken-json";
    await RejectAsync(() => client.CatalogAsync(configuration.ActivationIdentity, default));
    Console.WriteLine("Alipay preview checks passed: loopback-only origin, checkout allowlist, separate auth, request serialization, provider and delivery contract decoding, backend errors, redirects, and malformed responses.");
}
finally { if (Directory.Exists(root)) Directory.Delete(root, true); }

static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
static void Reject(Action action) { try { action(); } catch (InvalidOperationException) { return; } throw new Exception("Invalid input was accepted."); }
static async Task RejectAsync(Func<Task> action) { try { await action(); } catch (InvalidOperationException) { return; } throw new Exception("Invalid response was accepted."); }

internal sealed class RecordingHandler : HttpMessageHandler
{
    internal string Response = "{}";
    internal HttpStatusCode Status = HttpStatusCode.OK;
    internal string? Authorization, Body;
    internal List<Uri> AllUris = [];
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        AllUris.Add(request.RequestUri!); Authorization = request.Headers.Authorization?.ToString();
        Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        return new HttpResponseMessage(Status) { Content = new StringContent(Response, Encoding.UTF8, "application/json") };
    }
}
