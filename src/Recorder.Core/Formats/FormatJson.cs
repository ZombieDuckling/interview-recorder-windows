using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Recorder.Core.Formats;

/// <summary>Shared JSON options for session.json and transcript.json (camelCase, spec timestamps).</summary>
public static class FormatJson
{
    public static JsonSerializerOptions Options { get; } = CreateOptions();

    public static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DictionaryKeyPolicy = null,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            WriteIndented = true,
            IndentSize = 2,
        };

        options.Converters.Add(new Rfc3339UtcJsonConverter());
        options.Converters.Add(new NullableRfc3339UtcJsonConverter());
        return options;
    }

    public static SessionManifest DeserializeSession(ReadOnlySpan<byte> utf8Json) =>
        JsonSerializer.Deserialize<SessionManifest>(utf8Json, Options)
        ?? throw new JsonException("session.json did not contain an object.");

    public static SessionManifest DeserializeSession(string json) =>
        JsonSerializer.Deserialize<SessionManifest>(json, Options)
        ?? throw new JsonException("session.json did not contain an object.");

    public static string SerializeSession(SessionManifest manifest) =>
        JsonSerializer.Serialize(manifest, Options);

    public static Transcript DeserializeTranscript(ReadOnlySpan<byte> utf8Json) =>
        JsonSerializer.Deserialize<Transcript>(utf8Json, Options)
        ?? throw new JsonException("transcript.json did not contain an object.");

    public static Transcript DeserializeTranscript(string json) =>
        JsonSerializer.Deserialize<Transcript>(json, Options)
        ?? throw new JsonException("transcript.json did not contain an object.");

    public static string SerializeTranscript(Transcript transcript) =>
        JsonSerializer.Serialize(transcript, Options);
}
