using System.Security.Cryptography;

namespace Recorder.Security;

internal static class KeyDerivation
{
    internal static byte[] DeriveHmacKey(ReadOnlySpan<byte> sessionKey)
    {
        return HKDF.DeriveKey(
            HashAlgorithmName.SHA256,
            sessionKey.ToArray(),
            CryptoConstants.SessionKeyBytes,
            salt: null,
            info: CryptoConstants.HmacInfoLabel.ToArray());
    }
}
