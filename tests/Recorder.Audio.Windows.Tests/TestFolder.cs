using System.Text.Json.Nodes;

namespace Recorder.Audio.Windows.Tests;

internal sealed class TestFolder : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "audio-tests-" + Guid.NewGuid().ToString("N"));

    public TestFolder()
    {
        Directory.CreateDirectory(Path);
        File.WriteAllText(System.IO.Path.Combine(Path, "session.json"), """
            {"schemaVersion":"1.0.0","consent":{"confirmed":true},"state":"Recording",
             "recording":{"startedAt":null,"stoppedAt":null,"durationSeconds":null},"streams":[],"hashes":{}}
            """);
    }

    public JsonObject Manifest => JsonNode.Parse(File.ReadAllText(System.IO.Path.Combine(Path, "session.json")))!.AsObject();

    public void Dispose() => Directory.Delete(Path, true);
}
