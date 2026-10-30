using System.Text.Json.Serialization;

namespace ZhuoDazi.Models;

public sealed class PetMarketCatalog
{
    [JsonPropertyName("generatedAt")]
    public DateTimeOffset GeneratedAt { get; set; }

    [JsonPropertyName("defaultProductId")]
    public string? DefaultProductId { get; set; }

    [JsonPropertyName("payment")]
    public PetMarketPaymentInfo? Payment { get; set; }

    [JsonPropertyName("products")]
    public List<PetMarketProduct> Products { get; set; } = [];
}

public sealed class PetMarketProduct
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("chargingMode")]
    public string ChargingMode { get; set; } = string.Empty;

    [JsonPropertyName("priceFen")]
    public int PriceFen { get; set; }

    [JsonPropertyName("currency")]
    public string Currency { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("isDefault")]
    public bool IsDefault { get; set; }

    [JsonPropertyName("isFeatured")]
    public bool IsFeatured { get; set; }

    [JsonPropertyName("tags")]
    public List<string> Tags { get; set; } = [];

    [JsonPropertyName("sortOrder")]
    public int SortOrder { get; set; }

    [JsonPropertyName("activeVersion")]
    public string ActiveVersion { get; set; } = string.Empty;

    [JsonPropertyName("assetCount")]
    public int AssetCount { get; set; }

    [JsonPropertyName("previewMode")]
    public string PreviewMode { get; set; } = string.Empty;

    [JsonPropertyName("previewPaths")]
    public List<string> PreviewPaths { get; set; } = [];

    [JsonPropertyName("previewFiles")]
    public List<PetMarketPreviewFile> PreviewFiles { get; set; } = [];

    [JsonPropertyName("owned")]
    public bool Owned { get; set; }

    [JsonPropertyName("entitlementVersion")]
    public string? EntitlementVersion { get; set; }

    [JsonPropertyName("ownedVersion")]
    public string? OwnedVersion { get; set; }

    [JsonPropertyName("claimable")]
    public bool Claimable { get; set; }

    [JsonPropertyName("purchasable")]
    public bool Purchasable { get; set; }

    [JsonPropertyName("downloadable")]
    public bool Downloadable { get; set; }

    [JsonPropertyName("activationRequired")]
    public bool ActivationRequired { get; set; }

    [JsonIgnore]
    public bool IsFree => ChargingMode.Equals("free", StringComparison.OrdinalIgnoreCase);

    [JsonIgnore]
    public string PriceLabel => IsFree ? "免费" : $"¥{PriceFen / 100m:0.00}";

    [JsonIgnore]
    public string OwnershipLabel => Owned ? "已拥有" : IsFree ? "可免费领取" : PriceLabel;
}

public sealed class PetMarketPreviewFile
{
    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;

    [JsonPropertyName("sha256")]
    public string Sha256 { get; set; } = string.Empty;

    [JsonPropertyName("sizeBytes")]
    public long SizeBytes { get; set; }

    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;

    [JsonPropertyName("downloadPath")]
    public string DownloadPath { get; set; } = string.Empty;
}

