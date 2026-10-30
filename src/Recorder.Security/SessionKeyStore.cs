using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace Recorder.Security;

/// <summary>Wraps a session key for storage on disk. Production uses DPAPI (CurrentUser) only.</summary>
internal interface ISessionKeyWrapper
{
    byte[] Wrap(byte[] key);

    byte[] Unwrap(byte[] blob);
}

internal sealed class DpapiKeyWrapper : ISessionKeyWrapper
{
    public byte[] Wrap(byte[] key)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Session keys are protected with Windows DPAPI; this platform is not supported.");
        }

        return ProtectedData.Protect(key, CryptoConstants.DpapiEntropy.ToArray(), DataProtectionScope.CurrentUser);
    }

    public byte[] Unwrap(byte[] blob)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Session keys are protected with Windows DPAPI; this platform is not supported.");
        }

        return ProtectedData.Unprotect(blob, CryptoConstants.DpapiEntropy.ToArray(), DataProtectionScope.CurrentUser);
    }
}

internal static class SessionKeyStore
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Locks = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Test seam (visible to the test assembly only). Release code never contains a plaintext wrapper;
    /// tests running off Windows install their own.
    /// </summary>
    internal static ISessionKeyWrapper Wrapper { get; set; } = new DpapiKeyWrapper();

    internal static async Task<byte[]> EnsureKeyAsync(string sessionDirectory, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(sessionDirectory);

        var keyPath = CryptoConstants.SessionKeyPath(sessionDirectory);
        var gate = Locks.GetOrAdd(Path.GetFullPath(keyPath), static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var keysDir = Path.GetDirectoryName(keyPath)!;
            Directory.CreateDirectory(keysDir);
            SetHidden(keysDir);

            if (File.Exists(keyPath))
            {
                return await LoadKeyAsync(keyPath, cancellationToken).ConfigureAwait(false);
            }

            var key = RandomNumberGenerator.GetBytes(CryptoConstants.SessionKeyBytes);
            if (await TryCreateKeyFileAsync(keyPath, key, cancellationToken).ConfigureAwait(false))
            {
                return key;
            }

            // Another process created it first: use the winner's key.
            CryptographicOperations.ZeroMemory(key);
            return await LoadKeyAsync(keyPath, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
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
        var blob = await File.ReadAllBytesAsync(keyPath, cancellationToken).ConfigureAwait(false);
        var key = Wrapper.Unwrap(blob);
        if (key.Length != CryptoConstants.SessionKeyBytes)
        {
            CryptographicOperations.ZeroMemory(key);
            throw new CryptographicException("Invalid session key length.");
        }

        return key;
    }

    /// <summary>Write to a temp file, flush to disk, then move into place without overwriting. False if the target already exists.</summary>
    private static async Task<bool> TryCreateKeyFileAsync(string keyPath, byte[] key, CancellationToken cancellationToken)
    {
        var blob = Wrapper.Wrap(key);
        var tempPath = Path.Combine(Path.GetDirectoryName(keyPath)!, ".session.key.tmp-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(8)));
        try
        {
            await using (var stream = new FileStream(
                tempPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(blob, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }

            SetHidden(tempPath);
            try
            {
                File.Move(tempPath, keyPath, overwrite: false);
                return true;
            }
            catch (IOException) when (File.Exists(keyPath))
            {
                return false;
            }
        }
        finally
        {
            try
            {
                File.Delete(tempPath);
            }
            catch (IOException)
            {
                // Best effort; the temp file holds only the wrapped key.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
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
        catch (UnauthorizedAccessException)
        {
        }
    }
}
