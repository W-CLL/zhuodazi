using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;

namespace ZhuoDazi.Services;

internal static class Ed25519SignatureVerifier
{
    public static bool Verify(byte[] publicKey, byte[] payload, byte[] signature)
    {
        if (publicKey.Length != Ed25519PublicKeyParameters.KeySize || signature.Length != 64)
            return false;

        var verifier = new Ed25519Signer();
        verifier.Init(false, new Ed25519PublicKeyParameters(publicKey));
        verifier.BlockUpdate(payload, 0, payload.Length);
        return verifier.VerifySignature(signature);
    }
}
