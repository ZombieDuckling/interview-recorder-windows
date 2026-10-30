using System.Text.Json;
using Recorder.Core.Formats;

namespace Recorder.App.Services;

internal static class SessionCatalog
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public static IReadOnlyList<SessionListItem> ReadSessions(string dataRoot)
    {
        if (!Directory.Exists(dataRoot))
        {
            return Array.Empty<SessionListItem>();
        }

        var list = new List<SessionListItem>();
        foreach (var dir in Directory.EnumerateDirectories(dataRoot))
        {
            var manifestPath = Path.Combine(dir, "session.json");
            if (!File.Exists(manifestPath))
            {
                continue;
            }

            var item = TryRead(manifestPath, dir);
            if (item is not null)
            {
                list.Add(item);
            }
        }

        return list.OrderByDescending(s => s.RecordedAt).ToList();
    }

    private static SessionListItem? TryRead(string manifestPath, string directory)
    {
        try
        {
            using var stream = File.OpenRead(manifestPath);
            var doc = JsonDocument.Parse(stream);
            var root = doc.RootElement;
            if (!root.TryGetProperty("sessionCode", out var codeEl))
            {
                return null;
            }

            var code = codeEl.GetString();
            if (string.IsNullOrWhiteSpace(code))
            {
                return null;
            }

            var state = SessionState.Failed;
            if (root.TryGetProperty("state", out var stateEl))
            {
                Enum.TryParse(stateEl.GetString(), ignoreCase: false, out state);
            }

            DateTimeOffset recordedAt = DateTimeOffset.MinValue;
            if (root.TryGetProperty("recording", out var recEl)
                && recEl.TryGetProperty("startedAt", out var startedEl)
                && startedEl.ValueKind == JsonValueKind.String
                && DateTimeOffset.TryParse(startedEl.GetString(), out var started))
            {
                recordedAt = started;
            }
            else if (root.TryGetProperty("createdAt", out var createdEl)
                     && DateTimeOffset.TryParse(createdEl.GetString(), out var created))
            {
                recordedAt = created;
            }

            TimeSpan? duration = null;
            if (root.TryGetProperty("recording", out recEl)
                && recEl.TryGetProperty("durationSeconds", out var durEl)
                && durEl.ValueKind == JsonValueKind.Number)
            {
                duration = TimeSpan.FromSeconds(durEl.GetDouble());
            }

            return new SessionListItem(code, directory, state, recordedAt, duration);
        }
        catch (IOException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
