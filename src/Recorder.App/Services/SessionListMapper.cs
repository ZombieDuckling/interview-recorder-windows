using Recorder.Core.Formats;
using Recorder.Core.IO;
using Recorder.Core.SessionManagement;

namespace Recorder.App.Services;

internal static class SessionListMapper
{
    public static IReadOnlyList<SessionListItem> List(string dataRoot)
    {
        return SessionLibrary.List(dataRoot).Select(MapEntry).ToList();
    }

    private static SessionListItem MapEntry(SessionLibraryEntry entry)
    {
        if (entry.Damaged)
        {
            return new SessionListItem(
                entry.SessionCode,
                entry.FolderPath,
                State: null,
                RecordedAt: null,
                Duration: null,
                Damaged: true,
                NeedsRecovery: false,
                ListMessage: "This session could not be read (damaged session.json). Capture files were not changed.");
        }

        try
        {
            var manifest = SessionManifestIO.Read(Path.Combine(entry.FolderPath, "session.json"));
            DateTimeOffset? recordedAt = manifest.Recording.StartedAt ?? manifest.CreatedAt;
            TimeSpan? duration = manifest.Recording.DurationSeconds is null
                ? null
                : TimeSpan.FromSeconds(manifest.Recording.DurationSeconds.Value);
            return new SessionListItem(
                entry.SessionCode,
                entry.FolderPath,
                manifest.State,
                recordedAt,
                duration,
                Damaged: false,
                entry.NeedsRecovery,
                ListMessage: entry.NeedsRecovery
                    ? "This session was interrupted. Use Recover to continue safely."
                    : null);
        }
        catch (CorruptedFileException)
        {
            return new SessionListItem(
                entry.SessionCode,
                entry.FolderPath,
                State: null,
                RecordedAt: null,
                Duration: null,
                Damaged: true,
                NeedsRecovery: false,
                ListMessage: "This session could not be read. Capture files were not changed.");
        }
    }
}
