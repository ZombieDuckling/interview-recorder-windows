using System.Text.Json.Serialization;

namespace Recorder.Core.Formats;

/// <summary>Durable session states (shared spec section 2).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<SessionState>))]
public enum SessionState // JSON strings match member names (PascalCase)
{
    Recording,
    Finalizing,
    ReadyToTranscribe,
    Transcribing,
    Exported,
    Failed,
    Recovered,
}

/// <summary>Logical capture input (Abstractions). Maps to stream kind <c>mic</c> / <c>meeting</c> in session.json.</summary>
public enum StreamRole
{
    Microphone,
    Meeting,
}

/// <summary>Default speaker labels for transcript segments (spec section 5).</summary>
public enum SpeakerLabel
{
    Researcher,
    Participant,
    Unknown,
}

public static class StreamRoleMapping
{
    public static string ToJsonKind(StreamRole role) =>
        role switch
        {
            StreamRole.Microphone => "mic",
            StreamRole.Meeting => "meeting",
            _ => throw new ArgumentOutOfRangeException(nameof(role)),
        };

    public static StreamRole FromJsonKind(string kind) =>
        kind switch
        {
            "mic" => StreamRole.Microphone,
            "meeting" => StreamRole.Meeting,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
        };
}

public static class SpeakerLabelMapping
{
    public static string ToJson(SpeakerLabel label) => label switch
    {
        SpeakerLabel.Researcher => "Researcher",
        SpeakerLabel.Participant => "Participant",
        SpeakerLabel.Unknown => "Unknown",
        _ => throw new ArgumentOutOfRangeException(nameof(label)),
    };

    public static bool TryParse(string speaker, out SpeakerLabel label)
    {
        if (Enum.TryParse(speaker, ignoreCase: false, out label))
        {
            return true;
        }

        label = default;
        return false;
    }
}
