// CONTRACT (win-lead owns this folder). Implemented by Recorder.Security (win-sec).
namespace Recorder.Core.Abstractions;

/// <summary>Encryption at rest for sensitive working files: AES-GCM with a per-session key wrapped by DPAPI (CurrentUser).</summary>
public interface IFileProtector
{
    /// <summary>Create or load the session key for <paramref name="sessionDirectory"/>.</summary>
    Task EnsureSessionKeyAsync(string sessionDirectory, CancellationToken cancellationToken);

    Stream OpenEncryptedWrite(string sessionDirectory, string path);

    Stream OpenDecryptedRead(string sessionDirectory, string path);

    /// <summary>HMAC-SHA256 over the file with the session key (tamper detection).</summary>
    Task<string> ComputeMacAsync(string sessionDirectory, string path, CancellationToken cancellationToken);

    /// <summary>Restrict the folder ACL to the current user (and SYSTEM).</summary>
    void RestrictToCurrentUser(string directory);
}

public sealed record RetentionCandidate(string SessionDirectory, DateTimeOffset ExpiresAt);

public interface IRetentionService
{
    IReadOnlyList<RetentionCandidate> FindExpired(string dataDirectory, int retentionDays, DateTimeOffset now);

    /// <summary>Delete a session folder: overwrite then delete files (best effort; SSD caveat documented in docs/privacy).</summary>
    Task DeleteSessionAsync(string sessionDirectory, CancellationToken cancellationToken);
}
