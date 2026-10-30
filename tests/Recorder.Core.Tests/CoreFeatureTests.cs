using System.Globalization;
using System.Reflection;
using Recorder.Core.Diagnostics;
using Recorder.Core.Formats;
using Recorder.Core.Naming;
using Recorder.Core.Sessions;
using Recorder.Core.Settings;

namespace Recorder.Core.Tests;

public sealed class SessionStateMachineTests
{
    [Fact]
    public async Task TransitionToRecording_WithoutConsent_Throws()
    {
        var dir = CreateTempDir();
        var path = Path.Combine(dir, "session.json");
        var manifest = new SessionManifest
        {
            Consent = new SessionConsent { Confirmed = false },
        };
        var machine = new SessionStateMachine(manifest, path);
        await Assert.ThrowsAsync<SessionTransitionRefusedException>(() =>
            machine.TransitionToAsync(SessionState.Recording, DateTimeOffset.UtcNow));
    }

    [Fact]
    public async Task LegalTransition_AppendsHistoryAndPersists()
    {
        var dir = CreateTempDir();
        var path = Path.Combine(dir, "session.json");
        var manifest = new SessionManifest
        {
            Consent = new SessionConsent { Confirmed = true, ConfirmedAt = DateTimeOffset.UtcNow },
        };
        var machine = new SessionStateMachine(manifest, path);
        var at = DateTimeOffset.Parse("2026-10-31T07:15:00.010Z", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        await machine.TransitionToAsync(SessionState.Recording, at);
        Assert.Equal(SessionState.Recording, manifest.State);
        Assert.Single(manifest.StateHistory);
        Assert.True(File.Exists(path));
    }

    [Fact]
    public async Task IllegalTransition_Throws()
    {
        var dir = CreateTempDir();
        var path = Path.Combine(dir, "session.json");
        var manifest = new SessionManifest
        {
            Consent = new SessionConsent { Confirmed = true, ConfirmedAt = DateTimeOffset.UtcNow },
            State = SessionState.ReadyToTranscribe,
            StateHistory = [new SessionStateHistoryEntry { State = SessionState.ReadyToTranscribe, At = DateTimeOffset.UtcNow }],
        };
        var machine = new SessionStateMachine(manifest, path);
        await Assert.ThrowsAsync<SessionTransitionRefusedException>(() =>
            machine.TransitionToAsync(SessionState.Exported, DateTimeOffset.UtcNow));
    }

    private static string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ir-state-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }
}

public sealed class SafeFileNamingTests
{
    [Theory]
    [InlineData("CON", "_CON")]
    [InlineData("hello world", "hello-world")]
    public void Sanitize_ReplacesUnsafeCharacters(string input, string expectedStart)
    {
        var sanitized = SafeFileNaming.SanitizeFileName(input);
        Assert.StartsWith(expectedStart, sanitized, StringComparison.Ordinal);
        Assert.True(sanitized.Length <= SafeFileNaming.MaxNameLength);
    }

    [Fact]
    public void SessionCode_MatchesPattern()
    {
        var id = Guid.Parse("7f3a9c1e-2b4d-4e6f-8a1b-3c5d7e9f0a2b");
        var code = SafeFileNaming.BuildSessionCode(new DateTimeOffset(2026, 10, 31, 9, 15, 0, TimeSpan.FromHours(2)), id);
        Assert.Matches("^IR-[0-9]{8}-[0-9]{6}-[0-9A-F]{4}$", code);
    }
}

public sealed class ProcessingLogWriterTests
{
    [Fact]
    public void PublicLogMethods_DoNotTakeFreeTextStringParameters()
    {
        var methods = typeof(ProcessingLogWriter)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(static m => m.Name is "Info" or "Warn" or "Error");

        foreach (var method in methods)
        {
            Assert.DoesNotContain(
                method.GetParameters(),
                static p => p.ParameterType == typeof(string));
        }
    }
}

public sealed class SessionLibraryTests
{
    [Fact]
    public void List_MarksCorruptSessionAsDamaged()
    {
        var root = Path.Combine(Path.GetTempPath(), "ir-lib-" + Guid.NewGuid().ToString("N"));
        var folder = Path.Combine(root, "IR-20261031-091500-7F3A");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "session.json"), "{");
        try
        {
            var entries = SessionLibrary.List(root);
            Assert.Single(entries);
            Assert.True(entries[0].Damaged);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Retention_ComputesDueAt()
    {
        var created = DateTimeOffset.Parse("2026-10-31T07:14:58.400Z", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        var due = SessionRetentionLogic.ComputeDueAt(created, 365);
        Assert.Equal(created.AddDays(365), due);
    }
}

public sealed class SyncFolderDetectionTests
{
    [Fact]
    public void DetectsOneDrivePathSegment()
    {
        var path = OperatingSystem.IsWindows()
            ? @"C:\Users\me\OneDrive\Documents\Interview Recorder"
            : "/Users/me/OneDrive/Documents/Interview Recorder";
        Assert.True(SyncFolderDetection.IsKnownSyncFolder(path));
    }
}
