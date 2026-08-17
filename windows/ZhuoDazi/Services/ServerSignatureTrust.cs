namespace ZhuoDazi.Services;

internal static class ServerSignatureTrust
{
    private const string PublicKeySpki = DeskPetApi.SigningPublicKeySpki;
    private static readonly byte[] PublicKey = Convert.FromBase64String(PublicKeySpki)[^32..];

    public static bool Verify(byte[] payload, byte[] signature)
        => Ed25519SignatureVerifier.Verify(PublicKey, payload, signature);
}
