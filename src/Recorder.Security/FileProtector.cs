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
        _sessionKeys[sessionDirectory] = key;
    }

    public Stream OpenEncryptedWrite(string sessionDirectory, string path)
    {
        var key = GetSessionKey(sessionDirectory);
        var fullPath = ResolvePath(sessionDirectory, path);
        return new EncryptedFrameWriteStream(fullPath, key);
    }

    public Stream OpenDecryptedRead(string sessionDirectory, string path)
    {
        var key = GetSessionKey(sessionDirectory);
        var fullPath = ResolvePath(sessionDirectory, path);
        return new EncryptedFrameReadStream(fullPath, key);
    }

    public async Task<string> ComputeMacAsync(string sessionDirectory, string path, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(sessionDirectory);
        ArgumentException.ThrowIfNullOrEmpty(path);

        var key = GetSessionKey(sessionDirectory);
        var hmacKey = KeyDerivation.DeriveHmacKey(key);
        var fullPath = ResolvePath(sessionDirectory, path);

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
        return Convert.ToHexString(hmac.Hash!).ToLowerInvariant();
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

        foreach (FileSystemAccessRule rule in security.GetAccessRules(includeExplicit: true, includeInherited: true, typeof(SecurityIdentifier)))
        {
            security.RemoveAccessRule(rule);
        }

        if (WindowsIdentity.GetCurrent().User is { } userSid)
        {
            security.AddAccessRule(new FileSystemAccessRule(
                userSid,
                FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None,
                AccessControlType.Allow));
        }

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
        if (_sessionKeys.TryGetValue(sessionDirectory, out var key))
        {
            return key;
        }

        throw new InvalidOperationException("Call EnsureSessionKeyAsync before using the session key.");
    }

    private static string ResolvePath(string sessionDirectory, string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        return Path.IsPathRooted(path) ? path : Path.Combine(sessionDirectory, path);
    }
}
