using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Security.AccessControl;
using System.Security.Principal;
using Recorder.Core.Abstractions;

namespace Recorder.Security;

public sealed class FileProtector : IFileProtector
{
    private readonly ConcurrentDictionary<string, byte[]> _sessionKeys = new(StringComparer.OrdinalIgnoreCase);

    public async Task EnsureSessionKeyAsync(string sessionDirectory, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(sessionDirectory);
        var key = await SessionKeyStore.EnsureKeyAsync(sessionDirectory, cancellationToken).ConfigureAwait(false);
        _sessionKeys[NormalizeDirectory(sessionDirectory)] = key;
    }

    public Stream OpenEncryptedWrite(string sessionDirectory, string path)
    {
        var key = GetSessionKey(sessionDirectory);
        var fullPath = ResolvePath(sessionDirectory, path, out var context);
        return new EncryptedFrameWriteStream(fullPath, key, context);
    }

    public Stream OpenDecryptedRead(string sessionDirectory, string path)
    {
        var key = GetSessionKey(sessionDirectory);
        var fullPath = ResolvePath(sessionDirectory, path, out var context);
        return new EncryptedFrameReadStream(fullPath, key, context);
    }

    public async Task<string> ComputeMacAsync(string sessionDirectory, string path, CancellationToken cancellationToken)
    {
        var mac = await ComputeMacBytesAsync(sessionDirectory, path, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(mac).ToLowerInvariant();
    }

    /// <summary>Constant-time comparison of the file's MAC against <paramref name="expectedHex"/>. Callers must use this, never <c>==</c>.</summary>
    public async Task<bool> VerifyMacAsync(string sessionDirectory, string path, string expectedHex, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(expectedHex);
        var actual = await ComputeMacBytesAsync(sessionDirectory, path, cancellationToken).ConfigureAwait(false);
        try
        {
            if (expectedHex.Length != actual.Length * 2)
            {
                return false;
            }

            byte[] expected;
            try
            {
                expected = Convert.FromHexString(expectedHex);
            }
            catch (FormatException)
            {
                return false;
            }

            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(actual);
        }
    }

    private async Task<byte[]> ComputeMacBytesAsync(string sessionDirectory, string path, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(sessionDirectory);
        ArgumentException.ThrowIfNullOrEmpty(path);

        var key = GetSessionKey(sessionDirectory);
        var fullPath = ResolvePath(sessionDirectory, path, out _);
        var hmacKey = KeyDerivation.DeriveHmacKey(key);
        try
        {
            using var hmac = new HMACSHA256(hmacKey);
            await using var stream = new FileStream(
                fullPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: CryptoConstants.FramePlaintextBytes,
                FileOptions.Asynchronous | FileOptions.SequentialScan);

            var buffer = new byte[CryptoConstants.FramePlaintextBytes];
            int read;
            while ((read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            {
                hmac.TransformBlock(buffer, 0, read, null, 0);
            }

            hmac.TransformFinalBlock([], 0, 0);
            return (byte[])hmac.Hash!.Clone();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(hmacKey);
        }
    }

    public void RestrictToCurrentUser(string directory)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Directory ACL restriction is supported on Windows only.");
        }

        ArgumentException.ThrowIfNullOrEmpty(directory);
        Directory.CreateDirectory(directory);

        var dirInfo = new DirectoryInfo(directory);
        var security = dirInfo.GetAccessControl(AccessControlSections.Access);
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);

        var userSid = WindowsIdentity.GetCurrent().User
            ?? throw new InvalidOperationException("Cannot restrict the folder: the current user SID is unavailable.");

        var existing = security.GetAccessRules(includeExplicit: true, includeInherited: true, typeof(SecurityIdentifier))
            .Cast<FileSystemAccessRule>()
            .Select(r => (SecurityIdentifier)r.IdentityReference)
            .Distinct()
            .ToList();
        foreach (var sid in existing)
        {
            security.PurgeAccessRules(sid);
        }

        security.AddAccessRule(new FileSystemAccessRule(
            userSid,
            FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None,
            AccessControlType.Allow));

        var systemSid = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
        security.AddAccessRule(new FileSystemAccessRule(
            systemSid,
            FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None,
            AccessControlType.Allow));

        dirInfo.SetAccessControl(security);
    }

    private byte[] GetSessionKey(string sessionDirectory)
    {
        ArgumentException.ThrowIfNullOrEmpty(sessionDirectory);
        if (_sessionKeys.TryGetValue(NormalizeDirectory(sessionDirectory), out var key))
        {
            return key;
        }

        throw new InvalidOperationException("Call EnsureSessionKeyAsync before using the session key.");
    }

    private static string NormalizeDirectory(string sessionDirectory) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(sessionDirectory));

    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    /// <summary>Resolve <paramref name="path"/> inside the session folder; rejects anything outside it. <paramref name="context"/> is the session-relative path bound into the AAD.</summary>
    private static string ResolvePath(string sessionDirectory, string path, out string context)
    {
        ArgumentException.ThrowIfNullOrEmpty(sessionDirectory);
        ArgumentException.ThrowIfNullOrEmpty(path);

        var root = NormalizeDirectory(sessionDirectory);
        var fullPath = Path.GetFullPath(Path.Combine(root, path));
        var prefix = root + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(prefix, PathComparison))
        {
            throw new ArgumentException("Path is outside the session folder.", nameof(path));
        }

        context = fullPath[prefix.Length..].Replace('\\', '/').ToLowerInvariant();
        return fullPath;
    }
}
