using System.Text;
using System.Text.Json;
using Recorder.Core.Formats;
using Recorder.Core.IO;

namespace Recorder.Core.SessionManagement;

public sealed record SessionLibraryEntry(
    string SessionCode,
    string FolderPath,
    SessionState? State,
    bool Damaged,
    bool NeedsRecovery,
    bool UnsupportedSchema = false);

public static class SessionIntegrity
{
    public static string ComputeHashesCanonicalJson(IReadOnlyDictionary<string, string> hashes)
    {
        var ordered = hashes.OrderBy(static pair => pair.Key, StringComparer.Ordinal);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            foreach (var (key, value) in ordered)
            {
                writer.WriteString(key, value);
            }

            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }
}

public static class SessionRetentionLogic
{
    public static DateTimeOffset? ComputeDueAt(DateTimeOffset createdAtUtc, int? deleteAfterDays)
    {
        if (deleteAfterDays is null)
        {
            return null;
        }

        return createdAtUtc.AddDays(deleteAfterDays.Value);
    }
}

public static class SessionAudit
{
    public static void Append(SessionManifest manifest, SessionAuditEvent auditEvent, DateTimeOffset atUtc)
    {
        manifest.Audit.Add(new SessionAuditEntry { Event = auditEvent, At = atUtc });
    }
}

public static class SessionLibrary
{
    private static readonly HashSet<SessionState> RecoveryStates =
    [
        SessionState.Recording,
        SessionState.Finalizing,
        SessionState.Transcribing,
    ];

    public static IReadOnlyList<SessionLibraryEntry> List(string dataRoot)
    {
        if (!Directory.Exists(dataRoot))
        {
            return [];
        }

        var entries = new List<SessionLibraryEntry>();
        foreach (var directory in Directory.EnumerateDirectories(dataRoot))
        {
            var sessionPath = Path.Combine(directory, "session.json");
            if (!File.Exists(sessionPath))
            {
                continue;
            }

            try
            {
                var manifest = SessionManifestIO.Read(sessionPath);
                entries.Add(new SessionLibraryEntry(
                    manifest.SessionCode,
                    directory,
                    manifest.State,
                    Damaged: false,
                    NeedsRecovery: RecoveryStates.Contains(manifest.State)));
            }
            catch (UnsupportedSchemaVersionException)
            {
                entries.Add(new SessionLibraryEntry(
                    Path.GetFileName(directory),
                    directory,
                    State: null,
                    Damaged: false,
                    NeedsRecovery: false,
                    UnsupportedSchema: true));
            }
            catch (CorruptedFileException)
            {
                entries.Add(new SessionLibraryEntry(
                    Path.GetFileName(directory),
                    directory,
                    State: null,
                    Damaged: true,
                    NeedsRecovery: false));
            }
        }

        return entries.OrderByDescending(static e => e.SessionCode, StringComparer.Ordinal).ToArray();
    }

    public static IReadOnlyList<SessionLibraryEntry> FindInterruptedSessions(string dataRoot, bool appProcessRunning)
    {
        if (appProcessRunning)
        {
            return [];
        }

        return List(dataRoot).Where(static e => e is { Damaged: false, UnsupportedSchema: false, NeedsRecovery: true }).ToArray();
    }
}
