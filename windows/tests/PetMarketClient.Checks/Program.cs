using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using ZhuoDazi.Models;

namespace ZhuoDazi.Services;

internal static class Program
{
    private static async Task Main()
    {
        var root = Path.Combine(Path.GetTempPath(), $"zhuodazi-market-client-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var privateKey = new Ed25519PrivateKeyParameters(RandomNumberGenerator.GetBytes(32));
            CheckDebugDirectoryIsolation(root, privateKey.GeneratePublicKey().GetEncoded());
            var config = LoadConfig(root, privateKey.GeneratePublicKey().GetEncoded());
            await CheckPaymentMetadataAsync(config);
            await CheckOrderHistoryAsync(config);
            await CheckEntitlementVersionAsync(config, privateKey);
            await CheckSignedEnvelopeRejectionsAsync(config, privateKey);
            await CheckCompletedDownloadsMoveAsync(config);
            Console.WriteLine("PetMarketClient checks passed (20 assertions).");
        }
        finally
        {
            DeskPetHttp.HandlerFactory = null;
            Directory.Delete(root, recursive: true);
        }
    }

    private static async Task CheckOrderHistoryAsync(PetMarketDebugConfig config)
    {
        var requestedPaths = new List<string>();
        var authorization = string.Empty;
        var now = DateTimeOffset.UtcNow;
        var firstPage = new PetMarketOrderListResponse
        {
            ServerTime = now,
            NextCursor = "cursor_page_2",
            Orders =
            [
                new PetMarketOrder
                {
                    Id = "paid-order", ProductId = "paid-pet", ProductTitle = "  已购桌宠  ",
                    ProductVersion = "1.0.0", Status = "paid", AmountFen = 580, Currency = "CNY",
                    PaymentReference = "310FH", CreatedAt = now.AddMinutes(-2),
                    ExpiresAt = now.AddMinutes(13), PaidAt = now,
                    Payment = new PetMarketPaymentInfo
                    {
                        Enabled = true, Available = true, ListenerOnline = true, Method = "personal_transfer",
                        RecipientName = "Public recipient", RecipientAccount = "public_account"
                    }
                }
            ]
        };
        var secondPage = new PetMarketOrderListResponse
        {
            ServerTime = now.AddSeconds(1),
            Orders =
            [
                new PetMarketOrder
                {
                    Id = "canceled-order", ProductId = "other-pet", ProductTitle = "旧订单",
                    Status = "canceled", AmountFen = 280, Currency = "CNY",
                    PaymentReference = "453UJ", CreatedAt = now.AddDays(-1),
                    ExpiresAt = now.AddDays(-1).AddMinutes(15), CanceledAt = now.AddDays(-1).AddMinutes(1)
                }
            ]
        };
        DeskPetHttp.HandlerFactory = () => new StubHandler(request =>
        {
            requestedPaths.Add(request.RequestUri?.PathAndQuery ?? string.Empty);
            authorization = request.Headers.Authorization?.Scheme ?? string.Empty;
            return Json(request.RequestUri?.Query.Length > 0 ? secondPage : firstPage);
        });
        using var client = new PetMarketClient(config);
        var response = await client.ListOrdersAsync();
        Require(requestedPaths.SequenceEqual([
                    "/api/pet-market/orders",
                    "/api/pet-market/orders?cursor=cursor_page_2"
                ])
                && authorization == "Bearer" && response.Orders.Count == 2,
            "Order history must follow authenticated account-scoped cursor pages.");
        Require(response.Orders[0].ProductTitle == "已购桌宠"
                && response.Orders[0].Payment?.RecipientAccount == "public_account",
            "Order history must retain a normalized product title and frozen payment snapshot.");

        var invalidCursor = new PetMarketOrderListResponse
        {
            ServerTime = now,
            NextCursor = "https://outside.invalid/orders",
            Orders = []
        };
        DeskPetHttp.HandlerFactory = () => new StubHandler(_ => Json(invalidCursor));
        using var invalidClient = new PetMarketClient(config);
        await ThrowsAsync(() => invalidClient.ListOrdersAsync(),
            "Order-history cursors must be bounded base64url tokens, never external URLs.");
    }

