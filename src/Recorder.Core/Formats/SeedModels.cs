// SEED TYPES (win-lead). Owner from now on: win-core. Reshape these to the frozen shared spec
// (_shared/spec/shared-formats.md) when it lands; keep the names used by Abstractions or update both in one PR.
namespace Recorder.Core.Formats;

/// <summary>Durable session states (PLAN.md "Error recovery and observability").</summary>
public enum SessionState
{
    Recording,
    Finalizing,
    ReadyToTranscribe,
    Transcribing,
    Exported,
    Failed,
    Recovered,
}

/// <summary>Logical input. Researcher = local microphone, Participant = meeting playback (loopback).</summary>
public enum StreamRole
{
    Microphone,
    Meeting,
}

public enum SpeakerLabel
{
    Researcher,
    Participant,
    Unknown,
}

public sealed record TranscriptSegment(
    int Index,
    TimeSpan Start,
    TimeSpan End,
    SpeakerLabel Speaker,
    string Text,
    bool SpeakerVerified = false);

public sealed record Transcript(
    string ModelName,
    string ModelSha256,
    string EngineVersion,
    IReadOnlyList<TranscriptSegment> Segments);
