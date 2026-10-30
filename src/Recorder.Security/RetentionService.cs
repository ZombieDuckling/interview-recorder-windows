using System.Globalization;
using System.Text.Json;
using Recorder.Core.Abstractions;

namespace Recorder.Security;

public sealed class RetentionService : IRetentionService
{
    private readonly string? _dataRoot;

    public RetentionService()
    {
    }

    /// <param name="dataRoot">When set, <see cref="DeleteSessionAsync"/> only deletes folders directly under it.</param>
    public RetentionService(string dataRoot)
    {
        ArgumentException.ThrowIfNullOrEmpty(dataRoot);
        _dataRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(dataRoot));
    }

    public IReadOnlyList<RetentionCandidate> FindExpired(string dataDirectory, int retentionDays, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrEmpty(dataDirectory);
        if (!Directory.Exists(dataDirectory))
        {
            return [];
        }

        var results = new List<RetentionCandidate>();
        foreach (var sessionDirectory in Directory.EnumerateDirectories(dataDirectory))
        {
            var manifestPath = Path.Combine(sessionDirectory, "session.json");
            if (!File.Exists(manifestPath))
            {
                continue;
            }

            if (!TryReadRetention(manifestPath, out var retention, out var createdAt))
            {
                // Unreadable or corrupt manifest: skip this session only; the caller logs a code, never content.
                continue;
            }

            DateTimeOffset? effectiveDue;
            if (retention is null)
            {
                // Pre-spec manifest without a retention object: fall back to the global setting.
                effectiveDue = retentionDays > 0 ? SafeAddDays(createdAt, retentionDays) : null;
            }
            else if (retention.DueAt is not null)
            {
                effectiveDue = retention.DueAt;
            }
            else if (retention.DeleteAfterDays is not null)
            {
                effectiveDue = SafeAddDays(createdAt, retention.DeleteAfterDays.Value);
            }
            else
            {
                // Both null = keep (spec section 9): never expires.
                effectiveDue = null;
            }

            if (effectiveDue is not null && effectiveDue <= now)
            {
                results.Add(new RetentionCandidate(sessionDirectory, effectiveDue.Value));
            }
        }

        return results;
    }

    private static DateTimeOffset? SafeAddDays(DateTimeOffset start, int days)
    {
        if (days < 0)
        {
            return null;
        }

        try
        {
            return start.AddDays(days);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    /// <summary>
    /// Overwrites then deletes every file. Best effort only: on SSDs and with wear levelling the overwrite
    /// does not guarantee the old blocks are gone. What makes encrypted content unreadable is destroying
    /// the DPAPI-wrapped session key (.keys/session.key), which is deleted with the folder.
    /// </summary>
    public async Task DeleteSessionAsync(string sessionDirectory, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(sessionDirectory);
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(sessionDirectory));
        if (!Directory.Exists(root))
        {
            return;
        }

        if (!File.Exists(Path.Combine(root, "session.json")))
        {
            throw new InvalidOperationException("Refusing to delete: the folder is not a session (no session.json).");
        }

        if (_dataRoot is not null)
        {
            var parent = Path.GetDirectoryName(root);
            if (parent is null || !string.Equals(parent, _dataRoot, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Refusing to delete: the session is not directly under the data folder.");
            }
        }

        // Scan first so a junction or symlink anywhere inside aborts before anything is touched.
        var files = new List<string>();
        CollectFiles(root, files);

        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            File.SetAttributes(file, File.GetAttributes(file) & ~FileAttributes.ReadOnly);
            await OverwriteWithZerosAsync(file, cancellationToken).ConfigureAwait(false);
            File.Delete(file);
        }

        Directory.Delete(root, recursive: true);
    }

    private static void CollectFiles(string directory, List<string> files)
    {
        if (new DirectoryInfo(directory).Attributes.HasFlag(FileAttributes.ReparsePoint))
        {
            throw new InvalidOperationException("Refusing to delete: the session contains a reparse point (junction or symlink).");
        }

        foreach (var entry in new DirectoryInfo(directory).EnumerateFileSystemInfos())
        {
            if (entry.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                throw new InvalidOperationException("Refusing to delete: the session contains a reparse point (junction or symlink).");
            }

            if (entry is DirectoryInfo)
            {
                CollectFiles(entry.FullName, files);
            }
            else
            {
                files.Add(entry.FullName);
            }
        }
    }

    private static async Task OverwriteWithZerosAsync(string path, CancellationToken cancellationToken)
    {
        var length = new FileInfo(path).Length;
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Write,
            FileShare.None,
            bufferSize: CryptoConstants.FramePlaintextBytes,
            FileOptions.Asynchronous);

        var zeros = new byte[CryptoConstants.FramePlaintextBytes];
        var remaining = length;
        while (remaining > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var write = (int)Math.Min(zeros.Length, remaining);
            await stream.WriteAsync(zeros.AsMemory(0, write), cancellationToken).ConfigureAwait(false);
            remaining -= write;
        }

        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        stream.Flush(flushToDisk: true);
    }

    private sealed record RetentionInfo(DateTimeOffset? DueAt, int? DeleteAfterDays);

    private static bool TryParseDate(JsonElement element, out DateTimeOffset value)
    {
        value = default;
        return element.ValueKind == JsonValueKind.String &&
               DateTimeOffset.TryParse(element.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out value);
    }

    /// <summary>False when the manifest is unreadable or corrupt; <paramref name="retention"/> is null when the object is absent.</summary>
    private static bool TryReadRetention(string manifestPath, out RetentionInfo? retention, out DateTimeOffset createdAt)
    {
        retention = null;
        createdAt = default;
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("createdAt", out var createdAtElement) ||
                !TryParseDate(createdAtElement, out createdAt))
            {
                return false;
            }

            if (!root.TryGetProperty("retention", out var element) || element.ValueKind == JsonValueKind.Null)
            {
                return true;
            }

            if (element.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            DateTimeOffset? dueAt = null;
            if (element.TryGetProperty("dueAt", out var dueAtElement) && dueAtElement.ValueKind != JsonValueKind.Null)
            {
                if (!TryParseDate(dueAtElement, out var due))
                {
                    return false;
                }

                dueAt = due;
            }

            int? deleteAfterDays = null;
            if (element.TryGetProperty("deleteAfterDays", out var daysElement) && daysElement.ValueKind != JsonValueKind.Null)
            {
                if (daysElement.ValueKind != JsonValueKind.Number || !daysElement.TryGetInt32(out var days))
                {
                    return false;
                }

                deleteAfterDays = days;
            }

            retention = new RetentionInfo(dueAt, deleteAfterDays);
            return true;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or FormatException or ArgumentOutOfRangeException)
        {
            return false;
        }
    }
}
