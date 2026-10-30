using System.Globalization;
using System.Text.Json;
using Recorder.Core.Abstractions;

namespace Recorder.Security;

public sealed class RetentionService : IRetentionService
{
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

            if (!TryReadRetention(manifestPath, out var dueAt, out var deleteAfterDays, out var createdAt))
            {
                continue;
            }

            DateTimeOffset? effectiveDue = dueAt;
            if (effectiveDue is null && deleteAfterDays is not null)
            {
                effectiveDue = createdAt.AddDays(deleteAfterDays.Value);
            }
            else if (effectiveDue is null && retentionDays > 0)
            {
                effectiveDue = createdAt.AddDays(retentionDays);
            }

            if (effectiveDue is not null && effectiveDue <= now)
            {
                results.Add(new RetentionCandidate(sessionDirectory, effectiveDue.Value));
            }
        }

        return results;
    }

    public async Task DeleteSessionAsync(string sessionDirectory, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(sessionDirectory);
        if (!Directory.Exists(sessionDirectory))
        {
            return;
        }

        // Secure overwrite is best-effort only; SSDs and wear-leveling cannot guarantee data destruction.
        foreach (var file in Directory.EnumerateFiles(sessionDirectory, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await OverwriteWithZerosAsync(file, cancellationToken).ConfigureAwait(false);
            File.Delete(file);
        }

        Directory.Delete(sessionDirectory, recursive: true);
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
    }

    private static bool TryReadRetention(
        string manifestPath,
        out DateTimeOffset? dueAt,
        out int? deleteAfterDays,
        out DateTimeOffset createdAt)
    {
        dueAt = null;
        deleteAfterDays = null;
        createdAt = default;
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
            var root = document.RootElement;
            if (!root.TryGetProperty("createdAt", out var createdAtElement) ||
                createdAtElement.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            createdAt = DateTimeOffset.Parse(createdAtElement.GetString()!, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);

            if (!root.TryGetProperty("retention", out var retention))
            {
                return true;
            }

            if (retention.TryGetProperty("dueAt", out var dueAtElement) && dueAtElement.ValueKind == JsonValueKind.String)
            {
                dueAt = DateTimeOffset.Parse(dueAtElement.GetString()!, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);
            }

            if (retention.TryGetProperty("deleteAfterDays", out var daysElement) &&
                daysElement.ValueKind == JsonValueKind.Number &&
                daysElement.TryGetInt32(out var days))
            {
                deleteAfterDays = days;
            }

            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