public sealed class PetMarketPaymentInfo
{
    private const int MaxQrDataUrlLength = 768 * 1024;
    private const int MaxQrBytes = 512 * 1024;
    private const string PngPrefix = "data:image/png;base64,";
    private const string JpegPrefix = "data:image/jpeg;base64,";

    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; }

    [JsonPropertyName("available")]
    public bool Available { get; set; }

    [JsonPropertyName("method")]
    public string Method { get; set; } = string.Empty;

    [JsonPropertyName("recipientName")]
    public string RecipientName { get; set; } = string.Empty;

    [JsonPropertyName("recipientAccount")]
    public string RecipientAccount { get; set; } = string.Empty;

    [JsonPropertyName("qrCodeDataUrl")]
    public string QrCodeDataUrl { get; set; } = string.Empty;

    [JsonPropertyName("instructions")]
    public string Instructions { get; set; } = string.Empty;

    [JsonPropertyName("unavailableReason")]
    public string UnavailableReason { get; set; } = string.Empty;

    [JsonPropertyName("listenerOnline")]
    public bool ListenerOnline { get; set; }

    [JsonIgnore]
    public bool IsPersonalTransfer => string.Equals(Method, "personal_transfer", StringComparison.Ordinal);

    [JsonIgnore]
    public bool IsCollectionQr => string.Equals(Method, "collection_qr", StringComparison.Ordinal);

    [JsonIgnore]
    public bool HasRecipient => !string.IsNullOrWhiteSpace(RecipientName)
        && !string.IsNullOrWhiteSpace(RecipientAccount);

    [JsonIgnore]
    public bool CanCreateOrder => Enabled && Available && ListenerOnline
        && (IsPersonalTransfer && HasRecipient || IsCollectionQr && TryGetQrCodeBytes(out _));

    public bool TryGetQrCodeBytes(out byte[] bytes)
    {
        bytes = [];
        if (string.IsNullOrWhiteSpace(QrCodeDataUrl) || QrCodeDataUrl.Length > MaxQrDataUrlLength) return false;
        var isPng = QrCodeDataUrl.StartsWith(PngPrefix, StringComparison.Ordinal);
        var isJpeg = QrCodeDataUrl.StartsWith(JpegPrefix, StringComparison.Ordinal);
        var prefixLength = isPng ? PngPrefix.Length : isJpeg ? JpegPrefix.Length : 0;
        if (prefixLength == 0) return false;
        try
        {
            bytes = Convert.FromBase64String(QrCodeDataUrl[prefixLength..]);
            if (bytes.Length is <= 0 or > MaxQrBytes)
            {
                bytes = [];
                return false;
            }
            var structureValid = isPng
                ? bytes.Length >= 33
                    && bytes.AsSpan(0, 8).SequenceEqual(
                        new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })
                    && bytes.AsSpan(8, 8).SequenceEqual(
                        new byte[] { 0, 0, 0, 13, 73, 72, 68, 82 })
                    && bytes.AsSpan(bytes.Length - 12, 12).SequenceEqual(
                        new byte[] { 0, 0, 0, 0, 73, 69, 78, 68, 174, 66, 96, 130 })
                : bytes.Length >= 5 && bytes[0] == 0xff && bytes[1] == 0xd8 && bytes[2] == 0xff
                    && bytes[^2] == 0xff && bytes[^1] == 0xd9;
            if (structureValid) return true;
            bytes = [];
            return false;
        }
        catch (FormatException)
        {
            bytes = [];
            return false;
        }
    }
}

public sealed class PetMarketEntitlement
{
    [JsonPropertyName("productId")]
    public string ProductId { get; set; } = string.Empty;

    [JsonPropertyName("source")]
    public string Source { get; set; } = string.Empty;

    [JsonPropertyName("grantedAt")]
    public DateTimeOffset GrantedAt { get; set; }
}

public sealed class PetMarketClaimResponse
{
    [JsonPropertyName("entitlement")]
    public PetMarketEntitlement Entitlement { get; set; } = new();

    [JsonPropertyName("product")]
    public PetMarketProduct Product { get; set; } = new();
}

public sealed class PetMarketOrder
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("productId")]
    public string ProductId { get; set; } = string.Empty;

    [JsonPropertyName("productTitle")]
    public string ProductTitle { get; set; } = string.Empty;

    [JsonPropertyName("productVersion")]
    public string ProductVersion { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("amountFen")]
    public int AmountFen { get; set; }

    [JsonPropertyName("currency")]
    public string Currency { get; set; } = string.Empty;

    [JsonPropertyName("paymentReference")]
    public string PaymentReference { get; set; } = string.Empty;

    [JsonPropertyName("payment")]
    public PetMarketPaymentInfo? Payment { get; set; }

    [JsonPropertyName("createdAt")]
    public DateTimeOffset CreatedAt { get; set; }

    [JsonPropertyName("expiresAt")]
    public DateTimeOffset ExpiresAt { get; set; }

    [JsonPropertyName("paidAt")]
    public DateTimeOffset? PaidAt { get; set; }

    [JsonPropertyName("canceledAt")]
    public DateTimeOffset? CanceledAt { get; set; }
}

public sealed class PetMarketOrderResponse
{
    [JsonPropertyName("order")]
    public PetMarketOrder Order { get; set; } = new();

    [JsonPropertyName("reused")]
    public bool Reused { get; set; }

