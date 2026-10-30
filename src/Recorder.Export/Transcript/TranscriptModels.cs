using System.Text.Json.Serialization;

namespace Recorder.Export.Transcript;

public sealed class TranscriptFile
{
    public const string Disclaimer =
        "This transcript was produced by automated speech recognition and may contain errors. Check it against the audio recording before you rely on it.";

    [JsonPropertyName("schemaVersion")]
    public string SchemaVersion { get; set; } = "1.0.0";

    [JsonPropertyName("sessionId")]
    public string SessionId { get; set; } = string.Empty;

    [JsonPropertyName("sessionCode")]
    public string SessionCode { get; set; } = string.Empty;

    [JsonPropertyName("language")]
    public string Language { get; set; } = "en";

    [JsonPropertyName("engine")]
    public TranscriptEngine Engine { get; set; } = new();

    [JsonPropertyName("createdAt")]
    public string CreatedAt { get; set; } = string.Empty;

    [JsonPropertyName("updatedAt")]
    public string UpdatedAt { get; set; } = string.Empty;

    [JsonPropertyName("disclaimer")]
    public string DisclaimerText { get; set; } = Disclaimer;

    [JsonPropertyName("segments")]
    public List<TranscriptSegmentRecord> Segments { get; set; } = [];

    [JsonPropertyName("history")]
    public List<TranscriptHistoryEntry> History { get; set; } = [];

    [JsonPropertyName("review")]
    public TranscriptReview? Review { get; set; }
}

public sealed class TranscriptEngine
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "whisper.cpp";

    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;

    [JsonPropertyName("model")]
    public string Model { get; set; } = string.Empty;

    [JsonPropertyName("modelSha256")]
    public string ModelSha256 { get; set; } = string.Empty;
}

public sealed class TranscriptSegmentRecord
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("start")]
    public double Start { get; set; }

    [JsonPropertyName("end")]
    public double End { get; set; }

    [JsonPropertyName("speaker")]
    public string Speaker { get; set; } = "Unknown";

    [JsonPropertyName("speakerVerified")]
    public bool SpeakerVerified { get; set; }

    [JsonPropertyName("source")]
    public string Source { get; set; } = "mixed";

    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;

    [JsonPropertyName("originalText")]
    public string OriginalText { get; set; } = string.Empty;

    [JsonPropertyName("edited")]
    public bool Edited { get; set; }

    [JsonPropertyName("confidence")]
    public double? Confidence { get; set; }
}

public sealed class TranscriptHistoryEntry
{
    [JsonPropertyName("segmentId")]
    public int SegmentId { get; set; }

    [JsonPropertyName("field")]
    public string Field { get; set; } = string.Empty;

    [JsonPropertyName("at")]
    public string At { get; set; } = string.Empty;

    [JsonPropertyName("oldLength")]
    public int OldLength { get; set; }

    [JsonPropertyName("newLength")]
    public int NewLength { get; set; }
}

public sealed class TranscriptReview
{
    [JsonPropertyName("reviewerInitials")]
    public string ReviewerInitials { get; set; } = string.Empty;

    [JsonPropertyName("at")]
    public string At { get; set; } = string.Empty;

    [JsonPropertyName("note")]
    public string? Note { get; set; }
}
