using System.Text.Json.Nodes;
using Recorder.Core.Formats;

namespace Recorder.Audio.Windows.Tests;

internal sealed class TestFolder : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "audio-tests-" + Guid.NewGuid().ToString("N"));

    public TestFolder()
    {
        Directory.CreateDirectory(Path);
        var now = DateTimeOffset.UtcNow;
        var manifest = new SessionManifest
        {
            SessionId = Guid.NewGuid(), SessionCode = "IR-20261030-120000-ABCD", ParticipantId = "TEST",
            Consent = new() { Confirmed = true, ConfirmedAt = now }, CreatedAt = now, State = SessionState.Recording,
            StateHistory = [new() { State = SessionState.Recording, At = now }],
            App = new() { Platform = SessionPlatform.Windows, Arch = SessionArchitecture.X64, Version = "1.0.0", OsVersion = "test" },
        };
        File.WriteAllText(System.IO.Path.Combine(Path, "session.json"), FormatJson.SerializeSession(manifest));
    }

    public JsonObject Manifest => JsonNode.Parse(File.ReadAllText(System.IO.Path.Combine(Path, "session.json")))!.AsObject();
    public void Dispose() => Directory.Delete(Path, true);
}