    [JsonPropertyName("serverTime")]
    public DateTimeOffset ServerTime { get; set; }
}

public sealed class PetMarketOrderListResponse
{
    [JsonPropertyName("orders")]
    public List<PetMarketOrder> Orders { get; set; } = [];

    [JsonPropertyName("serverTime")]
    public DateTimeOffset ServerTime { get; set; }

    [JsonPropertyName("nextCursor")]
    public string? NextCursor { get; set; }
}

public sealed class PetMarketSignedEnvelope
{
    [JsonPropertyName("payload")]
    public object? Payload { get; set; }

    [JsonPropertyName("signedPayload")]
    public string SignedPayload { get; set; } = string.Empty;

    [JsonPropertyName("sha256")]
    public string Sha256 { get; set; } = string.Empty;

    [JsonPropertyName("signatureAlgorithm")]
    public string SignatureAlgorithm { get; set; } = string.Empty;

    [JsonPropertyName("signature")]
    public string Signature { get; set; } = string.Empty;
}

public sealed class PetMarketDownloadPayload
{
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; set; }

    [JsonPropertyName("kind")]
    public string Kind { get; set; } = string.Empty;

    [JsonPropertyName("accountId")]
    public string AccountId { get; set; } = string.Empty;

    [JsonPropertyName("licenseId")]
    public string LicenseId { get; set; } = string.Empty;

    [JsonPropertyName("installationId")]
    public string InstallationId { get; set; } = string.Empty;

    [JsonPropertyName("productId")]
    public string ProductId { get; set; } = string.Empty;

    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;

    [JsonPropertyName("fileName")]
    public string FileName { get; set; } = string.Empty;

    [JsonPropertyName("sizeBytes")]
    public long SizeBytes { get; set; }

    [JsonPropertyName("sha256")]
    public string Sha256 { get; set; } = string.Empty;

    [JsonPropertyName("manifestSha256")]
    public string ManifestSha256 { get; set; } = string.Empty;

    [JsonPropertyName("downloadPath")]
    public string DownloadPath { get; set; } = string.Empty;

    [JsonPropertyName("issuedAt")]
    public DateTimeOffset IssuedAt { get; set; }

    [JsonPropertyName("expiresAt")]
    public DateTimeOffset ExpiresAt { get; set; }
}

public sealed class PetMarketUseGrantPayload
{
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; set; }

    [JsonPropertyName("kind")]
    public string Kind { get; set; } = string.Empty;

    [JsonPropertyName("accountId")]
    public string AccountId { get; set; } = string.Empty;

    [JsonPropertyName("licenseId")]
    public string LicenseId { get; set; } = string.Empty;

    [JsonPropertyName("installationId")]
    public string InstallationId { get; set; } = string.Empty;

    [JsonPropertyName("productId")]
    public string ProductId { get; set; } = string.Empty;

    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;

    [JsonPropertyName("manifestSha256")]
    public string ManifestSha256 { get; set; } = string.Empty;

    [JsonPropertyName("issuedAt")]
    public DateTimeOffset IssuedAt { get; set; }

    [JsonPropertyName("expiresAt")]
    public DateTimeOffset ExpiresAt { get; set; }
}

public sealed class PetMarketStoredGrant
{
    [JsonPropertyName("signedPayload")]
    public string SignedPayload { get; set; } = string.Empty;

    [JsonPropertyName("sha256")]
    public string Sha256 { get; set; } = string.Empty;

    [JsonPropertyName("signatureAlgorithm")]
    public string SignatureAlgorithm { get; set; } = string.Empty;

    [JsonPropertyName("signature")]
    public string Signature { get; set; } = string.Empty;
}

public sealed class PetMarketInstallation
{
    [JsonPropertyName("productId")]
    public string ProductId { get; set; } = string.Empty;

    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("directory")]
    public string Directory { get; set; } = string.Empty;

    [JsonPropertyName("files")]
    public List<string> Files { get; set; } = [];

    [JsonPropertyName("manifestSha256")]
    public string ManifestSha256 { get; set; } = string.Empty;

    [JsonPropertyName("installedAt")]
    public DateTimeOffset InstalledAt { get; set; }

    [JsonPropertyName("useGrant")]
    public PetMarketStoredGrant? UseGrant { get; set; }
}
