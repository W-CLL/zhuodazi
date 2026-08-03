namespace ZhuoDazi.Services;

internal static class ServerSignatureTrust
{
    private const string PublicKeySpki = "MCowBQYDK2VwAyEANjBEMMQ5TY+0ECNoRqQy9780eoVOzkKpzFDq2TwLytU=";
    private static readonly byte[] PublicKey = Convert.FromBase64String(PublicKeySpki)[^32..];

    public static bool Verify(byte[] payload, byte[] signature)
        => Ed25519SignatureVerifier.Verify(PublicKey, payload, signature);
}
