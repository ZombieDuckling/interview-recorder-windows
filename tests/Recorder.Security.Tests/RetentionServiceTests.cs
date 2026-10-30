using Recorder.Core.Abstractions;

namespace Recorder.Security.Tests;

public sealed class RetentionServiceTests
{
    [Fact]
    public void FindExpired_UsesDueAtFromSessionJson()
    {
        var root = CreateDataRoot();
        var expiredDir = Path.Combine(root, "expired-session");
        Directory.CreateDirectory(expiredDir);
        WriteSessionManifest(
            expiredDir,
            createdAt: "2025-01-01T00:00:00.000Z",
            dueAt: "2025-01-10T00:00:00.000Z",
            deleteAfterDays: 30);

        var keepDir = Path.Combine(root, "keep-session");
        Directory.CreateDirectory(keepDir);
        WriteSessionManifest(
            keepDir,
            createdAt: "2025-01-01T00:00:00.000Z",
            dueAt: null,
            deleteAfterDays: null);

        var service = new RetentionService();
        var now = DateTimeOffset.Parse("2025-02-01T00:00:00.000Z", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal);
        var expired = service.FindExpired(root, retentionDays: 90, now);

        Assert.Single(expired);
        Assert.Equal(expiredDir, expired[0].SessionDirectory, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DeleteSession_OverwritesThenRemovesFolder()
    {
        var sessionDir = Path.Combine(Path.GetTempPath(), "recorder-sec-del-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(sessionDir);
        var filePath = Path.Combine(sessionDir, "secret.bin");
        await File.WriteAllBytesAsync(filePath, [1, 2, 3, 4, 5]);

        var service = new RetentionService();
        await service.DeleteSessionAsync(sessionDir, CancellationToken.None);

        Assert.False(Directory.Exists(sessionDir));
    }

    private static string CreateDataRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), "recorder-sec-ret-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void WriteSessionManifest(
        string sessionDirectory,
        string createdAt,
        string? dueAt,
        int? deleteAfterDays)
    {
        var dueAtJson = dueAt is null ? "null" : $"\"{dueAt}\"";
        var daysJson = deleteAfterDays is null ? "null" : deleteAfterDays.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var json =
            $$"""
            {
              "schemaVersion": "1.0.0",
              "sessionId": "11111111-1111-1111-1111-111111111111",
              "sessionCode": "IR-20250101-120000-ABCD",
              "participantId": "P001",
              "consent": { "confirmed": true, "confirmedAt": "2025-01-01T00:00:00.000Z" },
              "createdAt": "{{createdAt}}",
              "state": "Exported",
              "stateHistory": [ { "state": "Exported", "at": "{{createdAt}}" } ],
              "app": { "name": "Interview Recorder", "version": "1.0.0", "platform": "windows", "osVersion": "test", "arch": "x64" },
              "recording": { "startedAt": null, "stoppedAt": null, "durationSeconds": null },
              "streams": [],
              "hashes": {},
              "retention": { "deleteAfterDays": {{daysJson}}, "dueAt": {{dueAtJson}} },
              "audit": [ { "event": "created", "at": "{{createdAt}}" } ]
            }
            """;
        File.WriteAllText(Path.Combine(sessionDirectory, "session.json"), json);
    }
}
