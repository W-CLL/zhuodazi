using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using ZhuoDazi.Models;

namespace ZhuoDazi.Services;

internal static partial class ContentEnvelopeVerifier
{
    private const int MaximumSignedPayloadBytes = 16 * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static SignedContentPayload Validate(
        SignedContentEnvelope envelope,
        string expectedKind,
        byte[]? publicKey = null)
    {
        if (!string.Equals(envelope.SignatureAlgorithm, "ed25519", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("互动内容签名算法无效。");
        if (string.IsNullOrEmpty(envelope.SignedPayload)
            || envelope.SignedPayload.Length > MaximumSignedPayloadBytes * 2)
            throw new InvalidOperationException("互动内容签名原文无效。");
        if (string.IsNullOrEmpty(envelope.Signature))
            throw new InvalidOperationException("互动内容签名格式无效。");

        byte[] payloadBytes;
        byte[] signature;
        try
        {
            payloadBytes = Convert.FromBase64String(envelope.SignedPayload);
            signature = Convert.FromBase64String(envelope.Signature);
        }
        catch (FormatException error)
        {
            throw new InvalidOperationException("互动内容签名格式无效。", error);
        }
        if (payloadBytes.Length == 0 || payloadBytes.Length > MaximumSignedPayloadBytes)
            throw new InvalidOperationException("互动内容签名原文无效。");

        var actualHash = Convert.ToHexString(SHA256.HashData(payloadBytes)).ToLowerInvariant();
        if (envelope.Sha256?.Length != 64
            || !actualHash.Equals(envelope.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("互动内容 SHA-256 校验失败。");
        var verified = publicKey is null
            ? ServerSignatureTrust.Verify(payloadBytes, signature)
            : Ed25519SignatureVerifier.Verify(publicKey, payloadBytes, signature);
        if (!verified) throw new InvalidOperationException("互动内容签名验证失败。");

        SignedContentPayload payload;
        try
        {
            payload = JsonSerializer.Deserialize<SignedContentPayload>(payloadBytes, JsonOptions)
                ?? throw new JsonException();
        }
        catch (JsonException error)
        {
            throw new InvalidOperationException("互动内容签名原文无法解析。", error);
        }
        ValidatePayload(payload, expectedKind);
        return payload;
    }

    internal static bool IsValidItem(InteractionContentItem? item)
    {
        if (item is null || !IsValidItemId(item.Id)
            || item.Type is not ("joke" or "math" or "trivia")
            || item.Revision < 1 || item.Prompt is null || item.Prompt.Length is < 2 or > 500
            || item.Answer is null || item.Answer.Length is < 1 or > 500
            || item.Explanation is null || item.Explanation.Length > 1000
            || item.Difficulty is < 1 or > 5 || item.Locale is null || !LocalePattern().IsMatch(item.Locale)
            || item.Choices is null || item.Tags is null
            || item.Choices.Count > 6 || item.Tags.Count > 10)
            return false;
        if (item.Choices.Any(value => string.IsNullOrWhiteSpace(value) || value.Length > 200)
            || item.Tags.Any(value => string.IsNullOrWhiteSpace(value) || value.Length > 30))
            return false;
        return item.Choices.Count == 0
            || item.Choices.Count >= 2 && item.Choices.Contains(item.Answer, StringComparer.Ordinal);
    }

    internal static bool IsValidItemId(string? id)
        => id is not null && ContentIdPattern().IsMatch(id);

    private static void ValidatePayload(SignedContentPayload payload, string expectedKind)
    {
        if (payload.SchemaVersion != 1 || !string.Equals(payload.Kind, expectedKind, StringComparison.Ordinal)
            || payload.CatalogVersion < 0 || !DateTimeOffset.TryParse(payload.CatalogUpdatedAt, out _)
            || payload.Items is null || payload.DisabledIds is null
            || payload.Items.Count > 5000 || payload.DisabledIds.Count > 5000)
            throw new InvalidOperationException("互动内容目录格式无效。");
        if (payload.Items.Any(item => !IsValidItem(item))
            || payload.Items.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count() != payload.Items.Count
            || payload.DisabledIds.Any(id => !IsValidItemId(id))
            || payload.DisabledIds.Distinct(StringComparer.Ordinal).Count() != payload.DisabledIds.Count)
            throw new InvalidOperationException("互动内容条目格式无效。");
    }

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex ContentIdPattern();

    [GeneratedRegex("^[A-Za-z]{2,3}(?:-[A-Za-z0-9]{2,8})*$", RegexOptions.CultureInvariant)]
    private static partial Regex LocalePattern();
}
