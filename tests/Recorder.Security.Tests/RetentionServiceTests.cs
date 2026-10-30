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
    public void FindExpired_KeepSessionOlderThanGlobalRetention_IsNeverExpired()
    {
        var root = CreateDataRoot();
        var keepDir = Path.Combine(root, "keep-old");
        Directory.CreateDirectory(keepDir);
        WriteSessionManifest(keepDir, "2020-01-01T00:00:00.000Z", dueAt: null, deleteAfterDays: null);

        var expired = new RetentionService().FindExpired(root, retentionDays: 90, Now);

        Assert.Empty(expired);
    }

    [Fact]
    public void FindExpired_ManifestWithoutRetentionObject_FallsBackToGlobalDays()
    {
        var root = CreateDataRoot();
        var dir = Path.Combine(root, "legacy");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "session.json"), "{ \"createdAt\": \"2025-01-01T00:00:00.000Z\" }");

        var expired = new RetentionService().FindExpired(root, retentionDays: 10, Now);

        Assert.Single(expired);
    }

    [Fact]
    public void FindExpired_DeleteAfterDaysWithoutDueAt_UsesCreatedAt()
    {
        var root = CreateDataRoot();
        var dir = Path.Combine(root, "days");
        Directory.CreateDirectory(dir);
        WriteSessionManifest(dir, "2025-01-01T00:00:00.000Z", dueAt: null, deleteAfterDays: 5);

        Assert.Single(new RetentionService().FindExpired(root, retentionDays: 0, Now));
    }

    [Fact]
    public void FindExpired_CorruptManifests_SkipThatSessionOnly()
    {
        var root = CreateDataRoot();
        var good = Path.Combine(root, "good");
        Directory.CreateDirectory(good);
        WriteSessionManifest(good, "2025-01-01T00:00:00.000Z", dueAt: "2025-01-10T00:00:00.000Z", deleteAfterDays: null);

        var bodies = new[]
        {
            "{ not json",
            "[]",
            "{ \"createdAt\": \"not-a-date\" }",
            "{ \"createdAt\": \"2025-01-01T00:00:00Z\", \"retention\": { \"dueAt\": \"garbage\" } }",
            "{ \"createdAt\": \"2025-01-01T00:00:00Z\", \"retention\": { \"deleteAfterDays\": 2147483647 } }",
            "{ \"createdAt\": \"2025-01-01T00:00:00Z\", \"retention\": { \"deleteAfterDays\": 99999999999 } }",
            "{ \"createdAt\": 12 }",
        };
        for (var i = 0; i < bodies.Length; i++)
        {
            var dir = Path.Combine(root, "bad" + i);
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "session.json"), bodies[i]);
        }

        var locked = Path.Combine(root, "locked");
        Directory.CreateDirectory(locked);
        WriteSessionManifest(locked, "2025-01-01T00:00:00.000Z", dueAt: "2025-01-10T00:00:00.000Z", deleteAfterDays: null);
        using var hold = new FileStream(Path.Combine(locked, "session.json"), FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        var expired = new RetentionService().FindExpired(root, retentionDays: 90, Now);

        Assert.Contains(expired, c => c.SessionDirectory.EndsWith("good", StringComparison.Ordinal));
        Assert.DoesNotContain(expired, c => c.SessionDirectory.Contains("bad", StringComparison.Ordinal));
    }

    [Fact]
    public async Task DeleteSession_RefusesFolderWithoutManifest()
    {
        var dir = Path.Combine(Path.GetTempPath(), "recorder-sec-nomanifest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "keep.txt"), "x");

        await Assert.ThrowsAsync<InvalidOperationException>(() => new RetentionService().DeleteSessionAsync(dir, CancellationToken.None));

        Assert.True(File.Exists(Path.Combine(dir, "keep.txt")));
    }

    [Fact]
    public async Task DeleteSession_WithDataRoot_RefusesFolderNotDirectlyUnderIt()
    {
        var root = CreateDataRoot();
        var other = CreateDataRoot();
        var dir = Path.Combine(other, "s");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "session.json"), "{}");

        await Assert.ThrowsAsync<InvalidOperationException>(() => new RetentionService(root).DeleteSessionAsync(dir, CancellationToken.None));
        Assert.True(Directory.Exists(dir));
    }

    [Fact]
    public async Task DeleteSession_HandlesReadOnlyFilesAndSubfolders()
    {
        var dir = Path.Combine(CreateDataRoot(), "s");
        Directory.CreateDirectory(Path.Combine(dir, "sub"));
        File.WriteAllText(Path.Combine(dir, "session.json"), "{}");
        var ro = Path.Combine(dir, "sub", "ro.bin");
        File.WriteAllBytes(ro, [1, 2, 3]);
        File.SetAttributes(ro, FileAttributes.ReadOnly);

        await new RetentionService().DeleteSessionAsync(dir, CancellationToken.None);

        Assert.False(Directory.Exists(dir));
    }

    [Fact]
    public async Task DeleteSession_RefusesSymlinkInside_AndLeavesTargetUntouched()
    {
        var dir = Path.Combine(CreateDataRoot(), "s");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "session.json"), "{}");
        var outside = CreateDataRoot();
        var target = Path.Combine(outside, "precious.txt");
        File.WriteAllText(target, "precious");
        try
        {
            Directory.CreateSymbolicLink(Path.Combine(dir, "link"), outside);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            return; // Symlink creation not permitted here (Windows without privilege).
        }

        await Assert.ThrowsAsync<InvalidOperationException>(() => new RetentionService().DeleteSessionAsync(dir, CancellationToken.None));

        Assert.Equal("precious", File.ReadAllText(target));
        Assert.True(File.Exists(Path.Combine(dir, "session.json")));
    }

    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2025-02-01T00:00:00.000Z", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal);

    [Fact]
    public async Task DeleteSession_OverwritesThenRemovesFolder()
    {
        var sessionDir = Path.Combine(Path.GetTempPath(), "recorder-sec-del-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(sessionDir);
        await File.WriteAllTextAsync(Path.Combine(sessionDir, "session.json"), "{}");
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