    private static async Task CheckPaymentMetadataAsync(PetMarketDebugConfig config)
    {
        var payment = new PetMarketPaymentInfo
        {
            Enabled = true, Available = true, ListenerOnline = true, Method = "personal_transfer",
            RecipientName = "Public recipient", RecipientAccount = "public_account",
            Instructions = "Use the order reference."
        };
        DeskPetHttp.HandlerFactory = () => new StubHandler(_ => Json(new PetMarketCatalog { Payment = payment }));
        using (var client = new PetMarketClient(config))
        {
            var catalog = await client.GetCatalogAsync();
            Require(catalog.Payment?.CanCreateOrder == true && catalog.Payment.RecipientAccount == "public_account",
                "Catalog payment metadata must remain usable without hardcoded recipient details.");
        }

        DeskPetHttp.HandlerFactory = () => new StubHandler(_ => Json(new PetMarketCatalog()));
        using (var client = new PetMarketClient(config))
            Require((await client.GetCatalogAsync()).Payment is null,
                "A legacy catalog without payment metadata must remain explicitly unavailable.");

        var invalidQr = new PetMarketPaymentInfo
        {
            Enabled = true, Available = true, ListenerOnline = true, Method = "collection_qr",
            QrCodeDataUrl = "https://example.invalid/qr.png"
        };
        DeskPetHttp.HandlerFactory = () => new StubHandler(_ => Json(new PetMarketCatalog { Payment = invalidQr }));
        using (var client = new PetMarketClient(config))
        {
            var catalog = await client.GetCatalogAsync();
            Require(catalog.Payment is { Available: false } && !catalog.Payment.CanCreateOrder,
                "A remote or invalid QR value must disable new orders.");
        }

        var pending = new PetMarketOrderResponse
        {
            Order = new PetMarketOrder
            {
                Id = "pending-order", ProductId = "paid-pet", Status = "pending_payment",
                AmountFen = 580, PaymentReference = "310FH", ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(10),
                Payment = new PetMarketPaymentInfo
                {
                    Enabled = true, Available = false, ListenerOnline = false, Method = "personal_transfer",
                    RecipientName = "Public recipient", RecipientAccount = "public_account",
                    UnavailableReason = "Listener offline"
                }
            }
        };
        DeskPetHttp.HandlerFactory = () => new StubHandler(_ => Json(pending));
        using (var client = new PetMarketClient(config))
        {
            var order = await client.GetOrderAsync("pending-order");
            Require(order.Payment is { Available: false, ListenerOnline: false }
                    && order.Payment.RecipientAccount == "public_account" && order.PaymentReference == "310FH",
                "Polling must retain an existing order and its destination when payment health goes offline.");
        }
    }

    private static async Task CheckEntitlementVersionAsync(
        PetMarketDebugConfig config, Ed25519PrivateKeyParameters privateKey)
    {
        var download = new PetMarketDownloadPayload
        {
            SchemaVersion = 1,
            Kind = "pet-market-download",
            AccountId = config.AccountId,
            LicenseId = config.LicenseId,
            InstallationId = config.InstallationId,
            ProductId = "paid-pet",
            Version = "1.0.0",
            FileName = "paid-pet-1.0.0.zip",
            SizeBytes = 3,
            Sha256 = Convert.ToHexString(SHA256.HashData([1, 2, 3])).ToLowerInvariant(),
            ManifestSha256 = new string('a', 64),
            DownloadPath = "/api/pet-market/products/paid-pet/versions/1.0.0/download",
            IssuedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(10)
        };
        var envelope = Sign(download, privateKey);
        DeskPetHttp.HandlerFactory = () => new StubHandler(_ => Json(envelope));
        using var client = new PetMarketClient(config);

        var signedVersion = await client.GetDownloadDescriptorAsync("paid-pet");
        Require(signedVersion.Version == "1.0.0",
            "The signed entitlement version must remain downloadable when catalog activeVersion advances.");

        var expectedVersion = await client.GetDownloadDescriptorAsync("paid-pet", "1.0.0");
        Require(expectedVersion.Version == "1.0.0", "The explicit entitlement version must be accepted.");

        await ThrowsAsync(
            () => client.GetDownloadDescriptorAsync("paid-pet", "2.0.0"),
            "A descriptor that mismatches an explicit entitlement version must be rejected.");
    }

