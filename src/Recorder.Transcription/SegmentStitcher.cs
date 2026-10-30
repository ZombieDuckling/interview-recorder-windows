using Recorder.Core.Formats;

namespace Recorder.Transcription;

internal sealed record WindowSegment(double Start, double End, string Text);
internal sealed record StitchedSegment(int Index, TimeSpan Start, TimeSpan End, SpeakerLabel Speaker, string Text, StreamRole Role)
{
    public bool SpeakerVerified { get; init; }
}

/// <summary>Deduplicates time already emitted by the same stream; never removes the other stream's speech.</summary>
internal sealed class SegmentStitcher
{
    private readonly List<StitchedSegment> segments = [];
    private readonly Dictionary<StreamRole, double> emittedUntil = [];

    public void AddWindow(StreamRole role, double offset, double windowStart, double windowDuration, IEnumerable<WindowSegment> window)
    {
        var previousEnd = emittedUntil.GetValueOrDefault(role, offset);
        foreach (var segment in window.OrderBy(s => s.Start))
        {
            if (!double.IsFinite(segment.Start) || !double.IsFinite(segment.End) || string.IsNullOrWhiteSpace(segment.Text)) continue;
            var start = offset + windowStart + Math.Clamp(segment.Start, 0, windowDuration);
            var end = offset + windowStart + Math.Clamp(segment.End, 0, windowDuration);
            // Whisper emits word-sized segments. A word contained in the previous window is a duplicate.
            // Clip a straddling word's timestamp while retaining its text, rather than losing the new tail.
            if (end <= previousEnd || end <= start) continue;
            start = Math.Max(start, previousEnd);
            start = Math.Round(start, 3);
            end = Math.Round(end, 3);
            if (end <= start) continue;
            segments.Add(new(0, TimeSpan.FromSeconds(start), TimeSpan.FromSeconds(end),
                role == StreamRole.Microphone ? SpeakerLabel.Researcher : SpeakerLabel.Participant, segment.Text.Trim(), role));
            previousEnd = end;
        }
        emittedUntil[role] = previousEnd;
    }

    public IReadOnlyList<StitchedSegment> Build()
    {
        var ordered = segments.OrderBy(s => s.Start).ThenBy(s => s.Role).ToArray();
        // Sweep each stream against the other. Segments from a single stream are ordered/non-overlapping.
        foreach (var role in new[] { StreamRole.Microphone, StreamRole.Meeting })
        {
            var others = ordered.Where(s => s.Role != role).ToArray();
            var cursor = 0;
            for (var i = 0; i < ordered.Length; i++)
            {
                var segment = ordered[i];
                if (segment.Role != role) continue;
                while (cursor < others.Length && others[cursor].End <= segment.Start) cursor++;
                if (cursor < others.Length && others[cursor].Start < segment.End)
                    ordered[i] = segment with { Speaker = SpeakerLabel.Unknown };
            }
        }
        for (var i = 0; i < ordered.Length; i++) ordered[i] = ordered[i] with { Index = i + 1 };
        return ordered;
    }
}
