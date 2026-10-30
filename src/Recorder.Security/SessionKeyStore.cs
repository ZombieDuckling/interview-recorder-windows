using System.Security.Cryptography;

namespace Recorder.Security;

internal static class SessionKeyStore
{
    private const byte PlaintextDevMarker = 0xFE;

    internal static async Task<byte[]> EnsureKeyAsync(string sessionDirectory, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(sessionDirectory);

        var keyPath = CryptoConstants.SessionKeyPath(sessionDirectory);
        var keysDir = Path.GetDirectoryName(keyPath)!;
        Directory.CreateDirectory(keysDir);
        SetHidden(keysDir);

        if (File.Exists(keyPath))
        {
            return await LoadKeyAsync(keyPath, cancellationToken).ConfigureAwait(false);
        }

        var key = RandomNumberGenerator.GetBytes(CryptoConstants.SessionKeyBytes);
        await SaveKeyAsync(keyPath, key, cancellationToken).ConfigureAwait(false);
        return key;
    }

    internal static async Task<byte[]> LoadExistingKeyAsync(string sessionDirectory, CancellationToken cancellationToken)
    {
        var keyPath = CryptoConstants.SessionKeyPath(sessionDirectory);
        if (!File.Exists(keyPath))
        {
            throw new InvalidOperationException("Session key is missing.");
        }

        return await LoadKeyAsync(keyPath, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<byte[]> LoadKeyAsync(string keyPath, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            keyPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 4096,
            FileOptions.Asynchronous);
        var blob = new byte[stream.Length];
        var read = 0;
        while (read < blob.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var n = await stream.ReadAsync(blob.AsMemory(read, blob.Length - read), cancellationToken)
                .ConfigureAwait(false);
            if (n == 0)
            {
                throw new InvalidOperationException("Unexpected end of session key file.");
            }

            read += n;
        }

        return UnwrapKey(blob);
    }

    private static async Task SaveKeyAsync(string keyPath, byte[] key, CancellationToken cancellationToken)
    {
        if (key.Length != CryptoConstants.SessionKeyBytes)
        {
            throw new ArgumentException("Invalid session key length.", nameof(key));
        }

        var blob = WrapKey(key);
        await using var stream = new FileStream(
            keyPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 4096,
            FileOptions.Asynchronous);
        await stream.WriteAsync(blob, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        SetHidden(keyPath);
    }

    private static byte[] WrapKey(byte[] key)
    {
        if (OperatingSystem.IsWindows())
        {
            return ProtectedData.Protect(
                key,
                CryptoConstants.DpapiEntropy.ToArray(),
                DataProtectionScope.CurrentUser);
        }

        var blob = new byte[1 + key.Length];
        blob[0] = PlaintextDevMarker;
        key.CopyTo(blob.AsSpan(1));
        return blob;
    }

    private static byte[] UnwrapKey(byte[] blob)
    {
        if (OperatingSystem.IsWindows())
        {
            return ProtectedData.Unprotect(
                blob,
                CryptoConstants.DpapiEntropy.ToArray(),
                DataProtectionScope.CurrentUser);
        }

        if (blob.Length != 1 + CryptoConstants.SessionKeyBytes || blob[0] != PlaintextDevMarker)
        {
            throw new CryptographicException("Invalid session key envelope for this platform.");
        }

        var key = new byte[CryptoConstants.SessionKeyBytes];
        blob.AsSpan(1).CopyTo(key);
        return key;
    }

    private static void SetHidden(string path)
    {
        try
        {
            var attrs = File.GetAttributes(path);
            File.SetAttributes(path, attrs | FileAttributes.Hidden);
        }
        catch (IOException)
        {
            // Best effort (e.g. some Unix file systems ignore Hidden).
        }
    }
}
