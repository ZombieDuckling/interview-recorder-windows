using System.Security.Cryptography;

namespace Recorder.Security;

internal static class KeyDerivation
{
    /// <summary>HMAC key derived from the session key. The caller must zero the result.</summary>
    internal static byte[] DeriveHmacKey(byte[] sessionKey)
    {
        return HKDF.DeriveKey(
            HashAlgorithmName.SHA256,
            sessionKey,
            CryptoConstants.SessionKeyBytes,
            salt: null,
            info: CryptoConstants.HmacInfoLabel.ToArray());
    }

    /// <summary>Per-file AES-GCM key: HKDF(session key, random header salt). The caller must zero the result.</summary>
    internal static byte[] DeriveFileKey(byte[] sessionKey, byte[] salt)
    {
        return HKDF.DeriveKey(
            HashAlgorithmName.SHA256,
            sessionKey,
            CryptoConstants.SessionKeyBytes,
            salt,
            info: CryptoConstants.FileKeyInfoLabel.ToArray());
    }
}
