using System.Text.Json;
using System.Text.Json.Serialization;

namespace Recorder.Core.Formats;

public static class TranscriptDisclaimer
{
    public const string Text =
        "This transcript was produced by automated speech recognition and may contain errors. Check it against the audio recording before you rely on it.";
}

[JsonConverter(typeof(JsonStringEnumConverter<TranscriptSegmentSource>))]
public enum TranscriptSegmentSource
{
    [JsonStringEnumMemberName("mic")]
    Mic,

    [JsonStringEnumMemberName("meeting")]
    Meeting,

    [JsonStringEnumMemberName("mixed")]
    Mixed,
}

[JsonConverter(typeof(JsonStringEnumConverter<TranscriptHistoryField>))]
public enum TranscriptHistoryField
{
    [JsonStringEnumMemberName("text")]
    Text,

    [JsonStringEnumMemberName("speaker")]
    Speaker,

    [JsonStringEnumMemberName("speakerVerified")]
    SpeakerVerified,
}

public sealed class TranscriptEngineInfo
{
    [JsonPropertyOrder(0)]
    public string Name { get; set; } = "whisper.cpp";

    [JsonPropertyOrder(1)]
    public string Version { get; set; } = string.Empty;

    [JsonPropertyOrder(2)]
    public string Model { get; set; } = string.Empty;

    [JsonPropertyOrder(3)]
    public string ModelSha256 { get; set; } = string.Empty;

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

public sealed class TranscriptSegment
{
    [JsonPropertyOrder(0)]
    public int Id { get; set; }

    [JsonPropertyOrder(1)]
    public double Start { get; set; }

    [JsonPropertyOrder(2)]
    public double End { get; set; }

    [JsonPropertyOrder(3)]
    public string Speaker { get; set; } = string.Empty;

    [JsonPropertyOrder(4)]
    public bool SpeakerVerified { get; set; }

    [JsonPropertyOrder(5)]
    public TranscriptSegmentSource Source { get; set; }

    [JsonPropertyOrder(6)]
    public string Text { get; set; } = string.Empty;

    [JsonPropertyOrder(7)]
    public string OriginalText { get; set; } = string.Empty;

    [JsonPropertyOrder(8)]
    public bool Edited { get; set; }

    [JsonPropertyOrder(9)]
    public double? Confidence { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

public sealed class TranscriptHistoryEntry
{
    [JsonPropertyOrder(0)]
    public int SegmentId { get; set; }

    [JsonPropertyOrder(1)]
    public TranscriptHistoryField Field { get; set; }

    [JsonPropertyOrder(2)]
    [JsonConverter(typeof(Rfc3339UtcJsonConverter))]
    public DateTimeOffset At { get; set; }

    [JsonPropertyOrder(3)]
    public int OldLength { get; set; }

    [JsonPropertyOrder(4)]
    public int NewLength { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

public sealed class TranscriptReview
{
    [JsonPropertyOrder(0)]
    public string ReviewerInitials { get; set; } = string.Empty;

    [JsonPropertyOrder(1)]
    [JsonConverter(typeof(Rfc3339UtcJsonConverter))]
    public DateTimeOffset At { get; set; }

    [JsonPropertyOrder(2)]
    public string? Note { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

/// <summary>transcript.json root (shared spec section 5).</summary>
public sealed class Transcript
{
    [JsonPropertyOrder(0)]
    public string SchemaVersion { get; set; } = "1.0.0";

    [JsonPropertyOrder(1)]
    public Guid SessionId { get; set; }

    [JsonPropertyOrder(2)]
    public string SessionCode { get; set; } = string.Empty;

    [JsonPropertyOrder(3)]
    public string Language { get; set; } = string.Empty;

    [JsonPropertyOrder(4)]
    public TranscriptEngineInfo Engine { get; set; } = new();

    [JsonPropertyOrder(5)]
    [JsonConverter(typeof(Rfc3339UtcJsonConverter))]
    public DateTimeOffset CreatedAt { get; set; }

    [JsonPropertyOrder(6)]
    [JsonConverter(typeof(Rfc3339UtcJsonConverter))]
    public DateTimeOffset UpdatedAt { get; set; }

    [JsonPropertyOrder(7)]
    public string Disclaimer { get; set; } = TranscriptDisclaimer.Text;

    [JsonPropertyOrder(8)]
    public List<TranscriptSegment> Segments { get; set; } = [];

    [JsonPropertyOrder(9)]
    public List<TranscriptHistoryEntry> History { get; set; } = [];

    [JsonPropertyOrder(10)]
    public TranscriptReview? Review { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}