    private static async Task CheckCompletedDownloadsMoveAsync(PetMarketDebugConfig config)
    {
        byte[] packageBytes = [1, 2, 3, 4];
        byte[] previewBytes = Encoding.ASCII.GetBytes("GIF89a");
        DeskPetHttp.HandlerFactory = () => new StubHandler(request =>
        {
            var bytes = request.RequestUri!.AbsolutePath.EndsWith("/preview", StringComparison.Ordinal)
                ? previewBytes
                : packageBytes;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(bytes)
            };
        });
        using var client = new PetMarketClient(config);
        var descriptor = new PetMarketDownloadPayload
        {
            ProductId = "paid-pet",
            Version = "1.0.0",
            DownloadPath = "/api/pet-market/products/paid-pet/versions/1.0.0/download",
            SizeBytes = packageBytes.Length,
            Sha256 = Convert.ToHexString(SHA256.HashData(packageBytes)).ToLowerInvariant()
        };
        var packagePath = await client.DownloadPackageAsync(descriptor);
        Require(File.Exists(packagePath), "The package must move from .partial after the stream is disposed.");
        Require(File.ReadAllBytes(packagePath).SequenceEqual(packageBytes), "The moved package must retain its bytes.");

        var preview = new PetMarketProduct
        {
            Id = "paid-pet",
            Title = "Paid Pet",
            PreviewFiles =
            [
                new PetMarketPreviewFile
                {
                    Path = "gifs/preview.gif",
                    DownloadPath = "/api/pet-market/products/paid-pet/versions/1.0.0/preview?file=gifs%2Fpreview.gif",
                    SizeBytes = previewBytes.Length,
                    Sha256 = Convert.ToHexString(SHA256.HashData(previewBytes)).ToLowerInvariant()
                }
            ]
        };
        var previewPath = await client.DownloadPreviewAsync(preview);
        Require(previewPath is not null && File.Exists(previewPath),
            "The preview must move from .partial after the stream is disposed.");
        Require(File.ReadAllBytes(previewPath!).SequenceEqual(previewBytes), "The moved preview must retain its bytes.");
    }

    private static async Task CheckSignedEnvelopeRejectionsAsync(
        PetMarketDebugConfig config, Ed25519PrivateKeyParameters privateKey)
    {
        PetMarketDownloadPayload Payload(string accountId, DateTimeOffset expiresAt) => new()
        {
            SchemaVersion = 1,
            Kind = "pet-market-download",
            AccountId = accountId,
            LicenseId = config.LicenseId,
            InstallationId = config.InstallationId,
            ProductId = "secure-pet",
            Version = "1.0.0",
            FileName = "secure-pet.zip",
            SizeBytes = 3,
            Sha256 = Convert.ToHexString(SHA256.HashData([1, 2, 3])).ToLowerInvariant(),
            ManifestSha256 = new string('b', 64),
            DownloadPath = "/api/pet-market/products/secure-pet/versions/1.0.0/download",
            IssuedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
            ExpiresAt = expiresAt
        };

        var tampered = Sign(Payload(config.AccountId, DateTimeOffset.UtcNow.AddMinutes(9)), privateKey);
        tampered.SignedPayload = Convert.ToBase64String(Encoding.UTF8.GetBytes("{}"));
        DeskPetHttp.HandlerFactory = () => new StubHandler(_ => Json(tampered));
        using (var client = new PetMarketClient(config))
            await ThrowsAsync(() => client.GetDownloadDescriptorAsync("secure-pet"),
                "A modified signed payload must be rejected.");

        var wrongIdentity = Sign(Payload(Guid.NewGuid().ToString(), DateTimeOffset.UtcNow.AddMinutes(9)), privateKey);
        DeskPetHttp.HandlerFactory = () => new StubHandler(_ => Json(wrongIdentity));
        using (var client = new PetMarketClient(config))
            await ThrowsAsync(() => client.GetDownloadDescriptorAsync("secure-pet"),
                "A descriptor for another account must be rejected.");

        var expiredPayload = new PetMarketUseGrantPayload
        {
            SchemaVersion = 1,
            Kind = "pet-market-use-grant",
            AccountId = config.AccountId,
            LicenseId = config.LicenseId,
            InstallationId = config.InstallationId,
            ProductId = "secure-pet",
            Version = "1.0.0",
            ManifestSha256 = new string('c', 64),
            IssuedAt = DateTimeOffset.UtcNow.AddMinutes(-11),
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1)
        };
        var expired = Sign(expiredPayload, privateKey);
        using (var client = new PetMarketClient(config))
        {
            var installation = new PetMarketInstallation
            {
                ProductId = expiredPayload.ProductId,
                Version = expiredPayload.Version,
                ManifestSha256 = expiredPayload.ManifestSha256,
                UseGrant = new PetMarketStoredGrant
                {
                    SignedPayload = expired.SignedPayload,
                    Sha256 = expired.Sha256,
                    SignatureAlgorithm = expired.SignatureAlgorithm,
                    Signature = expired.Signature
                }
            };
            Require(!client.HasValidStoredUseGrant(installation), "An expired stored use grant must not activate a market pet.");
        }
    }

    private static PetMarketDebugConfig LoadConfig(string root, byte[] publicKey)
    {
        var dataDirectory = Path.Combine(root, "client-data");
        var configPath = Path.Combine(root, "client.json");
        WriteConfig(configPath, dataDirectory, publicKey);
        return PetMarketDebugConfig.Load(configPath);
    }

    private static void CheckDebugDirectoryIsolation(string root, byte[] publicKey)
    {
        var production = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "poko-desktop-pet");
        var trailingConfig = Path.Combine(root, "production-trailing.json");
        WriteConfig(trailingConfig, production + Path.DirectorySeparatorChar, publicKey);
        Throws(() => PetMarketDebugConfig.Load(trailingConfig),
            "The production data directory with a trailing separator must be rejected.");

        var childConfig = Path.Combine(root, "production-child.json");
        WriteConfig(childConfig, Path.Combine(production, "market-debug"), publicKey);
        Throws(() => PetMarketDebugConfig.Load(childConfig),
            "A child of the production data directory must be rejected.");
    }

    private static void WriteConfig(string configPath, string dataDirectory, byte[] publicKey)
    {
        File.WriteAllText(configPath, JsonSerializer.Serialize(new
        {
            baseUrl = "http://127.0.0.1:18777/",
            dataDirectory,
            accountId = Guid.NewGuid(),
            licenseId = Guid.NewGuid(),
            installationId = "installation_1234567890",
            credential = "credential_123456789012345678901234567890",
            signingPublicKeySpki = Convert.ToBase64String(publicKey)
        }));
    }

    private static PetMarketSignedEnvelope Sign<T>(T value, Ed25519PrivateKeyParameters privateKey)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value);
        var signer = new Ed25519Signer();
        signer.Init(true, privateKey);
        signer.BlockUpdate(bytes, 0, bytes.Length);
        return new PetMarketSignedEnvelope
        {
            Payload = new { ignored = true },
            SignedPayload = Convert.ToBase64String(bytes),
            Sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
            SignatureAlgorithm = "ed25519",
            Signature = Convert.ToBase64String(signer.GenerateSignature())
        };
    }

    private static HttpResponseMessage Json<T>(T value) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json")
    };

    private static async Task ThrowsAsync(Func<Task> action, string message)
    {
        try
        {
            await action();
        }
        catch (InvalidOperationException)
        {
            return;
        }
        throw new InvalidOperationException(message);
    }

    private static void Throws(Action action, string message)
    {
        try
        {
            action();
        }
        catch (InvalidOperationException)
        {
            return;
        }
        throw new InvalidOperationException(message);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}

internal sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
        => Task.FromResult(responder(request));
}

internal static class DeskPetHttp
{
    internal static Func<HttpMessageHandler>? HandlerFactory { get; set; }
}

internal static class DeskPetApi
{
    internal const string BaseUrl = "https://deskpet.invalid";
}

internal static class UpdateService
{
    internal const string CurrentVersion = "0.0.0-check";
}

internal static class ServerSignatureTrust
{
    internal static bool Verify(byte[] payload, byte[] signature) => false;
}

internal sealed class LicenseService
{
    internal bool IsActivated => false;
    internal string AccountId => string.Empty;
    internal string LicenseId => string.Empty;
    internal string InstallationId => string.Empty;
    internal void Authorize(HttpRequestMessage request) => throw new NotSupportedException();
    internal bool AcceptSignedAccountId(string accountId) => false;
}
