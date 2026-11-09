using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using ZhuoDazi.Services;

var root = Path.Combine(Path.GetTempPath(), "ZhuoDazi-FullPayment-Checks", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try
{
    var recorder = new StubHandler(_ => new { ok = true });
    using (var http = new HttpClient(new LocalPaymentTestEnvironment.LocalApiRouter(new Uri("http://127.0.0.1:18788/"), recorder)))
    {
        await http.GetAsync("https://in.desktoppet.online/api/trial");
        Check(recorder.LastUri == "http://127.0.0.1:18788/api/trial", "Known API must route to loopback.");
        await http.GetAsync("http://127.0.0.1:18788/api/pet-market/catalog");
        var allowed = recorder.RequestCount;
        foreach (var address in new[] { "https://example.com/api/trial", "http://127.0.0.1:18789/api/trial",
            "http://localhost:18788/api/trial", "https://in.desktoppet.online/admin", "http://127.0.0.1:18788/api/trial#x",
            "https://user@in.desktoppet.online/api/trial", "https://in.desktoppet.online:444/api/trial" })
            await RejectAsync(() => http.GetAsync(address));
        using var hostOverride = new HttpRequestMessage(HttpMethod.Get, "http://127.0.0.1:18788/api/trial");
        hostOverride.Headers.Host = "example.com";
        await RejectAsync(() => http.SendAsync(hostOverride));
        Check(recorder.RequestCount == allowed, "Rejected requests must never reach transport.");
    }

    var licenseId = Guid.NewGuid().ToString();
    var accountId = Guid.NewGuid().ToString();
    DeskPetHttp.HandlerFactory = () => new StubHandler(_ => new { license = new { licenseId, accountId, activatedAt = DateTimeOffset.UtcNow, deviceCount = 1 } });
    using (var licenses = new LicenseService(root))
    {
        Check(!licenses.IsActivated, "New test identity must start unactivated.");
        var id = Guid.NewGuid().ToString();
        var url = $"https://in.desktoppet.online/checkout/alipay/{id}#token={new string('t', 43)}";
        var paid = false;
        var handler = new StubHandler(_ => new { order = new { id, kind = "pet", title = "Test pet", productId = "test-pet",
            productVersion = "1.0.0", amountFen = 1, createdAt = DateTimeOffset.UtcNow,
            expiresAt = DateTimeOffset.UtcNow.AddMinutes(15), status = paid ? "paid" : "pending",
            fulfillmentStatus = paid ? "fulfilled" : "pending" }, checkoutUrl = url });
        Uri? opened = null;
        await licenses.CheckTrialAsync();
        Check(licenses.IsActivated, "Confirmed trial response must synchronize activation.");
        using (var payments = new FullAppPaymentClient(licenses, handler, uri => opened = uri))
        {
            await payments.OpenAsync(false, "test-pet", default);
            Check(handler.LastUri?.StartsWith("https://in.desktoppet.online/api/payments/alipay/") == true,
                "Normal builds must use the production HTTPS payment API.");
            Check(opened?.AbsoluteUri == url && handler.LastAuth?.StartsWith("Bearer ") == true, "Purchase must use current activated identity and validated checkout.");
            var pending = await payments.PendingPetOrdersAsync(default);
            Check(pending.Count == 1 && pending[0].Status == "pending_payment" && pending[0].Payment?.Method == "alipay_page", "Pending official orders must feed the existing market UI.");
        }
        Check(!Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(root, "payment-orders.dat"))).Contains(url), "Saved private checkout must be protected.");
        using var restored = new FullAppPaymentClient(licenses, new StubHandler(handler.Responder), uri => opened = uri);
        opened = null;
        restored.OpenOrder(id);
        Check(opened?.AbsoluteUri == url, "Pending order must reopen after client restart.");
        paid = true;
        Check((await restored.PendingPetOrdersAsync(default)).Count == 0, "Fulfilled orders must use authoritative market history.");
    }
    DeskPetHttp.HandlerFactory = null;

    if (args.Length == 3 && args[0] == "--live")
    {
        var environment = LocalPaymentTestEnvironment.Load(Path.GetFullPath(args[1]));
        Check(environment.DataDirectory.Contains(Path.Combine("AlipayLocalTest", environment.Profile)), "Live data must remain in the isolated profile.");
        DeskPetHttp.HandlerFactory = environment.CreateHandler;
        using (var fresh = new LicenseService(environment.DataDirectory))
        {
            Check(!fresh.IsActivated, "Live fresh profile must start unactivated.");
            await fresh.CheckTrialAsync();
            Check(fresh.IsTrialActive && !fresh.IsActivated, "Local trial must register without granting paid activation.");
            using var payments = new FullAppPaymentClient(fresh, origin: environment.Origin);
            var prices = await payments.CatalogAsync(default);
            Check(prices.Activation.AmountFen == 1 && prices.Provider.Configured, "Live activation must cost one fen with a configured real provider.");
        }
        using var document = JsonDocument.Parse(File.ReadAllText(args[2]));
        var shop = document.RootElement.GetProperty("shopIdentity");
        var configFile = Path.Combine(root, "private-live-identity.json");
        File.WriteAllText(configFile, JsonSerializer.Serialize(new {
            baseUrl = environment.BaseUrl, dataDirectory = Path.Combine(root, "installed"), signingPublicKeySpki = environment.SigningPublicKeySpki,
            installationId = shop.GetProperty("installationId").GetString(), credential = shop.GetProperty("credential").GetString(),
            licenseId = shop.GetProperty("licenseId").GetString(), accountId = shop.GetProperty("accountId").GetString()
        }));
        using var market = new PetMarketClient(PetMarketDebugConfig.Load(configFile));
        var catalog = await market.GetCatalogAsync();
        Check(catalog.Products.Any(p => p.Id == "alipay-preview-free" && p.PriceFen == 0)
            && catalog.Products.Any(p => p.Id == "alipay-preview-pet" && p.PriceFen == 1 && p.Owned), "Live catalog must include free and genuinely purchased fixtures.");
        await market.ClaimAsync("alipay-preview-free");
        foreach (var product in catalog.Products)
        {
            var preview = await market.DownloadPreviewAsync(product);
            Check(preview is not null && File.Exists(preview), "Live product preview must download.");
            var descriptor = await market.GetDownloadDescriptorAsync(product.Id, "1.0.0");
            var archive = await market.DownloadPackageAsync(descriptor);
            var installed = await PetMarketInstaller.InstallAsync(archive, Path.Combine(root, "installed"), product.Id, descriptor.Version, descriptor.SizeBytes, descriptor.Sha256);
            Check(installed.Files.Count > 0 && installed.Files.All(File.Exists), "Actual package must verify and install on Windows.");
            var grant = await market.AuthorizeUseAsync(product.Id, descriptor.Version, descriptor.ManifestSha256);
            Check(grant.Payload.ProductId == product.Id, "Installed product must receive a verified use grant.");
            Console.WriteLine($"Live preview, signed download, install and use grant passed: {product.Id}.");
        }
    }
    Console.WriteLine("Local full-payment integration checks passed.");
}
finally
{
    DeskPetHttp.HandlerFactory = null;
    Directory.Delete(root, true);
}

static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
static async Task RejectAsync(Func<Task> action)
{
    try { await action(); } catch (HttpRequestException) { return; }
    throw new Exception("Unsafe request was accepted.");
}

sealed class StubHandler(Func<HttpRequestMessage, object> responder) : HttpMessageHandler
{
    internal Func<HttpRequestMessage, object> Responder => responder;
    internal string? LastUri, LastAuth;
    internal int RequestCount;
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        RequestCount++;
        LastUri = request.RequestUri?.AbsoluteUri;
        LastAuth = request.Headers.Authorization?.ToString();
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(responder(request)), Encoding.UTF8, "application/json") });
    }
}
